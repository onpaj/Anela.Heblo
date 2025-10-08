import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  GetProductMarginSummaryResponse,
  ProductGroupingMode,
} from "../generated/api-client";

// Re-export the generated types for convenience
export { GetProductMarginSummaryResponse, ProductGroupingMode };

export const useProductMarginSummaryQuery = (
  timeWindow: string = "current-year",
  groupingMode: ProductGroupingMode = ProductGroupingMode.Products,
  marginLevel: string = "M3",
) => {
  return useQuery<GetProductMarginSummaryResponse, Error>({
    queryKey: [...QUERY_KEYS.productMarginSummary, timeWindow, groupingMode, marginLevel],
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      // Use sortBy parameter to sort by the selected margin level percentage (descending)
      const sortBy = `totalmargin`;
      
      // Use generated API client method with proper parameters
      return apiClient.analytics_GetProductMarginSummary(
        timeWindow,
        0, // topProductCount = 0 means no limit
        groupingMode,
        marginLevel,
        sortBy,
        true // sortDescending = true
      );
    },
    staleTime: 5 * 60 * 1000, // Consider data stale after 5 minutes
    gcTime: 10 * 60 * 1000, // Keep cache for 10 minutes
  });
};
