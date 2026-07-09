# Specification: Fix Issued Invoices page shell blocked by grid-data loading (Seznam tab never appears in E2E)

## Summary

The nightly E2E regression run (#191, `main@738a99c`) failed 29 tests in the `issued-invoices` module because `button:has-text("Seznam")`, `h1:has-text("Vydané faktury")`, and `button:has-text("Statistiky")` all time out after 30s. Code inspection of `IssuedInvoicesPage.tsx` found the root architectural defect: the entire page shell (heading + tab bar) is gated behind the **grid tab's** data fetch (`useIssuedInvoicesList`), even though the page defaults to the **Statistics** tab and the fetch is unconditional. Combined with the frontend having no client-side request timeout on API calls, any slowness in the `GET /api/invoices` response — however caused — makes the whole page appear frozen on a spinner with no heading or tabs, which is exactly the failure signature reported.

## Background

`frontend/src/pages/customer/IssuedInvoicesPage.tsx` renders the "Vydané faktury" page with two tabs: "Statistiky" (default) and "Seznam" (grid). The component eagerly calls two independent data hooks on mount regardless of which tab is active:

- `useIssuedInvoicesList(...)` (grid data) — `IssuedInvoicesPage.tsx:68-84`
- `useIssuedInvoiceSyncStats(...)` (statistics data) — `IssuedInvoicesPage.tsx:87-95`

The component has a **page-level** early return keyed off the grid query's `loading`/`error` state, placed *before* the JSX that renders the `<h1>` heading and the tab `<nav>`:

```tsx
// IssuedInvoicesPage.tsx:314-334
if (loading) {
  return (
    <div className="flex items-center justify-center h-64">
      ...
      <div className="text-gray-500 ...">Načítání faktur...</div>
    </div>
  );
}

if (error) {
  return (
    <div className="flex items-center justify-center h-64">
      ...
      <div>Chyba při načítání faktur: {error.message}</div>
    </div>
  );
}
```

The heading and tab bar only appear in the JSX returned starting at `IssuedInvoicesPage.tsx:405` (`return (<div className="flex flex-col w-full" ...>`), which contains:
- `<h1>Vydané faktury</h1>` — line 431
- "Statistiky" tab button — lines 437-447
- "Seznam" tab button — lines 448-458

Because the early returns at lines 314-334 happen first, **the heading and both tab buttons are unreachable for as long as the grid list query is loading or errored** — regardless of which tab the user is on. `activeTab` defaults to `'statistics'` (line 31), so on first page load the grid data isn't even needed yet, but its fetch still blocks the entire shell.

This exactly explains all three timeout signatures in the bug report:
1. `button:has-text("Seznam")` never appears (brief's primary signature, `filters.spec.ts:34`).
2. `h1:has-text("Vydané faktury")` never appears (page never reaches its main return).
3. `button:has-text("Statistiky")` never appears (same reason — collateral damage from a tab-bar-wide gate keyed to one tab's data).

A secondary, compounding factor: neither the `useIssuedInvoicesList` fetch (`frontend/src/api/hooks/useIssuedInvoices.ts:69`) nor the shared authenticated fetch wrapper (`frontend/src/api/client.ts:283-292`, used by `getAuthenticatedApiClient()`) attaches an `AbortSignal`/timeout to the `fetch()` call. `getApiConfig()` (`client.ts:465-471`) defines a `timeout: 30000` value, but nothing consumes it to actually abort a hung request. So if the `GET /api/invoices` request is slow or hangs on staging, there is no client-side ceiling — the spinner (and therefore the whole page, per the bug above) can hang indefinitely, until Playwright's own 30s action-wait gives up.

The backend endpoint the grid query calls (`GET /api/invoices` → `InvoicesController.GetInvoicesList` → `GetIssuedInvoicesListHandler` → `IssuedInvoiceRepository.GetPaginatedAsync`) was inspected and found to be a plain EF Core/PostgreSQL query set (count + skip/take) with supporting indexes on `InvoiceDate`, `LastSyncTime`, `IsSynced`, `ErrorType`, and `CustomerName` (`IssuedInvoiceConfiguration.cs:128-142`). It makes **no external Shoptet/ABRA calls** in the read path — those only happen in the invoice *import* flow (`EnqueueImportInvoicesRequest`, the daily Hangfire jobs). No feature flag gates this page or its API calls (verified via grep across the page, hooks, and controller). So the *why is it slow on staging* question could not be conclusively answered by static code review; see Open Questions.

One candidate external cause was investigated and largely ruled out: `DailyInvoiceImportCzkJob`/`DailyInvoiceImportEurJob` (`backend/src/.../Infrastructure/Jobs/DailyInvoiceImport{Czk,Eur}Job.cs`) write to the same `IssuedInvoices` table and do call Shoptet, running at cron `15 4 * * *` / `0 4 * * *`. However, the nightly workflow (`.github/workflows/e2e-nightly-regression.yml`) starts at `0 1 * * *` (1:00 AM UTC), and Playwright's `globalTimeout` is 60 minutes (`frontend/playwright.config.ts:83`), so the full "all modules, 1 worker" run must finish by roughly 2:10 AM UTC — well before the 4:00-4:15 AM UTC import jobs. `issued-invoices` is also the 2nd of 9 projects Playwright discovers (`playwright.config.ts:86-131`), so it runs early in that window. A lock/contention overlap with the import jobs is therefore unlikely to be the trigger, though it cannot be fully excluded without the actual run's timestamps (GitHub Actions access was unavailable in this investigation — see Open Questions).

## Functional Requirements

### FR-1: Root cause diagnosis (this investigation)

Document the confirmed defect and rank hypotheses for why the underlying request is slow enough to exceed the 30s E2E wait.

**Findings:**
- **Confirmed code defect:** `IssuedInvoicesPage.tsx:314-334` — page-level `loading`/`error` early return, keyed to the grid-tab-only `useIssuedInvoicesList` query, sits above the JSX (line 405 onward) that renders the `<h1>` and tab `<nav>` (lines 429-461). This blocks the whole page shell on one tab's data, regardless of `activeTab`.
- **Confirmed contributing factor:** no client-side fetch timeout/`AbortSignal` anywhere in the request path (`useIssuedInvoices.ts:69`, `client.ts:283-292`), so a slow/hung backend response has no client-enforced ceiling.
- **Backend read path is external-call-free:** `GetIssuedInvoicesListHandler.cs`, `IssuedInvoiceRepository.GetPaginatedAsync` (`IssuedInvoiceRepository.cs:76-145`) are DB-only, with indexes present. Not proven to be the source of the actual multi-second-plus delay from static review alone.
- **Daily Shoptet import jobs are an unlikely but not fully excluded contributor** — see Background for the timing analysis.
- **No feature flag involvement** confirmed via search.

**Acceptance criteria:**
- The investigation notes above are available to the architect/dev phase without re-discovery (file/line citations included).
- Any follow-up diagnostic needed to pin the exact backend latency cause (e.g., App Insights query, staging DB row count) is called out explicitly rather than guessed at (see Open Questions).

### FR-2: Page shell must render independently of tab-specific data loading

The `<h1>Vydané faktury</h1>` heading and the "Statistiky"/"Seznam" tab buttons must render as soon as the page mounts, without waiting for any per-tab data fetch to resolve. Loading and error states must be scoped to the content area of the active tab only, matching the pattern already used correctly inside `StatisticsTab` (`IssuedInvoicesPage.tsx:337-358`, whose `syncStatsLoading`/`syncStatsError` handling is self-contained) and the grid tab's own inline loading/error block (`IssuedInvoicesPage.tsx:591-604`, which is currently dead code for this purpose since the outer early return at lines 314-334 pre-empts it).

Concretely: remove (or relocate) the page-level `if (loading) return ...` / `if (error) return ...` block at `IssuedInvoicesPage.tsx:314-334` so it no longer gates the return at line 405. The grid tab already has its own scoped loading/error rendering at lines 591-604 that can take over this responsibility for the grid tab only.

**Acceptance criteria:**
- On page load, with the grid-list API call artificially delayed or failing, the `<h1>Vydané faktury</h1>` and both tab buttons ("Statistiky", "Seznam") are visible and clickable immediately (no dependency on the grid query's resolution).
- Switching to the "Seznam" tab while the grid query is still loading shows a loading indicator scoped to the grid panel only (consistent with current lines 591-604 behavior), not a full-page blank/spinner state.
- The existing "Statistiky" tab loading/error behavior (`StatisticsTab`, lines 337-358) is unaffected.
- All 29 previously-failing `issued-invoices` E2E specs (`filters.spec.ts`, `pagination.spec.ts`, `sorting.spec.ts`, `status-badges.spec.ts`, `navigation.spec.ts`) pass against staging.

### FR-3: Bound API requests with a client-side timeout

Add an explicit timeout/`AbortSignal` to the fetch calls used by `useIssuedInvoicesList` (`useIssuedInvoices.ts:69`) and, ideally, to the shared `authenticatedHttp.fetch` wrapper in `getAuthenticatedApiClient()` (`client.ts:283-292`), using the existing `getApiConfig().timeout` value (`client.ts:469`) that is currently defined but unused. This is a defense-in-depth fix, independent of FR-2: even with FR-2 in place, a genuinely hung backend request should surface as a bounded, user-visible error rather than an indefinite spinner in the grid panel.

**Acceptance criteria:**
- A backend response that never resolves causes the affected query to fail with a client-observable error within the configured timeout window (not indefinitely).
- Existing successful requests (well under the timeout) are unaffected.
- Scope check: confirm whether other hooks sharing `getAuthenticatedApiClient()`/`authenticatedHttp.fetch` should also get this timeout, or whether it should be opt-in per hook — see Open Questions.

## Non-Functional Requirements

### NFR-1: Performance

- The Issued Invoices page shell (heading + tabs) must be interactive within the existing app shell's normal load budget, independent of grid/statistics data latency.
- `GET /api/invoices` (default, unfiltered, first page) should be confirmed to respond within a low-single-digit-second budget on staging under normal conditions; if it does not, that is a separate backend performance issue to investigate (see Open Questions) — FR-2/FR-3 make the UI resilient to it either way but do not by themselves fix a genuinely slow backend.

### NFR-2: Security

- No change in authentication/authorization surface. `GetInvoicesList` continues to require the same auth as today; no new endpoints introduced.

## Data Model

No data model changes. Relevant existing entities:
- `IssuedInvoice` (`backend/src/Anela.Heblo.Domain/Features/Invoices/`), mapped via `IssuedInvoiceConfiguration.cs` to `public.IssuedInvoices`, with indexes on `InvoiceDate`, `LastSyncTime`, `IsSynced`, `ErrorType`, `CustomerName`.
- `IssuedInvoiceSyncStats` — aggregate computed on demand by `GetSyncStatsAsync` (`IssuedInvoiceRepository.cs:35-58`), not persisted.

## API / Interface Design

No API contract changes required for FR-2 (frontend-only fix). Data flow as traced:

1. `IssuedInvoicesPage` mounts → unconditionally calls `useIssuedInvoicesList(...)` (`useIssuedInvoices.ts:23-88`) and `useIssuedInvoiceSyncStats(...)`.
2. `useIssuedInvoicesList` builds `GET /api/invoices?pageNumber=...&pageSize=...&sortBy=...` and fetches via `getAuthenticatedApiClient().http.fetch` (bypassing the generated client's typed method, calling `.fetch` directly — `useIssuedInvoices.ts:66-74`).
3. `InvoicesController.GetInvoicesList` (`InvoicesController.cs:25-32`) → MediatR → `GetIssuedInvoicesListHandler.Handle` (`GetIssuedInvoicesListHandler.cs:29-80`) → `IssuedInvoiceRepository.GetPaginatedAsync` (`IssuedInvoiceRepository.cs:76-145`) → PostgreSQL via EF Core.
4. Response resolves `loading=false` in the `useQuery` result; only then does `IssuedInvoicesPage` fall through past the line 314-334 early return to render the shell.

If FR-3 is implemented, the fetch call at step 2 gains a bounded `AbortSignal`/timeout so step 4 always resolves (success or bounded failure) within a fixed window.

## Dependencies

- **PostgreSQL** (via EF Core) — sole backing store for `GET /api/invoices` and `GET /api/invoices/stats`; no external system in this read path.
- **Azure Entra ID / MSAL** — auth token acquisition for `getAuthenticatedApiClient()`; not implicated by this investigation (other modules' E2E tests were not reported failing in the same run, per the brief, which is inconsistent with a global auth issue).
- **Shoptet / ABRA Flexi** — used only by the invoice *import* path (`EnqueueImportInvoicesRequest`, `DailyInvoiceImportCzkJob`/`DailyInvoiceImportEurJob`), not by the list/stats read path this bug affects. Investigated as a possible indirect cause (DB contention during import) and found unlikely to overlap with when `issued-invoices` E2E tests run, per the nightly workflow's timing (see Background).
- **GitHub Actions nightly workflow** (`.github/workflows/e2e-nightly-regression.yml`) — deploys `main` to staging (`heblo-test` Web App, restarted) immediately before the E2E run, then polls `/health/ready`. A cold-start effect immediately after this restart is plausible but not confirmed (see Open Questions).

## Out of Scope

- Any change to the actual backend query performance (e.g., adding a covering index, query rewrite) — not warranted by evidence gathered here; revisit only if staging diagnostics (Open Questions) show the DB query itself is slow.
- Changing the daily invoice import job schedule or the nightly E2E workflow's cron/timing.
- Broader adoption of client-side fetch timeouts across all API hooks beyond `useIssuedInvoicesList` (FR-3 scope should be decided during implementation planning).
- Retrying/backoff strategy changes for React Query beyond what's already configured globally (`retry: 1` in `frontend/src/App.tsx:104-112`).

## Open Questions

1. **What is the actual staging latency of `GET /api/invoices` at the time of the failing run?** This investigation could not access GitHub Actions run #191 (`gh run view` returned "GitHub access is not enabled for this session") or staging Application Insights, so the *magnitude and cause* of the underlying delay (cold start after the pre-test deploy/restart, DB connection pool warm-up, genuinely slow query, or something else) remains unconfirmed. Recommend pulling App Insights request duration for `GET /api/invoices` on staging around the run-#191 timeframe, and/or staging Postgres slow-query log, before finalizing FR-2/FR-3 as a complete fix versus a resiliency improvement that masks a real backend regression.
2. **Should FR-3's client-side timeout apply to all `getAuthenticatedApiClient()`-backed requests, or only to `useIssuedInvoicesList`?** Applying it broadly is more consistent but is a larger blast-radius change; scoping it narrowly is safer but leaves the same "unbounded spinner" risk in other pages. Assumption for this spec: start narrow (this hook only) unless the architect phase decides otherwise.
3. **Is the nightly workflow's pre-test `az webapp restart` (`e2e-nightly-regression.yml:94-97`) followed by a long enough warm-up before tests begin?** The workflow does poll `/health/ready` (up to 20×15s) before starting tests, which should cover basic warm-up, but does not specifically warm the `IssuedInvoices` table's first query. Worth confirming whether `/health/ready` exercises a DB round-trip equivalent to the invoices query.
4. Git history in this worktree is squashed per-feature by the AgentHarness pipeline (verified: the entire `IssuedInvoicesPage.tsx`, `useIssuedInvoices.ts`, and `IssuedInvoiceRepository.cs` appear as full-file additions in a single unrelated commit, `bd2efd3`, "#3519: Split GridLayouts..."), so no meaningful "recent regression commit" could be identified via `git log`/`git blame` in this environment. If the real repository (outside this worktree) has non-squashed history, re-running blame there against `IssuedInvoicesPage.tsx:314-334` may pinpoint when the page-level gating was introduced.

## Status: HAS_QUESTIONS
