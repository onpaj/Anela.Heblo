import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import {
  DqtTestType,
  DqtRunStatus,
  RunDqtRequest,
  type GetDqtRunsResponse,
  type GetDqtRunDetailResponse,
  type RunDqtResponse,
} from '../generated/api-client';

// ---- Types ----

export interface GetDqtRunsParams {
  testType?: DqtTestType;
  status?: DqtRunStatus;
  pageNumber?: number;
  pageSize?: number;
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
    queryFn: (): Promise<GetDqtRunsResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.dataQuality_GetRuns(
        params.testType,
        params.status,
        params.pageNumber,
        params.pageSize,
      );
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
    queryFn: (): Promise<GetDqtRunDetailResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.dataQuality_GetRunDetail(runId!, resultPage, resultPageSize);
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
    mutationFn: (request: RunDqtRequest): Promise<RunDqtResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.dataQuality_RunDqt(request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: dataQualityKeys.all });
    },
  });
};

// Re-export types for convenience
export type { DqtRunDto, InvoiceDqtResultDto, DqtDriftResultDto } from '../generated/api-client';
