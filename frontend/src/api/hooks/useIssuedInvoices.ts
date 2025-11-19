import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

export interface IssuedInvoicesFilters {
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
  invoiceId?: string;
  customerName?: string;
  invoiceDateFrom?: string;
  invoiceDateTo?: string;
  isSynced?: boolean;
  showOnlyUnsynced?: boolean;
  showOnlyWithErrors?: boolean;
}

export interface IssuedInvoiceDto {
  id: string;
  invoiceDate: string;
  customerName: string | null;
  price: number;
  isSynced: boolean;
  lastSyncTime: string | null;
  errorType: string | null;
  errorMessage: string | null;
}

export interface IssuedInvoicesListResponse {
  items: IssuedInvoiceDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  success: boolean;
  errorCode?: number;
  params?: Record<string, string>;
}

export interface IssuedInvoiceDetailDto extends IssuedInvoiceDto {
  customerEmail: string | null;
  customerPhone: string | null;
  customerAddress: string | null;
  items: IssuedInvoiceItemDto[];
  syncHistory: IssuedInvoiceSyncHistoryDto[];
}

export interface IssuedInvoiceItemDto {
  productId: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
}

export interface IssuedInvoiceSyncHistoryDto {
  id: string;
  syncTime: string;
  syncStatus: string;
  errorMessage: string | null;
  responseData: string | null;
}

export interface IssuedInvoiceDetailResponse {
  invoice: IssuedInvoiceDetailDto;
  success: boolean;
  errorCode?: number;
  params?: Record<string, string>;
}

// Hook for fetching paginated list of issued invoices
export const useIssuedInvoicesList = (filters: IssuedInvoicesFilters) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.issuedInvoices, filters],
    queryFn: async (): Promise<IssuedInvoicesListResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      
      // Build query parameters
      const params = new URLSearchParams();
      
      if (filters.pageNumber !== undefined) {
        params.append('pageNumber', filters.pageNumber.toString());
      }
      if (filters.pageSize !== undefined) {
        params.append('pageSize', filters.pageSize.toString());
      }
      if (filters.sortBy) {
        params.append('sortBy', filters.sortBy);
      }
      if (filters.sortDescending !== undefined) {
        params.append('sortDescending', filters.sortDescending.toString());
      }
      if (filters.invoiceId) {
        params.append('invoiceId', filters.invoiceId);
      }
      if (filters.customerName) {
        params.append('customerName', filters.customerName);
      }
      if (filters.invoiceDateFrom) {
        params.append('invoiceDateFrom', filters.invoiceDateFrom);
      }
      if (filters.invoiceDateTo) {
        params.append('invoiceDateTo', filters.invoiceDateTo);
      }
      if (filters.isSynced !== undefined) {
        params.append('isSynced', filters.isSynced.toString());
      }
      if (filters.showOnlyUnsynced !== undefined) {
        params.append('showOnlyUnsynced', filters.showOnlyUnsynced.toString());
      }
      if (filters.showOnlyWithErrors !== undefined) {
        params.append('showOnlyWithErrors', filters.showOnlyWithErrors.toString());
      }

      const url = `/api/IssuedInvoices?${params.toString()}`;
      const fullUrl = `${(apiClient as any).baseUrl}${url}`;
      
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
};

// Hook for fetching detailed information about a specific issued invoice
export const useIssuedInvoiceDetail = (invoiceId: string) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.issuedInvoices, 'detail', invoiceId],
    queryFn: async (): Promise<IssuedInvoiceDetailResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      
      const url = `/api/IssuedInvoices/${encodeURIComponent(invoiceId)}`;
      const fullUrl = `${(apiClient as any).baseUrl}${url}`;
      
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
    enabled: !!invoiceId,
    staleTime: 5 * 60 * 1000, // 5 minutes
    gcTime: 10 * 60 * 1000, // 10 minutes
  });
};