import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { ApiClient as GeneratedApiClient } from "../generated/api-client";
import {
  GetAvailableGiftPackagesResponse,
  GetGiftPackageDetailResponse,
  CreateGiftPackageManufactureRequest,
  CreateGiftPackageManufactureResponse,
  GetManufactureLogResponse,
  EnqueueGiftPackageManufactureRequest,
  EnqueueGiftPackageManufactureResponse,
  GetGiftPackageManufactureJobStatusResponse,
  GetRunningJobsForGiftPackageResponse,
} from "../generated/api-client";


// Helper to get the correct API client instance
export const getGiftPackageClient = (): GeneratedApiClient => {
  const apiClient = getAuthenticatedApiClient();
  return apiClient as any as GeneratedApiClient;
};

// Parameters for gift package queries (for future API extensions)
export interface GiftPackageQueryParams {
  fromDate?: Date;
  toDate?: Date;
  salesCoefficient?: number;
}

/**
 * Hook to get available gift packages (without detailed BOM information)
 */
export const useAvailableGiftPackages = (params?: GiftPackageQueryParams) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.giftPackages, "available", params?.fromDate, params?.toDate, params?.salesCoefficient],
    queryFn: async (): Promise<GetAvailableGiftPackagesResponse> => {
      const client = getGiftPackageClient();
      return await client.logistics_GetAvailableGiftPackages(
        params?.salesCoefficient, 
        params?.fromDate, 
        params?.toDate
      );
    },
    staleTime: 5 * 60 * 1000, // 5 minutes
    gcTime: 10 * 60 * 1000, // 10 minutes (was cacheTime in v4)
  });
};

/**
 * Hook to get gift package detail with full BOM information
 */
export const useGiftPackageDetail = (giftPackageCode?: string, salesCoefficient?: number, fromDate?: Date, toDate?: Date) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.giftPackages, "detail", giftPackageCode || "", salesCoefficient, fromDate, toDate],
    queryFn: async (): Promise<GetGiftPackageDetailResponse> => {
      if (!giftPackageCode) {
        throw new Error("Gift package code is required");
      }
      
      const client = getGiftPackageClient();
      return await client.logistics_GetGiftPackageDetail(giftPackageCode, salesCoefficient, fromDate, toDate);
    },
    enabled: !!giftPackageCode,
    staleTime: 2 * 60 * 1000, // 2 minutes
    gcTime: 5 * 60 * 1000, // 5 minutes
  });
};

// TODO: Implement stock validation hooks when backend endpoint is available

/**
 * Hook to create gift package manufacture (synchronous)
 */
export const useCreateGiftPackageManufacture = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (request: CreateGiftPackageManufactureRequest): Promise<CreateGiftPackageManufactureResponse> => {
      const client = getGiftPackageClient();
      return await client.logistics_CreateGiftPackageManufacture(request);
    },
    onSuccess: () => {
      // Invalidate and refetch related queries after successful manufacturing
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.giftPackages, "available"] });
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.giftPackages, "manufacture", "log"] });
    },
  });
};

/**
 * Hook to enqueue gift package manufacture (asynchronous)
 */
export const useEnqueueGiftPackageManufacture = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (request: EnqueueGiftPackageManufactureRequest): Promise<EnqueueGiftPackageManufactureResponse> => {
      const client = getGiftPackageClient();
      return await client.logistics_EnqueueGiftPackageManufacture(request);
    },
    onSuccess: () => {
      // Invalidate running jobs queries to show the new job
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.giftPackages, "jobs"] });
    },
  });
};

/**
 * Hook to get manufacturing log
 */
export const useManufactureLog = (count: number = 10) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.giftPackages, "manufacture", "log", count],
    queryFn: async (): Promise<GetManufactureLogResponse> => {
      const client = getGiftPackageClient();
      return await client.logistics_GetManufactureLog(count);
    },
    staleTime: 1 * 60 * 1000, // 1 minute
    gcTime: 5 * 60 * 1000, // 5 minutes
  });
};

/**
 * Hook to get job status by job ID
 */
export const useGiftPackageManufactureJobStatus = (jobId?: string) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.giftPackages, "jobs", "status", jobId || ""],
    queryFn: async (): Promise<GetGiftPackageManufactureJobStatusResponse> => {
      if (!jobId) {
        throw new Error("Job ID is required");
      }
      
      const client = getGiftPackageClient();
      return await client.logistics_GetGiftPackageManufactureJobStatus(jobId);
    },
    enabled: !!jobId,
    refetchInterval: (data) => {
      // Poll every 2 seconds if job is still running
      const isRunning = data?.jobStatus?.isRunning;
      return isRunning ? 2000 : false;
    },
    staleTime: 0, // Always fetch fresh data for job status
    gcTime: 30 * 1000, // Keep in cache for 30 seconds
  });
};

/**
 * Hook to get running jobs for a specific gift package
 */
export const useRunningJobsForGiftPackage = (giftPackageCode?: string) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.giftPackages, "jobs", "running", giftPackageCode || ""],
    queryFn: async (): Promise<GetRunningJobsForGiftPackageResponse> => {
      if (!giftPackageCode) {
        throw new Error("Gift package code is required");
      }
      
      const client = getGiftPackageClient();
      return await client.logistics_GetRunningJobsForGiftPackage(giftPackageCode);
    },
    enabled: !!giftPackageCode,
    refetchInterval: 5000, // Poll every 5 seconds to check for running jobs
    staleTime: 0, // Always fetch fresh data
    gcTime: 10 * 1000, // Keep in cache for 10 seconds
  });
};

// Export centralized query key factory for external use (e.g., manual cache invalidation)
export const giftPackageQueryKeys = {
  all: () => QUERY_KEYS.giftPackages,
  available: () => [...QUERY_KEYS.giftPackages, "available"] as const,
  detail: (giftPackageCode: string) => [...QUERY_KEYS.giftPackages, "detail", giftPackageCode] as const,
  validation: () => [...QUERY_KEYS.giftPackages, "validation"] as const,
  manufacture: () => [...QUERY_KEYS.giftPackages, "manufacture"] as const,
  log: () => [...QUERY_KEYS.giftPackages, "manufacture", "log"] as const,
  jobs: () => [...QUERY_KEYS.giftPackages, "jobs"] as const,
  jobStatus: (jobId: string) => [...QUERY_KEYS.giftPackages, "jobs", "status", jobId] as const,
  runningJobs: (giftPackageCode: string) => [...QUERY_KEYS.giftPackages, "jobs", "running", giftPackageCode] as const,
};