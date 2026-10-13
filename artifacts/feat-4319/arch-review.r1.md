# Architecture Review: BackgroundJobs — invalidate detail query on CRON update

## Skip Design: true

## Architectural Fit Assessment
This is a one-line, client-side cache-invalidation fix inside an existing hook file (`frontend/src/api/hooks/useRecurringJobs.ts`). It touches no backend code, no API contract, no new component, and no new query key. The existing `recurringJobsKeys` factory already exposes `detail(jobName)`, and the sibling `useUpdateRecurringJobStatusMutation` already implements the exact pattern this fix needs to replicate for `useUpdateRecurringJobCronMutation`. There is no architectural risk: the change brings one mutation's cache-invalidation behavior in line with an already-established, already-tested sibling in the same file.

## Proposed Architecture

### Component Overview
No new components. Single file, single function body change:

```
frontend/src/api/hooks/useRecurringJobs.ts
  useUpdateRecurringJobStatusMutation   (unchanged — reference pattern)
  useUpdateRecurringJobCronMutation     (onSuccess handler modified)
  useTriggerRecurringJobMutation        (unchanged — out of scope)
```

Consumers unaffected in code, only in runtime cache freshness:
- `frontend/src/pages/RecurringJobsPage.tsx` (admin page, drives the CRON mutation)
- `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx` (confirmed consumer of `useRecurringJobQuery`, the detail-query hook that was going stale)

### Key Design Decisions

#### Decision 1: Match the existing sibling pattern exactly, no new abstraction
**Options considered:**
1. Extract a shared `invalidateRecurringJobCaches(queryClient, jobName)` helper used by all three mutations, to prevent this class of drift recurring.
2. Inline-fix only `useUpdateRecurringJobCronMutation`'s `onSuccess`, mirroring `useUpdateRecurringJobStatusMutation` verbatim.

**Chosen approach:** Option 2 — inline fix only.

**Rationale:** The brief's suggested fix and the spec's FR-1 both scope this narrowly. The file is small (roughly 110 lines) with only three mutations; introducing a shared helper is a refactor beyond what this finding asks for, and CLAUDE.md's "surgical changes" rule ("touch only what the task requires... don't improve adjacent code") directs against it. Two mutations (`status`, and now `cron`) will share the identical two-line invalidation body; that duplication is acceptable at this scale and is consistent with the file's current style (the third mutation, `trigger`, also has its own single-line invalidation with no shared helper). If a fourth mutation needing the same pattern appears later, extracting a helper becomes worth it — not now.

## Implementation Guidance

### Directory / Module Structure
No new files. Modify only:
- `frontend/src/api/hooks/useRecurringJobs.ts` — `useUpdateRecurringJobCronMutation`'s `onSuccess` handler (currently lines 100–102).
- `frontend/src/api/hooks/__tests__/useRecurringJobs.test.ts` — extend the existing `useUpdateRecurringJobCronMutation` describe block with an assertion that the detail query key was invalidated.

### Interfaces and Contracts
No interface changes. `recurringJobsKeys.detail(jobName: string)` already exists and is already used by `useUpdateRecurringJobStatusMutation` and `useRecurringJobQuery`. The mutation's `mutationFn` variables type, `{ jobName: string; cronExpression: string }`, already exposes `jobName` — no type change needed. The fix is exactly:

```typescript
onSuccess: (_data, variables) => {
  queryClient.invalidateQueries({ queryKey: recurringJobsKeys.list() });
  queryClient.invalidateQueries({ queryKey: recurringJobsKeys.detail(variables.jobName) });
},
```

Test assertion pattern to add (mirroring the existing test file's `QueryClient` + `jest.spyOn` conventions used elsewhere in this codebase for invalidation assertions — verify via `useUpdateRecurringJobStatusMutation`'s own test, if one exists, otherwise spy on `queryClient.invalidateQueries` through the `QueryClientProvider` instance used by `createWrapper`):

```typescript
it('invalidates both list and detail queries on success', async () => {
  mockApiClient.recurringJobs_UpdateJobCron.mockResolvedValue({
    success: true,
    jobName: 'test-job',
    cronExpression: '0 3 * * *',
    lastModifiedAt: new Date().toISOString(),
    lastModifiedBy: 'test-user',
  });

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const invalidateSpy = jest.spyOn(queryClient, 'invalidateQueries');
  const wrapper = ({ children }: { children: React.ReactNode }) =>
    React.createElement(QueryClientProvider, { client: queryClient }, children);

  const { result } = renderHook(() => useUpdateRecurringJobCronMutation(), { wrapper });

  await act(async () => {
    await result.current.mutateAsync({ jobName: 'test-job', cronExpression: '0 3 * * *' });
  });

  expect(invalidateSpy).toHaveBeenCalledWith(
    expect.objectContaining({ queryKey: expect.arrayContaining(['recurring-jobs', 'list']) })
  );
  expect(invalidateSpy).toHaveBeenCalledWith(
    expect.objectContaining({ queryKey: expect.arrayContaining(['recurring-jobs', 'detail', 'test-job']) })
  );
});
```

(Exact query-key array contents depend on `QUERY_KEYS.recurringJobs` from the mocked `client` module, already mocked in this test file as `['recurring-jobs']`.)

### Data Flow
1. Admin edits a job's CRON expression on `RecurringJobsPage` → calls `useUpdateRecurringJobCronMutation().mutate({ jobName, cronExpression })`.
2. On success, `onSuccess(_data, variables)` fires with `variables.jobName` available (TanStack Query always passes the original mutation variables as the second `onSuccess` argument — same mechanism the status mutation already relies on).
3. Both `recurringJobsKeys.list()` and `recurringJobsKeys.detail(variables.jobName)` are marked stale and refetched by any currently-mounted observer, including `ExpeditionJobControlsBar` (or any other shortcut control) that has `useRecurringJobQuery(jobName, enabled)` mounted for that same `jobName`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Extra network request per successful CRON edit (detail refetch in addition to list refetch) | Low | Identical to the already-shipped behavior of `useUpdateRecurringJobStatusMutation`; no new pattern, no measurable load — this is an admin-only, low-frequency action. |
| Test coverage regresses if the new test's query-key shape assumption doesn't match the mocked `QUERY_KEYS.recurringJobs` | Low | Developer must run the existing test file locally first to confirm the exact key array shape before writing assertions; do not guess it blind. |

## Specification Amendments
None. The spec's FR-1 is fully implementable as written with the pattern above.

## Prerequisites
None. No migration, no config, no infrastructure change. Purely a frontend code + test change on an existing file.
