# Implementation: create-expedition-job-controls-bar-failing-tests

## What was implemented

Created a new failing test file for the not-yet-extracted `ExpeditionJobControlsBar`
component. The tests were migrated verbatim (per the task-context file) from the
"expedition robot toggle" and "permission gating" `describe` blocks currently in
`frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx`, adjusted to mount
`<ExpeditionJobControlsBar />` directly and to import from one extra `../` level to
match the new nested directory location. This establishes the TDD red step for the
upcoming component-extraction task (FR-2).

## Files created/modified

- `frontend/src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx` — new test file, 9 test cases across two `describe` blocks (`expedition robot toggle`, `permission gating`), mocking `useExpeditionList`, `useRecurringJobs`, `PermissionsContext`, and `api/client`, and importing the not-yet-created `ExpeditionJobControlsBar` component.

No other files were modified. The target directory
`frontend/src/components/pages/ExpeditionListArchive/` did not previously exist and
was created to hold the new test file (and, in a future task, the extracted
component itself).

## Tests

`ExpeditionJobControlsBar.test.tsx` covers:

- **expedition robot toggle** (4 tests): reflects enabled/disabled state of the print
  job via `aria-checked`, calls the status mutation with the negated `isEnabled`
  value on toggle click, and renders an em dash + disables the toggle when the print
  job is missing.
- **permission gating** (5 tests): shows/hides the "Spustit tisk" run button based on
  `jobs.trigger.read`; shows/hides the toggle based on `jobs.disable.read`; hides
  both the toggle and the "Další běh" text when neither job permission is granted.

These were confirmed (against the current `ExpeditionListArchivePage.tsx`) to
reference real, currently-existing hook names (`useRunExpeditionListPrintFix`,
`usePrintExpeditionOrder`, `useTriggerRecurringJobMutation`, `useRecurringJobQuery`,
`useUpdateRecurringJobStatusMutation`, `usePermissionsContext`), permission string
constants (`jobs.trigger.read`, `jobs.disable.read`), and UI text (`Expediční
robot`, `Spustit tisk`, `Další běh:`) so the assertions will exercise real behavior
once the component is extracted.

## How to verify

```bash
cd frontend
npm install --legacy-peer-deps   # node_modules was not present in this worktree
CI=true npx react-scripts test src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx --watchAll=false
```

Result: **FAIL**, as expected —

```
Cannot find module '../ExpeditionJobControlsBar' from
'src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx'
```

The suite fails at module resolution because `ExpeditionJobControlsBar.tsx` does not
exist yet — it has not been extracted from `ExpeditionListArchivePage.tsx` (that is
a separate, later task). This is a clean, expected TDD-red failure: no syntax
errors, no typos, no unrelated mock/setup errors — the test file itself is valid
TypeScript/JSX and the only reason it fails is the missing component module.

Note: this worktree had no `frontend/node_modules` installed. `npm ci` failed on a
pre-existing peer-dependency conflict (`knip` vs. root `@types/node`) unrelated to
this change, so `npm install --legacy-peer-deps` was used instead purely to run the
verification locally; no `package.json`/`package-lock.json` changes were made.

## Notes

- Directly followed the task-context file's exact prescribed content for the test
  file; no deviations.
- Did not touch `ExpeditionListArchivePage.tsx` or `ExpeditionListArchivePage.test.tsx`
  — this task is scoped to adding the new failing test file only. The existing
  "expedition robot toggle" / "permission gating" blocks in
  `ExpeditionListArchivePage.test.tsx` remain in place untouched; their removal
  belongs to the later extraction task per the task-context scope.
- Committed the test file on the current branch per the developer agent's hard
  constraints (no new worktree/branch).

## PR Summary
Added `ExpeditionJobControlsBar.test.tsx`, a failing test suite (red step of TDD) for
a not-yet-extracted `ExpeditionJobControlsBar` component, migrating the "expedition
robot toggle" and "permission gating" test blocks from `ExpeditionListArchivePage.test.tsx`
to mount the future standalone component directly.

Verified the suite fails only because `../ExpeditionJobControlsBar` cannot be
resolved (component not yet created) — no syntax errors or typos in the test file
itself.

### Changes
- `frontend/src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx` — new failing test file (9 tests) for the future extracted component

## Status
DONE
