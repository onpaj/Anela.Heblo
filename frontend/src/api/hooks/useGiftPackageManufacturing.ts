import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { ApiClient as GeneratedApiClient } from "../generated/api-client";
import {
  GetAvailableGiftPackagesResponse,
  GetGiftPackageDetailResponse,
  CreateGiftPackageManufactureRequest,
  CreateGiftPackageManufactureResponse,
  DisassembleGiftPackageRequest,
  DisassembleGiftPackageResponse,
  GetManufactureLogResponse,
  EnqueueGiftPackageManufactureRequest,
  EnqueueGiftPackageManufactureResponse,
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
 * Hook to disassemble gift package back to components
 */
export const useDisassembleGiftPackage = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request: DisassembleGiftPackageRequest): Promise<DisassembleGiftPackageResponse> => {
      const client = getGiftPackageClient();
      return await client.logistics_DisassembleGiftPackage(request);
    },
    onSuccess: () => {
      // Invalidate and refetch related queries after successful disassembly
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

// Export centralized query key factory for external use (e.g., manual cache invalidation)
export const giftPackageQueryKeys = {
  all: () => QUERY_KEYS.giftPackages,
  available: () => [...QUERY_KEYS.giftPackages, "available"] as const,
  detail: (giftPackageCode: string) => [...QUERY_KEYS.giftPackages, "detail", giftPackageCode] as const,
  validation: () => [...QUERY_KEYS.giftPackages, "validation"] as const,
  manufacture: () => [...QUERY_KEYS.giftPackages, "manufacture"] as const,
  log: () => [...QUERY_KEYS.giftPackages, "manufacture", "log"] as const,
};