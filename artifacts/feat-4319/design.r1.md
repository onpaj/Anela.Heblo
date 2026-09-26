# Design: BackgroundJobs — invalidate detail query on CRON update

## Component Design

### `useUpdateRecurringJobCronMutation` (modified)
File: `frontend/src/api/hooks/useRecurringJobs.ts`

Responsibility: calls `recurringJobs_UpdateJobCron(jobName, request)` and, on success, marks both the recurring-jobs list cache and the specific job's detail cache as stale, so any mounted observer of either refetches fresh data.

Contract (unchanged input/output shape, only `onSuccess` body changes):

```typescript
export const useUpdateRecurringJobCronMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      jobName,
      cronExpression
    }: {
      jobName: string;
      cronExpression: string;
    }): Promise<UpdateRecurringJobCronResponse> => {
      const client = getAuthenticatedApiClient();
      const request = new UpdateJobCronRequestBody({ cronExpression });
      return await client.recurringJobs_UpdateJobCron(jobName, request);
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: recurringJobsKeys.list() });
      queryClient.invalidateQueries({ queryKey: recurringJobsKeys.detail(variables.jobName) });
    },
  });
};
```

Collaborators (unchanged, behavior-only consumers):
- `frontend/src/pages/RecurringJobsPage.tsx` — the admin page that invokes this mutation when a user edits a job's CRON expression.
- `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx` — a shortcut control that reads `useRecurringJobQuery(PRINT_JOB_NAME, ...)` for the same job; it will now observe a fresh detail value immediately after any CRON edit to that job, instead of stale data until its own unrelated refetch trigger.

No other component's interface changes: `useUpdateRecurringJobStatusMutation`, `useTriggerRecurringJobMutation`, and `useRecurringJobQuery` are all untouched.

## Data Schemas

No schema changes. For reference, the existing shapes this fix operates on:

```typescript
// Query key factory (unchanged)
const recurringJobsKeys = {
  all: [...QUERY_KEYS.recurringJobs] as const,
  list: () => [...recurringJobsKeys.all, 'list'] as const,
  detail: (jobName: string) => [...recurringJobsKeys.all, 'detail', jobName] as const,
};

// Mutation variables (unchanged)
type UpdateCronVariables = {
  jobName: string;
  cronExpression: string;
};

// Mutation response (unchanged, from generated API client)
// UpdateRecurringJobCronResponse: { success, jobName, cronExpression, lastModifiedAt, lastModifiedBy }
```
