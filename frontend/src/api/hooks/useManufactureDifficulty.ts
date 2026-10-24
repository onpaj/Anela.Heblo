import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  CreateManufactureDifficultyRequest,
  GetManufactureDifficultySettingsResponse,
  CreateManufactureDifficultyResponse,
  DeleteManufactureDifficultyResponse,
} from "../generated/api-client";

// Hook to get manufacture difficulty settings for a product
export const useManufactureDifficultySettings = (
  productCode: string | null,
  enabled: boolean = true,
) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.manufactureDifficulty, productCode],
    queryFn: async (): Promise<GetManufactureDifficultySettingsResponse> => {
      if (!productCode) {
        throw new Error("Product code is required");
      }

      const apiClient = await getAuthenticatedApiClient();
      return await apiClient.catalog_GetManufactureDifficultyHistory(
        productCode,
        null,
      );
    },
    enabled: enabled && !!productCode,
    staleTime: 5 * 60 * 1000, // 5 minutes
  });
};

// Hook to create a new manufacture difficulty setting
export const useCreateManufactureDifficulty = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (
      request: CreateManufactureDifficultyRequest,
    ): Promise<CreateManufactureDifficultyResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      return await apiClient.catalog_CreateManufactureDifficulty(request);
    },
    onSuccess: (data, variables) => {
      // Invalidate related queries
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.manufactureDifficulty, variables.productCode],
      });
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.catalog, "detail", variables.productCode],
      });
      queryClient.invalidateQueries({
        queryKey: QUERY_KEYS.catalog,
      });
    },
  });
};

// Hook to delete a manufacture difficulty setting
export const useDeleteManufactureDifficulty = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      id,
      productCode,
    }: {
      id: number;
      productCode: string;
    }): Promise<DeleteManufactureDifficultyResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      return await apiClient.catalog_DeleteManufactureDifficulty(id);
    },
    onSuccess: (data, variables) => {
      // Invalidate related queries
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.manufactureDifficulty, variables.productCode],
      });
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.catalog, "detail", variables.productCode],
      });
      queryClient.invalidateQueries({
        queryKey: QUERY_KEYS.catalog,
      });
    },
  });
};
