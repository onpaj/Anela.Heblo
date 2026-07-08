import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  GetFinancialComparisonResponse,
  YearComparisonSeriesDto,
  FinancialComparisonMetadataDto,
} from "../generated/api-client";

// Re-export the generated types for convenience
export {
  GetFinancialComparisonResponse,
  YearComparisonSeriesDto,
  FinancialComparisonMetadataDto,
};

export const useFinancialComparisonQuery = (
  years: number = 3,
  includeStockData: boolean = true,
  excludedDepartments: string[] = [],
  includePartialMonth: boolean = true,
  enabled: boolean = true,
) => {
  return useQuery<GetFinancialComparisonResponse, Error>({
    queryKey: [
      ...QUERY_KEYS.financialComparison,
      years,
      includeStockData,
      excludedDepartments,
      includePartialMonth,
    ],
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      return await apiClient.financialOverview_GetFinancialComparison(
        years,
        includeStockData,
        excludedDepartments,
        includePartialMonth,
      );
    },
    enabled,
    staleTime: 5 * 60 * 1000,
    gcTime: 10 * 60 * 1000,
  });
};
