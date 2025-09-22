import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { ApiClient as GeneratedApiClient } from "../generated/api-client";
import {
  ManufactureOrderState,
  CreateManufactureOrderRequest,
  CreateManufactureOrderResponse,
  UpdateManufactureOrderRequest,
  UpdateManufactureOrderResponse,
  UpdateManufactureOrderStatusRequest,
  UpdateManufactureOrderStatusResponse,
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

// Mutation for updating manufacture orders
export const useUpdateManufactureOrder = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request: UpdateManufactureOrderRequest): Promise<UpdateManufactureOrderResponse> => {
      const apiClient = getManufactureOrdersClient();
      return await apiClient.manufactureOrder_UpdateOrder(request.id, request);
    },
    onSuccess: (data, variables) => {
      // Invalidate and refetch manufacture orders list
      queryClient.invalidateQueries({
        queryKey: manufactureOrderKeys.lists(),
      });
      // Invalidate the specific order detail
      queryClient.invalidateQueries({
        queryKey: manufactureOrderKeys.detail(variables.id),
      });
    },
  });
};

// Mutation for updating manufacture order status
export const useUpdateManufactureOrderStatus = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request: UpdateManufactureOrderStatusRequest): Promise<UpdateManufactureOrderStatusResponse> => {
      const apiClient = getManufactureOrdersClient();
      return await apiClient.manufactureOrder_UpdateOrderStatus(request.id, request);
    },
    onSuccess: (data, variables) => {
      // Invalidate and refetch manufacture orders list
      queryClient.invalidateQueries({
        queryKey: manufactureOrderKeys.lists(),
      });
      // Invalidate the specific order detail
      queryClient.invalidateQueries({
        queryKey: manufactureOrderKeys.detail(variables.id),
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

// Re-export calendar types from generated client
export type { CalendarEventDto } from "../generated/api-client";

// Calendar view query hook using generated API client
export const useManufactureOrderCalendarQuery = (
  startDate: Date,
  endDate: Date,
  enabled: boolean = true
) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.manufactureOrders, "calendar", startDate.toISOString(), endDate.toISOString()],
    queryFn: async () => {
      const apiClient = getManufactureOrdersClient();
      return await apiClient.manufactureOrder_GetCalendarView(startDate, endDate);
    },
    enabled,
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
};