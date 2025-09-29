import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

export interface UseInvoiceImportStatisticsParams {
  dateType?: 'InvoiceDate' | 'LastSyncTime';
  daysBack?: number;
}

export interface DailyInvoiceCount {
  date: string;
  count: number;
  isBelowThreshold: boolean;
}

export interface InvoiceImportStatisticsResponse {
  data: DailyInvoiceCount[];
  minimumThreshold: number;
  success: boolean;
  errorCode?: string;
  params?: Record<string, string>;
}

/**
 * Hook for fetching invoice import statistics for monitoring
 */
export function useInvoiceImportStatistics(params: UseInvoiceImportStatisticsParams = {}) {
  const { dateType = 'InvoiceDate', daysBack = 14 } = params;

  return useQuery({
    queryKey: ['invoice-import-statistics', dateType, daysBack],
    queryFn: async (): Promise<InvoiceImportStatisticsResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      const relativeUrl = `/api/analytics/invoice-import-statistics?dateType=${dateType}&daysBack=${daysBack}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
        },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json();
    },
    staleTime: 5 * 60 * 1000, // 5 minutes
    gcTime: 10 * 60 * 1000, // 10 minutes  
  });
}