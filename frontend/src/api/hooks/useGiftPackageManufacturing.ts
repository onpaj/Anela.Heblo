import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { ApiClient as GeneratedApiClient } from "../generated/api-client";
import {
  GetAvailableGiftPackagesResponse,
  GetGiftPackageDetailResponse,
  CreateGiftPackageManufactureRequest,
  CreateGiftPackageManufactureResponse,
  GetManufactureLogResponse,
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
}

/**
 * Hook to get available gift packages (without detailed BOM information)
 * Note: Current API doesn't support date parameters, but hook is prepared for future extension
 */
export const useAvailableGiftPackages = (params?: GiftPackageQueryParams) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.giftPackages, "available", params?.fromDate, params?.toDate],
    queryFn: async (): Promise<GetAvailableGiftPackagesResponse> => {
      const client = getGiftPackageClient();
      // TODO: When API supports date parameters, pass them here:
      // return await client.logistics_GetAvailableGiftPackages(params?.fromDate, params?.toDate);
      return await client.logistics_GetAvailableGiftPackages();
    },
    staleTime: 5 * 60 * 1000, // 5 minutes
    gcTime: 10 * 60 * 1000, // 10 minutes (was cacheTime in v4)
  });
};

/**
 * Hook to get gift package detail with full BOM information
 */
export const useGiftPackageDetail = (giftPackageCode?: string) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.giftPackages, "detail", giftPackageCode || ""],
    queryFn: async (): Promise<GetGiftPackageDetailResponse> => {
      if (!giftPackageCode) {
        throw new Error("Gift package code is required");
      }
      
      const client = getGiftPackageClient();
      return await client.logistics_GetGiftPackageDetail(giftPackageCode);
    },
    enabled: !!giftPackageCode,
    staleTime: 2 * 60 * 1000, // 2 minutes
    gcTime: 5 * 60 * 1000, // 5 minutes
  });
};

// TODO: Implement stock validation hooks when backend endpoint is available

/**
 * Hook to create gift package manufacture
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