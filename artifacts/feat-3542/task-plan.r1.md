# Task Plan: Fix Transport Box E2E failures (E2E permission gap + broken nav fallback)

## Overview

The 18 nightly E2E failures across `transport/box-receive`, `transport/box-creation`, `transport/boxes-basic`, and `transport/box-management` are caused by two independent, low-risk bugs plus one robustness gap, all confirmed against the live code in this worktree:

1. The synthetic E2E service-principal user (`E2ESessionService.CreateSyntheticUserClaims`) is never granted the `Warehouse_Logistics` role that `TransportBoxController` and the frontend route guard (`RequireMenuPath`/`Sidebar`) both require, so every Transport Box page/API call for the E2E user gets redirected or 403s.
2. `frontend/test/e2e/helpers/e2e-auth-helper.ts`'s `navigateToTransportBoxReceive` falls back to a URL (`/warehouse/transport-box-receive`) that has no matching route in `App.tsx`, so the page renders nothing at all (no `<main>`) when the UI nav path fails.
3. `TransportBoxList.tsx`'s error branch is a secondary robustness gap: any query error (403 today, but any 5xx/timeout tomorrow) blanks the entire page, including the header and "Otevřít nový box" button that E2E specs assert on.

This plan has four tasks. Task 1 (FR-1) grants the E2E user the two Warehouse_Logistics roles and adds a new backend test file. Task 2 (FR-2) fixes the one incorrect fallback URL. Task 3 (FR-3) hardens `TransportBoxList`'s error branch so page chrome survives API failures, with a new component test. Task 4 (FR-4, recommended but not blocking) adds one strict assertion to each of the three currently all-lenient E2E specs so a regression like this one fails loudly instead of silently "passing." Tasks 1–3 are independent of each other and can be done in any order; Task 4 should land after 1–3 are verified against staging (per the architecture review's risk mitigation — landing it first could surface unrelated latent issues in specs that were never asserting anything before). Every task ends with its own build/test verification and its own commit.

### task: grant-e2e-warehouse-logistics-roles

**Goal (FR-1, per spec + architect's Specification Amendment 1):** Grant the E2E synthetic user BOTH `AccessRoles.WarehouseLogisticsRead` and `AccessRoles.WarehouseLogisticsWrite` — unconditionally, not just Read. This is not a judgment call: `box-creation.spec.ts` performs `POST /api/transport-boxes` and `box-receive.spec.ts` performs `POST .../open-by-code` / `PUT .../state`, all of which are guarded by `[FeatureAuthorize(Feature.Warehouse_Logistics, AccessLevel.Write)]` on `TransportBoxController` (confirmed at `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs` lines 81, 96, 116, 137, 161, 201 — every write action requires Write; only the class-level Read gate at line 19 covers GET endpoints).

**File to modify:** `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs`

Current `CreateSyntheticUserClaims` method body (lines 70–92):

```csharp
    public Claim[] CreateSyntheticUserClaims(string environmentName)
    {
        _logger.LogDebug("E2E Session: Creating synthetic user claims for environment: {Environment}", environmentName);

        return new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "e2e-test-user-id"),
            new Claim(ClaimTypes.Name, "E2E Test User"),
            new Claim(ClaimTypes.Email, "e2e-test@anela-heblo.com"),
            new Claim("preferred_username", "e2e-test@anela-heblo.com"),
            new Claim("name", "E2E Test User"),
            new Claim("given_name", "E2E"),
            new Claim("family_name", "Test"),
            new Claim("oid", "e2e-test-object-id"),
            new Claim("tid", environmentName), // Use environment as tenant for testing
            new Claim(ClaimTypes.Role, AccessRoles.Base), // Base role for application access
            new Claim("scp", "access_as_user"),
            // Grant the finance overview read role so E2E tests can reach /api/FinancialOverview.
            // FeatureAuthorize checks the role claim (permission strings were renamed away from the
            // old "FinancialOverview.View" form), so a stale "permission" claim no longer matches.
            new Claim(ClaimTypes.Role, AccessRoles.FinanceFinancialOverviewRead)
        };
    }
```

Use the Edit tool with this exact `old_string`/`new_string` pair (only the last two lines of the array change):

old_string:
```csharp
            // Grant the finance overview read role so E2E tests can reach /api/FinancialOverview.
            // FeatureAuthorize checks the role claim (permission strings were renamed away from the
            // old "FinancialOverview.View" form), so a stale "permission" claim no longer matches.
            new Claim(ClaimTypes.Role, AccessRoles.FinanceFinancialOverviewRead)
        };
```

new_string:
```csharp
            // Grant the finance overview read role so E2E tests can reach /api/FinancialOverview.
            // FeatureAuthorize checks the role claim (permission strings were renamed away from the
            // old "FinancialOverview.View" form), so a stale "permission" claim no longer matches.
            new Claim(ClaimTypes.Role, AccessRoles.FinanceFinancialOverviewRead),
            // Grant Warehouse_Logistics Read+Write so E2E tests can reach the Transport Box pages
            // and API (TransportBoxController requires Read at the class level and Write on every
            // create/receive/state-change action). Read alone unlocks navigation
            // (RequireMenuPath/Sidebar gate on Feature.Warehouse_Logistics) but every write-triggering
            // E2E interaction — box-creation.spec.ts (POST /api/transport-boxes) and
            // box-receive.spec.ts (POST .../open-by-code, PUT .../state) — needs Write too.
            new Claim(ClaimTypes.Role, AccessRoles.WarehouseLogisticsRead),
            new Claim(ClaimTypes.Role, AccessRoles.WarehouseLogisticsWrite)
        };
```

**Do not touch:** `access-matrix.json`, `AccessRoles.generated.cs`, `AccessMatrix.generated.cs`, or `frontend/src/auth/accessMatrix.generated.ts`. These are auto-generated (the first three lines of `AccessRoles.generated.cs` say "AUTO-GENERATED... Do not edit by hand") and already define `WarehouseLogisticsRead = "warehouse.logistics.read"` and `WarehouseLogisticsWrite = "warehouse.logistics.write"` (confirmed at `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs` lines 37–38). `E2ESessionService` only *consumes* these constants — it is hand-written application code, not generated.

**Test-first: write the failing test before making the change above.**

Create a new file `backend/test/Anela.Heblo.Tests/Authorization/E2ESessionServiceTests.cs` (this file does not currently exist — confirmed by directory listing of `backend/test/Anela.Heblo.Tests/Authorization/`). This test project already has a `ProjectReference` to `Anela.Heblo.API.csproj` (confirmed in `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`), so `E2ESessionService` is directly constructible. Follow the existing pattern for instantiating a service with a `NullLogger<T>` (see `backend/test/Anela.Heblo.Tests/Marketing/OutlookCalendarSyncServiceTests.cs` line 55) and the pattern for referencing API-layer types directly in this test namespace (see `backend/test/Anela.Heblo.Tests/Authorization/DashboardControllerAuthorizationTests.cs`, which does `using Anela.Heblo.API.Controllers;`).

Write the full file content exactly as follows:

```csharp
using System.Security.Claims;
using Anela.Heblo.API.Infrastructure.Authentication;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class E2ESessionServiceTests
{
    private static E2ESessionService CreateSut() =>
        new E2ESessionService(NullLogger<E2ESessionService>.Instance);

    [Fact]
    public void CreateSyntheticUserClaims_IncludesWarehouseLogisticsReadAndWrite()
    {
        var sut = CreateSut();

        var claims = sut.CreateSyntheticUserClaims("Staging");

        claims.Should().Contain(c =>
            c.Type == ClaimTypes.Role && c.Value == AccessRoles.WarehouseLogisticsRead);
        claims.Should().Contain(c =>
            c.Type == ClaimTypes.Role && c.Value == AccessRoles.WarehouseLogisticsWrite);
    }

    [Fact]
    public void CreateSyntheticUserClaims_StillIncludesExistingRoles_RegressionGuard()
    {
        var sut = CreateSut();

        var claims = sut.CreateSyntheticUserClaims("Staging");

        claims.Should().Contain(c =>
            c.Type == ClaimTypes.Role && c.Value == AccessRoles.Base);
        claims.Should().Contain(c =>
            c.Type == ClaimTypes.Role && c.Value == AccessRoles.FinanceFinancialOverviewRead);
    }

    [Fact]
    public void CreateSyntheticUserClaims_IncludesIdentityClaims()
    {
        var sut = CreateSut();

        var claims = sut.CreateSyntheticUserClaims("Staging");

        claims.Should().Contain(c =>
            c.Type == ClaimTypes.NameIdentifier && c.Value == "e2e-test-user-id");
        claims.Should().Contain(c =>
            c.Type == ClaimTypes.Email && c.Value == "e2e-test@anela-heblo.com");
    }
}
```

**Run the test before the fix (must fail on the first assertion):**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~E2ESessionServiceTests"
```

Expected output before the fix: `CreateSyntheticUserClaims_IncludesWarehouseLogisticsReadAndWrite` FAILS (the claim array does not yet contain `WarehouseLogisticsRead`/`WarehouseLogisticsWrite`); the other two tests pass.

**Now apply the `E2ESessionService.cs` edit above**, then re-run the same command. Expected output after the fix: all 3 tests pass, e.g. `Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3`.

**Full validation:**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece/backend
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Authorization"
```

All Authorization-namespace tests (including the pre-existing `FeatureAuthorizeAttributeTests`, `AccessMatrixTests`, `AccessMatrixJsonTests`, etc.) must continue to pass — this change does not touch any generated file or shared authorization infrastructure, only the E2E-only claim list.

**Commit:**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece
git add backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs backend/test/Anela.Heblo.Tests/Authorization/E2ESessionServiceTests.cs
git commit -m "$(cat <<'EOF'
Grant E2E test user Warehouse_Logistics Read+Write roles

The E2E synthetic user only held Base and FinanceFinancialOverviewRead,
so every Transport Box page/API call was silently redirected (frontend
RequireMenuPath gate) or 403'd (TransportBoxController's
FeatureAuthorize(Warehouse_Logistics)). Both Read and Write are granted
because box-creation.spec.ts and box-receive.spec.ts perform real write
actions (create/open-by-code/state-change) that require the Write role.

Fixes 12 of 18 nightly transport E2E failures reported in run #191.
EOF
)"
```

---

### task: fix-e2e-transport-box-receive-fallback-url

**Goal (FR-2):** `navigateToTransportBoxReceive`'s direct-navigation fallback currently goes to `/warehouse/transport-box-receive`, a URL with **no matching `<Route>`** anywhere in `App.tsx` (confirmed: the only registered route for this page is `/logistics/receive-boxes` at `frontend/src/App.tsx:437`, and there is no wildcard/catch-all `<Route path="*">` in the file). Navigating to an unmatched URL renders nothing at all — no `Layout`, no `<main>` — which is the distinct `locator('main, [role="main"]')` timeout unique to `box-receive.spec.ts` (6 of the 18 failures). Fix the one literal string.

**File to modify:** `frontend/test/e2e/helpers/e2e-auth-helper.ts`

Current code at line 314 (inside `navigateToTransportBoxReceive`, in the "UI navigation failed, fall back to direct URL" path):

```typescript
  // If UI navigation fails, go directly to the path
  console.log('🔄 Trying direct navigation to transport box receive...');
  const baseUrl = process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
  await page.goto(`${baseUrl}/warehouse/transport-box-receive`);
  await page.waitForLoadState('domcontentloaded');
  await waitForPageLoad(page);

  console.log('✅ Direct navigation to transport box receive completed');
}
```

Use the Edit tool with this exact `old_string`/`new_string` pair:

old_string:
```typescript
  // If UI navigation fails, go directly to the path
  console.log('🔄 Trying direct navigation to transport box receive...');
  const baseUrl = process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
  await page.goto(`${baseUrl}/warehouse/transport-box-receive`);
```

new_string:
```typescript
  // If UI navigation fails, go directly to the path
  console.log('🔄 Trying direct navigation to transport box receive...');
  const baseUrl = process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
  await page.goto(`${baseUrl}/logistics/receive-boxes`);
```

This is the only line that changes. `/logistics/receive-boxes` matches the registered route (`frontend/src/App.tsx:437`: `<Route path="/logistics/receive-boxes" element={guard("/logistics/receive-boxes", <TransportBoxReceivePage />)} />`) and the sidebar's own `href` (`frontend/src/components/Layout/Sidebar.tsx:273`: `href: "/logistics/receive-boxes"`).

**Spot-check for the same class of bug in the rest of the file (per FR-2's acceptance criteria — do not skip this check):** Read through every other `navigateTo*` function in `frontend/test/e2e/helpers/e2e-auth-helper.ts` and confirm each fallback URL matches a real registered route in `frontend/src/App.tsx`:
- `navigateToTransportBoxes` (line 221): `/logistics/transport-boxes` — matches `App.tsx`. No change needed.
- `navigateToCatalog` (line 256): `/catalog` — matches. No change needed.
- `navigateToStockOperations` (line 270): `/stock-up-operations` — matches. No change needed.
- `navigateToInvoiceClassification` (line 358): `/purchase/invoice-classification` — matches. No change needed.
- `navigateToIssuedInvoices` (line 402): `/customer/issued-invoices` — matches. No change needed.
- `navigateToMarketingCalendar` (line 451): `/marketing/calendar` — matches. No change needed.

Confirm this by running:

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece
grep -n "page.goto(\`\${baseUrl}" frontend/test/e2e/helpers/e2e-auth-helper.ts
grep -n "<Route path=" frontend/src/App.tsx
```

Cross-check each `goto` path against the `Route path=` list; every one but the one just fixed should already have a match. If you find another mismatch, fix it the same way and note it in the commit message — but as of this investigation (verified against the current worktree) none was found besides the one fixed above.

**There is no dedicated unit test for this helper file** (it is a Playwright E2E helper, exercised only by the E2E specs themselves against a live staging deploy — see NFR-1). Verification for this task is:
1. TypeScript compiles cleanly (this file is included in the frontend's `tsc` pass via `npm run build`).
2. The full functional verification happens in NFR-1's staging E2E run, which is covered as part of the overall feature's final validation (after Tasks 1–3 land) — see the plan's Overview. Do not attempt to run `box-receive.spec.ts` against staging in isolation for this task alone unless Task 1 (the role grant) has already been deployed to staging, since the fallback URL fix alone still lands the E2E user on a route that will redirect to `/` without FR-1.

**Validation commands:**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece/frontend
npx tsc --noEmit -p test/e2e/tsconfig.json 2>/dev/null || npx tsc --noEmit
npm run lint
```

(If `test/e2e` has its own `tsconfig.json`, use it; otherwise fall back to the root `tsc --noEmit`. Either way, this file must produce zero new TypeScript errors.)

**Commit:**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece
git add frontend/test/e2e/helpers/e2e-auth-helper.ts
git commit -m "$(cat <<'EOF'
Fix wrong fallback route in navigateToTransportBoxReceive E2E helper

The direct-navigation fallback pointed at /warehouse/transport-box-receive,
which has no matching <Route> in App.tsx and renders nothing (no Layout,
no <main>). Changed to /logistics/receive-boxes, the actual registered
route, matching Sidebar.tsx's own href and App.tsx:437.

Fixes the locator('main, [role="main"]') timeout unique to
box-receive.spec.ts (6 of 18 nightly transport E2E failures).
EOF
)"
```

---

### task: harden-transport-box-list-error-state

**Goal (FR-3):** Today, `TransportBoxList.tsx` has an early `if (error) { return (...) }` (lines 269–291) that replaces the *entire* page with a standalone red alert box — no `<h1>`, no "Otevřít nový box" button. Even after Task 1 fixes the permission gap, any future transient 403/500/timeout on `GET /api/transport-boxes` will reproduce the exact same "h1/button never found" signature. Restructure so the header (`<h1>Transportní boxy</h1>`, currently lines 298–303) and the primary action buttons (currently lines 480–497, inside the collapsible controls block) render in a shell shared by all three query states (`isLoading` / `error` / success), with only the content region (table/summary vs. error message) swapped underneath. Per the architecture review's Decision 3, this is a surgical JSX reposition — no new components, no change to the `isLoading` or empty-results (`data.items.length === 0`) branches, no change to the collapsible filters/summary-cards logic itself (only its container loses the now-redundant action-button pair).

**File to modify:** `frontend/src/components/pages/TransportBoxList.tsx`

**Step 1 — write the failing test first.**

File: `frontend/src/components/pages/__tests__/TransportBoxList.test.tsx`

This file already has a `describe("Error state", ...)` block (starting at line 239) with two tests (`"should show error message when data loading fails"` and `"should retry loading when retry button is clicked"`), both of which already pass today and must continue to pass unmodified (they don't currently assert on `<h1>` or the create-box button, so they're compatible with either the old or new structure). Add a **new** third test in that same `describe` block that captures FR-3's acceptance criteria and fails against the current code.

Read the block first to get exact context (it ends right before `describe("Filtering functionality", ...)` at line 273). Use the Edit tool with this `old_string`/`new_string` pair:

old_string:
```typescript
    it("should retry loading when retry button is clicked", () => {
      mockUseTransportBoxesQuery.mockReturnValue({
        data: null,
        isLoading: false,
        error: new Error("Network error"),
        refetch: mockRefetch,
      });

      render(<TransportBoxList />, { wrapper: createWrapper });

      const retryButton = screen.getByText("Zkusit znovu");
      fireEvent.click(retryButton);

      expect(mockRefetch).toHaveBeenCalledTimes(1);
    });
  });
```

new_string:
```typescript
    it("should retry loading when retry button is clicked", () => {
      mockUseTransportBoxesQuery.mockReturnValue({
        data: null,
        isLoading: false,
        error: new Error("Network error"),
        refetch: mockRefetch,
      });

      render(<TransportBoxList />, { wrapper: createWrapper });

      const retryButton = screen.getByText("Zkusit znovu");
      fireEvent.click(retryButton);

      expect(mockRefetch).toHaveBeenCalledTimes(1);
    });

    it("should still render the page header and primary action button when the query errors", () => {
      mockUseTransportBoxesQuery.mockReturnValue({
        data: null,
        isLoading: false,
        error: new Error("Network error"),
        refetch: mockRefetch,
      });

      render(<TransportBoxList />, { wrapper: createWrapper });

      expect(
        screen.getByRole("heading", { level: 1, name: "Transportní boxy" }),
      ).toBeInTheDocument();
      expect(screen.getByText("Otevřít nový box")).toBeInTheDocument();
      expect(screen.getByText("Zkusit znovu")).toBeInTheDocument();
    });
  });
```

**Run the test before the fix (must fail):**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece/frontend
CI=true npx react-scripts test src/components/pages/__tests__/TransportBoxList.test.tsx --watchAll=false
```

Expected output before the fix: the new test `"should still render the page header and primary action button when the query errors"` FAILS with something like `Unable to find role="heading"` (the current error branch returns only the red alert box, no `<h1>`). The other Error-state tests still pass.

**Step 2 — apply the component fix.**

Current file structure (verified against the live file): lines 269–291 hold the early `if (error) return (...)`; the main `return (...)` starts at line 293; the header `<div className="flex-shrink-0 mb-3">...<h1>...</h1></div>` is at lines 298–303; then the `showIndicator` block, then `{showTouchPanel ? (<TransportBoxTouchPanel .../>) : (<>...controls block + results table...</>)}` runs from line 313 to line 905; then `<TransportBoxDetail .../>` and the closing `</div>` end the component.

Make three edits with the Edit tool, in this order:

**Edit A — remove the early return, and turn the header into a header+action-bar row that opens the new `error ? ... : (<>...`:**

old_string:
```typescript
  if (error) {
    return (
      <div className="p-6 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-900/40 rounded-lg">
        <div className="flex items-center gap-2">
          <AlertCircle className="h-5 w-5 text-red-600 dark:text-red-400" />
          <div>
            <h3 className="text-red-800 dark:text-red-300 font-semibold">
              Chyba při načítání transportních boxů
            </h3>
            <p className="text-red-600 dark:text-red-400 text-sm mt-1">
              {error instanceof Error ? error.message : "Neznámá chyba"}
            </p>
            <button
              onClick={() => refetch()}
              className="mt-2 px-3 py-1 bg-red-600 text-white rounded hover:bg-red-700 transition-colors text-sm"
            >
              Zkusit znovu
            </button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div
      className="flex flex-col w-full"
      style={{ height: PAGE_CONTAINER_HEIGHT }}
    >
      {/* Header - Fixed */}
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">
          Transportní boxy
        </h1>
      </div>

      {/* StockUp Status Indicator */}
      {showIndicator && (
        <StockUpOperationStatusIndicator
          summary={stockUpSummary}
          sourceType={StockUpSourceType.TransportBox}
        />
      )}

      {showTouchPanel ? (
```

new_string:
```typescript
  return (
    <div
      className="flex flex-col w-full"
      style={{ height: PAGE_CONTAINER_HEIGHT }}
    >
      {/* Header - Fixed. The h1 and primary action buttons must render
          regardless of isLoading/error/success so the page chrome survives
          API failures (feature-3542 FR-3). */}
      <div className="flex-shrink-0 mb-3 flex items-center justify-between">
        <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">
          Transportní boxy
        </h1>
        <div className="flex items-center space-x-2">
          <button
            onClick={handleOpenNewBox}
            className="flex items-center px-3 py-1 border border-transparent rounded-md shadow-sm text-xs font-medium text-white bg-indigo-600 hover:bg-indigo-700"
          >
            <Plus className="h-3 w-3 mr-1" />
            Otevřít nový box
          </button>
          <button
            onClick={() => refetch()}
            disabled={isLoading}
            className="flex items-center px-2 py-1 border border-gray-300 dark:border-graphite-border rounded-md shadow-sm dark:shadow-soft-dark text-xs font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50"
          >
            <RefreshCw
              className={`h-3 w-3 mr-1 ${isLoading ? "animate-spin" : ""}`}
            />
            Obnovit
          </button>
        </div>
      </div>

      {error ? (
        <div className="flex-1 p-6 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-900/40 rounded-lg">
          <div className="flex items-center gap-2">
            <AlertCircle className="h-5 w-5 text-red-600 dark:text-red-400" />
            <div>
              <h3 className="text-red-800 dark:text-red-300 font-semibold">
                Chyba při načítání transportních boxů
              </h3>
              <p className="text-red-600 dark:text-red-400 text-sm mt-1">
                {error instanceof Error ? error.message : "Neznámá chyba"}
              </p>
              <button
                onClick={() => refetch()}
                className="mt-2 px-3 py-1 bg-red-600 text-white rounded hover:bg-red-700 transition-colors text-sm"
              >
                Zkusit znovu
              </button>
            </div>
          </div>
        </div>
      ) : (
        <>
          {/* StockUp Status Indicator */}
          {showIndicator && (
            <StockUpOperationStatusIndicator
              summary={stockUpSummary}
              sourceType={StockUpSourceType.TransportBox}
            />
          )}

          {showTouchPanel ? (
```

Notes on this edit:
- The `<h1>` text ("Transportní boxy") is preserved verbatim — do not rename it, `TransportBoxList.test.tsx`, `TransportBoxList.stockUpGate.test.tsx`, `TransportBoxList.touch.test.tsx`, and several E2E specs locate it by this exact string.
- The "Otevřít nový box" button text and its `onClick={handleOpenNewBox}` handler are preserved verbatim (same handler, same accessible text) — only its JSX position moves from inside the collapsible controls block to this always-rendered header row.
- The `isControlsCollapsed ? "" : "..."` text-hiding ternary that these two buttons used in their old location is intentionally dropped: they now live in the always-visible header row (unrelated to the collapsible filter/summary panel), not squeezed next to filter chips, so there is no longer a reason to shorten them to icon-only. Always show the full label.
- `handleOpenNewBox`, `refetch`, and `isLoading` are all already in scope at this point in the component (defined earlier via `useTransportBoxesQuery` and the `handleOpenNewBox` function) — no new imports or state needed. `AlertCircle`, `Plus`, and `RefreshCw` are already imported at the top of the file (lines 4–18).

**Edit B — remove the now-duplicated action-button pair from inside the collapsible controls block** (this pair used to be the only occurrence; leaving it in place after Edit A would create two "Otevřít nový box" buttons on screen simultaneously in the success path, breaking `getByText` / strict-mode Playwright locators that expect exactly one match):

old_string:
```typescript
              )}

              {/* Action buttons - always visible */}
              <button
                onClick={handleOpenNewBox}
                className="flex items-center px-3 py-1 border border-transparent rounded-md shadow-sm text-xs font-medium text-white bg-indigo-600 hover:bg-indigo-700"
              >
                <Plus className="h-3 w-3 mr-1" />
                {isControlsCollapsed ? "" : "Otevřít nový box"}
              </button>
              <button
                onClick={() => refetch()}
                disabled={isLoading}
                className="flex items-center px-2 py-1 border border-gray-300 dark:border-graphite-border rounded-md shadow-sm dark:shadow-soft-dark text-xs font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50"
              >
                <RefreshCw
                  className={`h-3 w-3 mr-1 ${isLoading ? "animate-spin" : ""}`}
                />
                {isControlsCollapsed ? "" : "Obnovit"}
              </button>
            </div>
```

new_string:
```typescript
              )}
            </div>
```

This leaves the surrounding `<div className="flex items-center space-x-3">...</div>` container (which still holds the `{isControlsCollapsed && (...)}` summary-chips/search-field block) intact — only the two `<button>` elements that followed it are removed. Do not touch the `{isControlsCollapsed && (...)}` block itself or anything above/below this container.

**Edit C — close the new `error ? ... : (<>` wrapper opened in Edit A, right before the `TransportBoxDetail` modal** (which must render in all three states, unchanged):

old_string:
```typescript
        </>
      )}

      {/* Transport Box Detail Modal */}
```

new_string:
```typescript
        </>
      )}
        </>
      )}

      {/* Transport Box Detail Modal */}
```

The first `</>` / `)}` pair (unchanged) closes the pre-existing `showTouchPanel` ternary's non-touch fragment. The second, newly added `</>` / `)}` pair closes the `<>` fragment and the `error ? (...) : (...)` ternary opened in Edit A. Indentation of the newly-added closing lines does not need to match surrounding code exactly (JSX/JS does not require it) — do not reformat the rest of the file.

**Run the test after the fix (must pass):**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece/frontend
CI=true npx react-scripts test src/components/pages/__tests__/TransportBoxList.test.tsx --watchAll=false
```

Expected output: all tests in this file pass, including the new one, e.g. `Tests: N passed, N total` with 0 failed.

**Run the full regression suite named in the spec's acceptance criteria (must all still pass unmodified):**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece/frontend
CI=true npx react-scripts test src/components/pages/__tests__/TransportBoxList --watchAll=false
```

This runs `TransportBoxList.test.tsx`, `TransportBoxList.stockUpGate.test.tsx`, and `TransportBoxList.touch.test.tsx` together (Jest's default test-file matching on the `TransportBoxList` prefix within `__tests__`). All must pass with zero changes to those other two files — this is the risk-mitigation gate the architecture review calls out explicitly (Decision 3's risk table: "Run existing TransportBoxList.test.tsx, TransportBoxList.stockUpGate.test.tsx, TransportBoxList.touch.test.tsx unmodified as a regression gate").

**Full validation:**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece/frontend
npm run build
npm run lint
```

**Commit:**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece
git add frontend/src/components/pages/TransportBoxList.tsx frontend/src/components/pages/__tests__/TransportBoxList.test.tsx
git commit -m "$(cat <<'EOF'
Harden TransportBoxList error state to keep header/action bar visible

Previously any query error blanked the entire page (no h1, no "Otevřít
nový box" button), reproducing the same "h1/button never found" E2E
failure signature for any future transient 403/500/timeout, independent
of the permission-gap root cause. The header and primary action buttons
now render in a shell shared by isLoading/error/success states; only the
error message replaces the table/summary content underneath.
EOF
)"
```

---

### task: add-strict-assertions-lenient-transport-specs

**Priority note: this task is recommended, not blocking, per the spec's FR-4 designation ("recommended, not blocking") and the architecture review's risk mitigation.** Land and verify Tasks 1–3 against staging first (per NFR-1). Only after that verification succeeds should this task's changes be added, so that if one of these three specs has its own latent, unrelated issue, it does not block or confuse the primary fix's rollout.

**Goal (FR-4):** `box-items.spec.ts`, `box-workflow.spec.ts`, and `ean-integration.spec.ts` wrap every meaningful assertion in `if (await x.count() > 0) { ... }` guards, so they report "pass" even when the Transport Box list never rendered at all (exactly what happened in nightly run #191 — these three specs "passed" despite hitting the identical root-cause failure as the four specs that did fail, purely because they never assert anything unconditionally). Add one unconditional assertion to each file's `beforeEach` so a regression like this one fails loudly in all specs that exercise this page, not just the ones that happen to assert strictly.

All three files share the identical `beforeEach` shape:

```typescript
  test.beforeEach(async ({ page }) => {
    // Navigate to transport boxes with full authentication
    await navigateToTransportBoxes(page);
  });
```

**File 1: `frontend/test/e2e/transport/box-items.spec.ts`**

old_string:
```typescript
  test.beforeEach(async ({ page }) => {
    // Navigate to transport boxes with full authentication
    await navigateToTransportBoxes(page);
  });
```

new_string:
```typescript
  test.beforeEach(async ({ page }) => {
    // Navigate to transport boxes with full authentication
    await navigateToTransportBoxes(page);

    // Strict assertion (feature-3542 FR-4): the rest of this file's assertions are
    // all guarded by `if (await x.count() > 0)`, so a broken navigation/permission
    // gap would previously make every test in this file "pass" without exercising
    // anything. Fail loudly here if the Transport Box list never rendered.
    await expect(page.locator('h1')).toContainText('Transportní boxy');
  });
```

**File 2: `frontend/test/e2e/transport/box-workflow.spec.ts`**

old_string:
```typescript
  test.beforeEach(async ({ page }) => {
    // Navigate to transport boxes with full authentication
    await navigateToTransportBoxes(page);
  });
```

new_string:
```typescript
  test.beforeEach(async ({ page }) => {
    // Navigate to transport boxes with full authentication
    await navigateToTransportBoxes(page);

    // Strict assertion (feature-3542 FR-4): the rest of this file's assertions are
    // all guarded by `if (await x.count() > 0)`, so a broken navigation/permission
    // gap would previously make every test in this file "pass" without exercising
    // anything. Fail loudly here if the Transport Box list never rendered.
    await expect(page.locator('h1')).toContainText('Transportní boxy');
  });
```

**File 3: `frontend/test/e2e/transport/ean-integration.spec.ts`**

old_string:
```typescript
  test.beforeEach(async ({ page }) => {
    // Navigate to transport boxes with full authentication
    await navigateToTransportBoxes(page);
  });
```

new_string:
```typescript
  test.beforeEach(async ({ page }) => {
    // Navigate to transport boxes with full authentication
    await navigateToTransportBoxes(page);

    // Strict assertion (feature-3542 FR-4): the rest of this file's assertions are
    // all guarded by `if (await x.count() > 0)`, so a broken navigation/permission
    // gap would previously make every test in this file "pass" without exercising
    // anything. Fail loudly here if the Transport Box list never rendered.
    await expect(page.locator('h1')).toContainText('Transportní boxy');
  });
```

All three files already `import { test, expect } from '@playwright/test';` at the top (verified in each file), so `expect` is already in scope — no new imports needed. Do not change anything else in these files: every subsequent `if (await x.count() > 0)` conditional block in the test bodies is explicitly out of scope per the spec ("No behavioral change to the rest of each spec's conditional logic").

**How to test this change:** These are Playwright specs that only run meaningfully against a live staging deploy with a real E2E session (per NFR-1 — there is no local/mocked equivalent, since the E2E service-principal identity only exists in Staging/Development, `E2ETestController.cs` lines 68–73). After Tasks 1–3 have been deployed and staging is confirmed healthy, run:

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece
./scripts/run-playwright-tests.sh --grep "box-items|box-workflow|ean-integration"
```

Expected output: all three spec files pass, and specifically the new `beforeEach` assertion (`h1` contains "Transportní boxy") succeeds for every test in these files — confirming the Transport Box list actually rendered before each test's conditional logic runs. If any of these three specs fails at the new assertion, that indicates a real, previously-hidden issue with this page for that spec's flow — investigate separately; do not revert the assertion to "fix" the failure (that would recreate the exact silent-pass problem this task exists to close).

**Validation commands (static check only, before the staging run):**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece/frontend
npx tsc --noEmit -p test/e2e/tsconfig.json 2>/dev/null || npx tsc --noEmit
npm run lint
```

**Commit:**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece
git add frontend/test/e2e/transport/box-items.spec.ts frontend/test/e2e/transport/box-workflow.spec.ts frontend/test/e2e/transport/ean-integration.spec.ts
git commit -m "$(cat <<'EOF'
Add strict h1 assertion to lenient transport E2E specs (FR-4)

box-items.spec.ts, box-workflow.spec.ts, and ean-integration.spec.ts wrap
every assertion in `if (await x.count() > 0)`, so they silently "pass"
even when the Transport Box list never renders (as happened in nightly
run #191, hitting the same root cause as the 4 specs that did fail
loudly). Adds one unconditional h1 check per file's beforeEach so a
regression like this one surfaces in every spec that exercises this
page, not just the ones that happened to assert strictly.

Recommended hardening per feature-3542 spec FR-4 (not blocking); land
after the primary fix (Warehouse_Logistics role grant + nav fallback +
error-state hardening) is verified against staging.
EOF
)"
```
