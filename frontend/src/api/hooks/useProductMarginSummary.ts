import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import { GetProductMarginSummaryResponse, ProductGroupingMode } from '../generated/api-client';

// Re-export the generated types for convenience
export { GetProductMarginSummaryResponse, ProductGroupingMode };

export const useProductMarginSummaryQuery = (
  timeWindow: string = 'current-year', 
  groupingMode: ProductGroupingMode = ProductGroupingMode.Products
) => {
  return useQuery<GetProductMarginSummaryResponse, Error>({
    queryKey: [...QUERY_KEYS.productMarginSummary, timeWindow, groupingMode],
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      // Backend now returns all products, topProductCount parameter is no longer needed
      return apiClient.analytics_GetProductMarginSummary(timeWindow, 0, groupingMode);
    },
    staleTime: 5 * 60 * 1000, // Consider data stale after 5 minutes
    gcTime: 10 * 60 * 1000, // Keep cache for 10 minutes
  });
};