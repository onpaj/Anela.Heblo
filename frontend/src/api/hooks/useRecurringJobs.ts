import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

export interface RecurringJobDto {
  jobName: string;
  displayName: string;
  description: string;
  cronExpression: string;
  isEnabled: boolean;
  lastModified?: string;
  modifiedBy?: string;
}

export interface UpdateRecurringJobStatusRequest {
  isEnabled: boolean;
}

// Query key factory for recurring jobs
export const recurringJobsKeys = {
  all: ['recurringJobs'] as const,
  lists: () => [...recurringJobsKeys.all, 'list'] as const,
  list: () => [...recurringJobsKeys.lists()] as const,
};

// Hook to fetch list of all recurring jobs
export const useRecurringJobs = () => {
  return useQuery({
    queryKey: recurringJobsKeys.list(),
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      const relativeUrl = `/api/recurringjobs`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
      });

      if (!response.ok) {
        throw new Error(`Failed to fetch recurring jobs: ${response.statusText}`);
      }

      return (await response.json()) as RecurringJobDto[];
    },
  });
};

// Hook to update recurring job status (enable/disable)
export const useUpdateRecurringJobStatus = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ jobName, isEnabled }: { jobName: string; isEnabled: boolean }) => {
      const apiClient = await getAuthenticatedApiClient();
      const relativeUrl = `/api/recurringjobs/${encodeURIComponent(jobName)}/status`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ isEnabled }),
      });

      if (!response.ok) {
        const errorText = await response.text();
        throw new Error(`Failed to update job status: ${errorText}`);
      }
    },
    onSuccess: () => {
      // Invalidate and refetch recurring jobs list after successful update
      queryClient.invalidateQueries({ queryKey: recurringJobsKeys.list() });
    },
  });
};
