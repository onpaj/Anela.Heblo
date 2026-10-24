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
} from "../generated/api-client";


// Helper to get the correct API client instance
const getGiftPackageClient = (): GeneratedApiClient => {
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
