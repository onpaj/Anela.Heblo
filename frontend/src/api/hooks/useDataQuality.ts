import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

// ---- Types ----

export interface DqtRunDto {
  id: string;
  testType: string;
  dateFrom: string;
  dateTo: string;
  status: string;
  startedAt: string;
  completedAt: string | null;
  triggerType: string;
  totalChecked: number;
  totalMismatches: number;
  errorMessage: string | null;
}

export interface InvoiceDqtResultDto {
  id: string;
  invoiceCode: string;
  mismatchType: number;
  mismatchFlags: string[];
  shoptetValue: string | null;
  flexiValue: string | null;
  details: string | null;
}

export interface GetDqtRunsParams {
  testType?: string;
  status?: string;
  pageNumber?: number;
  pageSize?: number;
}

export interface GetDqtRunsResponse {
  success: boolean;
  items: DqtRunDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}

export interface GetDqtRunDetailResponse {
  success: boolean;
  run: DqtRunDto | null;
  results: InvoiceDqtResultDto[];
}

export interface RunDqtRequest {
  testType?: string;
  dateFrom: string;
  dateTo: string;
}

export interface RunDqtResponse {
  success: boolean;
  dqtRunId: string | null;
}

// ---- Query key factory ----

export const dataQualityKeys = {
  all: [...QUERY_KEYS.dataQuality] as const,
  runs: (params?: GetDqtRunsParams) =>
    [...QUERY_KEYS.dataQuality, 'runs', params ?? {}] as const,
  runDetail: (runId: string) =>
    [...QUERY_KEYS.dataQuality, 'runs', runId, 'detail'] as const,
};

// ---- Hooks ----

/**
 * Fetch paginated, filtered DQT runs.
 * Refetches every 30 seconds to reflect running job status.
 */
export const useDqtRuns = (params: GetDqtRunsParams = {}) => {
  return useQuery({
    queryKey: dataQualityKeys.runs(params),
    queryFn: async (): Promise<GetDqtRunsResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const searchParams = new URLSearchParams();

      if (params.testType) searchParams.append('testType', params.testType);
      if (params.status) searchParams.append('status', params.status);
      if (params.pageNumber !== undefined)
        searchParams.append('pageNumber', params.pageNumber.toString());
      if (params.pageSize !== undefined)
        searchParams.append('pageSize', params.pageSize.toString());

      const query = searchParams.toString();
      const relativeUrl = `/api/data-quality/runs${query ? `?${query}` : ''}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });

      if (!response.ok) {
        throw new Error(`Failed to fetch DQT runs: ${response.status}`);
      }

      return response.json();
    },
    staleTime: 30 * 1000,
    gcTime: 5 * 60 * 1000,
    refetchInterval: 30 * 1000,
  });
};

/**
 * Fetch detail of a specific DQT run including per-invoice results.
 * Only fires when runId is non-null/empty.
 */
export const useDqtRunDetail = (
  runId: string | null,
  resultPage: number = 1,
  resultPageSize: number = 50,
) => {
  return useQuery({
    queryKey: dataQualityKeys.runDetail(runId ?? ''),
    queryFn: async (): Promise<GetDqtRunDetailResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const searchParams = new URLSearchParams({
        resultPage: resultPage.toString(),
        resultPageSize: resultPageSize.toString(),
      });
      const fullUrl = `${(apiClient as any).baseUrl}/api/data-quality/runs/${runId}?${searchParams.toString()}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });

      if (!response.ok) {
        throw new Error(`Failed to fetch DQT run detail: ${response.status}`);
      }

      return response.json();
    },
    enabled: !!runId,
    staleTime: 30 * 1000,
    gcTime: 5 * 60 * 1000,
  });
};

/**
 * Trigger a manual DQT run.
 * Invalidates the runs list on success.
 */
export const useRunDqt = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (payload: RunDqtRequest): Promise<RunDqtResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/data-quality/runs`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify(payload),
      });

      if (!response.ok) {
        throw new Error(`Failed to trigger DQT run: ${response.status}`);
      }

      return response.json();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: dataQualityKeys.all });
    },
  });
};
