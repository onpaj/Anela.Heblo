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
      const params = new URLSearchParams();
      params.set('months', String(months));
      params.set('includeStockData', String(includeStockData));
      params.set('includeCurrentMonth', String(includeCurrentMonth));
      excludedDepartments.forEach(d => params.append('excludedDepartments', d));
      const relativeUrl = `/api/FinancialOverview?${params.toString()}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });
      if (!response.ok) throw new Error(`Failed to fetch financial overview: ${response.statusText}`);
      return await response.json() as GetFinancialOverviewResponse;
    },
    staleTime: 5 * 60 * 1000, // Consider data stale after 5 minutes
    gcTime: 10 * 60 * 1000, // Keep cache for 10 minutes
  });
};
