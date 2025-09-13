import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import { ApiClient as GeneratedApiClient } from "../generated/api-client";
import {
  ManufactureOrderState,
  CreateManufactureOrderRequest,
  CreateManufactureOrderResponse,
} from "../generated/api-client";

// Define request interface matching the API parameters
export interface GetManufactureOrdersRequest {
  state?: ManufactureOrderState | null;
  dateFrom?: Date | null;
  dateTo?: Date | null;
  responsiblePerson?: string | null;
  orderNumber?: string | null;
  productCode?: string | null;
}

// Query keys
const manufactureOrderKeys = {
  all: ["manufacture-orders"] as const,
  lists: () => [...manufactureOrderKeys.all, "list"] as const,
  list: (filters: GetManufactureOrdersRequest) =>
    [...manufactureOrderKeys.lists(), filters] as const,
  details: () => [...manufactureOrderKeys.all, "detail"] as const,
  detail: (id: number) => [...manufactureOrderKeys.details(), id] as const,
};

// Helper to get the correct API client instance from generated file
const getManufactureOrdersClient = (): GeneratedApiClient => {
  const apiClient = getAuthenticatedApiClient();
  // The generated API client has the same structure, so we can use it directly
  return apiClient as any as GeneratedApiClient;
};

// Hooks
export const useManufactureOrdersQuery = (request: GetManufactureOrdersRequest = {}) => {
  return useQuery({
    queryKey: manufactureOrderKeys.list(request),
    queryFn: async () => {
      const apiClient = getManufactureOrdersClient();
      return await apiClient.manufactureOrder_GetOrders(
        request.state,
        request.dateFrom,
        request.dateTo,
        request.responsiblePerson,
        request.orderNumber,
        request.productCode
      );
    },
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
};

export const useManufactureOrderDetailQuery = (id: number) => {
  return useQuery({
    queryKey: manufactureOrderKeys.detail(id),
    queryFn: async () => {
      const apiClient = getManufactureOrdersClient();
      return await apiClient.manufactureOrder_GetOrder(id);
    },
    enabled: !!id,
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
};

// Mutation for creating manufacture orders
export const useCreateManufactureOrder = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request: CreateManufactureOrderRequest): Promise<CreateManufactureOrderResponse> => {
      const apiClient = getManufactureOrdersClient();
      return await apiClient.manufactureOrder_CreateOrder(request);
    },
    onSuccess: () => {
      // Invalidate and refetch manufacture orders list
      queryClient.invalidateQueries({
        queryKey: manufactureOrderKeys.lists(),
      });
    },
  });
};

// Re-export types from generated client for backward compatibility
export type {
  GetManufactureOrdersResponse,
  GetManufactureOrderResponse,
  ManufactureOrderDto,
  ManufactureOrderSemiProductDto,
  ManufactureOrderProductDto,
  ManufactureOrderNoteDto,
  ManufactureOrderAuditLogDto,
  ManufactureOrderAuditAction,
} from "../generated/api-client";

// Re-export enums as values
export {
  ManufactureOrderState,
} from "../generated/api-client";