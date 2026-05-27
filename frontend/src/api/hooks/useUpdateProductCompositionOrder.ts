import { useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

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
    mutationFn: async (payload: UpdateProductCompositionOrderPayload) => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/catalog/${encodeURIComponent(payload.productCode)}/composition/order`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ order: payload.order }),
      });
      if (!response.ok) {
        const text = await response.text();
        throw new Error(`Update composition order failed: ${response.status} ${text}`);
      }
      return response.json();
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.catalog, 'composition', variables.productCode],
      });
    },
  });
};
