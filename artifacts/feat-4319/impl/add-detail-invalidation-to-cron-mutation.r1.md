# Implementation: add-detail-invalidation-to-cron-mutation

## What was implemented

`useUpdateRecurringJobCronMutation`'s `onSuccess` handler in
`frontend/src/api/hooks/useRecurringJobs.ts` now invalidates both the
recurring-jobs list query and the affected job's detail query, mirroring
`useUpdateRecurringJobStatusMutation`'s existing pattern in the same file.

## Files created/modified

- `frontend/src/api/hooks/useRecurringJobs.ts` — `onSuccess` now takes
  `(_data, variables)` and calls `queryClient.invalidateQueries` for both
  `recurringJobsKeys.list()` and `recurringJobsKeys.detail(variables.jobName)`.
- `frontend/src/api/hooks/__tests__/useRecurringJobs.test.ts` — added
  `'invalidates both list and detail queries on success'` inside the
  `describe('useUpdateRecurringJobCronMutation', ...)` block, spying on
  `queryClient.invalidateQueries` and asserting both query keys are invalidated.

## Tests

- Followed TDD: added the test first, confirmed it failed against the
  pre-fix code (only the `list` key was invalidated — assertion on the
  `detail` key failed), then applied the minimal fix and reran.
- `npx react-scripts test --testPathPattern=useRecurringJobs` (CI=true):
  all 4 tests in the file pass, including the new test and the
  pre-existing `useUpdateRecurringJobCronMutation` and
  `useRecurringJobsQuery` tests.
- `npx eslint` on both changed files: clean, no issues.

## How to verify

```bash
cd frontend
CI=true npx react-scripts test --testPathPattern=useRecurringJobs
```

## Notes

- No new imports were required; the test file already imported
  `QueryClient`, `QueryClientProvider`, `React`, `renderHook`, and `act`.
- Change is a verbatim mirror of the sibling status mutation's
  `onSuccess` pattern already in the same file — no new abstractions
  introduced.
- `node_modules` in this worktree is a local symlink to the primary
  checkout's `frontend/node_modules` (created only to run tests in this
  worktree without hitting an unrelated `npm ci` peer-dependency conflict
  between `knip` and `react-scripts`/jest's `@types/node` requirement);
  it is gitignored and was not committed.

## PR Summary

Fixed a stale-cache bug: `useUpdateRecurringJobCronMutation`'s `onSuccess`
handler only invalidated the recurring-jobs list query, unlike the sibling
status mutation which also invalidates the single-job detail query. Any
shortcut control reading `useRecurringJobQuery` for a job whose CRON was
just edited would keep showing the stale CRON expression. The fix adds the
missing detail-query invalidation, matching the existing status-mutation
pattern exactly.

### Changes
- `frontend/src/api/hooks/useRecurringJobs.ts` — `useUpdateRecurringJobCronMutation`'s `onSuccess` now also invalidates `recurringJobsKeys.detail(variables.jobName)`
- `frontend/src/api/hooks/__tests__/useRecurringJobs.test.ts` — new test asserting both list and detail queries are invalidated on success

## Status
DONE
