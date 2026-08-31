# Specification: Extract ExpeditionJobControlsBar from ExpeditionListArchivePage

## Summary
`frontend/src/pages/ExpeditionListArchivePage.tsx` currently mixes archive browsing (date list, items table, open/reprint) with three unrelated operational concerns: recurring-job control, print-fix triggering, and ad‑hoc order printing — the latter two pulled in via a direct cross-module import from the `ExpeditionList` module. This spec extracts those operational concerns into a new, self-contained `ExpeditionJobControlsBar` component so `ExpeditionListArchivePage` is left responsible only for archive browsing. This is a pure internal refactor: no visible behavior, markup, styling, routes, or API calls change.

## Background
An automated architecture-review routine (brief: `artifacts/feat-4000/brief.md`) flagged that `ExpeditionListArchivePage.tsx` resolves 6+ hooks, 5 mutation handlers, and 3 independent permission checks across four distinct responsibilities:

1. Archive browsing — `useExpeditionDates`, `useExpeditionListsByDate`, `useReprintExpeditionList`, `getExpeditionListDownloadUrl` (all from `../api/hooks/useExpeditionListArchive`) — lines 5–11, 57–59.
2. Recurring job control — `useTriggerRecurringJobMutation`, `useRecurringJobQuery`, `useUpdateRecurringJobStatusMutation` (from `../api/hooks/useRecurringJobs`) — lines 13–17, 60, 66–67, 101–126, 166–195, 223–236.
3. Print-fix triggering — `useRunExpeditionListPrintFix`, imported directly from `../api/hooks/useExpeditionList` (line 12), a hook that belongs to the **ExpeditionList** module, not `ExpeditionListArchive` — lines 61, 128–135, 211–222.
4. Order printing — `PrintOrderModal` (line 21) plus its trigger button and success handler — lines 53, 137–141, 204–210, 364–368.

This violates single responsibility (a change to job-control or print-fix behavior forces edits to the archive page), creates a hidden cross-module dependency that can silently break if `useExpeditionList` changes, and forces every archive-browsing test to mock the full dependency graph (blob API, recurring-jobs API, print-fix API, print-order API) even though it only exercises date selection.

The fix is a co-located extraction, following a pattern already established in this codebase (e.g. `AccessManagementPage.tsx` composing `GroupsGrid`/`UsersGrid` from `components/pages/access/`, and `GiftPackageManufacturing`'s components under `components/pages/GiftPackageManufacturing/`): a flat page file in `frontend/src/pages/` stays in place and delegates a self-contained sub-component that owns its own hooks, permission checks, and modal state.

## Functional Requirements

### FR-1: No behavior change
The refactor must not change what the user sees or can do. Rendered DOM structure, CSS classes, button order/labels, permission gating, toast messages, and query invalidation behavior must be identical before and after the extraction.

**Acceptance criteria:**
- Visual output (header row: title, expedition-robot toggle + next-run text, "Obnovit", "Tisknout zakázku", "Spustit tisk oprav", "Spustit tisk" buttons) is pixel-identical: same conditional rendering (`canTriggerJob`, `canToggleJob`), same disabled/spinner states, same Czech copy.
- All 5 mutation flows behave identically: trigger job (`handleRunJob`), toggle job status (`handleToggleJob`), run print-fix (`handleRunFix`), print order (`handlePrintOrderSuccess`), reprint (`handleReprintConfirm`) — same success/error toasts, same query invalidation (`QUERY_KEYS.expeditionListArchive` invalidated after order-print success and after reprint; job-trigger and print-fix do **not** invalidate archive queries, matching current behavior — see Open Questions).
- `PRINT_JOB_NAME`, `TRIGGER_JOBS_PERMISSION`, `DISABLE_JOBS_PERMISSION` constants preserve their current values (`"print-picking-list"`, `"jobs.trigger.read"`, `"jobs.disable.read"`).
- The route `/logistics/expedition-archive` in `App.tsx` and its import of `ExpeditionListArchivePage` from `./pages/ExpeditionListArchivePage` are unchanged.
- `useScreenView('Logistics', 'ExpeditionArchive')` telemetry call remains in `ExpeditionListArchivePage` and fires exactly as before (no duplicate/second screen-view call introduced in the new component).

### FR-2: New `ExpeditionJobControlsBar` component, self-contained
Create `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx` (React function component, no required props) that owns all "operational controls" currently embedded in the page:

- Recurring job control: `useTriggerRecurringJobMutation`, `useRecurringJobQuery(PRINT_JOB_NAME, canTriggerJob || canToggleJob)`, `useUpdateRecurringJobStatusMutation` from `../../../api/hooks/useRecurringJobs`, plus the toggle switch, "Další běh: …" text, and "Spustit tisk" button.
- Print-fix triggering: `useRunExpeditionListPrintFix` from `../../../api/hooks/useExpeditionList`, plus the "Spustit tisk oprav" button.
- Order printing: local `isPrintOrderModalOpen` state, the "Tisknout zakázku" button, `<PrintOrderModal>` from `../../../components/modals/PrintOrderModal`, and the success handler that shows the toast and invalidates `QUERY_KEYS.expeditionListArchive`.
- Permission checks: `usePermissionsContext().hasPermission(...)` for `TRIGGER_JOBS_PERMISSION` and `DISABLE_JOBS_PERMISSION`, evaluated inside this component (not passed in from the page).
- Toasts: `useToast()` called inside this component for all of the above flows.
- Query invalidation: `useQueryClient()` called inside this component (it does not need anything from the parent to invalidate `QUERY_KEYS.expeditionListArchive` after a successful order print).

The component takes **no props** related to hooks, handlers, or permissions — it is fully self-contained, matching the `GroupsGrid`/`UsersGrid` pattern already used in this codebase. (This resolves the brief's two suggested alternatives — "receives hooks/handlers as props" vs. "thin container that composes" — in favor of the self-contained form; see Open Questions.)

**Acceptance criteria:**
- `ExpeditionJobControlsBar` has zero import from `../../../api/hooks/useExpeditionListArchive` (it must not need any archive-browsing hook).
- `ExpeditionListArchivePage.tsx` has zero import from `../api/hooks/useExpeditionList`, `../api/hooks/useRecurringJobs`, `../auth/PermissionsContext`, or `../components/modals/PrintOrderModal` after the refactor.
- Rendering `<ExpeditionJobControlsBar />` with no props inside `ExpeditionListArchivePage` reproduces the current header-right content exactly (toggle, next-run label, three action buttons), in the same DOM position relative to the "Obnovit" button and the `<h1>`.
- A new co-located test file `frontend/src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx` exists and mocks only `../../../../api/hooks/useRecurringJobs`, `../../../../api/hooks/useExpeditionList`, `../../../../auth/PermissionsContext`, `../../../../api/client`, and `ToastContext` — it must not need to mock `useExpeditionListArchive`.
- It covers (migrated from the current `ExpeditionListArchivePage.test.tsx`): the "expedition robot toggle" describe block (reflects enabled/disabled state, calls status mutation with negated value, em-dash + disabled toggle when job missing) and the "permission gating" describe block (run button / toggle shown or hidden per permission).

### FR-3: `ExpeditionListArchivePage` reduced to archive browsing only
`ExpeditionListArchivePage.tsx` keeps: `useScreenView`, page/date-pagination state (`page`, `selectedDate`), `useExpeditionDates`, `useExpeditionListsByDate`, `useReprintExpeditionList`, `getExpeditionListDownloadUrl`, `getAuthenticatedFetch`, the "Obnovit" refresh button and its `queryClient.invalidateQueries({ queryKey: QUERY_KEYS.expeditionListArchive })` call, the date sidebar, the items table, `handleOpen`, and the reprint confirmation dialog (`reprintConfirm` state, `handleReprintConfirm`).

It renders `<ExpeditionJobControlsBar />` where the job-status section and the "Tisknout zakázku" / "Spustit tisk oprav" / "Spustit tisk" buttons currently sit.

**Acceptance criteria:**
- `ExpeditionListArchivePage.tsx`'s only remaining feature-hook imports are from `../api/hooks/useExpeditionListArchive`.
- `ExpeditionListArchivePage.tsx` no longer imports `usePermissionsContext`, `useToast` is still used only for `handleOpen` and `handleReprintConfirm` error/success paths (archive-only flows), and no longer references `PRINT_JOB_NAME`, `TRIGGER_JOBS_PERMISSION`, `DISABLE_JOBS_PERMISSION`, `formatDateTime`'s job-only caller (note: `formatDateTime` itself moves to `ExpeditionJobControlsBar` since its only remaining caller is the "Další běh" text — keep it in the archive page only if some other archive-browsing usage needs it, otherwise move it; `formatFileSize` stays in the page since it formats item file sizes in the items table).
- The existing `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx` is updated so its `jest.mock` calls no longer include `../../api/hooks/useExpeditionList`, `../../api/hooks/useRecurringJobs`, or `../../auth/PermissionsContext`; it keeps and passes the "refresh button" describe block; the "expedition robot toggle" and "permission gating" describe blocks are removed from this file (moved to FR-2's new test file).

### FR-4: Cross-module import confined to one file
The direct dependency of the `ExpeditionListArchive` page module on the `ExpeditionList` module's `useRunExpeditionListPrintFix` hook is not eliminated (that would require a backend/contract change, out of scope — see Out of Scope) but is confined to `ExpeditionJobControlsBar.tsx`, the one file explicitly named and owned as "operational controls," instead of leaking into the archive-browsing page.

**Acceptance criteria:**
- `import { useRunExpeditionListPrintFix } from "../../../api/hooks/useExpeditionList";` (or equivalent relative path) appears only in `ExpeditionJobControlsBar.tsx` in the `ExpeditionListArchive` feature area.
- `grep -rn "useExpeditionList\"" frontend/src/pages/ExpeditionListArchivePage.tsx` (or equivalent) returns no match after the change.

### FR-5: Test suite reorganized, coverage preserved
Every currently-passing assertion in `ExpeditionListArchivePage.test.tsx` continues to be exercised after the split, just from the file matching its new owning component.

**Acceptance criteria:**
- `npm test` (or the project's Jest invocation) passes for both `ExpeditionListArchivePage.test.tsx` and the new `ExpeditionJobControlsBar.test.tsx` with no reduction in the number of assertions/describe blocks migrated (refresh-button tests stay put; robot-toggle and permission-gating tests move as a block, updated only to mount `<ExpeditionJobControlsBar />` directly instead of the full page).
- No new `act()` warnings or console errors are introduced by the split (each test file wraps its subject in the same `QueryClientProvider` / `ToastProvider` scaffolding used today).

## Non-Functional Requirements

### NFR-1: Performance
No measurable change expected — the same hooks fire the same number of times per render; splitting a component into two files does not add extra network calls or re-renders. No performance testing is required beyond the existing test suite passing.

### NFR-2: Security
No change to permission checks: `TRIGGER_JOBS_PERMISSION` (`jobs.trigger.read`) and `DISABLE_JOBS_PERMISSION` (`jobs.disable.read`) continue to gate the trigger button and toggle exactly as today, now evaluated inside `ExpeditionJobControlsBar` via the same `usePermissionsContext()` hook. No new data is exposed to users who previously could not see it.

## Data Model
No data model changes. This is a pure frontend component-boundary refactor; no DTOs, API contracts, or backend types are touched. Existing types remain as-is:
- `ExpeditionListItemDto`, `GetExpeditionDatesResponse`, `GetExpeditionListsByDateResponse`, `ReprintExpeditionListResponse` (from `useExpeditionListArchive.ts`) — used only by `ExpeditionListArchivePage`.
- `RunExpeditionListPrintFixResult` (from `useExpeditionList.ts`) — used only by `ExpeditionJobControlsBar` after this change.
- `RecurringJobDto` and related types (from `useRecurringJobs.ts`) — used only by `ExpeditionJobControlsBar` after this change.

## API / Interface Design
No backend/API changes. Frontend component interface after the refactor:

```
ExpeditionListArchivePage (frontend/src/pages/ExpeditionListArchivePage.tsx)
├── owns: date pagination, selected date, items table, reprint dialog, "Obnovit" refresh
├── hooks: useExpeditionDates, useExpeditionListsByDate, useReprintExpeditionList,
│          getExpeditionListDownloadUrl, useToast, useQueryClient, useScreenView
└── renders <ExpeditionJobControlsBar />  (no props)

ExpeditionJobControlsBar (frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx)
├── owns: recurring-job toggle/trigger, print-fix trigger, order-print modal + trigger
├── hooks: useTriggerRecurringJobMutation, useRecurringJobQuery, useUpdateRecurringJobStatusMutation
│          (../../../api/hooks/useRecurringJobs),
│          useRunExpeditionListPrintFix (../../../api/hooks/useExpeditionList),
│          usePermissionsContext, useToast, useQueryClient
└── renders <PrintOrderModal /> (../../../components/modals/PrintOrderModal)
```

Component contract: `ExpeditionJobControlsBar` takes no props (`React.FC` with no argument, or `() => JSX.Element`). If a future need arises to let the parent react to an operational-control event, add an explicit optional callback prop then — none is needed for this refactor since every current side effect (toast, query invalidation) is self-contained within the operational controls today.

## Dependencies
- `@tanstack/react-query` (`useQueryClient`, existing).
- `../api/hooks/useRecurringJobs`, `../api/hooks/useExpeditionList`, `../auth/PermissionsContext`, `../contexts/ToastContext`, `../components/modals/PrintOrderModal` — all pre-existing modules, only their import site moves.
- No new libraries, no new backend endpoints.

## Out of Scope
- Removing the underlying cross-module dependency on `useExpeditionList`'s `useRunExpeditionListPrintFix` (e.g. moving print-fix into an `ExpeditionListArchive`-owned endpoint/hook, or introducing a shared module for it). This refactor only confines the existing dependency to one clearly-scoped file.
- Any change to `PrintOrderModal.tsx` itself (it already imports `usePrintExpeditionOrder` from `useExpeditionList` — that is a pre-existing, separate cross-module coupling not addressed here).
- Any change to backend controllers/endpoints (`expeditionListArchive_*`, `expeditionList_RunFix`, `expeditionList_PrintOrder`, `recurringJobs_*`).
- Fixing the pre-existing asymmetry where job-trigger and print-fix do not invalidate `expeditionListArchive` queries while order-print and reprint do (see Open Questions — preserved as-is).
- Renaming or relocating `useExpeditionListArchive.ts`, `useExpeditionList.ts`, or `useRecurringJobs.ts`.
- Any visual redesign of the header row, buttons, or toggle.
- E2E test changes (no user-visible flow changes; existing Playwright specs for this page, if any, should continue to pass unmodified).

## Open Questions
1. **Sub-component location.** This spec assumes the new component lives at `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx`, following the existing `components/pages/<Feature>/` convention (e.g. `AccessManagementPage.tsx` → `components/pages/access/GroupsGrid.tsx`; `components/pages/GiftPackageManufacturing/`), which keeps `pages/ExpeditionListArchivePage.tsx` at its current path with no change to `App.tsx`'s import or route. An alternative, also present in this codebase (`pages/InvoiceClassification/{Page}.tsx` + `pages/InvoiceClassification/components/`), would move the page itself into `pages/ExpeditionListArchive/ExpeditionListArchivePage.tsx`. Please confirm the assumed location is acceptable, or specify the alternative.
2. **Self-contained vs. prop-driven sub-component.** The brief's suggested fix says `ExpeditionJobControlsBar` should "receive the relevant hooks and handlers as props," but also states the goal that archive-browsing tests should "only mock the three archive-related hooks" — the latter is only achievable if `ExpeditionJobControlsBar` calls the recurring-job/print-fix/permission hooks itself rather than receiving them from the page. This spec resolves the tension in favor of a fully self-contained component (no props), matching the existing `GroupsGrid`/`UsersGrid` pattern. Please confirm this reading is correct.
3. **Pre-existing invalidation asymmetry.** Today, a successful job-trigger or print-fix run does **not** invalidate `expeditionListArchive` queries (only order-print success and reprint do), even though both actions can produce new expedition-list files. This spec preserves that asymmetry as out-of-scope existing behavior. Confirm this should not be fixed opportunistically as part of this refactor.

## Status: HAS_QUESTIONS
