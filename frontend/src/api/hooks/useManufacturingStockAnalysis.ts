import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";

// Define types for the manufacturing stock analysis API
export interface GetManufacturingStockAnalysisRequest {
  timePeriod?: TimePeriodFilter;
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
}

export enum TimePeriodFilter {
  PreviousQuarter = "PreviousQuarter",
  FutureQuarter = "FutureQuarter",
  Y2Y = "Y2Y",
  PreviousSeason = "PreviousSeason",
  CustomPeriod = "CustomPeriod",
}

export enum ManufacturingStockSortBy {
  ProductCode = "ProductCode",
  ProductName = "ProductName",
  CurrentStock = "CurrentStock",
  Reserve = "Reserve",
  SalesInPeriod = "SalesInPeriod",
  DailySales = "DailySales",
  OptimalDaysSetup = "OptimalDaysSetup",
  StockDaysAvailable = "StockDaysAvailable",
  MinimumStock = "MinimumStock",
  OverstockPercentage = "OverstockPercentage",
  BatchSize = "BatchSize",
}

export enum ManufacturingStockSeverity {
  Critical = 0,
  Major = 1,
  Minor = 2,
  Adequate = 3,
  Unconfigured = 4,
}

export interface ManufacturingStockItemDto {
  code: string;
  name: string;
  currentStock: number;
  reserve: number;
  salesInPeriod: number;
  dailySalesRate: number;
  optimalDaysSetup: number;
  stockDaysAvailable: number;
  minimumStock: number;
  overstockPercentage: number;
  batchSize: string;
  productFamily?: string;
  severity: ManufacturingStockSeverity;
  isConfigured: boolean;
}

export interface ManufacturingStockSummaryDto {
  totalProducts: number;
  criticalCount: number;
  majorCount: number;
  minorCount: number;
  adequateCount: number;
  unconfiguredCount: number;
  analysisPeriodStart: string;
  analysisPeriodEnd: string;
  productFamilies: string[];
}

export interface GetManufacturingStockAnalysisResponse {
  items: ManufacturingStockItemDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  summary: ManufacturingStockSummaryDto;
}

// Query keys
const manufacturingStockAnalysisKeys = {
  all: ["manufacturing-stock-analysis"] as const,
  lists: () => [...manufacturingStockAnalysisKeys.all, "list"] as const,
  list: (filters: GetManufacturingStockAnalysisRequest) =>
    [...manufacturingStockAnalysisKeys.lists(), filters] as const,
};

// Helper to format date for API
const formatDateForApi = (date: Date): string => {
  return date.toISOString().split("T")[0]; // YYYY-MM-DD format
};

// Main hook for manufacturing stock analysis
export const useManufacturingStockAnalysisQuery = (
  request: GetManufacturingStockAnalysisRequest,
) => {
  return useQuery({
    queryKey: manufacturingStockAnalysisKeys.list(request),
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      const relativeUrl = `/api/manufacturing-stock-analysis`;
      const params = new URLSearchParams();

      if (
        request.timePeriod &&
        request.timePeriod !== TimePeriodFilter.PreviousQuarter
      ) {
        params.append("timePeriod", request.timePeriod);
      }
      if (request.customFromDate)
        params.append(
          "customFromDate",
          formatDateForApi(request.customFromDate),
        );
      if (request.customToDate)
        params.append("customToDate", formatDateForApi(request.customToDate));
      if (request.productFamily)
        params.append("productFamily", request.productFamily);
      if (request.criticalItemsOnly) params.append("criticalItemsOnly", "true");
      if (request.majorItemsOnly) params.append("majorItemsOnly", "true");
      if (request.adequateItemsOnly) params.append("adequateItemsOnly", "true");
      if (request.unconfiguredOnly) params.append("unconfiguredOnly", "true");
      if (request.searchTerm) params.append("searchTerm", request.searchTerm);
      if (request.pageNumber)
        params.append("pageNumber", request.pageNumber.toString());
      if (request.pageSize)
        params.append("pageSize", request.pageSize.toString());
      if (request.sortBy) params.append("sortBy", request.sortBy);
      if (request.sortDescending !== undefined)
        params.append("sortDescending", request.sortDescending.toString());

      const queryString = params.toString();
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}${queryString ? `?${queryString}` : ""}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "GET",
        headers: {
          Accept: "application/json",
        },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<GetManufacturingStockAnalysisResponse>;
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
export const formatNumber = (value: number, decimals: number = 2): string => {
  return value.toLocaleString("cs-CZ", {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  });
};

// Helper function to format percentage
export const formatPercentage = (value: number): string => {
  return `${formatNumber(value, 1)}%`;
};

// Helper function to format time period display text
export const getTimePeriodDisplayText = (
  timePeriod: TimePeriodFilter,
): string => {
  switch (timePeriod) {
    case TimePeriodFilter.PreviousQuarter:
      return "Minulý kvartal";
    case TimePeriodFilter.FutureQuarter:
      return "Budoucí kvartal";
    case TimePeriodFilter.Y2Y:
      return "Y2Y (12 měsíců)";
    case TimePeriodFilter.PreviousSeason:
      return "Předchozí sezona";
    case TimePeriodFilter.CustomPeriod:
      return "Vlastní období";
    default:
      return "Minulý kvartal";
  }
};

// Helper function to calculate date range for time period
export const calculateTimePeriodRange = (
  timePeriod: TimePeriodFilter,
): { fromDate: Date | null; toDate: Date | null } => {
  const now = new Date();

  switch (timePeriod) {
    case TimePeriodFilter.PreviousQuarter:
      // Last 3 completed months
      const startOfCurrentMonth = new Date(
        now.getFullYear(),
        now.getMonth(),
        1,
      );
      const endOfPreviousMonth = new Date(startOfCurrentMonth.getTime() - 1);
      const startOfPreviousQuarter = new Date(
        startOfCurrentMonth.getFullYear(),
        startOfCurrentMonth.getMonth() - 3,
        1,
      );
      return { fromDate: startOfPreviousQuarter, toDate: endOfPreviousMonth };

    case TimePeriodFilter.FutureQuarter:
      // Next 3 months from previous year (for demand forecasting)
      const startOfFutureQuarterLastYear = new Date(
        now.getFullYear() - 1,
        now.getMonth(),
        1,
      );
      const endOfFutureQuarterLastYear = new Date(
        now.getFullYear() - 1,
        now.getMonth() + 3,
        0,
      );
      return {
        fromDate: startOfFutureQuarterLastYear,
        toDate: endOfFutureQuarterLastYear,
      };

    case TimePeriodFilter.Y2Y:
      // Last 12 months
      const startOfY2Y = new Date(now.getFullYear(), now.getMonth() - 12, 1);
      const endOfY2Y = new Date(now.getFullYear(), now.getMonth(), 0);
      return { fromDate: startOfY2Y, toDate: endOfY2Y };

    case TimePeriodFilter.PreviousSeason:
      // October-January of previous year
      const seasonStart = new Date(now.getFullYear() - 1, 9, 1); // October (0-indexed)
      const seasonEnd = new Date(now.getFullYear(), 0, 31); // January 31
      return { fromDate: seasonStart, toDate: seasonEnd };

    case TimePeriodFilter.CustomPeriod:
      return { fromDate: null, toDate: null };

    default:
      // Default to previous quarter
      const defaultStart = new Date(now.getFullYear(), now.getMonth() - 3, 1);
      const defaultEnd = new Date(now.getFullYear(), now.getMonth(), 0);
      return { fromDate: defaultStart, toDate: defaultEnd };
  }
};
