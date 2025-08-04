import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

// Define types for the stock analysis API
export interface GetPurchaseStockAnalysisRequest {
  fromDate?: Date;
  toDate?: Date;
  stockStatus?: StockStatusFilter;
  onlyConfigured?: boolean;
  searchTerm?: string;
  pageNumber?: number;
  pageSize?: number;
  sortBy?: StockAnalysisSortBy;
  sortDescending?: boolean;
}

export enum StockStatusFilter {
  All = 'All',
  Critical = 'Critical',
  Low = 'Low',
  Optimal = 'Optimal',
  Overstocked = 'Overstocked',
  NotConfigured = 'NotConfigured'
}

export enum StockAnalysisSortBy {
  ProductCode = 'ProductCode',
  ProductName = 'ProductName',
  AvailableStock = 'AvailableStock',
  Consumption = 'Consumption',
  StockEfficiency = 'StockEfficiency',
  LastPurchaseDate = 'LastPurchaseDate'
}

export enum StockSeverity {
  Critical = 'Critical',
  Low = 'Low',
  Optimal = 'Optimal',
  Overstocked = 'Overstocked',
  NotConfigured = 'NotConfigured'
}

export interface StockAnalysisItemDto {
  productCode: string;
  productName: string;
  productType: string;
  availableStock: number;
  minStockLevel: number;
  optimalStockLevel: number;
  consumptionInPeriod: number;
  dailyConsumption: number;
  daysUntilStockout?: number;
  stockEfficiencyPercentage: number;
  severity: StockSeverity;
  minimalOrderQuantity: string;
  lastPurchase?: LastPurchaseInfoDto;
  suppliers: string[];
  recommendedOrderQuantity?: number;
  isConfigured: boolean;
}

export interface LastPurchaseInfoDto {
  date: string;
  supplierName: string;
  amount: number;
  unitPrice: number;
  totalPrice: number;
}

export interface StockAnalysisSummaryDto {
  totalProducts: number;
  criticalCount: number;
  lowStockCount: number;
  optimalCount: number;
  overstockedCount: number;
  notConfiguredCount: number;
  totalInventoryValue: number;
  analysisPeriodStart: string;
  analysisPeriodEnd: string;
}

export interface GetPurchaseStockAnalysisResponse {
  items: StockAnalysisItemDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  summary: StockAnalysisSummaryDto;
}

// Query keys
const stockAnalysisKeys = {
  all: ['purchase-stock-analysis'] as const,
  lists: () => [...stockAnalysisKeys.all, 'list'] as const,
  list: (filters: GetPurchaseStockAnalysisRequest) => [...stockAnalysisKeys.lists(), filters] as const,
};

// Helper to format date for API
const formatDateForApi = (date: Date): string => {
  return date.toISOString().split('T')[0]; // YYYY-MM-DD format
};

// Main hook for stock analysis
export const usePurchaseStockAnalysisQuery = (request: GetPurchaseStockAnalysisRequest) => {
  return useQuery({
    queryKey: stockAnalysisKeys.list(request),
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/purchase-stock-analysis`;
      const params = new URLSearchParams();
      
      if (request.fromDate) params.append('fromDate', formatDateForApi(request.fromDate));
      if (request.toDate) params.append('toDate', formatDateForApi(request.toDate));
      if (request.stockStatus && request.stockStatus !== StockStatusFilter.All) {
        params.append('stockStatus', request.stockStatus);
      }
      if (request.onlyConfigured) params.append('onlyConfigured', 'true');
      if (request.searchTerm) params.append('searchTerm', request.searchTerm);
      if (request.pageNumber) params.append('pageNumber', request.pageNumber.toString());
      if (request.pageSize) params.append('pageSize', request.pageSize.toString());
      if (request.sortBy) params.append('sortBy', request.sortBy);
      if (request.sortDescending !== undefined) params.append('sortDescending', request.sortDescending.toString());

      const queryString = params.toString();
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}${queryString ? `?${queryString}` : ''}`;
      
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: {
          'Accept': 'application/json'
        }
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<GetPurchaseStockAnalysisResponse>;
    },
    staleTime: 1000 * 60 * 2, // 2 minutes (shorter than purchase orders since stock data changes more frequently)
  });
};

// Helper function to get severity color class
export const getSeverityColorClass = (severity: StockSeverity): string => {
  switch (severity) {
    case StockSeverity.Critical:
      return 'text-red-600 bg-red-50';
    case StockSeverity.Low:
      return 'text-orange-600 bg-orange-50';
    case StockSeverity.Optimal:
      return 'text-green-600 bg-green-50';
    case StockSeverity.Overstocked:
      return 'text-blue-600 bg-blue-50';
    case StockSeverity.NotConfigured:
      return 'text-gray-600 bg-gray-50';
    default:
      return 'text-gray-600 bg-gray-50';
  }
};

// Helper function to get severity display text
export const getSeverityDisplayText = (severity: StockSeverity): string => {
  switch (severity) {
    case StockSeverity.Critical:
      return 'Kritický';
    case StockSeverity.Low:
      return 'Nízký';
    case StockSeverity.Optimal:
      return 'Optimální';
    case StockSeverity.Overstocked:
      return 'Přeskladněno';
    case StockSeverity.NotConfigured:
      return 'Nezkonfigurováno';
    default:
      return 'Neznámý';
  }
};

// Helper function to format Czech number
export const formatNumber = (value: number, decimals: number = 2): string => {
  return value.toLocaleString('cs-CZ', { 
    minimumFractionDigits: decimals, 
    maximumFractionDigits: decimals 
  });
};

// Helper function to format Czech currency
export const formatCurrency = (value: number): string => {
  return value.toLocaleString('cs-CZ', { 
    style: 'currency', 
    currency: 'CZK' 
  });
};