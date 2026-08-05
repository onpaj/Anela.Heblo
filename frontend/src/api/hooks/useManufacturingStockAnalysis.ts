import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import {
  TimePeriod,
  resolveTimePeriod,
  getTimePeriodDisplayText,
  type DateRange,
} from "../../utils/timePeriod";
import {
  TimePeriod as GeneratedTimePeriod,
  GetManufacturingStockAnalysisResponse,
  ManufacturingStockItemDto,
  ManufacturingStockSummaryDto,
  ManufacturingStockSeverity,
  ManufacturingStockSortBy,
} from "../generated/api-client";

export { TimePeriod as TimePeriodFilter };
export { getTimePeriodDisplayText };

// Re-exported so existing consumers (ManufacturingStockAnalysis.tsx, ManufactureBatchPlanning.tsx)
// keep importing types/enums from this hook module unchanged, per spec FR-4 / arch-review Decision 1.
export {
  GetManufacturingStockAnalysisResponse,
  ManufacturingStockItemDto,
  ManufacturingStockSummaryDto,
  ManufacturingStockSeverity,
  ManufacturingStockSortBy,
};

export function calculateTimePeriodRange(
  period: TimePeriod,
  customFrom?: Date,
  customTo?: Date,
): { fromDate: Date; toDate: Date; ranges?: DateRange[] } | null {
  const result = resolveTimePeriod(period, customFrom, customTo);
  if (!result.primary) return null;
  return {
    fromDate: result.primary.from,
    toDate: result.primary.to,
    ranges: result.ranges.length > 1 ? result.ranges : undefined,
  };
}

// Request shape accepted by the query hook. The generated client method takes positional scalar
// arguments (not a request object), so this local interface remains the hook's public parameter
// contract. `sortBy` types against the generated enum; `timePeriod` stays typed against the
// app-level TimePeriodFilter (unchanged for all other consumers, e.g. calculateTimePeriodRange) —
// see FR-3 in spec.r1.md and design.r1.md's Component Design section.
export interface GetManufacturingStockAnalysisRequest {
  timePeriod?: TimePeriod;
  customFromDate?: Date;
  customToDate?: Date;
  productFamily?: string;
  criticalItemsOnly?: boolean;
  majorItemsOnly?: boolean;
  adequateItemsOnly?: boolean;
  unconfiguredOnly?: boolean;
  searchTerm?: string;
  pageNumber?: number;
  pageSize?: number;
  sortBy?: ManufacturingStockSortBy;
  sortDescending?: boolean;
  salesMultiplier?: number;
}

/**
 * Converts the app-level TimePeriodFilter to the generated client's own TimePeriod enum at the
 * single API boundary (spec FR-3). Both are string enums with identical members
 * (PreviousQuarter/FutureQuarter/Y2Y/PreviousSeason/Q9M/CustomPeriod) — TypeScript enums are
 * nominal, so this is a same-string-value cast, not a data mapping; do not turn this into a
 * value-by-value mapping table unless the two enums genuinely diverge in membership.
 *
 * Q9M is the backend's implicit default: the pre-refactor query-string builder never appended
 * `timePeriod` when it was Q9M, so this returns `undefined` in that case to preserve that
 * omission. Used by both useManufacturingStockAnalysisQuery's queryFn and
 * ManufacturingStockAnalysis.tsx's handleExport, so there is exactly one conversion point.
 */
export const toGeneratedTimePeriod = (
  timePeriod: TimePeriod | undefined,
): GeneratedTimePeriod | undefined =>
  timePeriod && timePeriod !== TimePeriod.Q9M
    ? (timePeriod as unknown as GeneratedTimePeriod)
    : undefined;

// Query keys
const manufacturingStockAnalysisKeys = {
  all: ["manufacturing-stock-analysis"] as const,
  lists: () => [...manufacturingStockAnalysisKeys.all, "list"] as const,
  list: (filters: GetManufacturingStockAnalysisRequest) =>
    [...manufacturingStockAnalysisKeys.lists(), filters] as const,
};

// Main hook for manufacturing stock analysis
export const useManufacturingStockAnalysisQuery = (
  request: GetManufacturingStockAnalysisRequest,
) => {
  return useQuery({
    queryKey: manufacturingStockAnalysisKeys.list(request),
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();

      return apiClient.manufacturingStockAnalysis_GetStockAnalysis(
        toGeneratedTimePeriod(request.timePeriod),
        request.customFromDate,
        request.customToDate,
        request.productFamily,
        request.criticalItemsOnly,
        request.majorItemsOnly,
        request.adequateItemsOnly,
        request.unconfiguredOnly,
        request.searchTerm,
        request.pageNumber,
        request.pageSize,
        request.sortBy,
        request.sortDescending,
        request.salesMultiplier,
        false, // isExport — this hook is for interactive display, not export
      );
    },
    staleTime: 1000 * 60 * 2, // 2 minutes (stock data changes less frequently than purchase orders)
  });
};

// Helper function to get severity color class
export const getManufacturingSeverityColorClass = (
  severity: ManufacturingStockSeverity,
): string => {
  switch (severity) {
    case ManufacturingStockSeverity.Critical:
      return "text-red-600 bg-red-50";
    case ManufacturingStockSeverity.Major:
      return "text-orange-600 bg-orange-50";
    case ManufacturingStockSeverity.Minor:
      return "text-yellow-600 bg-yellow-50";
    case ManufacturingStockSeverity.Adequate:
      return "text-green-600 bg-green-50";
    case ManufacturingStockSeverity.Unconfigured:
      return "text-gray-600 bg-gray-50";
    default:
      return "text-gray-600 bg-gray-50";
  }
};

// Helper function to get severity display text
export const getManufacturingSeverityDisplayText = (
  severity: ManufacturingStockSeverity,
): string => {
  switch (severity) {
    case ManufacturingStockSeverity.Critical:
      return "Kritické";
    case ManufacturingStockSeverity.Major:
      return "Důležité";
    case ManufacturingStockSeverity.Minor:
      return "Menší";
    case ManufacturingStockSeverity.Adequate:
      return "Dostatečné";
    case ManufacturingStockSeverity.Unconfigured:
      return "Nezkonfigurováno";
    default:
      return "Neznámé";
  }
};

// Helper function to format Czech number
// Widened to accept `number | undefined` because the generated ManufacturingStockItemDto marks
// every numeric field optional (NSwag's default for all response DTOs), unlike the hand-coded
// interface it replaces. Mirrors the identical convention already shipped in the sibling
// frontend/src/api/hooks/usePurchaseStockAnalysis.ts.
export const formatNumber = (
  value: number | undefined,
  decimals: number = 2,
): string => {
  if (value === undefined || value === null) return "—";
  return value.toLocaleString("cs-CZ", {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  });
};

// Helper function to format percentage
export const formatPercentage = (value: number | undefined): string => {
  if (value === undefined || value === null) return "—";
  return `${formatNumber(value, 1)}%`;
};

// Helper function to format warehouse stock with transport + manufactured breakdown
export const formatWarehouseStock = (item: ManufacturingStockItemDto): string => {
  const totalStock = formatNumber(item.currentStock, 0);
  const transport = item.transportStock ?? 0;
  const manufactured = item.manufacturedStock ?? 0;

  // If there are no secondary parts, show just the total
  if (transport === 0 && manufactured === 0) {
    return totalStock;
  }

  // Otherwise show breakdown: "15 (5+7+3)" = total (primary+transport+manufactured)
  const primaryStock =
    item.primaryStockSource === "Erp"
      ? formatNumber(item.erpStock, 0)
      : formatNumber(item.eshopStock, 0);

  const parts = [primaryStock];
  if (transport !== 0) {
    parts.push(formatNumber(transport, 0));
  }
  if (manufactured !== 0) {
    parts.push(formatNumber(manufactured, 0));
  }

  return `${totalStock} (${parts.join("+")})`;
};
