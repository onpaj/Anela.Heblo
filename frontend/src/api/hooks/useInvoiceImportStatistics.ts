import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import { DailyInvoiceCount, GetInvoiceImportStatisticsResponse, ImportDateType } from '../generated/api-client';

export type { DailyInvoiceCount, GetInvoiceImportStatisticsResponse, ImportDateType };

export interface UseInvoiceImportStatisticsParams {
  dateType?: 'InvoiceDate' | 'LastSyncTime';
  daysBack?: number;
}

/**
 * Hook for fetching invoice import statistics for monitoring
 */
export function useInvoiceImportStatistics(params: UseInvoiceImportStatisticsParams = {}) {
  const { dateType = 'InvoiceDate', daysBack } = params;

  return useQuery({
    queryKey: [...QUERY_KEYS.invoiceImportStatistics, dateType, daysBack],
    queryFn: (): Promise<GetInvoiceImportStatisticsResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.analytics_GetInvoiceImportStatistics(dateType as ImportDateType, daysBack ?? null);
    },
    staleTime: 5 * 60 * 1000, // 5 minutes
    gcTime: 10 * 60 * 1000, // 10 minutes
  });
}
