import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import { GetProductMarginsResponse, ProductMarginDto } from '../generated/api-client';

// Re-export the generated types for convenience
export { GetProductMarginsResponse, ProductMarginDto };

export const useProductMarginsQuery = (
  productCode?: string,
  productName?: string,
  pageNumber: number = 1,
  pageSize: number = 20
) => {
  return useQuery<GetProductMarginsResponse, Error>({
    queryKey: ['productMargins', productCode, productName, pageNumber, pageSize],
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      return apiClient.productMargins_GetProductMargins(
        productCode || null,
        productName || null, 
        pageNumber,
        pageSize
      );
    },
    staleTime: 5 * 60 * 1000, // Consider data stale after 5 minutes
    gcTime: 10 * 60 * 1000, // Keep cache for 10 minutes
  });
};