import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import { 
  GetPurchaseStockAnalysisResponse,
  StockStatusFilter,
  StockAnalysisSortBy,
  StockSeverity,
  StockAnalysisItemDto,
  LastPurchaseInfoDto,
  StockAnalysisSummaryDto
} from '../generated/api-client';

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

// Export types from generated client
export { 
  StockStatusFilter,
  StockAnalysisSortBy,
  StockSeverity,
  StockAnalysisItemDto,
  LastPurchaseInfoDto,
  StockAnalysisSummaryDto,
  GetPurchaseStockAnalysisResponse
};

// Query keys
const stockAnalysisKeys = {
  all: ['purchase-stock-analysis'] as const,
  lists: () => [...stockAnalysisKeys.all, 'list'] as const,
  list: (filters: GetPurchaseStockAnalysisRequest) => [...stockAnalysisKeys.lists(), filters] as const,
};

// Main hook for stock analysis
export const usePurchaseStockAnalysisQuery = (request: GetPurchaseStockAnalysisRequest) => {
  return useQuery({
    queryKey: stockAnalysisKeys.list(request),
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      
      return apiClient.purchaseStockAnalysis_GetStockAnalysis(
        request.fromDate ?? null,
        request.toDate ?? null,
        request.stockStatus,
        request.onlyConfigured,
        request.searchTerm ?? null,
        request.pageNumber,
        request.pageSize,
        request.sortBy,
        request.sortDescending
      );
    },
    staleTime: 1000 * 60 * 2, // 2 minutes (shorter than purchase orders since stock data changes more frequently)
  });
};

// Helper function to get severity color class
export const getSeverityColorClass = (severity: StockSeverity | undefined): string => {
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
export const getSeverityDisplayText = (severity: StockSeverity | undefined): string => {
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
export const formatNumber = (value: number | undefined, decimals: number = 2): string => {
  if (value === undefined || value === null) return '—';
  return value.toLocaleString('cs-CZ', { 
    minimumFractionDigits: decimals, 
    maximumFractionDigits: decimals 
  });
};

// Helper function to format Czech currency
export const formatCurrency = (value: number | undefined): string => {
  if (value === undefined || value === null) return '—';
  return value.toLocaleString('cs-CZ', { 
    style: 'currency', 
    currency: 'CZK' 
  });
};