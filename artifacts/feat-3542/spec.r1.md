# Specification: Fix Transport Box E2E failures caused by missing E2E-user permission + broken test navigation fallback

## Summary

The 18 nightly E2E failures across `transport/box-receive`, `transport/box-creation`, `transport/boxes-basic`, and `transport/box-management` are **not** caused by a broken Transport Box UI. They are caused by the synthetic E2E service-principal user lacking the `Warehouse_Logistics` permission that the Transport Box routes and API now require, combined with an incorrect hardcoded fallback URL in one E2E navigation helper. Root cause is fully reproducible from the code: the backend grants the E2E user only two roles (`Base`, `FinanceFinancialOverviewRead`), while the frontend's menu/route guard and the `TransportBoxController` API both require the `Warehouse_Logistics` role — so the E2E user is silently redirected away from every Transport Box page it tries to reach.

## Background

Nightly run #191 (main @ `738a99c`) reported these 4 specs failing on `waiting for locator('main, [role="main"]')` / `locator('button', hasText: 'Otevřít nový box')` / `locator('h1')`. Three other transport specs (`box-items`, `box-workflow`, `ean-integration`) "passed" in the same run using the exact same navigation helpers and hitting the exact same underlying failure — they simply never assert anything strictly enough to notice, because every check in those files is wrapped in `if (await locator.count() > 0) { ... }` guards that silently no-op when the page never renders. Their "pass" status gives no evidence the pages actually work; it should not be used to rule out a shared root cause.

### Confirmed chain of evidence

1. **Backend grants the E2E user only two roles**, `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs:74-92` (`CreateSyntheticUserClaims`):
   ```csharp
   new Claim(ClaimTypes.Role, AccessRoles.Base),
   new Claim(ClaimTypes.Role, AccessRoles.FinanceFinancialOverviewRead)
   ```
   The comment directly above (line 87-89) documents that this list was already patched once, specifically to unblock `/api/FinancialOverview`, after permission strings were renamed to role claims — but no equivalent role was ever added for Warehouse/Logistics.

2. **`TransportBoxController` requires the Warehouse_Logistics role** — `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs:19`: `[FeatureAuthorize(Feature.Warehouse_Logistics)]`. `FeatureAuthorizeAttribute` (`backend/src/Anela.Heblo.Domain/Features/Authorization/FeatureAuthorizeAttribute.cs:11-16`) resolves this to `AccessRoles.For(Feature.Warehouse_Logistics, AccessLevel.Read)`, which is the `WarehouseLogisticsRead` role (`backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs:96`). The E2E user does not hold this role → every `/api/transport-boxes*` call (list, summary, create, receive-by-code) returns `403 Forbidden` for the E2E session.

3. **The frontend gates both the sidebar menu and the routes themselves on the identical permission.** `AccessMatrix.generated.cs:67-69,91` requires `Feature.Warehouse_Logistics` (Read) for `/logistics/transport-boxes`, `/logistics/receive-boxes`, and the mobile `#terminal` module. `Sidebar.tsx:51-59` filters menu entries with `hasPermission`, and `RequireMenuPath.tsx:12-19` (used as `guard(...)` in `App.tsx:436-437` for both routes) redirects to `/` (`<Navigate to={redirectTo} replace />`) whenever the permission check fails. So for the E2E user: the "Sklad → Transportní boxy / Příjem boxů" sidebar items never appear, and direct navigation to either URL is immediately bounced to the Dashboard.

4. **The E2E navigation helpers mask this as a "UI navigation failure" and fall back to a hardcoded URL** — `frontend/test/e2e/helpers/e2e-auth-helper.ts`:
   - `navigateToTransportBoxes` (line 178): Sklad menu click times out (item hidden) → falls into the `catch`/`else` path → `page.goto('${baseUrl}/logistics/transport-boxes')` (line 221). This URL is *correct*, but `RequireMenuPath` still redirects the SPA to `/` because the permission check happens client-side after the route resolves. The Dashboard renders instead of the Transport Box list, so its `h1` doesn't say "Transportní boxy" and there is no "Otevřít nový box" button — this is the direct cause of the `box-creation`, `boxes-basic`, and `box-management` failures (12 of 18).
   - `navigateToTransportBoxReceive` (line 277): same Sklad-menu timeout, but its fallback URL is **wrong**: `page.goto('${baseUrl}/warehouse/transport-box-receive')` (line 314). The only registered route for this page is `/logistics/receive-boxes` (`App.tsx:437`), and there is no wildcard/catch-all `<Route path="*">` anywhere in `App.tsx`. Navigating to a URL with no matching route renders **nothing** — no `Layout`, no `<main>`, no content at all — which is the distinct `locator('main, [role="main"]')` timeout unique to `box-receive.spec.ts` (6 of 18 failures).

5. **`TransportBoxList.tsx`'s error-state early return is a secondary robustness gap.** Lines 269-291 return a bare red alert box with no `<h1>` and no "Otevřít nový box" button whenever `useTransportBoxesQuery` surfaces any error (a 403 today, but equally a timeout or 5xx tomorrow). Even after the permission is fixed, any transient API failure on this page will reproduce the exact same "h1/button never found" signature, so it should be hardened as part of this fix rather than left as a trap for the next incident.

## Functional Requirements

### FR-1: Grant the E2E test user the Warehouse_Logistics permission it needs to reach Transport Box screens

`E2ESessionService.CreateSyntheticUserClaims` (backend) must add whatever role(s) are required so the synthetic E2E user can pass `[FeatureAuthorize(Feature.Warehouse_Logistics)]` and the matching frontend `RequireMenuPath`/`Sidebar` checks — i.e. add `AccessRoles.For(Feature.Warehouse_Logistics, AccessLevel.Read)` (and `Write`, if any E2E test performs a write action such as creating/receiving a box) to the claims list, following the same pattern already used for `AccessRoles.FinanceFinancialOverviewRead`.

**Acceptance criteria:**
- After the change, an E2E-authenticated session can `GET /api/transport-boxes` on staging and receive `200 OK` (not `403`).
- The "Sklad" sidebar menu, when opened by an E2E session, shows both "Transportní boxy" and "Příjem boxů" sub-items.
- Direct navigation to `/logistics/transport-boxes` and `/logistics/receive-boxes` as the E2E user renders the respective page (not a redirect to `/`).
- If any E2E transport test performs a write action (create box, receive box, add item), the E2E user also holds the corresponding Write role; document which role(s) were added and why in the commit/PR description.
- Existing authorization tests (e.g. `backend/test/Anela.Heblo.Tests/Authorization/*`) continue to pass; add or update a test asserting the E2E user's claim set includes the new role(s).

### FR-2: Fix the incorrect fallback route in `navigateToTransportBoxReceive`

`frontend/test/e2e/helpers/e2e-auth-helper.ts:314` currently falls back to `page.goto('${baseUrl}/warehouse/transport-box-receive')`, a URL with no matching route in `App.tsx`. Change it to the actual registered route, `/logistics/receive-boxes`, matching the sidebar's own `href` (`Sidebar.tsx:273`) and the `App.tsx:437` route definition.

**Acceptance criteria:**
- `navigateToTransportBoxReceive`'s direct-navigation fallback goes to `/logistics/receive-boxes`.
- With FR-1 applied, running `box-receive.spec.ts` against staging no longer times out waiting for `main, [role="main"]`; the receive page (`h1` "Příjem transportních boxů") renders.
- No other E2E helper in the same file references a stale/incorrect URL (spot-check the remaining `navigateTo*` functions for the same class of bug — none was found during this investigation, but re-verify after the fix).

### FR-3: Harden `TransportBoxList`'s error state so page chrome survives API failures

Update the `error` branch in `frontend/src/components/pages/TransportBoxList.tsx` (currently lines 269-291) so that a failed `useTransportBoxesQuery` still renders the page's `<h1>` title and the "Otevřít nový box" action button, with the error message shown in place of the table/summary content rather than replacing the entire page.

**Acceptance criteria:**
- When `useTransportBoxesQuery` returns an error (simulate via a forced 500/403 in a component test), the rendered output still contains an `h1` with text "Transportní boxy" and a button matching `/Otevřít nový box/`.
- The existing "Zkusit znovu" (retry) affordance in the error state is preserved.
- `frontend/src/components/pages/__tests__/TransportBoxList.test.tsx` gains a test covering the query-error case asserting the above; existing tests in that file and `TransportBoxList.stockUpGate.test.tsx` / `TransportBoxList.touch.test.tsx` continue to pass unmodified.

### FR-4 (recommended, not blocking): Add at least one strict assertion to the currently all-lenient transport specs

`box-items.spec.ts`, `box-workflow.spec.ts`, and `ean-integration.spec.ts` wrap every meaningful assertion in `if (await x.count() > 0)`, so they report "pass" even when the Transport Box list never renders (as happened in run #191). Add one unconditional assertion per file's `beforeEach`-adjacent setup (e.g. `await expect(page.locator('h1')).toContainText('Transportní boxy')`) so a regression like this one fails loudly in the specs that exercise the same page, instead of only in the 4 specs that happened to assert strictly.

**Acceptance criteria:**
- Each of the three files fails immediately (not silently passes) if `navigateToTransportBoxes` lands anywhere other than the Transport Box list page.
- No behavioral change to the rest of each spec's conditional logic.

## Non-Functional Requirements

### NFR-1: Test reliability
The fix must be verified against the actual staging environment (`https://heblo.stg.anela.cz`), not just local/mocked runs, since the failure is specific to the E2E service-principal identity used only in Staging/E2E mode. Re-run the full `transport/` E2E suite (`./scripts/run-playwright-tests.sh`) against staging after deploying FR-1 and FR-2 and confirm all 18 previously-failing tests pass.

### NFR-2: Security
Granting the E2E synthetic user a new role must be scoped to Read (and Write only if a test genuinely mutates transport box state) — do not grant `AccessRoles.SuperUser` or broader roles than the tests exercise. The E2E session endpoint already restricts itself to Staging/Development environments (`E2ETestController.cs:68-73`); this change does not alter that boundary and must not weaken it.

## Data Model

No data model changes. This is a permission-claim and routing-configuration fix; no entities, schemas, or migrations are affected.

## API / Interface Design

- No new endpoints. Behavior change is limited to the claim set returned by `POST /api/e2etest/auth` → `E2ESessionService.CreateSyntheticUserClaims`, which is only used to construct the internal `ClaimsPrincipal` for the E2E test session (not exposed as response data).
- No route changes to the application; the E2E helper's target URL is corrected to match the existing `/logistics/receive-boxes` route already defined in `App.tsx` and `Sidebar.tsx`.

## Dependencies

- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs` / `AccessMatrix.generated.cs` — generated authorization tables; confirm whether these are regenerated from a source-of-truth definition file elsewhere in the repo, and if so, make the FR-1 change there rather than hand-editing generated output.
- `frontend/src/auth/accessMatrix.generated.ts` — the frontend counterpart of the same matrix; FR-1's backend role must line up with whatever this generated file expects for `Feature.Warehouse_Logistics`.
- Nightly E2E pipeline (`🎭 E2E Nightly Regression Tests` GitHub Actions workflow) for verification once merged.

## Out of Scope

- Any change to real (non-E2E) user permission assignment or the Warehouse_Logistics feature's business rules.
- Redesigning the E2E navigation helpers' overall "try UI nav, fall back to direct URL" pattern — only the one incorrect URL is being fixed.
- Broader refactor of the lenient assertion style used across the E2E transport suite beyond the single strict check added per FR-4.
- Investigating whether other feature areas besides Warehouse_Logistics have the same "generated role required but E2E user never granted it" gap; this spec only confirms and fixes the Transport Box case that produced the reported failures. (Worth a follow-up audit, but not part of this fix.)

## Open Questions

None.
