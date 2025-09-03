import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import { 
  CreateManufactureDifficultyRequest,
  UpdateManufactureDifficultyRequest,
  GetManufactureDifficultySettingsResponse,
  CreateManufactureDifficultyResponse,
  UpdateManufactureDifficultyResponse,
  DeleteManufactureDifficultyResponse
} from '../generated/api-client';

// Hook to get manufacture difficulty settings for a product
export const useManufactureDifficultySettings = (productCode: string | null, enabled: boolean = true) => {
  return useQuery({
    queryKey: ['manufacture-difficulty-settings', productCode],
    queryFn: async (): Promise<GetManufactureDifficultySettingsResponse> => {
      if (!productCode) {
        throw new Error('Product code is required');
      }
      
      const apiClient = await getAuthenticatedApiClient();
      return await apiClient.catalog_GetManufactureDifficultyHistory(productCode, null);
    },
    enabled: enabled && !!productCode,
    staleTime: 5 * 60 * 1000, // 5 minutes
  });
};

// Hook to create a new manufacture difficulty setting
export const useCreateManufactureDifficulty = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request: CreateManufactureDifficultyRequest): Promise<CreateManufactureDifficultyResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      return await apiClient.catalog_CreateManufactureDifficulty(request);
    },
    onSuccess: (data, variables) => {
      // Invalidate related queries
      queryClient.invalidateQueries({ 
        queryKey: ['manufacture-difficulty-settings', variables.productCode] 
      });
      queryClient.invalidateQueries({ 
        queryKey: ['catalog', 'detail', variables.productCode] 
      });
      queryClient.invalidateQueries({ 
        queryKey: ['catalog'] 
      });
    },
  });
};

// Hook to update an existing manufacture difficulty setting
export const useUpdateManufactureDifficulty = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, request }: { id: number; request: UpdateManufactureDifficultyRequest }): Promise<UpdateManufactureDifficultyResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      return await apiClient.catalog_UpdateManufactureDifficulty(id, request);
    },
    onSuccess: (data, variables) => {
      // Invalidate related queries - need to get productCode from the response
      const productCode = data.difficultyHistory?.productCode;
      if (productCode) {
        queryClient.invalidateQueries({ 
          queryKey: ['manufacture-difficulty-settings', productCode] 
        });
        queryClient.invalidateQueries({ 
          queryKey: ['catalog-detail', productCode] 
        });
        queryClient.invalidateQueries({ 
          queryKey: ['catalog'] 
        });
      }
    },
  });
};

// Hook to delete a manufacture difficulty setting
export const useDeleteManufactureDifficulty = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, productCode }: { id: number; productCode: string }): Promise<DeleteManufactureDifficultyResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      return await apiClient.catalog_DeleteManufactureDifficulty(id);
    },
    onSuccess: (data, variables) => {
      // Invalidate related queries
      queryClient.invalidateQueries({ 
        queryKey: ['manufacture-difficulty-settings', variables.productCode] 
      });
      queryClient.invalidateQueries({ 
        queryKey: ['catalog', 'detail', variables.productCode] 
      });
      queryClient.invalidateQueries({ 
        queryKey: ['catalog'] 
      });
    },
  });
};