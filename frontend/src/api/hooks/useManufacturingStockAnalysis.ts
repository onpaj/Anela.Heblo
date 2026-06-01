import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import {
  TimePeriod,
  resolveTimePeriod,
  getTimePeriodDisplayText,
  type DateRange,
} from "../../utils/timePeriod";

export { TimePeriod as TimePeriodFilter };
export { getTimePeriodDisplayText };

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

// Define types for the manufacturing stock analysis API
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

export enum ManufacturingStockSortBy {
  ProductCode = "ProductCode",
  ProductName = "ProductName",
  CurrentStock = "CurrentStock",
  Reserve = "Reserve",
  Quarantine = "Quarantine",
  Planned = "Planned",
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
  erpStock: number;
  eshopStock: number;
  transportStock: number;
  manufacturedStock: number;
  primaryStockSource: string;
  reserve: number;
  quarantine: number;
  planned: number;
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
        request.timePeriod !== TimePeriod.Q9M
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
      if (request.salesMultiplier !== undefined && request.salesMultiplier !== 1.0)
        params.append("salesMultiplier", request.salesMultiplier.toString());

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

