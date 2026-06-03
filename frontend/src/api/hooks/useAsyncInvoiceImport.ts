import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import type {
  IBackgroundJobInfo,
  IEnqueueImportInvoicesRequest,
  IEnqueueImportInvoicesResponse,
} from "../generated/api-client";

/**
 * Hook to enqueue async invoice import
 */
export const useEnqueueInvoiceImport = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (request: IEnqueueImportInvoicesRequest): Promise<IEnqueueImportInvoicesResponse> => {
      const apiClient = await getAuthenticatedApiClient();

      const url = `/api/invoices/import/enqueue-async`;
      const fullUrl = `${(apiClient as any).baseUrl}${url}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(request),
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      // Wire format: response is the I* interface variant (plain JSON). Date-typed fields
      // (createdAt, startedAt) arrive as ISO strings; parse with `new Date(...)` if needed.
      return response.json();
    },
    onSuccess: () => {
      // Invalidate running jobs queries to show the new job
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.invoices, "jobs"] });
    },
  });
};

/**
 * Hook to get job status by job ID
 */
export const useInvoiceImportJobStatus = (jobId?: string) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.invoices, "import", "jobs", "status", jobId || ""],
    queryFn: async (): Promise<IBackgroundJobInfo | null> => {
      if (!jobId) {
        throw new Error("Job ID is required");
      }

      const apiClient = await getAuthenticatedApiClient();

      const url = `/api/invoices/import/job-status/${encodeURIComponent(jobId)}`;
      const fullUrl = `${(apiClient as any).baseUrl}${url}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
        },
      });

      if (response.status === 404) {
        return null; // Job not found
      }

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      // Wire format: response is the I* interface variant (plain JSON). Date-typed fields
      // (createdAt, startedAt) arrive as ISO strings; parse with `new Date(...)` if needed.
      return response.json();
    },
    enabled: !!jobId,
    refetchInterval: 2000, // Poll every 2 seconds
    staleTime: 0, // Always fetch fresh data for job status
    gcTime: 30 * 1000, // Keep in cache for 30 seconds
  });
};

/**
 * Hook to get running invoice import jobs
 */
export const useRunningInvoiceImportJobs = () => {
  return useQuery({
    queryKey: [...QUERY_KEYS.invoices, "import", "jobs", "running"],
    queryFn: async (): Promise<IBackgroundJobInfo[]> => {
      const apiClient = await getAuthenticatedApiClient();

      const url = `/api/invoices/import/running-jobs`;
      const fullUrl = `${(apiClient as any).baseUrl}${url}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
        },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      // Wire format: response is the I* interface variant (plain JSON). Date-typed fields
      // (createdAt, startedAt) arrive as ISO strings; parse with `new Date(...)` if needed.
      return response.json();
    },
    refetchInterval: 5000, // Poll every 5 seconds to check for running jobs
    staleTime: 0, // Always fetch fresh data
    gcTime: 10 * 1000, // Keep in cache for 10 seconds
    refetchOnWindowFocus: true, // Refetch when window gains focus
    refetchOnMount: true, // Refetch when component mounts
  });
};

// Export centralized query key factory for external use
export const invoiceImportQueryKeys = {
  all: () => [...QUERY_KEYS.invoices, "import"],
  jobs: () => [...QUERY_KEYS.invoices, "import", "jobs"] as const,
  jobStatus: (jobId: string) => [...QUERY_KEYS.invoices, "import", "jobs", "status", jobId] as const,
  runningJobs: () => [...QUERY_KEYS.invoices, "import", "jobs", "running"] as const,
};