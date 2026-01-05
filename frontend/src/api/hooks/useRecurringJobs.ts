import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import {
  UpdateJobStatusRequestBody,
  type RecurringJobDto,
  type UpdateRecurringJobStatusResponse
} from '../generated/api-client';

// Query key factory for recurring jobs using centralized QUERY_KEYS
const recurringJobsKeys = {
  all: [...QUERY_KEYS.recurringJobs] as const,
  list: () => [...recurringJobsKeys.all, 'list'] as const,
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
    onSuccess: () => {
      // Invalidate and refetch recurring jobs list after successful update
      queryClient.invalidateQueries({ queryKey: recurringJobsKeys.list() });
    },
  });
};

// Re-export types for convenience
export type { RecurringJobDto, UpdateRecurringJobStatusResponse };
