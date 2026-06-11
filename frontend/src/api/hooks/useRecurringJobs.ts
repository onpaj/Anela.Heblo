import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import {
  UpdateJobStatusRequestBody,
  UpdateJobCronRequestBody,
  type RecurringJobDto,
  type UpdateRecurringJobStatusResponse,
  type UpdateRecurringJobCronResponse,
  type TriggerRecurringJobResponse
} from '../generated/api-client';

// Query key factory for recurring jobs using centralized QUERY_KEYS
const recurringJobsKeys = {
  all: [...QUERY_KEYS.recurringJobs] as const,
  list: () => [...recurringJobsKeys.all, 'list'] as const,
  detail: (jobName: string) => [...recurringJobsKeys.all, 'detail', jobName] as const,
};

/**
 * Hook to fetch list of all recurring jobs
 * Uses generated API client method: recurringJobs_GetRecurringJobs
 */
export const useRecurringJobsQuery = () => {
  return useQuery({
    queryKey: recurringJobsKeys.list(),
    queryFn: async () => {
      const client = getAuthenticatedApiClient();
      const response = await client.recurringJobs_GetRecurringJobs();
      return response.jobs || [];
    },
  });
};

/**
 * Hook to fetch a single recurring job by name.
 * Readable by holders of the job trigger/disable permission (not only admins), so it can
 * back shortcut controls outside the recurring-jobs admin page. Pass `enabled: false` when
 * the current user lacks any of those permissions to avoid a 403 request.
 * Uses generated API client method: recurringJobs_GetRecurringJob
 */
export const useRecurringJobQuery = (jobName: string, enabled: boolean = true) => {
  return useQuery({
    queryKey: recurringJobsKeys.detail(jobName),
    enabled,
    queryFn: async (): Promise<RecurringJobDto | null> => {
      const client = getAuthenticatedApiClient();
      const response = await client.recurringJobs_GetRecurringJob(jobName);
      return response.job ?? null;
    },
  });
};

/**
 * Hook to update recurring job status (enable/disable)
 * Uses generated API client method: recurringJobs_UpdateJobStatus
 */
export const useUpdateRecurringJobStatusMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      jobName,
      isEnabled
    }: {
      jobName: string;
      isEnabled: boolean;
    }): Promise<UpdateRecurringJobStatusResponse> => {
      const client = getAuthenticatedApiClient();
      const request = new UpdateJobStatusRequestBody({ isEnabled });
      return await client.recurringJobs_UpdateJobStatus(jobName, request);
    },
    onSuccess: (_data, variables) => {
      // Invalidate both the list and the affected single-job detail after a successful update
      queryClient.invalidateQueries({ queryKey: recurringJobsKeys.list() });
      queryClient.invalidateQueries({ queryKey: recurringJobsKeys.detail(variables.jobName) });
    },
  });
};

/**
 * Hook to update recurring job CRON expression
 * Uses generated API client method: recurringJobs_UpdateJobCron
 */
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
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: recurringJobsKeys.list() });
    },
  });
};

/**
 * Hook to trigger a recurring job manually
 * Uses generated API client method: recurringJobs_TriggerJob
 */
export const useTriggerRecurringJobMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (jobName: string): Promise<TriggerRecurringJobResponse> => {
      const client = getAuthenticatedApiClient();
      return await client.recurringJobs_TriggerJob(jobName);
    },
    onSuccess: () => {
      // Invalidate and refetch recurring jobs list after successful trigger
      queryClient.invalidateQueries({ queryKey: recurringJobsKeys.list() });
    },
  });
};

// Re-export types for convenience
export type { RecurringJobDto, UpdateRecurringJobStatusResponse, UpdateRecurringJobCronResponse, TriggerRecurringJobResponse };
