import { useQuery, useMutation } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

export interface BankStatementImportStatisticsDto {
  date: string;
  importCount: number;
  totalItemCount: number;
}

export interface GetBankStatementImportStatisticsResponse {
  statistics: BankStatementImportStatisticsDto[];
  success: boolean;
  errorCode?: string;
  params?: any;
}

export interface GetBankStatementImportStatisticsRequest {
  startDate?: string;
  endDate?: string;
  dateType?: string;
}

export interface BankStatementImportDto {
  id: number;
  transferId: string;
  statementDate: string;
  importDate: string;
  account: string;
  currency: string;
  itemCount: number;
  importResult: string;
  errorType?: string;
}

export interface GetBankStatementListResponse {
  items: BankStatementImportDto[];
  totalCount: number;
}

export interface GetBankStatementListRequest {
  id?: number;
  statementDate?: string;
  importDate?: string;
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
    queryFn: async (): Promise<GetBankStatementImportStatisticsResponse> => {
      const apiClient = getAuthenticatedApiClient();
      
      const params = new URLSearchParams();
      if (request.startDate) {
        params.append('startDate', request.startDate);
      }
      if (request.endDate) {
        params.append('endDate', request.endDate);
      }
      if (request.dateType) {
        params.append('dateType', request.dateType);
      }

      const relativeUrl = `/api/analytics/bank-statement-import-statistics`;
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

export const useBankStatementsList = (
  request: GetBankStatementListRequest = {}
) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.bankStatements, 'list', request],
    queryFn: async (): Promise<GetBankStatementListResponse> => {
      const apiClient = getAuthenticatedApiClient();
      
      const params = new URLSearchParams();
      if (request.id !== undefined) {
        params.append('id', request.id.toString());
      }
      if (request.statementDate) {
        params.append('statementDate', request.statementDate);
      }
      if (request.importDate) {
        params.append('importDate', request.importDate);
      }
      if (request.skip !== undefined) {
        params.append('skip', request.skip.toString());
      }
      if (request.take !== undefined) {
        params.append('take', request.take.toString());
      }
      if (request.orderBy) {
        params.append('orderBy', request.orderBy);
      }
      if (request.ascending !== undefined) {
        params.append('ascending', request.ascending.toString());
      }

      const relativeUrl = `/api/bank-statements`;
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
    staleTime: 2 * 60 * 1000, // 2 minutes
  });
};

export interface BankImportRequest {
  accountName: string;
  statementDate: string;
}

export interface BankStatementImportResult {
  statements: BankStatementImportDto[];
}

export interface BankImportResponse {
  statements: BankStatementImportDto[];
}

export const useBankStatementImport = () => {
  return useMutation({
    mutationFn: async (request: BankImportRequest): Promise<BankImportResponse> => {
      const apiClient = getAuthenticatedApiClient();
      
      const relativeUrl = `/api/bank-statements/import`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(request),
      });

      if (!response.ok) {
        const errorText = await response.text();
        throw new Error(`Import failed: ${response.status} - ${errorText}`);
      }

      return await response.json();
    },
  });
};

// Account mapping interface for display purposes
export interface AccountOption {
  value: string; // The account name that backend expects (e.g., "ComgateCZK")
  label: string; // Display text (Account Name (Account Number))
  accountNumber: string; // The actual account number (e.g., "2301495165/2010")
  accountName: string; // The account name (e.g., "ComgateCZK")
}

// Account mapping - maps account numbers to account names that backend expects
const getAccountNameFromNumber = (accountNumber: string): string => {
  const accountMappings: Record<string, string> = {
    // Map account numbers to the account names that backend expects
    '2301495165/2010': 'ComgateCZK',
    '2501837465/2010': 'ComgateEUR',
  };

  return accountMappings[accountNumber] || accountNumber;
};

// Get display name for UI
const getAccountDisplayName = (accountNumber: string): string => {
  const accountName = getAccountNameFromNumber(accountNumber);
  return `${accountName} (${accountNumber})`;
};

// Get unique accounts from existing bank statements (for account selection)
export const useBankStatementAccounts = () => {
  return useQuery({
    queryKey: [...QUERY_KEYS.bankStatements, 'accounts'],
    queryFn: async (): Promise<AccountOption[]> => {
      const apiClient = getAuthenticatedApiClient();
      
      // Get all bank statements to extract unique account names
      const relativeUrl = `/api/bank-statements?take=1000`; // Get a large number to capture all accounts
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

      const data: GetBankStatementListResponse = await response.json();
      
      // Extract unique account numbers
      const uniqueAccounts = new Set(data.items.map(item => item.account));
      const accountNumbers = Array.from(uniqueAccounts);
      
      // Convert to AccountOption objects with display names
      const accountOptions: AccountOption[] = accountNumbers.map(accountNumber => {
        const accountName = getAccountNameFromNumber(accountNumber);
        const displayName = getAccountDisplayName(accountNumber);
        
        return {
          value: accountName, // Use account name for API calls, not account number
          label: displayName,
          accountNumber: accountNumber,
          accountName: accountName
        };
      });
      
      return accountOptions.sort((a, b) => a.label.localeCompare(b.label));
    },
    staleTime: 10 * 60 * 1000, // 10 minutes
  });
};