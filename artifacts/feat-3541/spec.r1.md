# Specification: Fix Issued Invoices page shell blocked by grid-data loading (Seznam tab never appears)

## Summary

29 nightly E2E tests in the `issued-invoices` module fail on staging because the "Seznam" (Grid) tab button, the "Vydané faktury" heading, and even the "Statistiky" tab never render within the 30s Playwright timeout. Root cause: `IssuedInvoicesPage.tsx` gates rendering of the **entire page shell** (heading + tab navigation) behind the loading state of the grid/list data query (`useIssuedInvoicesList`), even though the default active tab is "Statistiky" and does not need that data at all. Any slowness, hang, or delay in the `/api/invoices` call — the exact cause of which still needs to be confirmed on staging — leaves the user (and the E2E tests) staring at a bare "Načítání faktur..." spinner indefinitely, with no tab bar to interact with.

## Background

`IssuedInvoicesPage` (`frontend/src/pages/customer/IssuedInvoicesPage.tsx`) was added whole in commit `bd2efd3` (2026-07-07) together with its E2E coverage in `frontend/test/e2e/issued-invoices/*.spec.ts`. The component fetches two independent datasets on mount, unconditionally, regardless of which tab is active:

- `useIssuedInvoicesList(...)` (`frontend/src/api/hooks/useIssuedInvoices.ts`) — powers the **Grid** ("Seznam") tab. Calls `GET /api/invoices`.
- `useIssuedInvoiceSyncStats(...)` (`frontend/src/api/hooks/useIssuedInvoiceSyncStats.ts`) — powers the **Statistics** ("Statistiky") tab, which is the default (`activeTab` initial state is `'statistics'`).

The component's render logic has this shape (lines 314–334 and 405+):

```tsx
if (loading) {               // loading === useIssuedInvoicesList().isLoading
  return <...spinner only, no header, no tabs...>;
}
if (error) {
  return <...error only, no header, no tabs...>;
}
return (
  <div>
    {/* h1 "Vydané faktury" + tab nav (Statistiky / Seznam) rendered here */}
    ...
  </div>
);
```

Because `loading` reflects the **list** query and is checked before the tab bar is returned at all, the page header and tab navigation cannot appear until `/api/invoices` resolves — even for a user who only wants Statistics, and even though `useIssuedInvoiceSyncStats` has its own independent loading/error handling scoped inside `StatisticsTab`. There is no `enabled` flag on `useIssuedInvoicesList`, so it always fires on mount regardless of `activeTab`.

The nightly run (`#191`, commit `738a99c`, target `https://heblo.stg.anela.cz`) shows all `issued-invoices` specs timing out waiting for `button:has-text("Seznam")` (and, in a subset of cases, `h1:has-text("Vydané faktury")` / `button:has-text("Statistiky")`), which is consistent with this blocking-render architecture combined with the list request taking longer than 30s (or failing to resolve) on staging.

A secondary contributing factor confirmed in code: `waitForLoadingComplete()` (`frontend/test/e2e/helpers/wait-helpers.ts`) looks for `[data-loading="true"], .loading, .spinner, [aria-busy="true"]`, none of which `IssuedInvoicesPage` renders (its loading UI is a plain `<div>` with a `Loader2` icon and Czech text). This helper is already known to return immediately without actually waiting (see the inline comment in `wait-helpers.ts` documenting the same issue for `CatalogList`, and the workaround comment on `filters.spec.ts` test 4). It masks — rather than causes — the present failure, but the `beforeEach` block in `filters.spec.ts` (lines 6–36) does NOT rely on `waitForLoadingComplete` for the initial page-shell wait; it uses an explicit `Promise.race` between the loading-text disappearing and the Grid tab becoming visible, with a clear error path. That defensive coding proves the test authors already anticipated this exact failure mode — the assertion is correct; the application under test is the one that's broken.

This is a bug-fix task: correct the coupling between page-shell rendering and per-tab data loading, and confirm/address why `/api/invoices` is slow or hanging on staging. It is not a new feature.

## Functional Requirements

### FR-1: Decouple page shell (heading + tab navigation) from grid-data loading state

The `h1` "Vydané faktury" heading and the tab navigation bar (both "Statistiky" and "Seznam" buttons) must render as soon as the component mounts, independent of `useIssuedInvoicesList`'s `isLoading`/`error` state. The top-level `if (loading) return ...` / `if (error) return ...` guards currently at lines 314–334 of `IssuedInvoicesPage.tsx` must be removed or narrowed so they no longer short-circuit before the shared header/tab JSX.

**Acceptance criteria:**
- On initial page load (default tab = Statistics), the `h1:has-text("Vydané faktury")` and both tab buttons (`button:has-text("Statistiky")`, `button:has-text("Seznam")`) are visible within a small, bounded time (see NFR-1) even if `/api/invoices` has not yet returned.
- This holds regardless of whether the list request is slow, fails, or is still pending.

### FR-2: Only fetch grid list data when the Grid tab is active (or has been activated)

`useIssuedInvoicesList` must not fire an eager network request while the user is on the Statistics tab and has never opened the Grid tab. Gate the query with an `enabled` option tied to `activeTab === 'grid'` (React Query supports lazy/conditional fetching this way), consistent with how `refetchRunningJobs` is already only triggered when `activeTab === 'grid'` (existing `useEffect` at lines 191–195).

**Acceptance criteria:**
- With the network panel open, loading the page with the default Statistics tab active issues no request to `/api/invoices`.
- Switching to the Seznam tab for the first time triggers exactly one `/api/invoices` request (existing filter/sort/pagination-triggered refetches are unaffected).
- Switching back to Statistics and then back to Grid does not re-fetch if data is still within `staleTime` (existing React Query caching behavior preserved).

### FR-3: Scope the list-loading and list-error UI to the Grid tab content area only

The "Načítání faktur..." spinner and the "Chyba při načítání faktur" error message must only replace the **Grid tab's content region** (inside the `activeTab === 'grid'` branch, where equivalent loading/error UI already exists at lines 591–604), not the whole page. Remove the now-redundant top-level loading/error blocks entirely once FR-1 is implemented, since the in-tab-content copies already exist and are functionally equivalent.

**Acceptance criteria:**
- While `/api/invoices` is pending, the Grid tab (once selected) shows the existing scoped spinner inside the data-grid container; the header and tab bar remain interactive and clickable throughout.
- The Statistics tab is fully usable (cards, chart) while the Grid tab's data is loading or has errored, and vice versa.

### FR-4: Add/confirm a machine-readable loading marker for E2E stability

Add a `data-loading="true"` attribute (or equivalent, e.g. `aria-busy="true"`) to the Grid tab's loading container so `waitForLoadingComplete()` (`frontend/test/e2e/helpers/wait-helpers.ts`) works as intended instead of silently no-op'ing. This does not fix the root cause by itself but prevents the same class of "test looks like it waited but didn't" failure recurring here, consistent with the precedent already documented in `wait-helpers.ts` for the Catalog module.

**Acceptance criteria:**
- `waitForLoadingComplete(page)` called while the Grid tab is fetching actually blocks until the spinner is gone (verified by a short artificial delay in a local/dev run, or by code inspection confirming the selector now matches).
- No existing `waitForTimeout` workarounds in `filters.spec.ts` are required to be removed as part of this fix (they may remain until a separate test-cleanup pass), but no NEW arbitrary timeouts should be introduced to compensate for this bug.

### FR-5: Diagnose and address the underlying `/api/invoices` slowness/hang on staging

Independent of the frontend architecture fix (FR-1–FR-3), determine why `GET /api/invoices` took long enough (or failed to resolve) to blow a 30s Playwright timeout on staging during run #191. `GetIssuedInvoicesListHandler` (`backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoicesList/GetIssuedInvoicesListHandler.cs`) delegates directly to `IIssuedInvoiceRepository.GetPaginatedAsync` — a straightforward paginated DB query with no obvious N+1 or synchronous external (Shoptet) call in the request path — so the delay is not self-evident from the handler alone and needs to be confirmed with staging logs/APM (see Open Questions).

Additionally, note that `GetIssuedInvoicesListHandler`'s `catch` block returns `Success = false` with an `ErrorCode`, but `InvoicesController.GetInvoicesList` always returns `Ok(result)` (HTTP 200) regardless of `Success`. The frontend hook (`useIssuedInvoices.ts`) only treats non-2xx responses as errors (`if (!response.ok) throw ...`); it never inspects `data.success`/`data.errorCode`. A backend business-logic failure is therefore currently swallowed as an empty list rather than surfaced as an error — this does not explain the 30s timeout (loading would resolve, just with empty data) but is a related correctness gap worth fixing in the same change since it affects the same code path and the same error-handling story tested by `filters.spec.ts`'s `beforeEach`.

**Acceptance criteria:**
- A concrete cause for the staging timeout is identified and documented (e.g., slow query due to missing index, connection pool exhaustion, cold start, auth/token acquisition stall in `getAuthenticatedApiClient()`, or transient staging incident) — see Open Questions for how this investigation should be scoped.
- `useIssuedInvoicesList`'s fetch wrapper (or the handler/controller) is updated so a `Success: false` response is surfaced as an error state in the UI (`error` from `useIssuedInvoicesList`) rather than silently rendering as zero rows.

## Non-Functional Requirements

### NFR-1: Performance

- Page shell (heading + tab bar) must be interactive within the app's existing FCP/render budget for other pages in this module (no explicit SLA previously existed for this page; use "same order of magnitude as other Customer-module pages," i.e., well under 2s under normal staging conditions), independent of any single tab's data-fetch latency.
- `GET /api/invoices` at default page size (20) should complete in line with other paginated list endpoints in the app (existing precedent, e.g., `/api/catalog`); no new explicit SLA is introduced by this fix, but the investigation in FR-5 should record the observed p50/p95 latency on staging for future regression comparison.

### NFR-2: Security

No change to authentication/authorization. The page continues to rely on the existing MSAL-based `getAuthenticatedApiClient()` flow; no new endpoints or data exposure are introduced. If FR-5's investigation finds the hang is caused by token acquisition stalling, that must be fixed within the existing auth flow, not worked around by weakening auth.

## Data Model

No data model changes. Existing entities involved:
- `IssuedInvoiceDto` / `GetIssuedInvoicesListResponse` (paginated list, `Items`, `TotalCount`, `PageNumber`, `PageSize`, `Success`, `ErrorCode`, `Params`).
- `GetIssuedInvoiceSyncStatsResponse` (aggregate counts used by the Statistics tab, unaffected by this fix).

## API / Interface Design

No API contract changes required for FR-1–FR-4 (purely frontend rendering/query-gating changes). FR-5 may result in:
- A backend fix (query optimization, index, or removal of an accidental synchronous blocking call) with no change to the `GET /api/invoices` request/response shape.
- Optionally, the controller/handler being adjusted so failure responses are distinguishable by HTTP status or the frontend hook is adjusted to check `data.success` before treating a 200 response as a success — this is a contract-compatible change (`GetIssuedInvoicesListResponse` already carries `Success`/`ErrorCode`; only the frontend's handling of it changes, unless the team prefers to also change the controller to return a non-2xx status on `Success: false`, which would be a breaking-ish change to confirm with the team — see Open Questions).

UI flow (post-fix):
1. User navigates to `/customer/issued-invoices`.
2. Header ("Vydané faktury") and tab bar (Statistiky / Seznam) render immediately.
3. Statistics tab (default) loads its own data independently; Grid tab's query is not fired yet.
4. User clicks "Seznam" → Grid tab's query fires (first time only) → scoped spinner shows inside the grid container → data renders or a scoped error message renders, without ever hiding the header/tab bar.

## Dependencies

- `@tanstack/react-query` `enabled` option (already used elsewhere in the codebase; no new dependency).
- Existing E2E infrastructure: `frontend/test/e2e/helpers/e2e-auth-helper.ts` (`navigateToIssuedInvoices`), `frontend/test/e2e/helpers/wait-helpers.ts` (`waitForLoadingComplete`), staging environment `https://heblo.stg.anela.cz`.
- Staging observability (App Insights / logs / whatever APM the project uses) to complete FR-5's diagnosis — not confirmed which tool is available; see Open Questions.

## Out of Scope

- Rewriting `filters.spec.ts` / `pagination.spec.ts` / `sorting.spec.ts` / `status-badges.spec.ts` / `navigation.spec.ts` beyond what's needed to keep them passing against the fixed component (e.g., no requirement to remove the existing `waitForTimeout(500)` stabilization waits called out in the tests, or to redesign the `Promise.race` pattern in `filters.spec.ts`'s `beforeEach` — it already correctly detects the failure mode and should keep working once the app is fixed).
- General cleanup of `waitForLoadingComplete`'s known unreliability across the whole E2E suite (only the Issued Invoices page's markers are addressed here, per FR-4).
- Broader refactor of `IssuedInvoicesPage.tsx` (e.g., splitting into subcomponents, moving state to a reducer) beyond what FR-1–FR-3 require.
- Changing the `IssuedInvoicesFilters`/pagination/sorting behavior itself — those are assumed to work correctly once the page actually renders (that's what the 29 failing tests are trying to verify, but the failures are 100% attributable to the tab bar never appearing, not to filter/sort/pagination logic itself).
- Any changes to the Shoptet invoice sync/import pipeline (`ShoptetInvoiceClient`, `ShoptetApiInvoiceSource`) unless FR-5's investigation specifically implicates them.

## Open Questions

1. **FR-5 root cause confirmation**: What staging observability is available to confirm the actual `/api/invoices` response time during run #191 (App Insights request duration, container logs, DB slow-query log)? Assumption made for this spec: the investigating engineer will check staging logs/APM around the run's timestamp; if no APM exists, add temporary timing logs around `GetPaginatedAsync` as a stopgap and re-run the nightly suite to capture fresh data.
2. **Failure-response contract change**: Should `InvoicesController.GetInvoicesList` start returning a non-2xx status when `Success: false` (breaking-ish, but simpler for all consumers), or should `useIssuedInvoicesList` be changed to check `data.success` on an otherwise-200 response (backward compatible, more code paths to update)? Assumption made for this spec: prefer the frontend-side fix (check `data.success`) to avoid changing an existing contract other consumers might depend on — flag for confirmation with the developer before implementation.
3. Is there a known reason `/api/invoices` specifically (vs. `/api/invoices/stats`) would be slower on staging right now (e.g., recent data volume growth from a bulk import, a missing index after a recent migration)? Nothing in the repo history reviewed here points to a specific culprit; this needs a live staging check, not just static code review.
4. Should FR-2's tab-gated fetching also apply retroactively to `useIssuedInvoiceSyncStats` (i.e., should Statistics tab data similarly wait for `activeTab === 'statistics'`)? Out of scope as stated above since Statistics is the default tab and its query firing eagerly on mount is intentional/harmless to this bug, but worth flagging in case the team wants full tab-lazy-loading symmetry.

## Status: HAS_QUESTIONS
