# Implementation: wire-controls-bar-into-page

## What was implemented
`ExpeditionListArchivePage.tsx` was trimmed down to pure archive-browsing responsibility. All job-trigger/toggle/print-fix/print-order state, mutations, and handlers were removed and replaced with a single `<ExpeditionJobControlsBar />` render in the header, mounted next to the existing "Obnovit" (refresh) button. `ExpeditionJobControlsBar` (already implemented in a prior task at `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx`) is a self-contained default-exported component taking no required props — it owns its own `PrintOrderModal`, recurring-job query/mutations, and permission checks, and invalidates the shared `QUERY_KEYS.expeditionListArchive` query on success so the page's date/items lists refresh automatically. No deviation from the task spec was needed.

## Files created/modified
- `frontend/src/pages/ExpeditionListArchivePage.tsx` — removed `Play` from the lucide-react import; removed the `useRunExpeditionListPrintFix`, `useTriggerRecurringJobMutation`/`useRecurringJobQuery`/`useUpdateRecurringJobStatusMutation`, `usePermissionsContext`, and `PrintOrderModal` imports; removed the `PRINT_JOB_NAME`/`TRIGGER_JOBS_PERMISSION`/`DISABLE_JOBS_PERMISSION` constants; removed `isPrintOrderModalOpen` state, `triggerJobMutation`, `runFixMutation`, permission flags, `printJob` query, `updateStatusMutation`, and the `handleRunJob`/`handleToggleJob`/`handleRunFix`/`handlePrintOrderSuccess` handlers; replaced the header-right JSX (job toggle switch, "next run" label, print-order/print-fix/run-job buttons) with `<ExpeditionJobControlsBar />` placed before the "Obnovit" button; removed the standalone `<PrintOrderModal>` render. `formatDateTime` was kept (duplicated in the controls-bar component) per the task's explicit no-shared-util instruction. Net diff: 3 insertions, 133 deletions.

## Tests
No test files were touched in this task. The existing `ExpeditionListArchivePage.test.tsx` (or equivalent) is expected to fail to compile/run now since it still mocks modules the page no longer imports — this is called out as expected in the task context and is deferred to the `update-archive-page-tests` task.

## How to verify
1. `cd frontend && npx tsc --noEmit -p tsconfig.json` — no errors reference `ExpeditionListArchivePage.tsx` (the only errors present are pre-existing `tsconfig.json` deprecation warnings about `target=ES5` / `moduleResolution=node10`, unrelated to this change).
2. `grep -n 'useExpeditionList"' frontend/src/pages/ExpeditionListArchivePage.tsx` — no matches.
3. `grep -n "useRecurringJobs\|PermissionsContext\|PrintOrderModal" frontend/src/pages/ExpeditionListArchivePage.tsx` — no matches.
4. Visually: page header now renders `ExpeditionJobControlsBar` (robot toggle + next-run label + "Tisknout zakázku"/"Spustit tisk oprav"/"Spustit tisk" buttons, permission-gated) immediately before the "Obnovit" button; the date sidebar, items table, "Otevřít"/"Přetisk" actions, and reprint confirmation dialog are unchanged.

## Notes
- `ExpeditionJobControlsBar` matched the assumed API exactly (default export, no required props), so Step 3 needed no adaptation.
- `git status` also showed `artifacts/feat-4000/state.json` as modified (pre-existing pipeline bookkeeping change, not part of this task) — left unstaged/uncommitted per the task's instruction to `git add` only the page file.
- Did not run the broader FE test suite or `npm run build`/`npm run lint` since the task explicitly scopes verification to this file's type-check and defers test-file fixes to a later task; the pre-existing archive-page test file is expected to fail at this point.

## PR Summary
Reduced `ExpeditionListArchivePage` to its core archive-browsing responsibility (date list, items table, open/reprint actions, refresh) by delegating all recurring-job/print-order UI and logic to the already-built `ExpeditionJobControlsBar` component. The page no longer imports `useRecurringJobs`, `PermissionsContext`, `useExpeditionList` (print-fix hook), or `PrintOrderModal` directly — those now live entirely inside the controls-bar component, which the page mounts with `<ExpeditionJobControlsBar />` in its header. Behavior for the end user is unchanged (same buttons, same order relative to "Obnovit"); this is a pure internal refactor completing the arch-review-driven page split.

### Changes
- `frontend/src/pages/ExpeditionListArchivePage.tsx` — trimmed imports, state, hooks, handlers, and header JSX; delegates job-control UI to `ExpeditionJobControlsBar`.

## Status
DONE
