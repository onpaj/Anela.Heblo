import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../ApiClient';

export interface BankStatementImportStatisticsDto {
  date: string;
  importCount: number;
  totalItemCount: number;
}

export interface GetBankStatementImportStatisticsResponse {
  statistics: BankStatementImportStatisticsDto[];
}

export interface GetBankStatementImportStatisticsRequest {
  startDate?: string;
  endDate?: string;
}

export const useBankStatementImportStatistics = (
  request: GetBankStatementImportStatisticsRequest = {}
) => {
  return useQuery({
    queryKey: ['bank-statement-import-statistics', request],
    queryFn: async (): Promise<GetBankStatementImportStatisticsResponse> => {
      const apiClient = getAuthenticatedApiClient();
      
      const params = new URLSearchParams();
      if (request.startDate) {
        params.append('startDate', request.startDate);
      }
      if (request.endDate) {
        params.append('endDate', request.endDate);
      }

      const relativeUrl = `/api/bankstatements/statistics`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}?${params.toString()}`;
      
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
        },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return await response.json();
    },
    staleTime: 5 * 60 * 1000, // 5 minutes
  });
};