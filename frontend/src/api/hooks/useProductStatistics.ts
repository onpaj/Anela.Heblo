import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { ProductStatisticsMetric } from "../generated/api-client";

export { ProductStatisticsMetric };
export type StatisticsMetric = ProductStatisticsMetric;

const MONTH_PATTERN = /^\d{4}-(0[1-9]|1[0-2])$/;

/**
 * First month any history reaches. Mirrors the backend's
 * CatalogConstants.HISTORY_FLOOR_DATE — a range ending before it expands to no months
 * at all, so it is rejected here rather than sent.
 */
export const HISTORY_FLOOR_MONTH = "2020-01";

/** Mirrors GetProductStatisticsRequestValidator.MaxMonths — anything wider is a 400. */
export const MAX_RANGE_MONTHS = 120;

export function isValidMonth(month: string): boolean {
  return MONTH_PATTERN.test(month);
}

/** Inclusive month count. "yyyy-MM" is fixed-width, so the parts parse positionally. */
function monthSpan(dateFrom: string, dateTo: string): number {
  const fromYear = Number(dateFrom.slice(0, 4));
  const fromMonth = Number(dateFrom.slice(5, 7));
  const toYear = Number(dateTo.slice(0, 4));
  const toMonth = Number(dateTo.slice(5, 7));

  return (toYear - fromYear) * 12 + (toMonth - fromMonth) + 1;
}

/**
 * The single source of truth for range validity, shared by the filter (which shows the
 * message) and the query (which refuses to fire). Keeping them on one rule is what stops
 * a range the filter accepts from silently disabling the query with nothing on screen.
 *
 * Returns a user-facing Czech message, or null when the range is usable.
 */
export function getMonthRangeError(
  dateFrom: string,
  dateTo: string,
): string | null {
  if (!isValidMonth(dateFrom) || !isValidMonth(dateTo)) {
    return "Zadejte období ve formátu RRRR-MM.";
  }

  // "yyyy-MM" sorts lexicographically the same as chronologically.
  if (dateFrom > dateTo) {
    return 'Datum "Od" musí být dříve než "Do".';
  }

  if (dateTo < HISTORY_FLOOR_MONTH) {
    return `Data jsou k dispozici až od ${HISTORY_FLOOR_MONTH}.`;
  }

  if (monthSpan(dateFrom, dateTo) > MAX_RANGE_MONTHS) {
    return `Rozsah nesmí přesáhnout ${MAX_RANGE_MONTHS} měsíců.`;
  }

  return null;
}

export function isValidMonthRange(dateFrom: string, dateTo: string): boolean {
  return getMonthRangeError(dateFrom, dateTo) === null;
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
