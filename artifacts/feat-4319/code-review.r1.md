## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Summary

The feature diff is exactly what the spec (`spec.r1.md` FR-1) calls for:
`useUpdateRecurringJobCronMutation`'s `onSuccess` handler in
`frontend/src/api/hooks/useRecurringJobs.ts` now takes `(_data, variables)`
and invalidates both `recurringJobsKeys.list()` and
`recurringJobsKeys.detail(variables.jobName)`, verbatim-mirroring the
already-correct sibling `useUpdateRecurringJobStatusMutation` pattern in the
same file (lines 73-77). `useTriggerRecurringJobMutation` is untouched, as
required.

The accompanying test in
`frontend/src/api/hooks/__tests__/useRecurringJobs.test.ts` spies on
`queryClient.invalidateQueries` and asserts both the `list` and
`detail('test-job')` query keys are invoked after a successful mutation —
this is a real regression guard, not a tautology, since it would have
failed against the pre-fix code (which only called `invalidateQueries`
once).

Verified independently in this review round:
- `CI=true npx react-scripts test --testPathPattern=useRecurringJobs` — 1
  suite, 4 tests, all PASS (including the new test).
- `npx eslint src/api/hooks/useRecurringJobs.ts
  src/api/hooks/__tests__/useRecurringJobs.test.ts` (from `frontend/`) —
  zero errors, zero warnings.
- Read the full diff: only `frontend/src/api/hooks/useRecurringJobs.ts`
  and its test file changed in real source; everything else in the diff is
  pipeline artifacts under `artifacts/feat-4319/`. No unrelated changes, no
  scope creep.

Change is minimal, correct, matches an established in-file pattern exactly,
and is well covered by a test that would have caught the original bug.
