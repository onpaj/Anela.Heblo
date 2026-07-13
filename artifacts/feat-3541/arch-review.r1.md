# Architecture Review: Fix Issued Invoices page shell blocked by grid-data loading

## Skip Design: true

Pure bug fix to existing rendering/data-fetching logic. No new screens, components, or visual
design decisions — the heading and tab bar already exist and are visually correct; the defect is
purely in *when* they render and which query gates them. `Skip Design: true`.

## Architectural Fit Assessment

This is a textbook regression against an already-established, already-correct convention in this
codebase, not a new architectural problem. Three sibling pages follow the same shape and get it
right:

- `frontend/src/pages/customer/BankStatementsOverviewPage.tsx` (lines 12–63) — shell (`<h1>` +
  tab `<nav>`) renders unconditionally with **zero** data hooks at the page level; each tab is a
  separate component (`StatisticsTab`, `ImportTab`) that owns its own fetch/loading/error state.
- `frontend/src/pages/KnowledgeBasePage.tsx` (lines 28–61) — same shape.
- `frontend/src/pages/ArticlesPage.tsx` (lines 28–85) — `isLoading` is passed down as a prop into
  the list component, never used to gate the page shell.

`IssuedInvoicesPage.tsx` deviates: it calls `useIssuedInvoicesList` (grid) at the page level, and
places a page-level `if (loading) return …` / `if (error) return …` guard (lines 314–334, verified
in the current worktree) *before* the JSX that renders the `<h1>` (line 431) and tab `<nav>` (lines
435–460). Because `activeTab` defaults to `'statistics'` (line 31) but the guard is keyed to the
**grid** query, the entire shell — including the tab the user is actually on — is held hostage by
a fetch the user hasn't asked for yet. The grid tab already has its own correctly-scoped
loading/error block (lines 591–604) that is currently unreachable dead code because the page-level
guard short-circuits before it.

`StatisticsTab` (lines 337–358, an inline component within the same file) is the one piece of this
file that already follows the correct pattern: `syncStatsLoading`/`syncStatsError` are fully
self-contained within its own render, not hoisted to the page level. The fix is to make the grid
tab behave the same way `StatisticsTab` already does — not to invent a new pattern.

No backend, contract, or cross-module changes are required for the core fix. `GET /api/invoices`
(`InvoicesController.GetInvoicesList` → `GetIssuedInvoicesListHandler` →
`IssuedInvoiceRepository.GetPaginatedAsync`) is unchanged; this is a frontend rendering/query-gating
fix only.

## Proposed Architecture

### Component Overview

```
IssuedInvoicesPage
├── (page level, mount-time)
│   ├── useIssuedInvoiceSyncStats(...)      # unconditional, unchanged
│   └── useIssuedInvoicesList(..., { enabled: activeTab === 'grid' })  # now tab-gated
├── Shell (renders unconditionally, no data-dependent early return)
│   ├── <h1>Vydané faktury</h1>
│   └── <nav> Statistiky | Seznam </nav>
└── Tab Content (mutually exclusive, each owns its own loading/error region)
    ├── activeTab === 'statistics' → StatisticsTab
    │     (already correct: syncStatsLoading/syncStatsError scoped internally, lines 337–358)
    └── activeTab === 'grid' → grid panel
          (filters/import bar + data grid; loading/error scoped to the grid container,
           lines 591–604 — currently dead code, becomes live once the page-level guard is removed)
```

No new components are introduced. The existing two-branch structure (`StatisticsTab` inline
component + inline grid JSX under `activeTab === 'grid'`) is preserved as-is; only the gating logic
changes.

### Key Design Decisions

#### Decision 1: Remove the page-level `if (loading) / if (error)` guard (lines 314–334)

**Options considered:**
1. Delete the guard outright and rely on the grid tab's existing scoped loading/error block
   (lines 591–604).
2. Keep a page-level guard but narrow its condition to something like
   `loading && activeTab === 'grid' && !hasEverMountedShell`.
3. Extract the grid tab into its own `GridTab` component (mirroring
   `BankStatementsOverviewPage`'s `ImportTab` / `StatisticsTab` split) so the page component itself
   never touches `useIssuedInvoicesList`'s loading state at all.

**Chosen approach:** Option 1 — delete the guard. The scoped replacement already exists in the
file (lines 591–604) and is functionally equivalent; no new JSX needs to be written.

**Rationale:** Option 2 reintroduces exactly the coupling that caused the bug, just with an extra
condition — fragile and harder to reason about. Option 3 (full extraction into `GridTab.tsx`/
`StatisticsTab.tsx` files matching the `BankStatementsOverviewPage` convention) is architecturally
the *nicest* long-term shape, but the spec explicitly places "broader refactor of
`IssuedInvoicesPage.tsx`... beyond what FR-1–FR-3 require" out of scope, and CLAUDE.md's "surgical
changes" rule applies directly here: touch only what the bug requires. Option 1 fixes the defect
with the smallest possible diff and reuses code that's already written and already correct.

#### Decision 2: Gate `useIssuedInvoicesList` with `enabled: activeTab === 'grid'` (spec FR-2)

**Options considered:**
1. Leave the grid query eager (fires on mount regardless of active tab), rely solely on Decision 1
   to keep the shell unblocked.
2. Add `enabled: activeTab === 'grid'` to `useIssuedInvoicesList`'s `useQuery` options.

**Chosen approach:** Option 2.

**Rationale:** Decision 1 alone is sufficient to fix the *reported* bug (shell no longer blocks on
this query). But leaving the grid query eager means a slow/hanging `/api/invoices` response is
still fetched on every page load even when the user never opens the Seznam tab — the exact request
that caused this incident keeps firing unnecessarily. `enabled` is a one-line, well-established
React Query option already used implicitly via the `useEffect` gate at lines 191–195
(`refetchRunningJobs` only fires when `activeTab === 'grid'`), so this isn't a new pattern for this
file. Risk is low and the diff is trivial; recommend including it in this PR.

#### Decision 3: Add `data-loading="true"` to the grid loading container (spec FR-4)

**Options considered:**
1. Leave the loading container as-is (plain `<div>` with `Loader2` + Czech text, no machine
   marker).
2. Add `data-loading="true"` (or `aria-busy="true"`) to the container at (formerly dead, now live)
   lines 591–597.

**Chosen approach:** Option 2.

**Rationale:** `waitForLoadingComplete()` (`frontend/test/e2e/helpers/wait-helpers.ts`) looks for
`[data-loading="true"], .loading, .spinner, [aria-busy="true"]` and silently no-ops when none
match — a known issue already documented in that file for the Catalog module. Once this page's
shell fix makes the scoped spinner reachable, leaving it unmarked reproduces the same "test looks
like it waited but didn't" failure class for a different reason. This is a one-attribute addition
to markup already being touched in this PR (same lines as Decision 1's fix) — effectively free to
include.

#### Decision 4: FR-5 (backend latency diagnosis) and the `Success: false` swallowing bug — split out of this PR

**Options considered:**
1. Bundle FR-5's diagnosis and the `Success: false`-swallowed-as-empty-list fix into this PR.
2. Fix only the rendering/gating defect (Decisions 1–3) in this PR; track FR-5 separately.

**Chosen approach:** Option 2.

**Rationale — diagnosis half of FR-5:** Confirming *why* `/api/invoices` was slow enough to blow a
30s Playwright timeout requires staging Application Insights / logs access this pipeline does not
have (the spec's own Open Question #1/#3 already flags this as unresolved and unresolvable via
static review). This review does not attempt to answer it. Instead, the architecture is designed
to be **correct regardless of the underlying latency cause**: once the shell no longer blocks on
the grid query (Decision 1) and the grid query only fires when the user actually opens that tab
(Decision 2), a slow or hanging `/api/invoices` response degrades to "the Seznam tab shows a
spinner" — a contained, recoverable UI state — instead of "the entire page, including tabs the user
didn't ask for, is frozen." That is the resilience this fix can deliver without knowing the root
cause.

**Rationale — `Success: false` swallowing:** This is a real, independent correctness bug
(`GetIssuedInvoicesListHandler`'s catch block returns `Success = false` +
`ErrorCode = ErrorCodes.Exception`, but `InvoicesController.GetInvoicesList` unconditionally
returns `Ok(result)`, and `useIssuedInvoices.ts`'s hand-rolled fetch only treats non-2xx as an
error — so a caught backend exception silently renders as "0 invoices found" instead of an error).
It is **not** the cause of the reported 30s-timeout failure (the spec itself says so: "this does
not explain the 30s timeout"). It was found incidentally during this investigation, on a code path
adjacent to — but not the same as — the one this PR fixes. Per CLAUDE.md's "surgical changes" rule
and the instruction to keep a single-developer-reviewed bugfix PR tight, this belongs in its own
follow-up PR with its own test, not folded into the E2E-unblocking fix. It is *not* an "open
design question" in the way spec Open Question #2 frames it, though: the codebase already answers
it — `useIssuedInvoiceSyncStats.ts:53` (`if (!data.success) { throw new Error(...) }`) is the
sibling hook for the *same feature*, calling `getAuthenticatedApiClient()` the same hand-rolled
way, and it already checks `data.success`. The follow-up fix is: make `useIssuedInvoicesList` do
exactly what `useIssuedInvoiceSyncStats` already does — no controller/HTTP-status change, no new
open question to resolve with the team.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. All changes are within:
- `frontend/src/pages/customer/IssuedInvoicesPage.tsx` — remove lines 314–334; add
  `data-loading="true"` to the container currently at lines 591–597.
- `frontend/src/api/hooks/useIssuedInvoices.ts` — add `enabled: activeTab === 'grid'` to the
  `useQuery` options in `useIssuedInvoicesList` (requires passing `activeTab` or an `enabled`
  boolean into the hook's filters/options; keep the hook's existing signature shape rather than
  inventing a new parameter-passing convention — e.g. extend `IssuedInvoicesFilters` is the wrong
  place since `enabled` isn't a filter; add a second, optional `options: { enabled?: boolean }`
  argument to `useIssuedInvoicesList`, matching how `useIssuedInvoiceDetail` already uses `enabled`
  internally at line 116 for its own `!!invoiceId` gate).

Follow-up (separate PR, not this one): `useIssuedInvoices.ts` — add the `data.success` check
mirroring `useIssuedInvoiceSyncStats.ts:53`.

### Interfaces and Contracts

No DTO, controller, or API contract changes. `GetIssuedInvoicesListResponse` is unchanged.
`IssuedInvoicesFilters` (the frontend filter interface, `useIssuedInvoices.ts:8–20`) is unchanged;
the tab-gating flag is a `useQuery`-level `enabled` option, not a filter value, and must not be
folded into `IssuedInvoicesFilters` or the query key — doing so would change the query's cache key
and defeat the `staleTime`-based caching FR-2's acceptance criteria depends on ("switching back to
Statistics and then back to Grid does not re-fetch if data is still within `staleTime`").

### Data Flow

1. `IssuedInvoicesPage` mounts. `activeTab` = `'statistics'`. Shell (`<h1>` + tab `<nav>`) renders
   immediately — no data dependency.
2. `useIssuedInvoiceSyncStats` fires unconditionally (unchanged); `StatisticsTab` renders its own
   scoped loading/error/content based on that query alone.
3. `useIssuedInvoicesList` does **not** fire yet (`enabled: false` while `activeTab !== 'grid'`).
4. User clicks "Seznam" → `activeTab` becomes `'grid'` → `useIssuedInvoicesList` becomes enabled →
   fires `GET /api/invoices` for the first time → the grid panel (not the whole page) shows its own
   scoped spinner (now carrying `data-loading="true"`) → resolves to data or a scoped error, while
   the shell and tab bar remain interactive throughout.
5. Subsequent tab switches back and forth reuse React Query's cache per the existing `staleTime: 5
   * 60 * 1000` / `gcTime: 10 * 60 * 1000` — unaffected by this fix.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Removing the page-level guard also removes its (currently unreachable) error surface, and a mistake in the refactor leaves the grid tab with no error UI at all | Medium | The scoped block at lines 591–604 already contains both the loading *and* error branches; verify with a manual test (artificially force `useIssuedInvoicesList` to reject) that the grid panel — not the whole page — shows the error message, per FR-2/FR-3 acceptance criteria |
| `enabled: activeTab === 'grid'` changes when the first `/api/invoices` request fires (on tab click instead of on mount), which could make the Seznam tab feel slower on first click than today (today's request is already in flight by the time the user gets there) | Low | Acceptable, explicitly called out in spec FR-2's acceptance criteria as the intended behavior; the scoped spinner (Decision 3) makes this an expected, bounded wait rather than a silent stall |
| Existing E2E specs (`filters.spec.ts`, `pagination.spec.ts`, etc.) may have assumptions baked in about the grid query firing on page load (e.g. timing assumptions in `beforeEach`) | Medium | Spec's Out of Scope section explicitly preserves the existing `Promise.race` / `waitForTimeout` patterns in these specs as already correct; re-run the full `issued-invoices` E2E suite against staging before merging (per CLAUDE.md's "All tests touched by the change must pass" and the nightly E2E gate) — do not assume green from code review alone |
| Bundling the `Success: false` swallowing fix (Decision 4) into this PR would touch a second, independently-reasoned code path in the same review cycle as the E2E-unblocking fix | Low (if avoided) | Keep it out per Decision 4; file as a small, separate follow-up PR referencing the sibling pattern in `useIssuedInvoiceSyncStats.ts:53` |

## Specification Amendments

1. **FR-5's diagnosis sub-goal cannot be executed by this pipeline.** No App Insights, staging log,
   or GitHub Actions run-history access is available here (confirmed in the spec's own Open
   Questions). Recommend re-scoping FR-5 in the spec to two independently-trackable items: (a) a
   manual/human follow-up to pull staging APM data for `GET /api/invoices` around run #191's
   timestamp — outside this PR's deliverable — and (b) the `Success: false` swallowing fix, which
   *is* independently fixable and should move to its own small follow-up ticket rather than block
   or bloat this PR.
2. **FR-5's `Success: false` fix has a settled answer, not an open question.** Spec Open Question
   #2 asks whether the controller should return non-2xx or the frontend should check `data.success`.
   The codebase already answers this: `useIssuedInvoiceSyncStats.ts:53` checks `data.success` on an
   HTTP-200 response for the exact same feature. The follow-up fix for `useIssuedInvoicesList`
   should mirror that hook exactly — no controller/HTTP-status change, no team discussion required.
3. **FR-2's `enabled` flag placement**: the spec doesn't specify how `activeTab` reaches
   `useIssuedInvoicesList` (the hook currently only takes `IssuedInvoicesFilters`). Implementation
   guidance above specifies: add an optional second argument (`options: { enabled?: boolean }`) to
   `useIssuedInvoicesList`, do not add `activeTab` to `IssuedInvoicesFilters` or the query key.
4. Recommend the spec's Acceptance Criteria for FR-1/FR-3 be tested first locally (React DevTools /
   throttled network) before the full staging E2E re-run, since the staging E2E run is slow
   (nightly-only per CLAUDE.md) and the fix itself does not require staging to validate — only the
   *original* latency cause (out of scope here) does.

## Prerequisites

None. No migrations, config, feature flags, or infrastructure changes are required. The fix is
deployable independently of any resolution to FR-5's diagnosis question — that is precisely the
point of Decision 1/2 (make the UI resilient to the underlying cause rather than dependent on
knowing it). Before implementation starts, confirm locally that `npm run build` and `npm run lint`
pass (per CLAUDE.md validation rules) and that the existing `issued-invoices` Playwright specs can
be pointed at a local/dev backend with an artificially delayed `/api/invoices` response to validate
FR-1–FR-4's acceptance criteria without needing a staging deploy.
