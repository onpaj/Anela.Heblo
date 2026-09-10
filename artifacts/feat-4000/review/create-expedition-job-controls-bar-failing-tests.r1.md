# Code Review: create-expedition-job-controls-bar-failing-tests

## Summary

The new test file matches the task-context file's prescribed content verbatim,
lands at the exact specified path, and was confirmed to fail for the correct
reason (missing `ExpeditionJobControlsBar` module) rather than a syntax or
typo error. Referenced hook names, permission constants, and UI text were
cross-checked against the current `ExpeditionListArchivePage.tsx` and all
exist. This is a clean TDD-red step.

## Review Result: PASS

### task: create-expedition-job-controls-bar-failing-tests
**Status:** PASS

## Docs to Update
(none — this is an internal test-scaffolding change with no public behavior, CLI, or docs impact)

## Overall Notes

- Verified `frontend/src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx` content is character-for-character what the task-context file specified.
- Verified the referenced hooks (`useRunExpeditionListPrintFix`, `usePrintExpeditionOrder` from `api/hooks/useExpeditionList`; `useTriggerRecurringJobMutation`, `useRecurringJobQuery`, `useUpdateRecurringJobStatusMutation` from `api/hooks/useRecurringJobs`; `usePermissionsContext` from `auth/PermissionsContext`) and the permission string constants (`jobs.trigger.read`, `jobs.disable.read`) all exist in the current codebase, matching what `ExpeditionListArchivePage.tsx` itself uses — so the mocks/assertions target real names.
- Ran `CI=true npx react-scripts test src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx --watchAll=false` (after `npm install --legacy-peer-deps`, since the worktree had no `node_modules`). Result: suite fails with `Cannot find module '../ExpeditionJobControlsBar'` — expected and correct, since the component has not been extracted yet. No parse/syntax errors.
- Existing `ExpeditionListArchivePage.test.tsx` was left untouched, consistent with this task's scope being additive-only (extraction/cleanup of the old page test is a separate later task).
- No source/component code was added in this task, matching the task-context file's scope (test-only).
