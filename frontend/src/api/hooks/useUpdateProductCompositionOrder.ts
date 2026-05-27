import { useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import {
  UpdateProductCompositionOrderRequest,
  IngredientOrderItem as GeneratedIngredientOrderItem,
} from '../generated/api-client';

export interface IngredientOrderItem {
  ingredientProductCode: string;
  sortOrder: number;
}

export interface UpdateProductCompositionOrderPayload {
  productCode: string;
  order: IngredientOrderItem[];
}

export const useUpdateProductCompositionOrder = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload: UpdateProductCompositionOrderPayload) => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.catalog_UpdateCompositionOrder(
        payload.productCode,
        new UpdateProductCompositionOrderRequest({
          productCode: payload.productCode,
          order: payload.order.map(
            (item) => new GeneratedIngredientOrderItem(item),
          ),
        }),
      );
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.catalog, 'composition', variables.productCode],
      });
    },
  });
};
