import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

export interface IssuedInvoiceSyncStats {
  totalInvoices: number;
  syncedInvoices: number;
  unsyncedInvoices: number;
  invoicesWithErrors: number;
  criticalErrors: number;
  lastSyncTime?: string;
  syncSuccessRate: number;
}

export interface UseIssuedInvoiceSyncStatsParams {
  fromDate?: Date;
  toDate?: Date;
}

export function useIssuedInvoiceSyncStats(params?: UseIssuedInvoiceSyncStatsParams) {
  // Stabilize the dates to avoid constant re-fetching
  const fromDateString = params?.fromDate ? params.fromDate.toDateString() : undefined;
  const toDateString = params?.toDate ? params.toDate.toDateString() : undefined;
  
  return useQuery({
    queryKey: [...QUERY_KEYS.issuedInvoices, 'sync-stats', fromDateString, toDateString],
    queryFn: async (): Promise<IssuedInvoiceSyncStats> => {
      const apiClient = await getAuthenticatedApiClient();
      
      const searchParams = new URLSearchParams();
      if (params?.fromDate) {
        searchParams.append('fromDate', params.fromDate.toISOString());
      }
      if (params?.toDate) {
        searchParams.append('toDate', params.toDate.toISOString());
      }

      const url = `/api/IssuedInvoices/sync-stats?${searchParams.toString()}`;
      const fullUrl = `${(apiClient as any).baseUrl}${url}`;
      
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
        },
      });

      if (!response.ok) {
        throw new Error(`Failed to fetch sync stats: ${response.status}`);
      }

      const data = await response.json();
      
      if (!data.success) {
        throw new Error(data.params?.ErrorMessage || 'Failed to load sync stats');
      }

      return {
        totalInvoices: data.totalInvoices,
        syncedInvoices: data.syncedInvoices,
        unsyncedInvoices: data.unsyncedInvoices,
        invoicesWithErrors: data.invoicesWithErrors,
        criticalErrors: data.criticalErrors,
        lastSyncTime: data.lastSyncTime,
        syncSuccessRate: data.syncSuccessRate,
      };
    },
    staleTime: 5 * 60 * 1000, // 5 minutes
    retry: 2, // Only retry 2 times
    retryDelay: (attemptIndex) => Math.min(1000 * 2 ** attemptIndex, 30000), // Exponential backoff
  });
}