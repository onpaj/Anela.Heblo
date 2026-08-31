import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { ProductStatisticsMetric } from "../generated/api-client";

export { ProductStatisticsMetric };
export type StatisticsMetric = ProductStatisticsMetric;

const MONTH_PATTERN = /^\d{4}-(0[1-9]|1[0-2])$/;

export function isValidMonthRange(dateFrom: string, dateTo: string): boolean {
  if (!MONTH_PATTERN.test(dateFrom) || !MONTH_PATTERN.test(dateTo)) {
    return false;
  }
  return dateFrom <= dateTo; // "yyyy-MM" sorts lexicographically the same as chronologically
}

export function useProductStatistics(
  productCodes: string[],
  metric: StatisticsMetric,
  dateFrom: string,
  dateTo: string,
) {
  const isEnabled =
    productCodes.length > 0 && isValidMonthRange(dateFrom, dateTo);

  return useQuery({
    queryKey: [
      ...QUERY_KEYS.catalog,
      "product-statistics",
      productCodes,
      metric,
      dateFrom,
      dateTo,
    ],
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      // The generated client throws on non-200; errors surface via React Query's `error`.
      return apiClient.catalog_GetProductStatistics(
        productCodes,
        metric,
        dateFrom,
        dateTo,
      );
    },
    enabled: isEnabled,
    staleTime: 5 * 60 * 1000,
  });
}
