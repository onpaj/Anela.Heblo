import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { ApiClient } from "../generated/api-client";
import {
  GetStockUpOperationsResponse,
  RetryStockUpOperationResponse,
  StockUpOperationState,
} from "../generated/api-client";

// Define request interface matching the backend contract
export interface GetStockUpOperationsRequest {
  state?: StockUpOperationState;
  pageSize?: number;
  page?: number;
}

// Query keys
const stockUpOperationsKeys = {
  all: [...QUERY_KEYS.stockUpOperations] as const,
  lists: () => [...QUERY_KEYS.stockUpOperations, "list"] as const,
  list: (filters: GetStockUpOperationsRequest) =>
    [...QUERY_KEYS.stockUpOperations, "list", filters] as const,
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
        request.page ?? undefined
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
