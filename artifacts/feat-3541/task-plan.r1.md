# Implementation Task Plan: Fix Issued Invoices page shell blocked by grid-data loading

Source documents: `artifacts/feat-3541/spec.r1.md`, `artifacts/feat-3541/arch-review.r1.md`,
`artifacts/feat-3541/design.r1.md`. All three agree this is a small, surgical frontend fix
(Decisions 1–3 in the arch review). This plan defines **one** task because the two files that
change (`useIssuedInvoices.ts` and `IssuedInvoicesPage.tsx`) are tightly coupled — the page's call
site cannot compile/pass without the hook's new `options` parameter existing first, and both edits
are reviewed together as a single before/after behavior change. Splitting them into separate PRs
would leave an intermediate broken state with no independent value. There is no meaningful second
task to extract without violating the "keep it tight" instruction.

**Explicitly excluded from this task** (per arch review Decision 4 and spec's Out of Scope): the
`Success: false` swallowing bug in `useIssuedInvoicesList` (i.e., adding a `if (!data.success) throw`
check mirroring `useIssuedInvoiceSyncStats.ts:53`) and the FR-5 backend latency diagnosis. Do not
touch `useIssuedInvoices.ts`'s response-handling logic beyond adding the `enabled` option. Do not
touch `GetIssuedInvoicesListHandler.cs` or `InvoicesController.cs`. These are tracked as a separate
follow-up per the architect's explicit decision to keep this PR's diff to the rendering/query-gating
defect only.

---

### task: decouple-issued-invoices-shell-from-grid-loading

**Goal**

Make the Issued Invoices page header (`<h1>Vydané faktury</h1>`) and tab navigation
(Statistiky / Seznam buttons) render immediately on mount, independent of the grid/list data
query's loading or error state, and stop that query from firing at all until the user opens the
Seznam tab. This fixes 29 nightly E2E specs in `frontend/test/e2e/issued-invoices/` that currently
time out because the tab bar never appears while `GET /api/invoices` is pending or slow.

**Context**

Root cause (confirmed in the current worktree, exact line numbers verified):

`frontend/src/pages/customer/IssuedInvoicesPage.tsx` calls `useIssuedInvoicesList(...)` at the page
level (lines 68–84) and, before returning the shell JSX, has:

```tsx
// lines 314–323
if (loading) {
  return (
    <div className="flex items-center justify-center h-64">
      <div className="flex items-center space-x-2">
        <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
        <div className="text-gray-500 dark:text-graphite-muted">Načítání faktur...</div>
      </div>
    </div>
  );
}

// lines 325–334
if (error) {
  return (
    <div className="flex items-center justify-center h-64">
      <div className="flex items-center space-x-2 text-red-600 dark:text-red-400">
        <AlertCircle className="h-5 w-5" />
        <div>Chyba při načítání faktur: {error.message}</div>
      </div>
    </div>
  );
}
```

`loading`/`error` here are destructured from `useIssuedInvoicesList` (the **grid** query, line 70
`isLoading: loading`, line 71 `error`). `activeTab` defaults to `'statistics'` (line 31), but these
guards run unconditionally before the `<h1>` (line 431) and tab `<nav>` (lines 435–460) are
reached — so the whole page is held hostage by a query for data the default-tab user never asked
for.

The fix does **not** need new JSX: an equivalent, correctly-scoped loading/error block already
exists — currently dead code — inside the `activeTab === 'grid'` branch at lines 591–604:

```tsx
{loading ? (
  <div className="flex items-center justify-center h-64">
    <div className="flex items-center space-x-2">
      <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
      <div className="text-gray-500 dark:text-graphite-muted">Načítání faktur...</div>
    </div>
  </div>
) : error ? (
  <div className="flex items-center justify-center h-64">
    <div className="flex items-center space-x-2 text-red-600 dark:text-red-400">
      <AlertCircle className="h-5 w-5" />
      <div>Chyba při načítání faktur: {(error as Error)?.message || 'Neočekávaná chyba'}</div>
    </div>
  </div>
) : (
  ... table ...
)}
```

This becomes live and reachable once the page-level guard is removed (arch review Decision 1,
Option 1 — delete outright, do not narrow the condition, do not extract a new `GridTab` component;
Options 2 and 3 were explicitly rejected as reintroducing coupling / over-scoping a surgical fix).

`useIssuedInvoicesList` (`frontend/src/api/hooks/useIssuedInvoices.ts`, lines 22–88) has no
`enabled` gate today — it always fires on mount. The design doc specifies the exact new shape
(arch review Decision 2, design doc "Component Design" section):

```ts
export interface UseIssuedInvoicesListOptions {
  enabled?: boolean;
}

export const useIssuedInvoicesList = (
  filters: IssuedInvoicesFilters,
  options?: UseIssuedInvoicesListOptions,
) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.issuedInvoices, filters],   // UNCHANGED — do not append options/enabled
    queryFn: async (): Promise<IGetIssuedInvoicesListResponse> => { /* unchanged body */ },
    enabled: options?.enabled ?? true,                    // NEW
    staleTime: 5 * 60 * 1000,                              // unchanged
    gcTime: 10 * 60 * 1000,                                // unchanged
  });
};
```

Critical constraint (design doc, "Interfaces and Contracts" + arch review Decision 2 rationale):
`enabled` must be a **second, optional** parameter — never folded into `IssuedInvoicesFilters` and
never appended to `queryKey`. Doing either would change the cache key and break FR-2's acceptance
criterion that switching back to Grid within `staleTime` (5 min) does not refetch. Default
(`options` omitted or `options.enabled` omitted) must be `true`, so any other current or future
caller of `useIssuedInvoicesList` keeps today's eager-fetch behavior with no changes required at
that call site. This mirrors the existing internal pattern already in the same file:
`useIssuedInvoiceDetail` (line 116) already does `enabled: !!invoiceId`.

The page's call site (lines 68–84) passes the new option:

```tsx
} = useIssuedInvoicesList(
  {
    pageNumber,
    pageSize,
    sortBy,
    sortDescending,
    invoiceId: invoiceIdFilter.trim() || undefined,
    customerName: customerNameFilter.trim() || undefined,
    invoiceDateFrom: invoiceDateFrom ? new Date(invoiceDateFrom).toISOString() : undefined,
    invoiceDateTo: invoiceDateTo ? new Date(invoiceDateTo).toISOString() : undefined,
    showOnlyUnsynced,
    showOnlyWithErrors,
  },
  { enabled: activeTab === 'grid' },   // NEW second argument
);
```

This is the same gating precedent already used in this file for `refetchRunningJobs` at the
existing `useEffect` (lines 191–195: `if (activeTab === 'grid') { refetchRunningJobs(); }`) — not a
new pattern for this component.

Finally, per spec FR-4 and arch review Decision 3, add a `data-loading="true"` marker to the
now-reachable loading container (line 592) so `waitForLoadingComplete()`
(`frontend/test/e2e/helpers/wait-helpers.ts`, lines 81–92) — which matches
`[data-loading="true"], .loading, .spinner, [aria-busy="true"]` and currently silently no-ops
because none of those match — actually blocks. This is a documented, known class of bug in this
same helper file (see the long comment at lines 94–130 describing the identical issue for the
Catalog module); this task only fixes the marker for the Issued Invoices grid, not the helper's
general reliability (out of scope per spec).

**Files to modify**

1. `frontend/src/api/hooks/useIssuedInvoices.ts`
2. `frontend/src/pages/customer/IssuedInvoicesPage.tsx`

No new files. No backend files. No test-helper files (`wait-helpers.ts` itself is not modified —
only the component markup that helper reads).

**Implementation steps**

1. In `frontend/src/api/hooks/useIssuedInvoices.ts`:
   a. Add `export interface UseIssuedInvoicesListOptions { enabled?: boolean; }` above
      `useIssuedInvoicesList` (after the `IssuedInvoicesFilters` interface, line 20).
   b. Change the `useIssuedInvoicesList` signature from
      `(filters: IssuedInvoicesFilters)` to
      `(filters: IssuedInvoicesFilters, options?: UseIssuedInvoicesListOptions)`.
   c. Add `enabled: options?.enabled ?? true,` to the `useQuery({...})` options object (alongside
      the existing `staleTime`/`gcTime`, line 85–86 area). Do not touch `queryKey` (line 25) or
      `queryFn` (lines 26–84).

2. In `frontend/src/pages/customer/IssuedInvoicesPage.tsx`:
   a. Update the `useIssuedInvoicesList(...)` call site (lines 73–84) to pass
      `{ enabled: activeTab === 'grid' }` as the second argument, per the exact snippet above.
   b. Delete the page-level `if (loading) { return ...; }` block (lines 314–323) in full.
   c. Delete the page-level `if (error) { return ...; }` block (lines 325–334) in full.
   d. Add `data-loading="true"` to the loading container inside the `activeTab === 'grid'` branch
      (the outermost `<div className="flex items-center justify-center h-64">` at line 592, the
      one wrapping the `Loader2`/"Načítání faktur..." markup — do **not** add it to the
      `StatisticsTab`'s equivalent loading block at line 340, which is out of scope; only the grid
      tab's marker is required by FR-4).
   e. Verify (visually re-read the file after edits) that the grid tab's loading/error/content
      ternary at (previously) lines 591–620 is otherwise untouched, and that no other reference to
      the deleted top-level `loading`/`error` guards remains (the `loading`/`error` variables
      themselves stay — they're still used inside the `activeTab === 'grid'` ternary and are still
      destructured from the hook at lines 68–71).

3. Do not modify: `StatisticsTab` inline component (lines 337–358), the `useEffect` at lines
   191–195, `useIssuedInvoiceSyncStats` usage (unconditional/eager, unchanged per spec Open
   Question 4 being explicitly out of scope), `IssuedInvoicesFilters` interface, any backend file,
   any OpenAPI-generated client file.

4. Do not add the `data.success` check to `useIssuedInvoicesList`'s `queryFn` — that is the
   Decision-4 follow-up, explicitly deferred to a separate PR. The `if (!response.ok) throw ...`
   at line 76–78 of `useIssuedInvoices.ts` stays exactly as-is.

**Tests to write/update**

No new E2E spec files are required — the 29 existing specs in
`frontend/test/e2e/issued-invoices/` (`filters.spec.ts`, `navigation.spec.ts`,
`status-badges.spec.ts`, `sorting.spec.ts`, `pagination.spec.ts`) already assert the tab bar and
heading appear; they are expected to pass once this fix lands, with no spec-file edits needed (per
spec's Out of Scope: do not redesign the `Promise.race` pattern in `filters.spec.ts`'s
`beforeEach`, lines 6–36, or remove existing `waitForTimeout` stabilization waits — they already
correctly detect the failure mode and should simply start passing).

Validation steps for the developer:
1. Local/dev manual check: load `/customer/issued-invoices` with the Network tab open — confirm
   `GET /api/invoices` is **not** issued until the "Seznam" tab is clicked, and that the header +
   both tab buttons are visible immediately regardless of that request's timing (per spec FR-1/FR-2
   acceptance criteria). If feasible, artificially delay `/api/invoices` locally (e.g., a dev-only
   network throttle or a temporary `await new Promise(r => setTimeout(r, 5000))` in a local branch
   only, never committed) to confirm the shell still renders and only the grid panel shows the
   scoped spinner.
2. Run the full `issued-invoices` module E2E suite locally/against a reachable backend per
   `docs/testing/playwright-e2e-testing.md` (using `navigateToApp()`-based auth, not
   `createE2EAuthSession()` alone) and confirm all 29 specs pass. If a local run against staging
   isn't feasible in this environment, this must be confirmed on the next nightly E2E run
   (`./scripts/run-playwright-tests.sh` against staging, per CLAUDE.md) — do not claim the fix is
   validated on staging without that run's evidence.
3. Confirm switching Statistics → Seznam → Statistics → Seznam again within 5 minutes does not
   re-issue `GET /api/invoices` a second time (React Query `staleTime` cache reuse, FR-2's third
   acceptance criterion) — observable via the Network tab (only one request logged across the
   round trip).

**Acceptance criteria**

- `h1:has-text("Vydané faktury")` and both `button:has-text("Statistiky")` /
  `button:has-text("Seznam")` render immediately on mount, regardless of `/api/invoices`'s timing,
  failure, or hang state (spec FR-1).
- No `GET /api/invoices` request fires while the default Statistics tab is active and Grid has
  never been opened; exactly one request fires on first switch to Seznam; no duplicate request on
  toggling back to Grid within `staleTime` (spec FR-2).
- The "Načítání faktur..." spinner and "Chyba při načítání faktur" message only ever appear inside
  the Grid tab's content region, never full-page; the header and tab bar remain visible and
  clickable at all times, including while the Grid tab is loading or erroring (spec FR-3).
- The Grid tab's loading container carries `data-loading="true"`, and `waitForLoadingComplete()`
  actually waits for it to disappear rather than no-op'ing (spec FR-4).
- `useIssuedInvoicesList`'s `queryKey` and cache behavior (`staleTime`, `gcTime`) are byte-for-byte
  unchanged; `IssuedInvoicesFilters` interface is unchanged; no wire/API contract changes.
- The `Success: false` response-swallowing issue (arch review Decision 4) is **not** touched in
  this change — `useIssuedInvoicesList`'s `queryFn` response handling (the `if (!response.ok)
  throw` check) is identical before and after this task's diff.
- No backend files (`GetIssuedInvoicesListHandler.cs`, `InvoicesController.cs`,
  `IssuedInvoiceRepository`) are modified.
- All 29 existing specs under `frontend/test/e2e/issued-invoices/` pass against the fixed
  component (confirmed locally where possible; confirmed on the next nightly staging run per
  CLAUDE.md's E2E validation rule — the nightly suite is not part of PR CI).
- `npm run build` and `npm run lint` both pass with no new errors or warnings introduced by this
  change (per CLAUDE.md's mandatory validation-before-completion rule).
- The diff touches only `frontend/src/api/hooks/useIssuedInvoices.ts` and
  `frontend/src/pages/customer/IssuedInvoicesPage.tsx` — no unrelated formatting, comment, or
  adjacent-code changes (CLAUDE.md "surgical changes" rule).
