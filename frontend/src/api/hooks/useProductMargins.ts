import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import { GetProductMarginsResponse, ProductMarginDto } from '../generated/api-client';

// Re-export the generated types for convenience
export { GetProductMarginsResponse, ProductMarginDto };

export const useProductMarginsQuery = (
  productCode?: string,
  productName?: string,
  pageNumber: number = 1,
  pageSize: number = 20,
  sortBy?: string,
  sortDescending: boolean = false,
  dateFrom?: Date,
  dateTo?: Date
) => {
  return useQuery<GetProductMarginsResponse, Error>({
    queryKey: ['productMargins', productCode, productName, pageNumber, pageSize, sortBy, sortDescending, dateFrom, dateTo],
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      return apiClient.productMargins_GetProductMargins(
        productCode || null,
        productName || null, 
        pageNumber,
        pageSize,
        sortBy || null,
        sortDescending,
        dateFrom || null,
        dateTo || null
      );
    },
    staleTime: 5 * 60 * 1000, // Consider data stale after 5 minutes
    gcTime: 10 * 60 * 1000, // Keep cache for 10 minutes
  });
};