import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { GetWarehouseStatisticsResponse } from "../generated/api-client";

export function useWarehouseStatistics() {
  return useQuery({
    queryKey: QUERY_KEYS.warehouseStatistics,
    queryFn: async (): Promise<GetWarehouseStatisticsResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      return apiClient.catalog_GetWarehouseStatistics();
    },
    refetchInterval: 5 * 60 * 1000, // Refetch every 5 minutes
  });
}