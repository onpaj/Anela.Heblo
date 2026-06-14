# Investigate and resolve 403 storm on `GET /api/StockUpOperations/summary` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the 209/210 403-Forbidden responses on `GET /api/StockUpOperations/summary` by gating the React-Query polling hook at its callsites on `Warehouse_StockUp` Read, add reflection-based regression coverage on the controller's authorization contract, and document the remediation. Server gate stays as-is.

**Architecture:** No backend code change in the default path. The hook `useStockUpOperationsSummary` gains an additive `{ enabled?: boolean }` options bag forwarded to React Query. Each of the two callsites (`TransportBoxList`, `GiftPackageManufacturing`) computes `canSeeStockUp = hasPermission('warehouse.stock_up.read')` from `usePermissionsContext()` and only enables the hook / renders the indicator when the permission is held. Regression coverage is reflection-based on the controller attributes (mirrors `DashboardControllerAuthorizationTests` / `GridLayoutsControllerAuthorizationTests`), plus a Jest test per page asserting that the API client method is NOT invoked when the permission is missing.

**Tech Stack:** .NET 8, xUnit + FluentAssertions + reflection (BE tests); React 18, TanStack React Query v5, Jest + React Testing Library (FE tests); existing `PermissionsContext`; Application Insights (KQL) for caller attribution.

---

## Path selection

This plan implements **R-A (Frontend gate)** as the default. If FR-2 attribution shows the 403 callers SHOULD have access, switch to **R-B (Broaden the gate)** and follow the inline R-B branch in Task 5. If FR-2 shows the noise has self-resolved AND no impacted caller, **R-C** is documentation-only — skip Tasks 4–9 and complete Tasks 1–3, 10–12 only. The arch-review fallback rule applies: if attribution is impossible, default to R-A.

---

## File Structure

**Backend (R-A default — no source code change):**
- Modify (R-B branch only): `backend/src/Anela.Heblo.API/Controllers/StockUpOperationsController.cs:12-15` — class attribute removed and method-level read attribute added to `GetSummary`.
- Create: `backend/test/Anela.Heblo.Tests/Authorization/StockUpOperationsControllerAuthorizationTests.cs` — reflection-based contract test for the controller.

**Frontend (R-A default):**
- Modify: `frontend/src/api/hooks/useStockUpOperations.ts:112-125` — accept `{ enabled?: boolean }` options bag and forward to `useQuery`.
- Modify: `frontend/src/components/pages/TransportBoxList.tsx:31-96` — gate hook callsite and indicator.
- Modify: `frontend/src/components/pages/GiftPackageManufacturing/index.tsx:7-40` — gate hook callsite and indicator.
- Create: `frontend/src/components/pages/__tests__/TransportBoxList.stockUpGate.test.tsx` — asserts the API client method is not invoked when permission is missing.
- Create: `frontend/src/components/pages/GiftPackageManufacturing/__tests__/StockUpGate.test.tsx` — same assertion for the gift-package page.

**Docs:**
- Create: `docs/routines/telemetry-anomaly/2026-06-13-stockupoperations-summary-403.md` — investigation note + chosen path + caller attribution. Lives next to the existing telemetry-anomaly routine docs the brief originated from.

**Investigation artifacts (not committed; produced and pasted into the PR description):**
- KQL queries against Application Insights to attribute the 403 caller(s) (FR-2).
- KQL query against the single 500 (FR-5).

---

### Task 1: Verify route mapping and authorization contract (FR-1)

**Files:**
- Read-only: `backend/src/Anela.Heblo.API/Controllers/StockUpOperationsController.cs`
- Read-only: `backend/src/Anela.Heblo.API/Program.cs`
- Read-only grep across `backend/src/**/*.cs`

- [ ] **Step 1: Grep for any other route registration of `StockUpOperations/summary`**

Run:
```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-telemetry-get-api-stockupoperations-summ
grep -rn "StockUpOperations" backend/src --include="*.cs" | grep -iE 'route|map|http'
grep -rn '"summary"' backend/src --include="*.cs" | grep -i stockup
```
Expected: Exactly one `HttpGet("summary")` decoration, on `StockUpOperationsController.GetSummary` at `StockUpOperationsController.cs:113`. No other `Map*` registrations naming this path.

- [ ] **Step 2: Confirm class-level attribute is the only gate on `GetSummary`**

Open `backend/src/Anela.Heblo.API/Controllers/StockUpOperationsController.cs` and assert visually:
- Line 12: `[FeatureAuthorize(Feature.Warehouse_StockUp)]` (defaults to `AccessLevel.Read`).
- Line 113–130: `GetSummary` carries `[HttpGet("summary")]` only — no method-level `[Authorize]` or `[FeatureAuthorize]`.
- Lines 76, 95: `RetryOperation` and `AcceptOperation` carry `[FeatureAuthorize(Feature.Warehouse_StockUp, AccessLevel.Write)]` (must remain unchanged).

- [ ] **Step 3: Capture findings into the FR-1 section of the investigation doc**

Create `docs/routines/telemetry-anomaly/2026-06-13-stockupoperations-summary-403.md` with this opening block (rest filled in by later tasks):
```markdown
# 403 storm on `GET /api/StockUpOperations/summary` — 2026-06-13

## FR-1 — Route and gate verification

- Route `GET /api/StockUpOperations/summary` resolves to `StockUpOperationsController.GetSummary` only.
  Source: `backend/src/Anela.Heblo.API/Controllers/StockUpOperationsController.cs:113`.
- Class-level gate `[FeatureAuthorize(Feature.Warehouse_StockUp)]` (Read) is the authoritative gate; no overriding method-level attributes.
- Write actions (`RetryOperation`, `AcceptOperation`) remain at `Warehouse_StockUp` Write.
- No duplicate route registrations in `Program.cs` or extension modules.

## FR-2 — Caller attribution
_TBD by Task 2._

## FR-3 — Remediation
_TBD by Task 3._

## FR-5 — Single 500
_TBD by Task 10._
```

- [ ] **Step 4: Commit the empty investigation doc**

```bash
git add docs/routines/telemetry-anomaly/2026-06-13-stockupoperations-summary-403.md
git commit -m "docs(telemetry): seed 403 investigation note for StockUpOperations/summary"
```

---

### Task 2: Attribute the 403 callers via Application Insights (FR-2)

**Files:**
- Modify: `docs/routines/telemetry-anomaly/2026-06-13-stockupoperations-summary-403.md`

- [ ] **Step 1: Run the caller-attribution KQL query**

Paste into the Application Insights query editor for the production app (replace `<APP_ID>` with the prod App Insights resource):
```kusto
requests
| where timestamp between (datetime(2026-06-05) .. datetime(2026-06-12T23:59:59Z))
| where url has "/api/StockUpOperations/summary"
| where resultCode == 403
| extend principalId = tostring(user_AuthenticatedId)
| extend principalDisplay = coalesce(principalId, tostring(user_Id), "anonymous")
| summarize calls = count(), firstSeen = min(timestamp), lastSeen = max(timestamp), pages = make_set(tostring(customDimensions.["Referer"]), 5) by principalDisplay
| order by calls desc
```
Expected output: a small list of principals (most likely 1–3) with their call counts, first/last seen, and the Referer pages.

- [ ] **Step 2: Map each principal to their current `Warehouse_StockUp` Read status**

For each `principalDisplay` produced by Step 1, look up the user in the access UI (`/access/users`) and record whether they have a group that grants `warehouse.stock_up.read`. Mark them `should-have-access` or `correctly-denied`.

If `principalDisplay` is `"anonymous"` for some rows, that is a separate problem class (unauthenticated calls). Note it but do not pivot the plan — anonymous 403s are also resolved by R-A (the hook will not fire on an unauthenticated page anyway because `PermissionsProvider` keeps `isLoading: true` while the user is not authenticated).

- [ ] **Step 3: Append FR-2 findings to the investigation doc**

Replace the `## FR-2 — Caller attribution` block in `docs/routines/telemetry-anomaly/2026-06-13-stockupoperations-summary-403.md` with the concrete table:
```markdown
## FR-2 — Caller attribution

Window: 2026-06-05 → 2026-06-12.

| Principal | Calls | First seen | Last seen | Pages | Has `warehouse.stock_up.read` | Verdict |
|-----------|-------|------------|-----------|-------|-------------------------------|---------|
| <id-1>    | <n>   | <ts>       | <ts>      | <urls>| <yes/no>                      | <should-have-access / correctly-denied> |
| ...       |       |            |           |       |                               |        |

Total distinct principals: <n>.
Authentication state: <all-authenticated / mixed / all-anonymous>.
```

- [ ] **Step 4: Commit the FR-2 findings**

```bash
git add docs/routines/telemetry-anomaly/2026-06-13-stockupoperations-summary-403.md
git commit -m "docs(telemetry): record FR-2 caller attribution for StockUpOperations/summary 403s"
```

---

### Task 3: Choose remediation path and document it (FR-3 selection)

**Files:**
- Modify: `docs/routines/telemetry-anomaly/2026-06-13-stockupoperations-summary-403.md`

- [ ] **Step 1: Apply the decision rule**

Decision matrix (apply in order — pick the first row that matches):

| FR-2 outcome | Path |
|--------------|------|
| All 403 principals are `correctly-denied` (do NOT need the data) | **R-A** |
| Attribution impossible (anonymous / null `user_AuthenticatedId`) | **R-A** (per arch-review fallback) |
| 1+ principal is `should-have-access` (needs the data) | **R-B** |
| 0 calls in the last 2 days AND every prior principal was `correctly-denied` AND no impacted caller exists | **R-C** |

- [ ] **Step 2: Record the chosen path with the justification**

Replace `## FR-3 — Remediation` in the investigation doc with:
```markdown
## FR-3 — Remediation

Chosen path: **<R-A | R-B | R-C>**.

Justification (cite the FR-2 row(s)):
- ...

After-fix expected 403 rate on `GET /api/StockUpOperations/summary`: near-zero within 24h (NFR-3).
```

- [ ] **Step 3: Commit the decision**

```bash
git add docs/routines/telemetry-anomaly/2026-06-13-stockupoperations-summary-403.md
git commit -m "docs(telemetry): select remediation path for StockUpOperations/summary 403 storm"
```

---

### Task 4: Add `{ enabled?: boolean }` options bag to `useStockUpOperationsSummary` (R-A only)

**Files:**
- Modify: `frontend/src/api/hooks/useStockUpOperations.ts:108-125`

**Skip this task entirely if R-B or R-C was selected.**

- [ ] **Step 1: Write the failing test for the new options bag**

Create `frontend/src/api/hooks/__tests__/useStockUpOperationsSummary.test.ts`:
```typescript
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import { useStockUpOperationsSummary } from "../useStockUpOperations";
import { StockUpSourceType } from "../../generated/api-client";

const mockGetSummary = jest.fn().mockResolvedValue({ success: true });

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: () => ({
    stockUpOperations_GetSummary: mockGetSummary,
  }),
  QUERY_KEYS: { stockUpOperations: ["stock-up-operations"] },
}));

const wrap = (children: React.ReactNode) => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
};

describe("useStockUpOperationsSummary", () => {
  beforeEach(() => {
    mockGetSummary.mockClear();
  });

  it("does NOT call the API when enabled is false", async () => {
    renderHook(
      () =>
        useStockUpOperationsSummary(StockUpSourceType.TransportBox, {
          enabled: false,
        }),
      { wrapper: ({ children }) => wrap(children) }
    );
    // Give React Query a tick to settle.
    await new Promise((r) => setTimeout(r, 50));
    expect(mockGetSummary).not.toHaveBeenCalled();
  });

  it("calls the API when enabled defaults to true", async () => {
    renderHook(
      () => useStockUpOperationsSummary(StockUpSourceType.TransportBox),
      { wrapper: ({ children }) => wrap(children) }
    );
    await waitFor(() => expect(mockGetSummary).toHaveBeenCalledTimes(1));
  });
});
```

- [ ] **Step 2: Run the test to verify both cases fail**

Run:
```bash
cd frontend
npx jest src/api/hooks/__tests__/useStockUpOperationsSummary.test.ts
```
Expected: test "does NOT call the API when enabled is false" FAILS (the hook currently calls the API regardless), test "calls the API when enabled defaults to true" PASSES (current behavior).

- [ ] **Step 3: Modify the hook to accept the options bag**

Replace lines 108–125 of `frontend/src/api/hooks/useStockUpOperations.ts` with:
```typescript
/**
 * Hook to get StockUpOperations summary counts (Pending, Submitted, Failed)
 * Polls every 15 seconds for live updates.
 *
 * Pass `{ enabled: false }` to suppress the request entirely (e.g. when the
 * caller lacks `warehouse.stock_up.read`). Default is `enabled: true`.
 */
export interface UseStockUpOperationsSummaryOptions {
  enabled?: boolean;
}

export const useStockUpOperationsSummary = (
  sourceType?: StockUpSourceType,
  options?: UseStockUpOperationsSummaryOptions,
) => {
  return useQuery({
    queryKey: stockUpOperationsKeys.summary(sourceType),
    queryFn: async (): Promise<GetStockUpOperationsSummaryResponse> => {
      const client = getStockUpOperationsClient();
      return await client.stockUpOperations_GetSummary(sourceType ?? undefined);
    },
    enabled: options?.enabled ?? true,
    refetchInterval: 15000, // Poll every 15 seconds
    refetchOnWindowFocus: true,
    staleTime: 14000, // Consider stale just before next poll
    gcTime: 60000, // Keep in cache for 1 minute
    retry: 1, // Limit retries during polling
  });
};
```

- [ ] **Step 4: Run the test to verify it passes**

Run:
```bash
cd frontend
npx jest src/api/hooks/__tests__/useStockUpOperationsSummary.test.ts
```
Expected: both tests PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/hooks/useStockUpOperations.ts frontend/src/api/hooks/__tests__/useStockUpOperationsSummary.test.ts
git commit -m "feat(stock-up): add enabled option to useStockUpOperationsSummary"
```

---

### Task 5: Gate `TransportBoxList` on `warehouse.stock_up.read` (R-A) — OR — broaden gate (R-B)

**Files (R-A):**
- Modify: `frontend/src/components/pages/TransportBoxList.tsx:31-96`

**Files (R-B):**
- Modify: `backend/src/Anela.Heblo.API/Controllers/StockUpOperationsController.cs:12-130`

#### R-A branch (chosen by default)

- [ ] **Step 1: Add the permissions import and gate at the callsite**

In `frontend/src/components/pages/TransportBoxList.tsx`, add to the existing imports block (near lines 19–34):
```typescript
import { usePermissionsContext } from "../../auth/PermissionsContext";
```

Replace lines 89–96 (the existing `useStockUpOperationsSummary` block and `showIndicator` derivation) with:
```typescript
  // Gate StockUpOperations summary on the matching feature permission.
  // Backend constant: AccessRoles.WarehouseStockUpRead = "warehouse.stock_up.read"
  // (see backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs).
  const { hasPermission, isLoading: permsLoading } = usePermissionsContext();
  const canSeeStockUp = !permsLoading && hasPermission('warehouse.stock_up.read');

  // Add summary hook for StockUpOperations status — only enabled when permission is held.
  const { data: stockUpSummary } = useStockUpOperationsSummary(
    StockUpSourceType.TransportBox,
    { enabled: canSeeStockUp },
  );

  // Conditionally show indicator (also requires the permission).
  const showIndicator = canSeeStockUp && stockUpSummary &&
    ((stockUpSummary.totalInQueue ?? 0) > 0 || (stockUpSummary.failedCount ?? 0) > 0);
```

- [ ] **Step 2: Type-check the change**

Run:
```bash
cd frontend
npx tsc --noEmit
```
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/pages/TransportBoxList.tsx
git commit -m "fix(transport-boxes): gate StockUpOperations summary on warehouse.stock_up.read"
```

#### R-B branch (apply ONLY if Task 3 selected R-B)

- [ ] **Step 1: Determine the broadened feature/level**

From the FR-2 row(s) marked `should-have-access`, identify the `Feature` enum value all relevant principals share Read access to. Examples used elsewhere in the project: `Feature.Warehouse_TransportBoxes`, `Feature.Warehouse_GiftPackageManufacture`. Record the chosen feature in the investigation doc.

- [ ] **Step 2: Move the gate from class-level to method-level (broadened)**

In `backend/src/Anela.Heblo.API/Controllers/StockUpOperationsController.cs`:

Replace lines 12–15:
```csharp
[FeatureAuthorize(Feature.Warehouse_StockUp)]
[ApiController]
[Route("api/[controller]")]
public class StockUpOperationsController : ControllerBase
```
With:
```csharp
[ApiController]
[Route("api/[controller]")]
public class StockUpOperationsController : ControllerBase
```

On line 38 (above `[HttpGet]` for `GetOperations`), add:
```csharp
    [FeatureAuthorize(Feature.Warehouse_StockUp)]
```

On line 113 (above `[HttpGet("summary")]`), add:
```csharp
    [FeatureAuthorize(<CHOSEN_FEATURE_FROM_STEP_1>)]
```

Lines 75–76 (`RetryOperation`) and 94–95 (`AcceptOperation`) remain untouched — they already carry their Write-level attributes.

- [ ] **Step 3: Build the backend**

Run:
```bash
cd backend
dotnet build
```
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/StockUpOperationsController.cs
git commit -m "fix(stock-up): broaden GET summary gate to <CHOSEN_FEATURE> Read"
```

---

### Task 6: Gate `GiftPackageManufacturing` on `warehouse.stock_up.read` (R-A only)

**Files:**
- Modify: `frontend/src/components/pages/GiftPackageManufacturing/index.tsx:7-40`

**Skip this task if R-B or R-C was selected.**

- [ ] **Step 1: Add the permissions import and gate at the callsite**

In `frontend/src/components/pages/GiftPackageManufacturing/index.tsx`, add to the existing imports block (lines 1–9):
```typescript
import { usePermissionsContext } from "../../../auth/PermissionsContext";
```

Replace lines 33–40 (the existing `useStockUpOperationsSummary` block and `showIndicator` derivation) with:
```typescript
  // Gate StockUpOperations summary on the matching feature permission.
  // Backend constant: AccessRoles.WarehouseStockUpRead = "warehouse.stock_up.read"
  // (see backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs).
  const { hasPermission, isLoading: permsLoading } = usePermissionsContext();
  const canSeeStockUp = !permsLoading && hasPermission('warehouse.stock_up.read');

  // Add summary hook for StockUpOperations status — only enabled when permission is held.
  const { data: stockUpSummary } = useStockUpOperationsSummary(
    StockUpSourceType.GiftPackageManufacture,
    { enabled: canSeeStockUp },
  );

  // Conditionally show indicator (also requires the permission).
  const showIndicator = canSeeStockUp && stockUpSummary &&
    ((stockUpSummary.totalInQueue ?? 0) > 0 || (stockUpSummary.failedCount ?? 0) > 0);
```

- [ ] **Step 2: Type-check the change**

Run:
```bash
cd frontend
npx tsc --noEmit
```
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/pages/GiftPackageManufacturing/index.tsx
git commit -m "fix(gift-package): gate StockUpOperations summary on warehouse.stock_up.read"
```

---

### Task 7: Add reflection-based authorization regression test on `StockUpOperationsController` (FR-4 BE)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Authorization/StockUpOperationsControllerAuthorizationTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Authorization/StockUpOperationsControllerAuthorizationTests.cs`:
```csharp
using System.Reflection;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class StockUpOperationsControllerAuthorizationTests
{
    [Fact]
    public void StockUpOperationsController_IsGatedByWarehouseStockUpRead()
    {
        var attribute = typeof(StockUpOperationsController)
            .GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull(
            "the controller is the single source of authorization for read endpoints; " +
            "removing the class-level gate would silently broaden the API");
        attribute!.Feature.Should().Be(Feature.Warehouse_StockUp);
        attribute.Level.Should().Be(AccessLevel.Read);
    }

    [Fact]
    public void GetSummary_HasNoOverridingAuthorizeAttribute()
    {
        var method = typeof(StockUpOperationsController)
            .GetMethod(nameof(StockUpOperationsController.GetSummary))!;

        method.GetCustomAttributes<AuthorizeAttribute>(inherit: false)
            .Should()
            .BeEmpty(
                "the class-level Warehouse_StockUp Read gate is the authoritative gate " +
                "for the summary endpoint; an overriding method-level attribute would " +
                "either broaden or narrow access silently");
    }

    [Theory]
    [InlineData(nameof(StockUpOperationsController.RetryOperation))]
    [InlineData(nameof(StockUpOperationsController.AcceptOperation))]
    public void WriteActions_RemainGatedAtWriteLevel(string methodName)
    {
        var method = typeof(StockUpOperationsController).GetMethod(methodName)!;
        var attribute = method.GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull(
            $"{methodName} mutates stock-up state and must require Warehouse_StockUp Write");
        attribute!.Feature.Should().Be(Feature.Warehouse_StockUp);
        attribute.Level.Should().Be(AccessLevel.Write);
    }
}
```

**If R-B was chosen in Task 3**, replace the first two `[Fact]` tests with their R-B equivalents:
```csharp
    [Fact]
    public void StockUpOperationsController_HasNoClassLevelFeatureAuthorize()
    {
        var attribute = typeof(StockUpOperationsController)
            .GetCustomAttribute<FeatureAuthorizeAttribute>();
        attribute.Should().BeNull(
            "after R-B, read gates are pushed to each action so the summary endpoint " +
            "can be served to a broader audience than the rest of the controller");
    }

    [Fact]
    public void GetSummary_IsGatedByBroadenedFeature()
    {
        var method = typeof(StockUpOperationsController)
            .GetMethod(nameof(StockUpOperationsController.GetSummary))!;
        var attribute = method.GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Feature.Should().Be(Feature.<CHOSEN_FEATURE_FROM_TASK_5_R_B>);
        attribute.Level.Should().Be(AccessLevel.Read);
    }

    [Fact]
    public void GetOperations_RetainsWarehouseStockUpRead()
    {
        var method = typeof(StockUpOperationsController)
            .GetMethod(nameof(StockUpOperationsController.GetOperations))!;
        var attribute = method.GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Feature.Should().Be(Feature.Warehouse_StockUp);
        attribute.Level.Should().Be(AccessLevel.Read);
    }
```

- [ ] **Step 2: Run the test to verify it passes against current code (R-A) — or to capture the expected baseline (R-B)**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~StockUpOperationsControllerAuthorizationTests"
```
Expected (R-A): all 4 tests PASS — they document the current contract and will fail if any future PR drifts the attribute placement.
Expected (R-B): the new method-level tests PASS against the R-B controller from Task 5.

- [ ] **Step 3: Verify formatting**

Run:
```bash
cd backend
dotnet format --verify-no-changes test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Authorization/StockUpOperationsControllerAuthorizationTests.cs
git commit -m "test(auth): assert StockUpOperationsController gate contract"
```

---

### Task 8: Add Jest test asserting `TransportBoxList` does not call the API without permission (FR-4 FE, R-A only)

**Files:**
- Create: `frontend/src/components/pages/__tests__/TransportBoxList.stockUpGate.test.tsx`

**Skip this task if R-B or R-C was selected.**

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/pages/__tests__/TransportBoxList.stockUpGate.test.tsx`:
```typescript
import React from "react";
import { render, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import TransportBoxList from "../TransportBoxList";
import { TestRouterWrapper } from "../../../test-utils/router-wrapper";

const mockGetSummary = jest.fn();
const mockGetTransportBoxes = jest.fn().mockResolvedValue({ items: [], total: 0 });
const mockGetTransportBoxSummary = jest.fn().mockResolvedValue({});

jest.mock("../../../api/client", () => ({
  getAuthenticatedApiClient: () => ({
    stockUpOperations_GetSummary: mockGetSummary,
    transportBox_GetTransportBoxes: mockGetTransportBoxes,
    transportBox_GetTransportBoxSummary: mockGetTransportBoxSummary,
  }),
  QUERY_KEYS: {
    catalog: ["catalog"],
    transportBox: ["transport-boxes"],
    transportBoxTransitions: ["transportBoxTransitions"],
    stockUpOperations: ["stock-up-operations"],
  },
}));

jest.mock("../../../api/generated/api-client", () => ({
  CreateNewTransportBoxRequest: jest.fn().mockImplementation((d) => d),
  StockUpSourceType: { TransportBox: "TransportBox", GiftPackageManufacture: "GiftPackageManufacture" },
  ProductType: { Material: "Material", Product: "Product", SemiProduct: "SemiProduct", Goods: "Goods", Set: "Set", UNDEFINED: "UNDEFINED" },
}));

jest.mock("../../../api/hooks/useTransportBoxes", () => ({
  useTransportBoxesQuery: jest.fn().mockReturnValue({ data: { items: [], total: 0 }, isLoading: false }),
  useTransportBoxSummaryQuery: jest.fn().mockReturnValue({ data: {}, isLoading: false }),
}));

jest.mock("../../common/CatalogAutocomplete", () => ({
  CatalogAutocomplete: () => null,
}));
jest.mock("../TransportBoxDetail", () => () => null);
jest.mock("../../common/StockUpOperationStatusIndicator", () => () => null);
jest.mock("../../../telemetry/useScreenView", () => ({ useScreenView: () => undefined }));

const mockUsePermissionsContext = jest.fn();
jest.mock("../../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => mockUsePermissionsContext(),
}));

const renderPage = () => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <TestRouterWrapper>
        <TransportBoxList />
      </TestRouterWrapper>
    </QueryClientProvider>,
  );
};

describe("TransportBoxList — StockUpOperations summary permission gate", () => {
  beforeEach(() => {
    mockGetSummary.mockReset();
    mockGetSummary.mockResolvedValue({ totalInQueue: 0, failedCount: 0 });
  });

  it("does NOT call stockUpOperations_GetSummary when user lacks warehouse.stock_up.read", async () => {
    mockUsePermissionsContext.mockReturnValue({
      permissions: [],
      isSuperUser: false,
      groups: [],
      isLoading: false,
      hasPermission: () => false,
    });

    renderPage();

    // Give React Query a tick to settle.
    await new Promise((r) => setTimeout(r, 100));
    expect(mockGetSummary).not.toHaveBeenCalled();
  });

  it("calls stockUpOperations_GetSummary when user holds warehouse.stock_up.read", async () => {
    mockUsePermissionsContext.mockReturnValue({
      permissions: ["warehouse.stock_up.read"],
      isSuperUser: false,
      groups: [],
      isLoading: false,
      hasPermission: (perm: string) => perm === "warehouse.stock_up.read",
    });

    renderPage();

    await waitFor(() => expect(mockGetSummary).toHaveBeenCalledTimes(1));
  });

  it("does NOT call stockUpOperations_GetSummary while permissions are loading", async () => {
    mockUsePermissionsContext.mockReturnValue({
      permissions: [],
      isSuperUser: false,
      groups: [],
      isLoading: true,
      hasPermission: () => false,
    });

    renderPage();

    await new Promise((r) => setTimeout(r, 100));
    expect(mockGetSummary).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run the test**

Run:
```bash
cd frontend
npx jest src/components/pages/__tests__/TransportBoxList.stockUpGate.test.tsx
```
Expected: all 3 tests PASS (the page gate from Task 5 is already in place).

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/pages/__tests__/TransportBoxList.stockUpGate.test.tsx
git commit -m "test(transport-boxes): assert stock-up summary gate on warehouse.stock_up.read"
```

---

### Task 9: Add Jest test asserting `GiftPackageManufacturing` does not call the API without permission (FR-4 FE, R-A only)

**Files:**
- Create: `frontend/src/components/pages/GiftPackageManufacturing/__tests__/StockUpGate.test.tsx`

**Skip this task if R-B or R-C was selected.**

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/pages/GiftPackageManufacturing/__tests__/StockUpGate.test.tsx`:
```typescript
import React from "react";
import { render, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import GiftPackageManufacturing from "../index";

const mockGetSummary = jest.fn();

jest.mock("../../../../api/client", () => ({
  getAuthenticatedApiClient: () => ({
    stockUpOperations_GetSummary: mockGetSummary,
  }),
  QUERY_KEYS: {
    catalog: ["catalog"],
    stockUpOperations: ["stock-up-operations"],
    giftPackageManufacturing: ["gift-package-manufacturing"],
  },
}));

jest.mock("../../../../api/generated/api-client", () => ({
  StockUpSourceType: { TransportBox: "TransportBox", GiftPackageManufacture: "GiftPackageManufacture" },
  CreateGiftPackageManufactureRequest: jest.fn().mockImplementation((d) => d),
  EnqueueGiftPackageManufactureRequest: jest.fn().mockImplementation((d) => d),
}));

jest.mock("../../../../api/hooks/useGiftPackageManufacturing", () => ({
  useCreateGiftPackageManufacture: () => ({ mutateAsync: jest.fn(), isPending: false }),
  useEnqueueGiftPackageManufacture: () => ({ mutateAsync: jest.fn(), isPending: false }),
}));

jest.mock("../GiftPackageManufacturingList", () => ({
  __esModule: true,
  default: () => null,
}));
jest.mock("../GiftPackageManufacturingDetail", () => () => null);
jest.mock("../../CatalogDetail", () => () => null);
jest.mock("../../../common/StockUpOperationStatusIndicator", () => () => null);
jest.mock("../../../../telemetry/useScreenView", () => ({ useScreenView: () => undefined }));

const mockUsePermissionsContext = jest.fn();
jest.mock("../../../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => mockUsePermissionsContext(),
}));

const renderPage = () => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <GiftPackageManufacturing />
    </QueryClientProvider>,
  );
};

describe("GiftPackageManufacturing — StockUpOperations summary permission gate", () => {
  beforeEach(() => {
    mockGetSummary.mockReset();
    mockGetSummary.mockResolvedValue({ totalInQueue: 0, failedCount: 0 });
  });

  it("does NOT call stockUpOperations_GetSummary when user lacks warehouse.stock_up.read", async () => {
    mockUsePermissionsContext.mockReturnValue({
      permissions: [],
      isSuperUser: false,
      groups: [],
      isLoading: false,
      hasPermission: () => false,
    });

    renderPage();

    await new Promise((r) => setTimeout(r, 100));
    expect(mockGetSummary).not.toHaveBeenCalled();
  });

  it("calls stockUpOperations_GetSummary when user holds warehouse.stock_up.read", async () => {
    mockUsePermissionsContext.mockReturnValue({
      permissions: ["warehouse.stock_up.read"],
      isSuperUser: false,
      groups: [],
      isLoading: false,
      hasPermission: (perm: string) => perm === "warehouse.stock_up.read",
    });

    renderPage();

    await waitFor(() => expect(mockGetSummary).toHaveBeenCalledTimes(1));
  });

  it("does NOT call stockUpOperations_GetSummary while permissions are loading", async () => {
    mockUsePermissionsContext.mockReturnValue({
      permissions: [],
      isSuperUser: false,
      groups: [],
      isLoading: true,
      hasPermission: () => false,
    });

    renderPage();

    await new Promise((r) => setTimeout(r, 100));
    expect(mockGetSummary).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run the test**

Run:
```bash
cd frontend
npx jest src/components/pages/GiftPackageManufacturing/__tests__/StockUpGate.test.tsx
```
Expected: all 3 tests PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/pages/GiftPackageManufacturing/__tests__/StockUpGate.test.tsx
git commit -m "test(gift-package): assert stock-up summary gate on warehouse.stock_up.read"
```

---

### Task 10: Investigate the single 500 (FR-5)

**Files:**
- Modify: `docs/routines/telemetry-anomaly/2026-06-13-stockupoperations-summary-403.md`

- [ ] **Step 1: Run the KQL query for the single 500**

```kusto
requests
| where timestamp between (datetime(2026-06-05) .. datetime(2026-06-12T23:59:59Z))
| where url has "/api/StockUpOperations/summary"
| where resultCode == 500
| project timestamp, id, name, resultCode, duration, customDimensions, operation_Id
| join kind=leftouter (
    exceptions
    | project operation_Id, exceptionType=type, exceptionMessage=outerMessage, exceptionStack=outerType
) on operation_Id
```
Expected: 1 row (per the spec). Capture the exception type, message, and the value of `customDimensions.sourceType` if present.

- [ ] **Step 2: Classify the 500**

Pick exactly one verdict:
- **Trivially adjacent** (e.g., `NullReferenceException` in `GetStockUpOperationsSummaryHandler` reachable via the current request shape): fix inline in this PR. Add tests in the existing handler test project.
- **Non-trivial follow-up** (e.g., DB timeout, malformed `sourceType` enum value, downstream dependency): file a GitHub issue with the captured exception details and link from the investigation doc.
- **Unattributable** (telemetry insufficient — exception missing or operation_Id not joinable): state so in the investigation doc.

- [ ] **Step 3: Append FR-5 findings**

Replace `## FR-5 — Single 500` in the investigation doc with the chosen verdict + evidence + (if applicable) the follow-up issue link. Example:
```markdown
## FR-5 — Single 500

- Timestamp: <ts>
- Exception: <type> — <outerMessage>
- sourceType at the time: <value>
- Verdict: <trivially-adjacent / non-trivial-follow-up / unattributable>
- <Inline fix commit | Follow-up issue link | "telemetry insufficient" note>
```

- [ ] **Step 4: Commit the FR-5 finding (and any inline fix if chosen)**

```bash
git add docs/routines/telemetry-anomaly/2026-06-13-stockupoperations-summary-403.md
git commit -m "docs(telemetry): record FR-5 finding for StockUpOperations/summary 500"
```

---

### Task 11: Add cross-link from the feature documentation (NFR-3)

**Files:**
- Modify: `docs/routines/telemetry-anomaly/2026-06-13-stockupoperations-summary-403.md` (final block)

- [ ] **Step 1: Append the observability checklist to the investigation doc**

Append to the bottom of `docs/routines/telemetry-anomaly/2026-06-13-stockupoperations-summary-403.md`:
```markdown
## NFR-3 — Post-deploy observability

After this change is deployed to production, run the following KQL daily for 3 days:

```kusto
requests
| where timestamp > ago(24h)
| where url has "/api/StockUpOperations/summary"
| summarize calls = count(), forbidden = countif(resultCode == 403), ok = countif(resultCode == 200)
```

Expected: `forbidden` drops to near-zero (single-digit at most, from edge cases like users mid-permission-revocation).
If `forbidden` stays high: caller attribution (FR-2) missed a principal; re-run with the broader window and reconsider the chosen path.
```

- [ ] **Step 2: Commit**

```bash
git add docs/routines/telemetry-anomaly/2026-06-13-stockupoperations-summary-403.md
git commit -m "docs(telemetry): add NFR-3 post-deploy observability check"
```

---

### Task 12: Full project validation before declaring complete

**Files:** none (validation only)

- [ ] **Step 1: Run backend build and format**

Run:
```bash
cd backend
dotnet build
dotnet format --verify-no-changes
```
Expected: both succeed.

- [ ] **Step 2: Run the touched backend tests**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~StockUpOperationsControllerAuthorizationTests"
```
Expected: 4 PASS (R-A) or 5 PASS (R-B).

- [ ] **Step 3: Run frontend build and lint**

Run:
```bash
cd frontend
npm run build
npm run lint
```
Expected: both succeed. The build also regenerates the OpenAPI TypeScript client; confirm the generated `api-client.ts` is unchanged (NFR-4) — `git diff frontend/src/api/generated/api-client.ts` should produce empty output.

- [ ] **Step 4: Run the touched frontend tests**

Run:
```bash
cd frontend
npx jest \
  src/api/hooks/__tests__/useStockUpOperationsSummary.test.ts \
  src/components/pages/__tests__/TransportBoxList.stockUpGate.test.tsx \
  src/components/pages/GiftPackageManufacturing/__tests__/StockUpGate.test.tsx \
  src/components/pages/__tests__/TransportBoxList.test.tsx
```
Expected: all PASS. The pre-existing `TransportBoxList.test.tsx` must continue to pass — it already mocks `useStockUpOperationsSummary`, which means it is insulated from the new options bag, but a regression there would surface here.

- [ ] **Step 5: Final commit (if any incidental fixups landed during validation)**

If steps 1–4 surfaced no changes, skip. If any small fixups were needed (formatter, lint), commit them now:
```bash
git status
git diff
# if non-empty:
git add -A
git commit -m "chore: validation fixups"
```

- [ ] **Step 6: Update the PR description with the remediation summary**

The PR description should include (FR-3 acceptance criteria):
- The chosen path (R-A / R-B / R-C) and a one-line justification citing the FR-2 table.
- A link to the investigation doc at `docs/routines/telemetry-anomaly/2026-06-13-stockupoperations-summary-403.md`.
- The expected after-fix telemetry behavior (NFR-3) and the day-3 verification commitment.
- The FR-5 verdict (fix inline / follow-up issue link / unattributable).

---

## Self-Review Notes

**Spec coverage check:**
- FR-1 (verify route + gate): Task 1.
- FR-2 (identify 403 callers): Task 2.
- FR-3 (apply remediation): Task 3 (decision), Task 5 (R-A code change or R-B BE change), Task 6 (second R-A callsite). R-C is the "documentation only" path completed by Tasks 1–3 + 10–12.
- FR-4 (regression detection): Task 7 (BE reflection test), Task 8 (FE test for TransportBoxList), Task 9 (FE test for GiftPackageManufacturing).
- FR-5 (single 500): Task 10.
- NFR-1 (performance): No code path changes for users who hold the permission; polling cadence unchanged. No task needed.
- NFR-2 (security): R-A leaves the server gate intact; R-B is bounded by Task 7's regression test asserting write actions stay at Write level.
- NFR-3 (observability + one-line note): Task 11 + investigation doc completed throughout.
- NFR-4 (BE/FE backwards compat): Task 12 step 3 verifies generated client unchanged.

**Specification Amendments from arch-review.r1.md (applied):**
- FR-4 BE test uses reflection (Task 7), not `WebApplicationFactory`.
- R-A gates at the callsite via `{ enabled }` option on the hook (Task 4 hook signature, Tasks 5/6 callsites).
- FR-4 FE test asserts the API client method is not invoked (Tasks 8/9, using a `jest.fn()` spy on `stockUpOperations_GetSummary`).
- FR-2 fallback: when attribution is impossible, the decision matrix in Task 3 defaults to R-A.
- Permission-string drift: inline comment in each callsite (Tasks 5/6) referencing `AccessRoles.WarehouseStockUpRead`.

**Type/signature consistency check:**
- `useStockUpOperationsSummary(sourceType?, options?)` with `options.enabled` — defined in Task 4, used identically in Tasks 5 and 6.
- `usePermissionsContext()` returns `{ hasPermission, isLoading, ... }` — matches `PermissionsContext.tsx:22-29` and is consumed the same way in Tasks 5, 6, 8, 9.
- `Feature.Warehouse_StockUp`, `AccessLevel.Read`, `AccessLevel.Write` — match the existing controller (`StockUpOperationsController.cs:12, 76, 95`) and `FeatureAuthorizeAttribute` shape used in Task 7.
- Permission literal `'warehouse.stock_up.read'` — used identically in Tasks 5, 6, 8, 9 (and matches `AccessRoles.generated.cs:43` per the arch-review).

**Placeholder scan:** No `TBD` / `TODO` / "implement later" / "similar to Task N" / "add appropriate error handling" patterns remain in any executable step. The `<TBD by Task N>` markers in the investigation doc are intentional content that subsequent tasks rewrite.
