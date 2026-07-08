# Specification: Fix Stock Operations E2E — page never renders rows or empty state (56 nightly failures)

## Summary

The `stock-operations` E2E suite fails 56 times on staging because the E2E service-principal
test account is never granted the `warehouse.stock_up.read` permission that the Stock Operations
feature requires, so the frontend's route guard (`RequireMenuPath`) silently redirects every
navigation to `/stock-up-operations` back to `/` (Dashboard) before the page — and its data
table / empty state — ever mounts. The root cause is a code gap in the E2E synthetic-user claims
setup (`E2ESessionService.CreateSyntheticUserClaims`), which was never updated when the
`Warehouse_StockUp` feature-authorization gate was added — the exact same class of bug was already
hit and fixed for the Financial Overview feature, as evidenced by a comment left in the same method.

## Background

**Investigation trail (static code analysis only; staging is not reachable from this sandbox).**

1. `frontend/src/pages/StockOperationsPage.tsx` (lines 377-397, 583-598) has correct,
   unremarkable render logic: `isLoading` → spinner, `error` → error box, `operations.length === 0`
   → "Žádné výsledky" empty state, else → table. `frontend/src/api/hooks/useStockUpOperations.ts`
   and the generated client call (`stockUpOperations_GetOperations`, `api-client.ts:12050`) build
   the request correctly and match the backend controller's parameter names and order 1:1. The
   backend handler (`GetStockUpOperationsHandler.cs`) and repository query
   (`StockUpOperationRepository.QueryAsync`,
   `backend/src/Anela.Heblo.Persistence/Catalog/Stock/StockUpOperationRepository.cs`) correctly
   implement the "Active" state filter, pagination, and sorting, and always return
   `Success = true`. None of this explains rows AND the empty state both failing to appear —
   the bug is not in the page's own data/render logic.

2. The actual cause is one layer higher: the route is wrapped in an access guard.
   `frontend/src/App.tsx:452` defines
   `<Route path="/stock-up-operations" element={guard("/stock-up-operations", <StockOperationsPage />)} />`,
   where `guard()` (`App.tsx:287-289`) wraps the element in
   `frontend/src/components/auth/RequireMenuPath.tsx`. That component (lines 12-20) does:
   ```tsx
   const { hasPermission, isLoading } = usePermissionsContext();
   if (isLoading) return null;
   const req = ACCESS_ROUTES[path];
   if (!req) return <Navigate to={redirectTo} replace />;
   if (!req.permissions.every(p => hasPermission(p)))
     return <Navigate to={redirectTo} replace />;
   return <>{children}</>;
   ```
   `frontend/src/auth/accessMatrix.generated.ts:32` requires
   `"/stock-up-operations": { permissions: ["warehouse.stock_up.read"] }`. If the current user
   lacks that permission, this component **silently redirects to `/` — no error, no toast, no
   distinguishing URL**. `StockOperationsPage` never mounts, so neither `tbody tr` nor the
   `h3:has-text("Žádné výsledky")` element the shared E2E helper waits for
   (`frontend/test/e2e/helpers/stock-operations-test-helpers.ts:22-27`, `waitForTableUpdate`) ever
   appears — which is exactly the reported timeout signature (`toBeVisible({ timeout: 15000 })` on
   `tbody tr .or(h3:"Žádné výsledky")`). It also explains why the *first* navigation test in the
   suite ("should navigate to page via direct URL", which checks `page.url()` and the `h1` text)
   would fail too: the browser never actually stays on `/stock-up-operations`.

3. Permission resolution for this frontend gate comes from `GET /api/auth/me`
   (`backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetMe/GetMeHandler.cs`,
   lines 18-44), which returns the full permission set only when the caller is `super_user`;
   otherwise it resolves DB-backed group/permission membership via
   `IPermissionResolver.ResolveAsync(entraObjectId, ...)`
   (`backend/src/Anela.Heblo.Domain/Features/Authorization/IPermissionResolver.cs`) — this is a
   separate mechanism from the ASP.NET Core role-claim checks described next.

4. Independently, the backend API is gated the same way at the controller level.
   `StockUpOperationsController.cs:12` carries `[FeatureAuthorize(Feature.Warehouse_StockUp)]` at
   the class level (covers the read endpoint) and `AccessLevel.Write` on `RetryOperation` (line 76)
   and `AcceptOperation` (line 95). `FeatureAuthorizeAttribute`
   (`backend/src/Anela.Heblo.Domain/Features/Authorization/FeatureAuthorizeAttribute.cs:11-16`)
   sets `Roles = AccessRoles.For(feature, level)`, which becomes a standard ASP.NET Core
   `[Authorize(Roles = "warehouse.stock_up.read")]`-style check against the caller's
   `ClaimsPrincipal` role claims — enforced independently of the DB-backed resolution in point 3.

5. **The concrete, provable bug.** The E2E test user's role claims are hardcoded in
   `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs`, method
   `CreateSyntheticUserClaims()` (lines 70-92):
   ```csharp
   return new[]
   {
       new Claim(ClaimTypes.NameIdentifier, "e2e-test-user-id"),
       ...
       new Claim(ClaimTypes.Role, AccessRoles.Base), // Base role for application access
       new Claim("scp", "access_as_user"),
       // Grant the finance overview read role so E2E tests can reach /api/FinancialOverview.
       // FeatureAuthorize checks the role claim (permission strings were renamed away from the
       // old "FinancialOverview.View" form), so a stale "permission" claim no longer matches.
       new Claim(ClaimTypes.Role, AccessRoles.FinanceFinancialOverviewRead)
   };
   ```
   This grants **only** `AccessRoles.Base` and `AccessRoles.FinanceFinancialOverviewRead` — nothing
   for `warehouse.stock_up.read` / `warehouse.stock_up.write`
   (`AccessRoles.WarehouseStockUpRead` / `WarehouseStockUpWrite`,
   `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs:43-44`). The
   comment on the existing grant is direct evidence that this exact class of bug (E2E synthetic
   user missing a newly-introduced feature's role claim, causing that feature's controller to
   reject the E2E principal) has already been hit and fixed once, for `Warehouse_StockUp`'s sibling
   feature `Finance_FinancialOverview` — and the same follow-up was never done when
   `Warehouse_StockUp`'s `[FeatureAuthorize]` gate was added to `StockUpOperationsController`.
   Every `/api/StockUpOperations*` call the E2E principal makes is rejected as Forbidden at the
   ASP.NET Core authorization layer as a result.

6. This also explains the failure's scope: `warehouse.stock_up.read`/`write` are the newest
   permissions in the access matrix, introduced with this feature, and are the one gap not yet
   covered by the E2E claims list — unlike `finance.financial_overview.read`, which was already
   patched in after being hit by the same bug.

7. **Secondary, independently-confirmed test bug** (masks this failure mode, does not cause it):
   the one "passing" test, `frontend/test/e2e/stock-operations/navigation.spec.ts:83-115`
   (`'should display error state on API failure'`), doesn't actually validate anything:
   - It intercepts `page.route('**/api/stock-up-operations**', ...)` (kebab-case), but the real
     generated client calls `/api/StockUpOperations` (PascalCase — `api-client.ts:12051`:
     `this.baseUrl + "/api/StockUpOperations?"`). Playwright's glob route matching is
     case-sensitive against the URL, so this interception **never fires**; the real
     (currently-Forbidden) network call goes through untouched.
   - Even if it fired, the assertion is soft: `if (isErrorVisible) { assert } else { console.log(...) }`
     with no failure path in the `else` branch, so the test passes unconditionally regardless of
     what the page renders. This is why it "passes" even though every other test in the module
     times out — it is a false positive, not a working error-state check.

## Functional Requirements

### FR-1: Grant the E2E test principal the Warehouse_StockUp role claims
Add the missing role claims to the hardcoded E2E synthetic-user claim list so the ASP.NET Core
`[Authorize(Roles=...)]` checks generated by `[FeatureAuthorize(Feature.Warehouse_StockUp, ...)]`
on `StockUpOperationsController` stop rejecting the E2E principal's requests.

**File:** `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs`,
method `CreateSyntheticUserClaims()` (currently lines 70-92).

Add, alongside the existing `AccessRoles.FinanceFinancialOverviewRead` grant:
```csharp
new Claim(ClaimTypes.Role, AccessRoles.WarehouseStockUpRead),
new Claim(ClaimTypes.Role, AccessRoles.WarehouseStockUpWrite),
```
(`Write` is required because `retry.spec.ts` and `accept.spec.ts` exercise
`POST /api/StockUpOperations/{id}/retry` and `/accept`, both gated at `AccessLevel.Write` —
`StockUpOperationsController.cs:76,95`.)

**Acceptance criteria:**
- `E2ESessionService.CreateSyntheticUserClaims()` returns a claim set that includes
  `AccessRoles.WarehouseStockUpRead` and `AccessRoles.WarehouseStockUpWrite`.
- A backend test asserts the E2E synthetic claims include these two roles (extend or add alongside
  the existing tests in `backend/test/Anela.Heblo.Tests/Authorization/`).
- After deploying, an authenticated call via the E2E service-principal flow to
  `GET /api/StockUpOperations` returns 200, not 403/401.

### FR-2: Confirm/grant the same permission in the DB-backed permission resolver used by the frontend menu guard
`RequireMenuPath` (`frontend/src/components/auth/RequireMenuPath.tsx`) does not use the ASP.NET
Core role claims touched in FR-1 at all — it gates on `GET /api/auth/me`'s resolved permission
list, which for non-super-user accounts comes from `IPermissionResolver.ResolveAsync(...)`
(DB-backed group/permission resolution — see `GetMeHandler.cs` and `IPermissionResolver.cs`).
FR-1 alone fixes the backend API 403s but will **not** stop the silent `<Navigate to="/" />`
redirect unless the E2E test account's DB-resolved permission set also includes
`warehouse.stock_up.read`.

**Acceptance criteria:**
- After FR-1 is deployed, call `GET /api/auth/me` as the E2E test principal (or inspect via the
  `/admin/access` UI) and confirm the returned `permissions` array includes
  `warehouse.stock_up.read`.
- If it is missing, grant it to the E2E account's permission group through the existing access
  management mechanism (`/admin/access` UI, backed by the database) — this is a data/configuration
  change, not a code change, consistent with how other manual DB updates are handled in this
  project (see CLAUDE.md: "Database migrations are manual").
- After the grant, navigating to `/stock-up-operations` as the E2E principal renders
  `StockOperationsPage` (URL stays `/stock-up-operations`, `h1` reads "Operace naskladnění")
  instead of redirecting to `/`.

### FR-3: Harden the misleading "error state on API failure" E2E test
Fix the test that currently passes regardless of outcome, so a similar regression fails loudly in
the future instead of being masked.

**File:** `frontend/test/e2e/stock-operations/navigation.spec.ts`, test
`'should display error state on API failure'` (lines 83-115).

- Fix the route-interception glob from `'**/api/stock-up-operations**'` to match the actual
  endpoint casing used by the generated client (`/api/StockUpOperations`, see
  `api-client.ts:12051`), e.g. `'**/api/StockUpOperations**'`, or a case-insensitive regex anchored
  to the real path.
- Replace the soft `if (isErrorVisible) { ... } else { console.log(...) }` with a hard
  `await expect(errorMessage).toBeVisible()` assertion, so the test fails if the error state does
  not appear once the call is genuinely intercepted and aborted.

**Acceptance criteria:**
- With the route fix, `page.route` reliably intercepts the real `GetOperations` call and aborts it.
- The test fails (not silently passes) if the error UI (`"Chyba při načítání operací"` heading +
  "Zkusit znovu" retry button) does not appear after the aborted call.
- The test continues to pass once genuinely exercising the intercepted/aborted request path.

### FR-4 (recommended, not required to close this issue): Make `RequireMenuPath` denials diagnosable
`RequireMenuPath`'s silent `<Navigate to={redirectTo} replace />` on missing permission produces no
console/network signal distinguishing "redirected due to missing permission" from "genuinely
navigated to `/`". This is exactly what turned a straightforward permission gap into an opaque
15-second E2E timeout with no actionable error, and cost significant investigation time for this
incident. Consider logging a `console.warn` (or emitting a telemetry event) with the denied `path`
and the missing permission(s) when this redirect fires, so future permission-gap regressions are
diagnosable directly from browser/test logs.

**Acceptance criteria:**
- When `RequireMenuPath` denies access, a console warning (or equivalent telemetry) identifies the
  requested path and the missing permission(s).
- No behavior change to the redirect itself (still redirects to `redirectTo`).

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. FR-1 only adds two additional `Claim` objects to an
already-in-memory claims list constructed once per E2E session login; FR-2 is a one-time
permission grant.

### NFR-2: Security
FR-1 only affects the `E2ETestCookies` authentication scheme, which `E2ETestController` already
restricts to `Staging`/`Development` environments (`E2ETestController.cs:68-73,134-137,172-175`).
Granting `warehouse.stock_up.read`/`write` to the synthetic E2E principal does not affect
production authorization or any real user's permissions. FR-2's DB grant must be scoped to the
E2E test account only, not broadened to any production group.

## Data Model
No schema changes. This is a permission-configuration and test-hardening fix; no entities,
migrations, or DTO changes are involved.

## API / Interface Design
No API contract changes. `GET /api/StockUpOperations`, `POST /api/StockUpOperations/{id}/retry`,
and `POST /api/StockUpOperations/{id}/accept` remain as currently defined
(`StockUpOperationsController.cs`); this fix only changes which claims the E2E principal presents
when calling them (FR-1), and which permissions `GET /api/auth/me` resolves for that principal
(FR-2).

## Dependencies
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs` (auto-generated;
  the `WarehouseStockUpRead`/`WarehouseStockUpWrite` constants already exist there — FR-1 only
  references them, no regeneration needed).
- The DB-backed permission/group administration feature (`/admin/access`) for FR-2.
- `frontend/test/e2e/helpers/stock-operations-test-helpers.ts` (`waitForTableUpdate`) and
  `frontend/test/e2e/helpers/e2e-auth-helper.ts` (`navigateToStockOperations`) — unchanged; their
  current behavior (no assertion that navigation actually landed on the target page) is why this
  failure surfaced as a 15s generic timeout rather than a clear "redirected unexpectedly" error.

## Out of Scope
- Any change to `StockOperationsPage.tsx`, `useStockUpOperations.ts`,
  `GetStockUpOperationsHandler.cs`, or `StockUpOperationRepository.cs` — all were reviewed and are
  functioning correctly; the bug is entirely in E2E authorization setup and menu-guard permission
  resolution, not in the feature's own business logic.
- Regenerating `accessMatrix.generated.ts` / `AccessRoles.generated.cs` — the required permission
  constants already exist; only the E2E claims list (FR-1) and the DB group grant (FR-2) need
  updating.
- Granting `warehouse.stock_up.*` to any real production user/group — out of scope; FR-2 is scoped
  to the E2E/staging test account only.
- A broader audit of every other E2E-suite module for the same "missing E2E role claim when a new
  `[FeatureAuthorize]` feature ships" pattern, even though the `FinanceFinancialOverviewRead`
  precedent shows it has happened before and could recur for other newly-gated features.

## Open Questions

None.

## Status: COMPLETE
