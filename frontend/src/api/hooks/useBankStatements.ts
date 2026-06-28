import { useQuery, useMutation } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import {
  GetBankStatementImportStatisticsResponse,
  DailyBankStatementStatistics,
  BankStatementDateType,
  GetBankStatementListResponse,
  BankStatementImportDto,
  BankStatementImportResultDto,
  BankAccountDto,
  BankImportRequestDto,
} from '../generated/api-client';

export type {
  GetBankStatementImportStatisticsResponse,
  DailyBankStatementStatistics,
  BankStatementDateType,
  GetBankStatementListResponse,
  BankStatementImportDto,
  BankStatementImportResultDto,
};

export interface GetBankStatementImportStatisticsRequest {
  startDate?: string;
  endDate?: string;
  dateType?: string;
}

export interface GetBankStatementListRequest {
  id?: number;
  transferId?: string;
  account?: string;
  statementDate?: string;
  importDate?: string;
  dateFrom?: string;   // ISO date 'YYYY-MM-DD'
  dateTo?: string;     // ISO date 'YYYY-MM-DD'
  errorsOnly?: boolean;
  skip?: number;
  take?: number;
  orderBy?: string;
  ascending?: boolean;
}

export const useBankStatementImportStatistics = (
  request: GetBankStatementImportStatisticsRequest = {}
) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.bankStatements, 'import-statistics', request],
    queryFn: (): Promise<GetBankStatementImportStatisticsResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.analytics_GetBankStatementImportStatistics(
        request.startDate ? new Date(request.startDate) : null,
        request.endDate ? new Date(request.endDate) : null,
        request.dateType as BankStatementDateType | undefined
      );
    },
    staleTime: 5 * 60 * 1000, // 5 minutes
  });
};

export const useBankStatementsList = (
  request: GetBankStatementListRequest = {}
) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.bankStatements, 'list', request],
    queryFn: (): Promise<GetBankStatementListResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.bankStatements_GetBankStatements(
        request?.id ?? undefined,
        request?.transferId?.trim() ?? undefined,
        request?.account?.trim() ?? undefined,
        request?.statementDate ?? undefined,
        request?.importDate ?? undefined,
        request?.dateFrom ?? undefined,
        request?.dateTo ?? undefined,
        request?.errorsOnly ?? undefined,
        request?.skip,
        request?.take,
        request?.orderBy ?? undefined,
        request?.ascending
      );
    },
    staleTime: 2 * 60 * 1000, // 2 minutes
  });
};

export interface BankImportRequest {
  accountName: string;
  dateFrom: string;
  dateTo: string;
}

export const useBankStatementImport = () => {
  return useMutation({
    mutationFn: async (request: BankImportRequest): Promise<BankStatementImportResultDto> => {
      const apiClient = getAuthenticatedApiClient();
      const dto = new BankImportRequestDto({
        accountName: request.accountName,
        dateFrom: new Date(request.dateFrom),
        dateTo: new Date(request.dateTo),
      });
      return apiClient.bankStatements_ImportStatements(dto);
    },
  });
};

// Account option interface for display purposes
export interface AccountOption {
  value: string;       // Account name used by the backend (e.g., "ShoptetPay-CZK")
  label: string;       // Display text (e.g., "ShoptetPay-CZK (ShoptetPay)")
  accountNumber: string;
  provider: string;
  currency: string;
}

// Get configured bank accounts from the backend
export const useBankStatementAccounts = () => {
  return useQuery({
    queryKey: [...QUERY_KEYS.bankStatements, 'accounts'],
    queryFn: async (): Promise<AccountOption[]> => {
      const apiClient = getAuthenticatedApiClient();
      const accounts = await apiClient.bankStatements_GetAccounts();
      return accounts.map((a: BankAccountDto) => ({
        value: a.name ?? '',
        label: `${a.name ?? ''} (${a.provider ?? ''})`,
        accountNumber: a.accountNumber ?? '',
        provider: a.provider ?? '',
        currency: a.currency ?? '',
      }));
    },
    staleTime: 10 * 60 * 1000,
  });
};