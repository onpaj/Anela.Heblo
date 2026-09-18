import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  ChannelCostDto,
  ChannelInfoDto,
  GetMarketingPerformanceComparisonResponse,
  GetMarketingPerformanceMonthsResponse,
  MarketingYearSeriesDto,
  MonthlyMarketingPerformanceDto,
  RecomputeMarketingPerformanceRequest,
  RecomputeMarketingPerformanceResponse,
} from "../generated/api-client";

export type {
  ChannelCostDto,
  ChannelInfoDto,
  GetMarketingPerformanceComparisonResponse,
  GetMarketingPerformanceMonthsResponse,
  MarketingYearSeriesDto,
  MonthlyMarketingPerformanceDto,
  RecomputeMarketingPerformanceResponse,
};

const STALE_TIME_MS = 5 * 60 * 1000;
const GC_TIME_MS = 10 * 60 * 1000;

export interface MonthsQueryParams {
  from?: string;
  to?: string;
  includeWholesale: boolean;
}

export interface ComparisonQueryParams {
  years: number;
  includeWholesale: boolean;
}

export const useMarketingPerformanceMonthsQuery = (params: MonthsQueryParams, enabled = true) =>
  useQuery<GetMarketingPerformanceMonthsResponse, Error>({
    queryKey: [...QUERY_KEYS.marketingPerformanceMonths, params.from ?? null, params.to ?? null, params.includeWholesale],
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      return await apiClient.marketingPerformance_GetMonths(params.from, params.to, params.includeWholesale);
    },
    enabled,
    staleTime: STALE_TIME_MS,
    gcTime: GC_TIME_MS,
  });

export const useMarketingPerformanceComparisonQuery = (params: ComparisonQueryParams, enabled = true) =>
  useQuery<GetMarketingPerformanceComparisonResponse, Error>({
    queryKey: [...QUERY_KEYS.marketingPerformanceComparison, params.years, params.includeWholesale],
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      return await apiClient.marketingPerformance_GetComparison(params.years, params.includeWholesale);
    },
    enabled,
    staleTime: STALE_TIME_MS,
    gcTime: GC_TIME_MS,
  });

export const useRecomputeMarketingPerformanceMutation = () => {
  const queryClient = useQueryClient();
  return useMutation<RecomputeMarketingPerformanceResponse, Error, { from: string; to: string }>({
    mutationFn: async ({ from, to }) => {
      const apiClient = getAuthenticatedApiClient();
      const body = new RecomputeMarketingPerformanceRequest({ from, to });
      return await apiClient.marketingPerformance_Recompute(body);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.marketingPerformanceMonths });
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.marketingPerformanceComparison });
    },
  });
};
