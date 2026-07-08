# Specification: Fix Stock Operations list page never rendering rows/empty-state (56 nightly E2E failures)

## Summary
56 nightly E2E tests in the `stock-operations` module fail because the Stock Operations list page (`StockOperationsPage.tsx`, route `/stock-up-operations`) never settles into a state the shared test helper recognizes: neither a data row (`tbody tr`) nor the empty-state header ("Žádné výsledky") appears within 15 seconds. Code review shows the page has exactly three terminal render states — loading, empty, error — and the error state renders a *different* heading ("Chyba při načítání operací"), which the shared wait helper does not match. This means the most likely root cause is that the real `/api/StockUpOperations` call is failing (permission/authorization or server error) on staging, not that the page is stuck loading or that the dataset is genuinely empty. A second, independently confirmed bug in the test suite itself (a case-mismatched route-interception pattern with a soft assertion) has been masking this failure mode from being caught by its own dedicated "error state" test.

## Background
The Stock Operations list page (`frontend/src/pages/StockOperationsPage.tsx`) fetches data via `useStockUpOperationsQuery` (`frontend/src/api/hooks/useStockUpOperations.ts`), which calls the generated client method `stockUpOperations_GetOperations`, hitting `GET /api/StockUpOperations` on the backend (`StockUpOperationsController.GetOperations`, routed through `GetStockUpOperationsHandler` → `StockUpOperationRepository.QueryAsync`).

The page has three mutually exclusive render branches:
1. `isLoading` → spinner ("Načítání dat...")
2. `error` (truthy) → red box with heading **"Chyba při načítání operací"** and a "Zkusit znovu" retry button
3. `operations.length === 0` → heading **"Žádné výsledky"**
4. otherwise → `<table>` with `<tbody><tr>` rows

The shared E2E helper `waitForTableUpdate()` (`frontend/test/e2e/helpers/stock-operations-test-helpers.ts:22-27`) waits only for `tbody tr` OR an `h3` containing "Žádné výsledky":
```ts
await expect(
  page.locator('tbody tr').first().or(page.locator('h3').filter({ hasText: 'Žádné výsledky' }))
).toBeVisible({ timeout: 15000 });
```
Critically, this locator does **not** match branch 2 (the error state), which has its own `h3` with different text ("Chyba při načítání operací"). A page that is deterministically erroring on every real load — rather than being stuck in `isLoading` — produces exactly the observed symptom: the wait times out after 15s because it is looking for the wrong two of the three non-loading terminal states.

Since the global React Query client (`frontend/src/App.tsx:104-112`) is configured with `retry: 1`, a failing request settles into the error state within a few seconds, not 15s — ruling out "still loading" as the likely steady state at the 15s timeout mark, and pointing instead at the error branch (or, less likely, a genuine network/request hang independent of React Query, e.g. a hung backend request or CORS preflight failure that never resolves the fetch promise).

An empty active dataset (0 rows for the default `state=Active` filter) is **not** a plausible root cause: that outcome correctly renders "Žádné výsledky", which the wait helper matches successfully. The bug must be something that produces the *error* branch (or an indefinite hang) on effectively every navigation to the page, across all 9 spec files and 56 test cases.

**Confirmed compounding bug in the test suite** (found by code review, not the primary cause but must be fixed regardless): `frontend/test/e2e/stock-operations/navigation.spec.ts:83-115` ("should display error state on API failure") intercepts `page.route('**/api/stock-up-operations**', ...)` — a kebab-case, lowercase pattern. The actual backend route is `api/StockUpOperations` (from `[Route("api/[controller]")]` on `StockUpOperationsController`, confirmed by the generated client at `frontend/src/api/generated/api-client.ts:12051`: `this.baseUrl + "/api/StockUpOperations?"`). Playwright glob route patterns are case-sensitive, so this interception never fires — `route.abort('failed')` is dead code in this test. Worse, the test's own assertion is soft:
```ts
const isErrorVisible = await errorMessage.isVisible();
if (isErrorVisible) { /* assert retry button */ } else { console.log('...possible caching'); }
```
It passes unconditionally whether or not the error banner appears. This is why this is the one test in the suite that "passes" — it is a false positive, not evidence that the error-rendering path works correctly. It provides no real coverage of the error state and does not confirm or rule out the primary hypothesis on its own.

## Functional Requirements

### FR-1: Diagnose the actual failure mode against staging before implementing a code fix
This spec's code-review evidence narrows the cause to "the real API call errors (403/401/500) or hangs on every load" but cannot pinpoint which, since staging access, server logs, and captured network traces were not available during spec authoring.
**Acceptance criteria:**
- Reproduce the failure against `https://heblo.stg.anela.cz/stock-up-operations` (or the relevant nightly-run target) while capturing: browser console output, the network response (status code and body) for `GET /api/StockUpOperations`, and backend application logs for the corresponding request.
- Confirm which of the following is occurring: (a) HTTP 401/403 from `[FeatureAuthorize(Feature.Warehouse_StockUp)]` denying the E2E service principal/test user, (b) HTTP 5xx from an unhandled exception in `GetStockUpOperationsHandler` / `StockUpOperationRepository.QueryAsync` / AutoMapper mapping of `StockUpOperationDto`, (c) a network-level failure (CORS, timeout, DNS) that never resolves the fetch promise, or (d) something else not anticipated by this spec's static analysis.
- Attach the findings (status code, response body, stack trace if any) to the implementation ticket before FR-2 is coded.

### FR-2: Fix the confirmed root cause so the Stock Operations page reliably reaches a correct terminal state
Once FR-1 identifies the concrete cause, fix it so that a normal authenticated user with `Warehouse_StockUp` read access loading `/stock-up-operations` with the default filters (`state=Active`, all source types) reliably reaches either the data table or the "Žádné výsledky" empty state within the existing test timeout — never gets stuck in the error branch.
**Acceptance criteria:**
- If FR-1 finds a permissions gap (e.g., the E2E test user/role is missing `Feature.Warehouse_StockUp` Read access): grant the appropriate role/permission to the E2E test identity, OR correct the `[FeatureAuthorize]` configuration if it is wrong relative to the intended access matrix (do not weaken production authorization to work around a test-identity gap).
- If FR-1 finds a server-side exception: fix the throwing code path in the handler/repository/mapper and add a regression test in `backend/test/Anela.Heblo.Tests` covering the failing input shape.
- If FR-1 finds a hang: identify and fix the blocking call (e.g., missing `CancellationToken` propagation, DB connection pool exhaustion, synchronous-over-async deadlock) and add a timeout at the appropriate layer so the frontend at least reaches the error state deterministically instead of hanging indefinitely.
- After the fix, manually verify (or via a smoke E2E run) that `GET /api/StockUpOperations?state=Active` against staging returns HTTP 200 with a valid `GetStockUpOperationsResponse` body for the E2E test identity.
- All 56 previously-failing tests across `filters.spec.ts`, `badges.spec.ts`, `panel.spec.ts`, `retry.spec.ts`, `state-filter.spec.ts`, `navigation.spec.ts`, `accept.spec.ts`, `sorting.spec.ts`, and `source-filter.spec.ts` pass in the next nightly run.

### FR-3: Fix the broken route interception and soft assertion in the "error state" E2E test
Independent of FR-1/FR-2, `navigation.spec.ts`'s "should display error state on API failure" test is broken and must be fixed so it provides real coverage and cannot silently pass regardless of behavior.
**Acceptance criteria:**
- The `page.route(...)` pattern is corrected to match the actual endpoint, e.g. `**/api/StockUpOperations**` (case-correct, matching the generated client's request URL), or better, matched via a case-insensitive/regex pattern anchored to the controller's actual route so future controller renames are caught rather than silently passing.
- The test asserts unconditionally (no `if (isErrorVisible) { assert } else { log }` branching that allows a pass without exercising the assertion) — use `await expect(errorMessage).toBeVisible()` (with an appropriate timeout) so a regression here fails the test.
- Verify the retry button ("Zkusit znovu") assertion still runs and passes when the error state is genuinely triggered via the now-correctly-intercepted route.
- Confirm this test still passes after the fix, exercising the real interception (not a no-op).

### FR-4: Strengthen `waitForTableUpdate` so a persistent error state fails fast with a clear signal, instead of a generic 15s timeout
The current failure mode (15s generic timeout, "element(s) not found") does not tell engineers *why* the table never loaded — it looks identical whether the cause is a slow network, a genuine hang, or a rendered error banner. This ambiguity cost investigation time for this very incident.
**Acceptance criteria:**
- `waitForTableUpdate` (or a wrapper around it used by the module's tests) also observes the error branch's heading ("Chyba při načítání operací") and, if it appears instead of rows/empty-state, fails immediately with a descriptive error message (e.g., including the error banner's text) rather than waiting out the full 15s generic timeout.
- This does not change the assertion for the tests that are specifically testing the error state (FR-3) — those tests should continue to use their own explicit error-state assertions, not the strengthened `waitForTableUpdate`.
- Existing passing tests in the module are unaffected (no new flakiness introduced).

## Non-Functional Requirements

### NFR-1: Performance
- `GET /api/StockUpOperations` with default filters and `pageSize=50` must respond within the existing implicit expectation embedded in the E2E suite (well under the 15s wait timeout; target p95 < 2s against the staging dataset size).
- No new N+1 queries or unbounded scans introduced by any FR-2 fix; the existing `StockUpOperationRepository.QueryAsync` pagination (`Skip`/`Take`) and count-then-fetch pattern must be preserved.

### NFR-2: Security
- Any permission-grant fix under FR-2 must follow the existing `Feature`/`AccessLevel`/role model (`AccessRoles.generated.cs`, `AccessMatrix.generated.cs`) — do not bypass or weaken `[FeatureAuthorize(Feature.Warehouse_StockUp)]` on the controller. If the E2E test identity needs the permission, grant it through the same mechanism a real user would receive it (role assignment), not through an authorization code change.
- Do not log sensitive data (tokens, PII) when adding diagnostic instrumentation for FR-1.

## Data Model
No schema changes are anticipated by this spec. Relevant existing entities (unchanged unless FR-1 uncovers an entity/mapping bug):
- `StockUpOperation` (backend entity, table `StockUpOperations`) — `Id`, `DocumentNumber`, `ProductCode`, `Amount`, `State` (enum: `Pending`, `Submitted`, `Completed`, `Failed` — post `RemoveVerifiedStateFromStockUpOperations` migration), `SourceType` (`TransportBox` | `GiftPackageManufacture`), `SourceId`, `CreatedAt`, `SubmittedAt`, `CompletedAt`, `FailedAt`, `ErrorMessage`.
- `StockUpOperationDto` (`GetStockUpOperationsResponse.cs`) — DTO mirror of the above, returned to the frontend.
- Partial index `IX_StockUpOperations_State_Active` (migration `20260506145627_AddPartialIndexForActiveStockUpOperations`) covers `State IN (Pending, Submitted, Failed)` — used by `GetActiveCountsAsync`, not directly by `QueryAsync`'s "Active" filter path, but worth checking under FR-1 if the query planner behaves unexpectedly on the `state=Active` OR-filter.

## API / Interface Design
- `GET /api/StockUpOperations` — existing endpoint (`StockUpOperationsController.GetOperations`), no signature changes proposed. Query params: `state`, `pageSize`, `page`, `sourceType`, `sourceId`, `productCode`, `documentNumber`, `createdFrom`, `createdTo`, `sortBy`, `sortDescending`. Gated by `[FeatureAuthorize(Feature.Warehouse_StockUp)]` (Read).
- No new endpoints. Any fix is expected to be either a permission/config change, a bug fix inside the existing handler/repository/mapper, or a test-file correction — not a new interface.

## Dependencies
- ASP.NET Core authorization pipeline and the generated `AccessRoles`/`AccessMatrix`/`Feature` files (`backend/src/Anela.Heblo.Domain/Features/Authorization/`).
- React Query (`@tanstack/react-query`) client configuration in `frontend/src/App.tsx`.
- Playwright E2E harness and staging environment `https://heblo.stg.anela.cz`, and the E2E service identity's role/permission assignment (owner/location unknown from code alone — needs FR-1 investigation).
- Nightly E2E workflow (`E2E Nightly Regression Tests`) that surfaced this issue (run #191).

## Out of Scope
- Redesigning the Stock Operations page's loading/error/empty UI beyond what's needed to fix the root cause and add the FR-4 diagnostic improvement.
- Changes to `StockUpOperationRepository.QueryAsync`'s filtering/sorting/pagination logic unless FR-1 specifically implicates it as the cause.
- Broader refactor of the `waitForLoadingComplete`/`wait-helpers.ts` utilities beyond the targeted FR-4 change; the pre-existing race-condition notes in that file's docstring (US-001/US-002) describe a separate, already-addressed issue in the `catalog` module and are not part of this fix.
- Fixing or hardening other soft/vacuous assertions elsewhere in the E2E suite outside the `stock-operations` module, even if the same anti-pattern (case-mismatched route + `if (isVisible)` branching) is suspected to exist elsewhere.
- Any change to the `RemoveVerifiedStateFromStockUpOperations` or `AddPartialIndexForActiveStockUpOperations` migrations themselves; they are considered already-applied and correct unless FR-1 proves otherwise.

## Open Questions
1. What is the actual HTTP status/response body (or hang behavior) returned by staging for `GET /api/StockUpOperations` when called as the E2E test identity? This cannot be determined from static code review alone and blocks confirming which of the FR-2 sub-cases applies — needs a staging repro session (browser DevTools network tab, or backend logs for the nightly run's timeframe around run #191).
2. Does the E2E test identity used by the nightly Playwright run currently hold `Feature.Warehouse_StockUp` Read access in the staging role/permission configuration? If not, was this permission ever granted, or did the `stock-operations` E2E suite pass previously without it (suggesting a recent regression in role assignment rather than a pre-existing gap)?
3. Is there a way to correlate "since when" these 56 tests started failing (e.g., a previous nightly run where they passed), to determine whether this is a fresh regression (pointing at a recent deploy/config change) versus a suite that has never actually validated this path correctly? Run #191 alone doesn't establish this; earlier nightly run history should be checked in GitHub Actions.

## Status: HAS_QUESTIONS
