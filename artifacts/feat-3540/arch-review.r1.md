# Architecture Review: Fix Stock Operations list page never rendering rows/empty-state (56 nightly E2E failures)

## Skip Design: true

## Architectural Fit Assessment

This is a permission-configuration bug, not a UI or architecture defect. `StockOperationsPage.tsx` and `useStockUpOperationsQuery` follow the same loading/error/empty/data pattern used across the app; no new component, screen, or design decision is needed. The fix belongs entirely in the E2E identity's authorization setup (one file) plus a test-suite correction, both of which slot into existing, already-established patterns in this codebase — this exact class of bug (E2E synthetic identity missing a `Feature`-gated role) has been hit and fixed twice before in this repo.

Two independent, both-necessary integration points:
1. **Backend/E2E auth config**: `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs` — the synthetic claims list handed to every E2E request.
2. **E2E test file**: `frontend/test/e2e/stock-operations/navigation.spec.ts` — a pre-existing, independently broken test (confirmed by the analyst, confirmed again here).

## Definitive root cause (resolves the spec's Open Questions)

**The E2E synthetic test identity does not hold `Feature.Warehouse_StockUp` Read (`warehouse.stock_up.read`), and this is gated in *two* places, not one — both of which produce the exact "neither rows nor empty-state" symptom.**

Evidence chain:

1. **Backend gate** — `backend/src/Anela.Heblo.API/Controllers/StockUpOperationsController.cs:12`:
   ```csharp
   [FeatureAuthorize(Feature.Warehouse_StockUp)]   // AccessLevel.Read by default
   public class StockUpOperationsController : ControllerBase
   ```
   `GetOperations` (the list endpoint) has no method-level override, so the class-level Read gate applies. `FeatureAuthorizeAttribute` resolves this to `Roles = AccessRoles.WarehouseStockUpRead = "warehouse.stock_up.read"` (`AccessRoles.generated.cs:43`, `AccessMatrix.generated.cs:39`).

2. **Frontend route gate (a second, earlier point of failure)** — `frontend/src/App.tsx:452` wraps the route in `guard("/stock-up-operations", <StockOperationsPage />)`, which renders `RequireMenuPath`. `RequireMenuPath.tsx:17-18` redirects to `/` (`<Navigate to={redirectTo} replace />`) if the current user's resolved permissions don't include every permission in `ACCESS_ROUTES["/stock-up-operations"]`. That entry, `frontend/src/auth/accessMatrix.generated.ts:32`, requires exactly `["warehouse.stock_up.read"]`. **If the E2E identity lacks this permission, `StockOperationsPage` never mounts at all** — the SPA silently redirects to the dashboard. This produces the same "no `tbody tr`, no `h3` empty-state, no `h3` error banner" symptom as the analyst's error-branch hypothesis, but via a different mechanism (route redirect vs. in-page error render). It also better explains why `navigation.spec.ts`'s *first* test ("should navigate to page via direct URL", which asserts `page.url()).toContain('/stock-up-operations')` and the page `<h1>` text) is among the 4 failing tests in that file — that assertion has nothing to do with `waitForTableUpdate` and would only fail on a redirect, not on an in-page error state.

3. **The E2E identity's actual claims** — `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs:71-86`, `CreateSyntheticUserClaims()`:
   ```csharp
   new Claim(ClaimTypes.Role, AccessRoles.Base),
   new Claim("scp", "access_as_user"),
   // Grant the finance overview read role so E2E tests can reach /api/FinancialOverview.
   new Claim(ClaimTypes.Role, AccessRoles.FinanceFinancialOverviewRead)
   ```
   Only two roles are hardcoded: `Base` and `FinanceFinancialOverviewRead`. **There is no `AccessRoles.WarehouseStockUpRead` claim.** The comment on the existing `FinanceFinancialOverviewRead` line is direct proof this file is where E2E-specific permission grants are made *by hand, one feature at a time*, precisely because the synthetic principal has no real Entra group membership to inherit permissions from.
   (`PermissionClaimsTransformation.cs` additionally resolves DB-backed group permissions for the synthetic `oid="e2e-test-object-id"` AppUser and unions them in, so it is theoretically possible staging's DB grants this role out-of-band — but that is not verifiable from code, is not how the prior, identical gap was fixed, and would be a fragile, undocumented dependency to rely on instead of the established in-code pattern.)

4. **Direct precedent for this exact failure class** — `docs/routines/telemetry-anomaly/2026-06-13-stockupoperations-summary-403.md` documents a near-identical incident: `GET /api/StockUpOperations/summary`, gated by the same `[FeatureAuthorize(Feature.Warehouse_StockUp)]` class-level attribute, was returning 403 to callers without `warehouse.stock_up.read`. That incident's fix (R-A) only gated the two *optional widget* callsites (`TransportBoxList`, `GiftPackageManufacturing`) on the frontend so they degrade gracefully without the permission — it did **not** grant the permission to the E2E identity, and it did **not** touch the Stock Operations list page itself, whose entire purpose requires the data (there is no graceful degrade option for the primary page). That gap is exactly what is now surfacing as 56 E2E failures.

5. **Server-side code review turns up no throw hazard.** `GetStockUpOperationsHandler` and `StockUpOperationRepository.QueryAsync` (`backend/src/Anela.Heblo.Persistence/Catalog/Stock/StockUpOperationRepository.cs`) are straightforward EF LINQ with no null-deref or unhandled-cast risk on the default `state=Active` path — this weakens hypothesis (b) (unhandled server exception) relative to the permission hypothesis. A hang (hypothesis c) is also inconsistent with `retry: 1` settling the query client within seconds, as the spec itself already argued.

**Conclusion:** hypothesis (a) from the spec (permission gap) is confirmed as the primary root cause, with the added, more precise detail that the failure most likely happens at the frontend route guard (silent redirect to `/`) before the backend 403 branch is even reached for most of the 56 tests — though both mechanisms exist and both point to the same one-line fix.

## Proposed Architecture

No new components. This is a one-claim addition plus a test-file correction.

### Component Overview

```
Playwright test (stock-operations/*.spec.ts)
        │  navigateToStockOperations(page) / navigateToApp(page)
        ▼
E2ETestAuthenticationMiddleware  ──uses──▶  E2ESessionService.CreateSyntheticUserClaims()
        │  sets ClaimsPrincipal (Base, FinanceFinancialOverviewRead, [MISSING: WarehouseStockUpRead])
        ▼
PermissionClaimsTransformation  ──unions in resolved DB permissions, adds Role claims
        │
        ├──▶ Frontend: GET /api/Auth/me → usePermissions → PermissionsContext.hasPermission()
        │         └──▶ RequireMenuPath("/stock-up-operations") → redirect to "/" if permission missing  [FAILS HERE, likely]
        │
        └──▶ Backend: [FeatureAuthorize(Feature.Warehouse_StockUp)] on StockUpOperationsController
                  └──▶ 403 on GET /api/StockUpOperations if permission missing            [OR FAILS HERE]
```

### Key Design Decisions

#### Decision 1: Where to grant the permission
**Options considered:**
- (A) Add `AccessRoles.WarehouseStockUpRead` as a hardcoded claim in `E2ESessionService.CreateSyntheticUserClaims()`.
- (B) Grant the permission via the staging database (assign the "e2e-test-user" AppUser to an access group holding `warehouse.stock_up.read`), mirroring how a real user gets it.
- (C) Weaken/relax the `[FeatureAuthorize]` gate or `ACCESS_ROUTES` entry for this route.

**Chosen approach:** (A), matching the existing, working precedent for `FinanceFinancialOverviewRead` in the same file/method.

**Rationale:** The synthetic E2E principal has no real Entra Object ID or group membership — DB-based group assignment (B) would be an out-of-band, undocumented, hard-to-audit dependency invisible to code review and not reproducible by `dotnet build`/tests. Option (A) is exactly the pattern this codebase already uses for this identity (see the existing comment on the `FinanceFinancialOverviewRead` claim), keeps the grant in source control, and is self-documenting. Option (C) is explicitly forbidden by the spec's NFR-2 and would broaden production access to work around a test-identity gap — not applicable here since (A) doesn't touch production authorization at all, it only affects the E2E-only synthetic principal that already only exists in Staging/Development environments (`E2ETestAuthenticationMiddleware.ShouldBeRegistered`).

#### Decision 2: Scope of the granted role
**Options considered:**
- Grant `Warehouse_StockUp` Read only (matches what `GetOperations`/`GetSummary` need).
- Also grant `Warehouse_StockUp` Write (would let E2E tests exercise `RetryOperation`/`AcceptOperation`, which `retry.spec.ts` and `accept.spec.ts` need).

**Chosen approach:** Grant **both Read and Write** (`AccessRoles.WarehouseStockUpRead` and `AccessRoles.WarehouseStockUpWrite`).

**Rationale:** The affected-specs table in the brief includes `retry.spec.ts` (6 failures) and `accept.spec.ts` (3 failures), which exercise `POST /{id}/retry` and `POST /{id}/accept` — both gated at `[FeatureAuthorize(Feature.Warehouse_StockUp, AccessLevel.Write)]` (`StockUpOperationsController.cs:76,95`). Granting Read only would fix the list-rendering symptom but leave those two spec files failing on the write actions for an unrelated (but adjacent) reason. Since this is the E2E-only synthetic identity in non-production environments, granting both levels for one feature carries no production security risk.

## Implementation Guidance

### Directory / Module Structure

- **Modify:** `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs`
  Add two claims to `CreateSyntheticUserClaims()`, following the existing comment style:
  ```csharp
  new Claim(ClaimTypes.Role, AccessRoles.WarehouseStockUpRead),
  new Claim(ClaimTypes.Role, AccessRoles.WarehouseStockUpWrite),
  ```
  with a comment analogous to the existing `FinanceFinancialOverviewRead` one, referencing the Stock Operations E2E module and this ticket.

- **Add (recommended, not strictly required by the spec but cheap insurance):** a unit test asserting the synthetic claim set contains `warehouse.stock_up.read`/`warehouse.stock_up.write`, e.g. `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ESessionServiceTests.cs`, calling `CreateSyntheticUserClaims("Staging")` and asserting on the resulting `Claim[]`. This directly prevents the exact regression class this ticket is fixing (a claim silently missing from this list) — mirrors the existing precedent of `StockUpOperationsControllerAuthorizationTests.cs`, which reflection-tests the *controller* side of this same authorization contract but had no counterpart testing the *E2E identity* side.

- **Modify:** `frontend/test/e2e/stock-operations/navigation.spec.ts` (lines 83-115) per spec FR-3:
  - Fix the intercept pattern from `**/api/stock-up-operations**` to match the actual generated-client request path (`**/api/StockUpOperations**`, case-correct — confirmed at `frontend/src/api/generated/api-client.ts:12051`: `this.baseUrl + "/api/StockUpOperations?"`).
  - Replace the `if (isErrorVisible) {...} else { console.log(...) }` soft branch with an unconditional `await expect(errorMessage).toBeVisible({ timeout: <appropriate> })`.

- **Modify:** `frontend/test/e2e/helpers/stock-operations-test-helpers.ts` (`waitForTableUpdate`, lines 22-27) per spec FR-4:
  - Extend the locator union to also match the error heading (`h3` containing "Chyba při načítání operací"), and if it resolves, throw immediately with the error banner's text rather than falling through to the generic 15s timeout. Do not change what the dedicated error-state test in FR-3 asserts on — that test should keep using its own explicit assertion, not this shared helper's short-circuit.

### Interfaces and Contracts

No public interface/contract changes. `AccessRoles.WarehouseStockUpRead` / `AccessRoles.WarehouseStockUpWrite` are pre-existing generated constants (`AccessRoles.generated.cs:43-44`) — the fix only changes which claims are attached to one synthetic `ClaimsIdentity`, it does not touch `FeatureAuthorizeAttribute`, `AccessMatrix`, or any generated file. No OpenAPI/client regeneration needed.

### Data Flow

1. Playwright test calls `navigateToStockOperations(page)` (or `navigateToApp` + manual nav), which authenticates via the `X-E2E-Test-Token` header or `E2ETestCookies` session.
2. `E2ETestAuthenticationMiddleware` builds the `ClaimsPrincipal` from `E2ESessionService.CreateSyntheticUserClaims()` — after the fix, this includes `warehouse.stock_up.read` and `warehouse.stock_up.write` role claims.
3. `PermissionClaimsTransformation` unions in any DB-resolved permissions (unaffected by this fix either way) and stamps `authz_applied`.
4. Frontend `GET /api/Auth/me` returns `permissions` including `warehouse.stock_up.read` → `PermissionsContext.hasPermission('warehouse.stock_up.read')` → `RequireMenuPath` allows `StockOperationsPage` to mount instead of redirecting to `/`.
5. `useStockUpOperationsQuery` calls `GET /api/StockUpOperations`, now authorized (200), and the page settles into either the data-table branch or, if staging genuinely has zero `Active` operations at test time, the correctly-matched "Žádné výsledky" branch — either way, `waitForTableUpdate` now resolves within its 15s window.
6. `retry.spec.ts` / `accept.spec.ts` calls to `POST /{id}/retry` and `/{id}/accept` are now authorized (200) via the added Write role.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| The "e2e-test-object-id" AppUser already has DB-granted permissions overlapping/conflicting with the new hardcoded claims | Low | `PermissionClaimsTransformation` only *adds* claims not already present (`if (!identity.HasClaim(...))`), so this is additive and idempotent — no conflict possible. |
| Granting Write access to the E2E identity could let a buggy test mutate/corrupt staging data it shouldn't | Low | Scope is limited to `Warehouse_StockUp` Write only (retry/accept of stock-up operations), consistent with what `retry.spec.ts`/`accept.spec.ts` are explicitly designed to exercise; identical risk profile already accepted for every other feature area E2E currently covers (transport boxes, catalog, etc.), and confined to Staging/Development per `E2ETestAuthenticationMiddleware.ShouldBeRegistered`. |
| Root cause is not (solely) the permission gap — e.g., staging dataset happens to be empty for `Active` state at test time, or there's a genuine intermittent server error | Medium | Spec's FR-1 (staging repro capturing network status/body) should still be executed before closing the ticket, even though static evidence strongly points to the permission gap — a quick `curl`/DevTools check against staging with a token lacking `warehouse.stock_up.read` vs. one with it would conclusively confirm 403 vs 200, and is cheap to do before merging. |
| `navigation.spec.ts` FR-3 fix and the permission fix are both required — fixing only one leaves failures | Medium | Both must land together: the permission fix alone does not fix the case-mismatched intercept test (it's a Playwright-route bug independent of auth), and the intercept fix alone does not fix the other 8 spec files' 56-test root cause. |
| FR-4's stronger `waitForTableUpdate` could introduce new flakiness if the error heading briefly flashes during a legitimate retry/refetch | Low | Only fail-fast on the error heading; do not fail-fast on the loading spinner. Since React Query's `retry: 1` means the error state is only reached after retries are exhausted, a transient flash before recovery is not expected in normal operation. |

## Specification Amendments

- **FR-1 is now largely answered by static analysis**, not just "leave for staging repro": the concrete, most-likely finding is (a) — a permissions gap — manifesting via the **frontend route redirect** (`RequireMenuPath`) as the primary mechanism for most of the 56 failures, not solely the backend error-branch mechanism the spec's Background section focused on. Both mechanisms trace to the same missing role claim, so FR-1's staging repro becomes a confirmation step, not a discovery step — it can run in parallel with implementing the FR-2 fix rather than strictly gating it.
- **FR-2's acceptance criteria should explicitly include the Write role**, not just Read: `retry.spec.ts` (6 tests) and `accept.spec.ts` (3 tests) require `warehouse.stock_up.write` on `POST /{id}/retry` and `POST /{id}/accept`, which is a separate gate from the Read gate on `GetOperations`/`GetSummary`. Granting Read alone would leave 9 of the 56 tests still failing.
- **Add an explicit FR (or fold into FR-2)**: add regression coverage on the E2E synthetic claims list itself (`E2ESessionServiceTests`), not just on the controller's authorization contract (which already exists via `StockUpOperationsControllerAuthorizationTests.cs`). The controller-side test proves the gate is correctly *configured*; nothing currently proves the E2E identity actually *holds* the roles the gate requires — which is precisely the gap that caused this incident and the prior `FinancialOverview` one.

## Prerequisites

None. This is a same-PR code change (one file's claims list + one E2E test file + one E2E helper file) with no migrations, no infrastructure changes, and no new configuration. No Key Vault secrets, no Azure Portal changes. Recommended but not blocking: a quick staging repro (FR-1) to double-confirm 403 vs. 200 before/after, using either browser DevTools against `https://heblo.stg.anela.cz/stock-up-operations` or a direct `curl` with/without the E2E service-principal token.
