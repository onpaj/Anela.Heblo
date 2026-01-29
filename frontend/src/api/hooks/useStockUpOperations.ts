import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { ApiClient } from "../generated/api-client";
import {
  GetStockUpOperationsResponse,
  GetStockUpOperationsSummaryResponse,
  RetryStockUpOperationResponse,
  AcceptStockUpOperationResponse,
  StockUpSourceType,
} from "../generated/api-client";

// Define request interface matching the backend contract
export interface GetStockUpOperationsRequest {
  state?: string; // Changed to string to support "Active" special value
  pageSize?: number;
  page?: number;
  sourceType?: StockUpSourceType;
  sourceId?: number;
  productCode?: string;
  documentNumber?: string;
  createdFrom?: Date;
  createdTo?: Date;
  sortBy?: string;
  sortDescending?: boolean;
}

// Query keys
const stockUpOperationsKeys = {
  all: [...QUERY_KEYS.stockUpOperations] as const,
  lists: () => [...QUERY_KEYS.stockUpOperations, "list"] as const,
  list: (filters: GetStockUpOperationsRequest) =>
    [...QUERY_KEYS.stockUpOperations, "list", filters] as const,
  summaries: () => [...QUERY_KEYS.stockUpOperations, "summary"] as const,
  summary: (sourceType?: StockUpSourceType) =>
    [...QUERY_KEYS.stockUpOperations, "summary", sourceType] as const,
};

// Helper to get the correct API client instance from generated file
const getStockUpOperationsClient = (): ApiClient => {
  return getAuthenticatedApiClient();
};

// Hooks
export const useStockUpOperationsQuery = (request: GetStockUpOperationsRequest) => {
  return useQuery({
    queryKey: stockUpOperationsKeys.list(request),
    queryFn: async (): Promise<GetStockUpOperationsResponse> => {
      const client = getStockUpOperationsClient();
      return await client.stockUpOperations_GetOperations(
        request.state ?? undefined,
        request.pageSize ?? undefined,
        request.page ?? undefined,
        request.sourceType ?? undefined,
        request.sourceId ?? undefined,
        request.productCode ?? undefined,
        request.documentNumber ?? undefined,
        request.createdFrom ?? undefined,
        request.createdTo ?? undefined,
        request.sortBy ?? undefined,
        request.sortDescending ?? undefined
      );
    },
  });
};

export const useRetryStockUpOperationMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (operationId: number): Promise<RetryStockUpOperationResponse> => {
      const client = getStockUpOperationsClient();
      return await client.stockUpOperations_RetryOperation(operationId);
    },
    onSuccess: () => {
      // Invalidate all stock-up operations queries to refresh the list
      queryClient.invalidateQueries({
        queryKey: stockUpOperationsKeys.lists(),
      });
    },
  });
};

/**
 * Hook to accept a failed StockUpOperation
 * Invalidates both operation list and summary queries on success
 */
export const useAcceptStockUpOperationMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (operationId: number): Promise<AcceptStockUpOperationResponse> => {
      const client = getStockUpOperationsClient();
      return await client.stockUpOperations_AcceptOperation(operationId);
    },
    onSuccess: () => {
      // Invalidate all stock-up operations queries to refresh the list
      queryClient.invalidateQueries({
        queryKey: stockUpOperationsKeys.lists(),
      });
      // Invalidate summaries to update failure counts
      queryClient.invalidateQueries({
        queryKey: stockUpOperationsKeys.summaries(),
      });
    },
  });
};

/**
 * Hook to get StockUpOperations summary counts (Pending, Submitted, Failed)
 * Polls every 15 seconds for live updates
 */
export const useStockUpOperationsSummary = (sourceType?: StockUpSourceType) => {
  return useQuery({
    queryKey: stockUpOperationsKeys.summary(sourceType),
    queryFn: async (): Promise<GetStockUpOperationsSummaryResponse> => {
      const client = getStockUpOperationsClient();
      return await client.stockUpOperations_GetSummary(sourceType ?? undefined);
    },
    refetchInterval: 15000, // Poll every 15 seconds
    refetchOnWindowFocus: true,
    staleTime: 14000, // Consider stale just before next poll
    gcTime: 60000, // Keep in cache for 1 minute
    retry: 1, // Limit retries during polling
  });
};
