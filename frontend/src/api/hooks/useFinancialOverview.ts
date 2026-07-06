import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  GetFinancialOverviewResponse,
  MonthlyFinancialDataDto,
  FinancialSummaryDto,
  StockChangeDto,
  StockSummaryDto,
} from "../generated/api-client";

// Re-export the generated types for convenience
export {
  GetFinancialOverviewResponse,
  MonthlyFinancialDataDto,
  FinancialSummaryDto,
  StockChangeDto,
  StockSummaryDto,
};

export const useFinancialOverviewQuery = (
  months: number = 6,
  includeStockData: boolean = true,
  excludedDepartments: string[] = [],
  includeCurrentMonth: boolean = false,
) => {
  return useQuery<GetFinancialOverviewResponse, Error>({
    queryKey: [...QUERY_KEYS.financialOverview, months, includeStockData, excludedDepartments, includeCurrentMonth],
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      return await apiClient.financialOverview_GetFinancialOverview(
        months,
        includeStockData,
        excludedDepartments,
        includeCurrentMonth,
      );
    },
    staleTime: 5 * 60 * 1000, // Consider data stale after 5 minutes
    gcTime: 10 * 60 * 1000, // Keep cache for 10 minutes
  });
};
