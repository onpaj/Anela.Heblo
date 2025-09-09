import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import { ApiClient as GeneratedApiClient } from "../generated/api-client";
import {
  GetAvailableGiftPackagesResponse,
  GetGiftPackageDetailResponse,
  ValidateGiftPackageStockRequest,
  ValidateGiftPackageStockResponse,
  CreateGiftPackageManufactureRequest,
  CreateGiftPackageManufactureResponse,
  GetManufactureLogResponse,
} from "../generated/api-client";

// Query keys
const giftPackageKeys = {
  all: ["gift-packages"] as const,
  available: () => [...giftPackageKeys.all, "available"] as const,
  detail: (giftPackageCode: string) => [...giftPackageKeys.all, "detail", giftPackageCode] as const,
  validation: () => [...giftPackageKeys.all, "validation"] as const,
  validateStock: (giftPackageCode: string, quantity: number) =>
    [...giftPackageKeys.validation(), giftPackageCode, quantity] as const,
  manufacture: () => [...giftPackageKeys.all, "manufacture"] as const,
  log: () => [...giftPackageKeys.manufacture(), "log"] as const,
  logWithCount: (count: number) => [...giftPackageKeys.log(), count] as const,
};

// Helper to get the correct API client instance
const getGiftPackageClient = (): GeneratedApiClient => {
  const apiClient = getAuthenticatedApiClient();
  return apiClient as any as GeneratedApiClient;
};

/**
 * Hook to get available gift packages (without detailed BOM information)
 */
export const useAvailableGiftPackages = () => {
  return useQuery({
    queryKey: giftPackageKeys.available(),
    queryFn: async (): Promise<GetAvailableGiftPackagesResponse> => {
      const client = getGiftPackageClient();
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
    queryKey: giftPackageKeys.detail(giftPackageCode || ""),
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

/**
 * Hook to validate stock for gift package manufacturing
 */
export const useValidateGiftPackageStock = (
  giftPackageCode?: string,
  quantity?: number
) => {
  return useQuery({
    queryKey: giftPackageKeys.validateStock(giftPackageCode || "", quantity || 0),
    queryFn: async (): Promise<ValidateGiftPackageStockResponse> => {
      if (!giftPackageCode || !quantity) {
        throw new Error("Gift package code and quantity are required");
      }
      
      const client = getGiftPackageClient();
      const request = new ValidateGiftPackageStockRequest({
        giftPackageCode,
        quantity,
      });
      
      return await client.logistics_ValidateGiftPackageStock(request);
    },
    enabled: !!giftPackageCode && !!quantity && quantity > 0,
    staleTime: 30 * 1000, // 30 seconds - stock changes frequently
    gcTime: 2 * 60 * 1000, // 2 minutes
  });
};

/**
 * Hook to manually validate stock (for on-demand validation)
 */
export const useValidateStockMutation = () => {
  return useMutation({
    mutationFn: async (request: ValidateGiftPackageStockRequest): Promise<ValidateGiftPackageStockResponse> => {
      const client = getGiftPackageClient();
      return await client.logistics_ValidateGiftPackageStock(request);
    },
  });
};

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
      queryClient.invalidateQueries({ queryKey: giftPackageKeys.available() });
      queryClient.invalidateQueries({ queryKey: giftPackageKeys.log() });
      queryClient.invalidateQueries({ queryKey: giftPackageKeys.validation() });
    },
  });
};

/**
 * Hook to get manufacturing log
 */
export const useManufactureLog = (count: number = 10) => {
  return useQuery({
    queryKey: giftPackageKeys.logWithCount(count),
    queryFn: async (): Promise<GetManufactureLogResponse> => {
      const client = getGiftPackageClient();
      return await client.logistics_GetManufactureLog(count);
    },
    staleTime: 1 * 60 * 1000, // 1 minute
    gcTime: 5 * 60 * 1000, // 5 minutes
  });
};

// Export query keys for external use (e.g., manual cache invalidation)
export { giftPackageKeys };