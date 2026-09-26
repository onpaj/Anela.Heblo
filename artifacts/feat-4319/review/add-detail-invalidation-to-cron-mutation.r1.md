# Code Review: add-detail-invalidation-to-cron-mutation

## Summary

The implementation adds detail-query invalidation to
`useUpdateRecurringJobCronMutation`'s `onSuccess`, mirroring
`useUpdateRecurringJobStatusMutation`'s existing pattern verbatim, and adds
a test that fails without the fix and passes with it. The change matches
the task context exactly.

## Review Result: PASS

### task: add-detail-invalidation-to-cron-mutation
**Status:** PASS

Verified:
- `onSuccess` signature changed to `(_data, variables)` and now calls
  `queryClient.invalidateQueries` for both `recurringJobsKeys.list()` and
  `recurringJobsKeys.detail(variables.jobName)`, matching the sibling
  status mutation's pattern and the issue's suggested fix.
- New test `'invalidates both list and detail queries on success'` spies
  on `queryClient.invalidateQueries` and asserts both query keys are hit.
- All 4 tests in `useRecurringJobs.test.ts` pass
  (`CI=true npx react-scripts test --testPathPattern=useRecurringJobs`).
- `npx eslint` on both changed files reports no issues.
- No unrelated changes; diff is minimal and confined to the two files the
  task context specified.

## Docs to Update

(none — internal cache-invalidation fix, no public behavior, CLI, or
config change)

## Overall Notes

No concerns. The fix is a straightforward, verbatim mirror of an existing
pattern already used elsewhere in the same file.
