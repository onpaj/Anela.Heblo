# Architecture Review: 403 storm on `GET /api/StockUpOperations/summary`

## Skip Design: true

This is an authorization defect investigation and remediation. No new visual components, layouts, or design decisions are required. The R-A path conditionally suppresses an existing indicator; the R-B path moves a class-level attribute; the R-C path is documentation only. None of these introduce visual changes that need UI/UX review.

## Architectural Fit Assessment

The feature fits cleanly into existing patterns and requires no new abstractions.

**Server-side authorization** is already centralized in `Anela.Heblo.Domain.Features.Authorization.FeatureAuthorizeAttribute` (`backend/src/Anela.Heblo.Domain/Features/Authorization/FeatureAuthorizeAttribute.cs:6`), which wraps `AuthorizeAttribute` and resolves roles via the generated `AccessRoles.For(Feature, AccessLevel)` mapping (`AccessRoles.generated.cs:103`). The controller in question (`backend/src/Anela.Heblo.API/Controllers/StockUpOperationsController.cs:12`) already uses this — class-level `[FeatureAuthorize(Feature.Warehouse_StockUp)]` (Read), with write actions overriding at method level. Any remediation in R-B should follow the same attribute layering pattern.

**Client-side permission checks** flow through `usePermissionsContext()` exposed by `frontend/src/auth/PermissionsContext.tsx:34`, which is already consumed by several pages (`MarketingFeedbackPage`, `ExpeditionListArchivePage`, `PhotobankPage`, etc.) with `hasPermission('warehouse.stock_up.read')`-style gating. R-A must reuse this hook — no new permission abstraction is needed.

**Authorization regression tests** in this codebase do NOT use `WebApplicationFactory` for 403/200 distinction, despite the spec's wording. `HebloWebApplicationFactory` registers `MockAuthenticationHandler` (`backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs:39`) which **always** claims `AccessRoles.SuperUser`, making it impossible to assert 403 over HTTP without a non-trivial test-auth override. The two existing authorization-tests-by-controller (`DashboardControllerAuthorizationTests`, `GridLayoutsControllerAuthorizationTests`) instead use **reflection** to assert the attribute contract directly. This is the established pattern and FR-4 should follow it. See **Specification Amendments** below.

The single integration point worth flagging: the `useStockUpOperationsSummary` hook is shared between two pages that may have different permission audiences. R-A must gate at the **hook callsite** (page level), not inside the hook itself, because gating inside the hook would couple the hook to one feature when its design is reusable.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                       BACKEND (no structural change)                    │
│                                                                         │
│  [FeatureAuthorize(Warehouse_StockUp)] ─class─level─attr                │
│              │                                                          │
│              ▼                                                          │
│  StockUpOperationsController                                            │
│    ├─ GET   /api/StockUpOperations           (Read — class attr)        │
│    ├─ GET   /api/StockUpOperations/summary   (Read — class attr) ◄── focus
│    ├─ POST  /api/StockUpOperations/{id}/retry   (Write — method attr)   │
│    └─ POST  /api/StockUpOperations/{id}/accept  (Write — method attr)   │
│                                                                         │
│   In R-B only: a method-level attribute on GetSummary lowers the gate.  │
└─────────────────────────────────────────────────────────────────────────┘
                                  ▲ HTTP 200/403
                                  │
┌─────────────────────────────────────────────────────────────────────────┐
│                              FRONTEND                                   │
│                                                                         │
│  PermissionsProvider ──► usePermissionsContext()                        │
│                                  │                                      │
│         ┌────────────────────────┼────────────────────────┐             │
│         ▼                        ▼                        ▼             │
│  TransportBoxList         GiftPackageManufacturing   (other pages)      │
│         │                        │                                      │
│   (R-A) hasPermission check      (R-A) hasPermission check              │
│         │                        │                                      │
│         ▼                        ▼                                      │
│  useStockUpOperationsSummary(sourceType)  — polls every 15s             │
│         │                                                               │
│         ▼                                                               │
│  ApiClient.stockUpOperations_GetSummary()                               │
└─────────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Gate at the callsite, not in the hook
**Options considered:**
- (a) Embed `hasPermission('warehouse.stock_up.read')` inside `useStockUpOperationsSummary` and short-circuit `enabled: false`.
- (b) Gate at the page (callsite) — call the hook only when the page has already established the user has permission.

**Chosen approach:** (b). The page reads `usePermissionsContext()` and only invokes `useStockUpOperationsSummary` when `hasPermission('warehouse.stock_up.read')` is true. The dependent `StockUpOperationStatusIndicator` is also hidden in the same condition.

**Rationale:** The hook itself is a generic data-fetch hook; coupling it to one feature permission would bleed authz policy into the data layer and force future callers to inherit the same gate. Callsite gating mirrors how `MarketingFeedbackPage` / `ExpeditionListArchivePage` already enforce permissions. Conditional hook invocation is safe because React requires hooks to be unconditional only when ordering changes; here both pages call the hook in a stable position — the conditional applies to `enabled`-style behavior, which we express through the `enabled` option of `useQuery` (preferred over not calling the hook at all, to preserve hook order across renders).

**Concrete pattern:**
```typescript
const { hasPermission, isLoading: permsLoading } = usePermissionsContext();
const canSeeStockUp = hasPermission('warehouse.stock_up.read');

const { data: stockUpSummary } = useStockUpOperationsSummary(
  StockUpSourceType.TransportBox,
  { enabled: !permsLoading && canSeeStockUp }   // new option on the hook
);
```
This requires a small additive change to `useStockUpOperationsSummary` to accept an `{ enabled?: boolean }` options bag and forward it to `useQuery`. The hook stays generic — the gate decision lives at the page.

#### Decision 2: Use reflection-based attribute tests, not HTTP integration tests, for FR-4
**Options considered:**
- (a) Spec's literal reading: xUnit + `WebApplicationFactory` test asserting 403 for unauthorized principal, 200 for authorized.
- (b) Reflection-based attribute test asserting the `[FeatureAuthorize]` contract on the controller/action.
- (c) Build a new test auth scheme that issues role-restricted principals, then write (a).

**Chosen approach:** (b).

**Rationale:** `MockAuthenticationHandler` issues `AccessRoles.SuperUser` for every request — option (a) cannot produce a 403 today without first building (c). (c) is real infrastructure work (new auth scheme, test fixture for "user without role X", deprecation risk for other tests), out of scope for resolving a 403 storm. (b) matches the existing pattern in `DashboardControllerAuthorizationTests` (attribute should be **absent**) and `GridLayoutsControllerAuthorizationTests` (attribute should be **absent**) and gives the same regression coverage: any future PR that changes the class-level attribute on `StockUpOperationsController` or accidentally bypasses it on `GetSummary` will fail the test.

The spec FR-4 wording should be amended to reflect this (see **Specification Amendments**).

#### Decision 3: Single source of truth for the permission string
**Options considered:**
- (a) Frontend hardcodes the string `'warehouse.stock_up.read'`.
- (b) Frontend generates/imports the string from a shared constant.

**Chosen approach:** (a) — hardcode, consistent with how `MarketingFeedbackPage`, `ExpeditionListArchivePage`, and similar pages already use string literals against `usePermissionsContext().hasPermission(...)`.

**Rationale:** The backend already generates `AccessRoles.WarehouseStockUpRead = "warehouse.stock_up.read"` (`AccessRoles.generated.cs:43`), but there is no existing pipeline to sync that constant to the frontend, and other frontend gates use literals. Introducing a sync mechanism is out of scope. The reflection-based BE test in FR-4 will catch the string mismatch indirectly: if backend roles ever change, the controller test asserts the attribute on the controller and the existing `GateConsistencyTests` enforces the matrix; the frontend literal mismatch would surface as a continued 403 in telemetry. Document the literal next to its usage with a `// Backend: AccessRoles.WarehouseStockUpRead` comment in the affected pages.

## Implementation Guidance

### Directory / Module Structure

**Backend — no new files** for R-A and R-C. R-B would change only the attribute placement on `backend/src/Anela.Heblo.API/Controllers/StockUpOperationsController.cs`.

**Backend test (FR-4) — one new file:**
```
backend/test/Anela.Heblo.Tests/Authorization/
  └── StockUpOperationsControllerAuthorizationTests.cs    ← NEW
```
Mirror the structure of `DashboardControllerAuthorizationTests.cs` and `GridLayoutsControllerAuthorizationTests.cs`.

**Frontend (R-A only) — modify existing files, no new files:**
```
frontend/src/api/hooks/useStockUpOperations.ts                          ← MODIFY (add `enabled` option)
frontend/src/components/pages/TransportBoxList.tsx                       ← MODIFY (gate callsite)
frontend/src/components/pages/GiftPackageManufacturing/index.tsx         ← MODIFY (gate callsite)
```

**Frontend test (FR-4, R-A only) — co-located unit tests:**
```
frontend/src/components/pages/__tests__/TransportBoxList.stockUpGate.test.tsx                   ← NEW
frontend/src/components/pages/GiftPackageManufacturing/__tests__/StockUpGate.test.tsx          ← NEW
```
Each test mocks `usePermissionsContext` (mirror the existing `PhotobankPage.selection.test.tsx` and `ExpeditionListArchivePage.test.tsx` patterns) and asserts that the underlying API client method (`stockUpOperations_GetSummary`) is not invoked when the user lacks the permission.

### Interfaces and Contracts

**Hook signature change (additive, backward-compatible):**
```typescript
// frontend/src/api/hooks/useStockUpOperations.ts
export interface UseStockUpOperationsSummaryOptions {
  enabled?: boolean;
}

export const useStockUpOperationsSummary = (
  sourceType?: StockUpSourceType,
  options?: UseStockUpOperationsSummaryOptions
) => {
  return useQuery({
    queryKey: stockUpOperationsKeys.summary(sourceType),
    queryFn: async (): Promise<GetStockUpOperationsSummaryResponse> => { /* unchanged */ },
    enabled: options?.enabled ?? true,    // ← new
    refetchInterval: 15000,
    refetchOnWindowFocus: true,
    staleTime: 14000,
    gcTime: 60000,
    retry: 1,
  });
};
```
Default `enabled: true` preserves all existing call sites if any future caller forgets to pass the option (defensive default; the FR-4 unit tests are the authoritative regression check at the two known callsites).

**Backend test contract (reflection-based, R-A/R-C):**
```csharp
namespace Anela.Heblo.Tests.Authorization;

public class StockUpOperationsControllerAuthorizationTests
{
    [Fact]
    public void StockUpOperationsController_IsGatedByWarehouseStockUpRead()
    {
        var attr = typeof(StockUpOperationsController)
            .GetCustomAttribute<FeatureAuthorizeAttribute>();

        attr.Should().NotBeNull();
        attr!.Feature.Should().Be(Feature.Warehouse_StockUp);
        attr.Level.Should().Be(AccessLevel.Read);
    }

    [Fact]
    public void GetSummary_HasNoOverridingAuthorizeAttribute()
    {
        var method = typeof(StockUpOperationsController).GetMethod(nameof(StockUpOperationsController.GetSummary))!;
        method.GetCustomAttributes<AuthorizeAttribute>(inherit: false).Should().BeEmpty(
            "the class-level Warehouse_StockUp Read gate is the authoritative gate for the summary endpoint");
    }

    [Theory]
    [InlineData(nameof(StockUpOperationsController.RetryOperation))]
    [InlineData(nameof(StockUpOperationsController.AcceptOperation))]
    public void WriteActions_RemainGatedAtWriteLevel(string methodName)
    {
        var attr = typeof(StockUpOperationsController).GetMethod(methodName)!
            .GetCustomAttribute<FeatureAuthorizeAttribute>();
        attr.Should().NotBeNull();
        attr!.Level.Should().Be(AccessLevel.Write);
    }
}
```
For R-B, the second test is inverted to assert the broadened method-level attribute and that write actions still carry their Write-level attribute.

### Data Flow

**Today (broken for unauthorized callers):**
1. Page mounts → `useStockUpOperationsSummary` always invoked.
2. React Query fires `GET /api/StockUpOperations/summary` immediately and every 15s.
3. ASP.NET Core authorization filter evaluates `[FeatureAuthorize(Warehouse_StockUp)]`.
4. Caller lacks the role → 403, logged in App Insights as `GET /api/StockUpOperations/summary` (raw URL, because action selection was short-circuited).
5. Repeats every 15s while the page is open.

**After R-A:**
1. Page mounts → reads `usePermissionsContext()`.
2. While `isLoading` true: hook disabled, no fetch.
3. When permissions resolve: hook runs only if `hasPermission('warehouse.stock_up.read')`.
4. If false: no HTTP request is ever issued. The `StockUpOperationStatusIndicator` is hidden.
5. If true: existing behavior — 200 every 15s.

**After R-B:** server changes only; the hook is unchanged. 403 turns into 200 for the broadened audience.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| FR-2 (App Insights query) cannot attribute callers due to `user_AuthenticatedId` being null/anonymous | Medium | If attribution fails, default to **R-A** — it is a no-loss UX/cost optimization for any caller without the role, and it leaves the server gate intact for whoever does have it. Document the inability to attribute. |
| Hardcoded permission string in frontend drifts from `AccessRoles.WarehouseStockUpRead` | Medium | Inline comment `// Backend: AccessRoles.WarehouseStockUpRead` at each callsite; rely on telemetry (post-deploy NFR-3) to surface a regression within 24h. Out of scope to build a generated-constants sync. |
| Conditional `enabled: false` masks a legitimate backend regression — page silently shows no indicator | Low | The indicator is already conditionally rendered on data presence (`stockUpSummary && ...`), so the "no indicator" state is already a valid UI. Backend regressions still surface in App Insights for users who DO have the permission. |
| `MockAuthenticationHandler` issuing super_user makes future HTTP-level 403 testing harder | Low | Out of scope. Reflection-based attribute tests give equivalent regression coverage and match the existing pattern. If real 403 HTTP tests are wanted later, that's a separate piece of test infrastructure. |
| R-B (broadening) — accidentally also broadening write endpoints | High | Test asserts (in Theory above) that `RetryOperation` and `AcceptOperation` retain method-level `[FeatureAuthorize(..., Write)]`. The class-level attribute change does not affect them because their explicit method attribute wins for role evaluation. |
| Telemetry quirk (dual operation_name rows) gets "fixed" by future work, breaking the assumption baked into FR-2 | Low | Out of scope. The spec already calls this out; document the assumption in the PR. |
| FR-5 (single 500) turns out to be a real defect in `GetStockUpOperationsSummaryHandler` | Low/Medium | If a defect surfaces during investigation but is non-trivial, file a follow-up issue and link in the PR (matches FR-5 acceptance criteria). Do not block the 403 remediation on it. |

## Specification Amendments

1. **FR-4, BE test — replace "xUnit + `WebApplicationFactory`" with "xUnit + reflection".** The codebase pattern for "assert a controller's authorization contract" is reflection-based (see `DashboardControllerAuthorizationTests`, `GridLayoutsControllerAuthorizationTests`), and `MockAuthenticationHandler` does not support HTTP-level 403 assertions without building new test infrastructure that is out of scope. New wording: *"An xUnit reflection test under `backend/test/Anela.Heblo.Tests/Authorization/StockUpOperationsControllerAuthorizationTests.cs` asserting the class-level `[FeatureAuthorize(Feature.Warehouse_StockUp)]` attribute (Read) is present and that `GetSummary` has no overriding `[Authorize]` / `[FeatureAuthorize]` attribute that broadens or narrows the gate. Write-level actions (`RetryOperation`, `AcceptOperation`) must still carry their method-level Write attribute."*

2. **FR-3 R-A — clarify the gating mechanism.** Specify that gating happens at the **callsite** (page), not inside the hook, via an additive `{ enabled?: boolean }` options bag on `useStockUpOperationsSummary`, with the `enabled` flag derived from `usePermissionsContext().hasPermission('warehouse.stock_up.read')` while `isLoading` is false.

3. **FR-4 FE test — name the assertion target precisely.** Add: *"Mock `usePermissionsContext` to return `hasPermission: () => false` and `isLoading: false`. Render the page. Assert that the API client method `stockUpOperations_GetSummary` is not called (e.g., via `jest.fn()` spy on the client factory)."*

4. **FR-2 fallback — when attribution is impossible, default to R-A.** Add: *"If App Insights cannot identify the caller(s) (anonymous, or all rows have null `user_AuthenticatedId`), default to R-A. R-A is correct under the worst case (legitimate caller is unidentified but has the role) because the gate is unchanged for that caller, and the cost saving for any unauthorized caller is preserved."*

5. **Frontend permission constant note.** Add to Out of Scope: *"Generating a shared TypeScript constants file from the backend's `AccessRoles.generated.cs`. The frontend literal `'warehouse.stock_up.read'` is annotated with a comment pointing to the backend constant. Telemetry monitoring per NFR-3 is the regression check for drift."*

## Prerequisites

1. **App Insights access** for FR-2 caller attribution (existing routine at `docs/routines/telemetry-anomaly/`).
2. **Local backend build** working with `dotnet build` + `dotnet format` (no migrations or infra changes required).
3. **Local frontend build** working with `npm run build` + `npm run lint`; React Query and the existing `PermissionsContext` are already in place.
4. **No DB migrations.** The endpoint is read-only and the permission model is unchanged.
5. **No OpenAPI regeneration** needed for R-A or R-C. R-B does not change the route, method, query parameters, or response shape — only an attribute placement — so the generated client also stays unchanged (NFR-4 holds).
6. **No new test infrastructure** beyond mirroring the existing reflection-based authorization-test pattern. Verify before starting that `Anela.Heblo.Domain.Features.Authorization.FeatureAuthorizeAttribute`, `Feature.Warehouse_StockUp`, and `AccessLevel.Read` are still exported as referenced in this review (`backend/src/Anela.Heblo.Domain/Features/Authorization/FeatureAuthorizeAttribute.cs:6`).