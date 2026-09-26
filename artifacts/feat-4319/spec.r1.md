# Specification: BackgroundJobs — invalidate detail query on CRON update

## Summary
`useUpdateRecurringJobCronMutation` in `frontend/src/api/hooks/useRecurringJobs.ts` invalidates only the recurring-jobs list query on success, unlike the sibling `useUpdateRecurringJobStatusMutation`, which invalidates both the list and the affected job's detail query. This causes any component reading a single job via `useRecurringJobQuery` (e.g. shortcut controls outside the admin page) to keep showing a stale CRON expression after a successful edit. This is a small, self-contained cache-invalidation fix with no new UI, API, or data model changes.

## Background
The recurring-jobs feature exposes a query-key factory (`recurringJobsKeys`) with a `list()` key and a per-job `detail(jobName)` key. Three mutations exist against this cache: status update, CRON update, and trigger. The status mutation (lines 73–77) already follows the correct pattern — invalidate both `list()` and `detail(variables.jobName)` — because it receives `jobName` in its mutation variables and TanStack Query's `onSuccess(_data, variables)` gives access to those variables. The CRON mutation (lines 100–102) receives the same shape of variables (`{ jobName, cronExpression }`) but its `onSuccess` handler only invalidates `list()`, omitting the `detail()` invalidation. `useRecurringJobQuery(jobName, enabled)` is confirmed (via `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx`) to be consumed by at least one shortcut control outside the recurring-jobs admin page (`PRINT_JOB_NAME`), which is exactly the scenario the bug report describes: a detail-query consumer left holding stale cache data after a CRON edit made elsewhere.

## Functional Requirements

### FR-1: Invalidate the detail query on successful CRON update
`useUpdateRecurringJobCronMutation`'s `onSuccess` handler must invalidate both `recurringJobsKeys.list()` and `recurringJobsKeys.detail(variables.jobName)`, matching the existing pattern in `useUpdateRecurringJobStatusMutation`.

**Acceptance criteria:**
- After `useUpdateRecurringJobCronMutation` succeeds, `queryClient.invalidateQueries` is called with `{ queryKey: recurringJobsKeys.list() }`.
- After the same success, `queryClient.invalidateQueries` is also called with `{ queryKey: recurringJobsKeys.detail(jobName) }`, where `jobName` is the value passed into the mutation (from `variables.jobName`, not a hook-scoped closure variable).
- No other mutation (`useUpdateRecurringJobStatusMutation`, `useTriggerRecurringJobMutation`) is modified.
- Existing tests in `frontend/src/api/hooks/__tests__/useRecurringJobs.test.ts` continue to pass, and a new test (or extended existing test) asserts the detail-query invalidation for the CRON mutation specifically.

## Non-Functional Requirements

### NFR-1: Performance
None beyond existing behavior — this adds one additional (cheap) cache invalidation call per successful mutation, on par with the existing status-mutation pattern. No measurable performance impact.

### NFR-2: Security
None. No new data exposure, no new endpoint, no change to authorization. The `useRecurringJobQuery` hook already gates itself with an `enabled` flag driven by the caller's permission check; this fix does not touch that gating.

## Data Model
No data model changes. This is a client-side React Query cache-invalidation fix only. Relevant existing shape:

```typescript
const recurringJobsKeys = {
  all: [...QUERY_KEYS.recurringJobs] as const,
  list: () => [...recurringJobsKeys.all, 'list'] as const,
  detail: (jobName: string) => [...recurringJobsKeys.all, 'detail', jobName] as const,
};
```

## API / Interface Design
No API or route changes. The mutation's `mutationFn` argument shape already exposes `jobName` (`{ jobName: string; cronExpression: string }`), so the fix only needs to read `variables.jobName` inside `onSuccess`, exactly as `useUpdateRecurringJobStatusMutation` already does:

```typescript
onSuccess: (_data, variables) => {
  queryClient.invalidateQueries({ queryKey: recurringJobsKeys.list() });
  queryClient.invalidateQueries({ queryKey: recurringJobsKeys.detail(variables.jobName) });
},
```

## Dependencies
None beyond what already exists: `@tanstack/react-query`'s `useQueryClient`/`useMutation`, and the existing `recurringJobsKeys` factory. No backend change required — `recurringJobs_UpdateJobCron` already returns successfully today; only the client-side cache-invalidation behavior changes.

## Out of Scope
- Any change to `useUpdateRecurringJobStatusMutation` or `useTriggerRecurringJobMutation` (both already correct or unaffected by this finding).
- Any backend/API change.
- Any new UI, new shortcut control, or new detail-consumer component.
- Broader cache-invalidation audit of other feature areas.

## Open Questions
None.

## Status: COMPLETE
