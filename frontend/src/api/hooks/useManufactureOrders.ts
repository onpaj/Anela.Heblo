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
  UpdateManufactureOrderScheduleRequest,
  UpdateManufactureOrderScheduleResponse,
  ConfirmSemiProductManufactureRequest,
  ConfirmSemiProductManufactureResponse,
  ConfirmProductCompletionRequest,
  ConfirmProductCompletionResponse,
  DuplicateManufactureOrderResponse,
  ResolveManualActionRequest,
  ResolveManualActionResponse,
} from "../generated/api-client";

// Define request interface matching the API parameters
export interface GetManufactureOrdersRequest {
  state?: ManufactureOrderState | null;
  dateFrom?: Date | null;
  dateTo?: Date | null;
  responsiblePerson?: string | null;
  orderNumber?: string | null;
  productCode?: string | null;
  erpDocumentNumber?: string | null;
  manualActionRequired?: boolean | null;
}

// Query keys using QUERY_KEYS for consistency
const manufactureOrderKeys = {
  all: QUERY_KEYS.manufactureOrders,
  lists: () => [...QUERY_KEYS.manufactureOrders, "list"] as const,
  list: (filters: GetManufactureOrdersRequest) =>
    [...QUERY_KEYS.manufactureOrders, "list", filters] as const,
  details: () => [...QUERY_KEYS.manufactureOrders, "detail"] as const,
  detail: (id: number) => [...QUERY_KEYS.manufactureOrders, "detail", id] as const,
  calendar: () => [...QUERY_KEYS.manufactureOrders, "calendar"] as const,
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
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/manufactureorder`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      
      const params = new URLSearchParams();
      if (request.state !== undefined && request.state !== null) params.append('state', request.state.toString());
      if (request.dateFrom) params.append('dateFrom', request.dateFrom.toISOString());
      if (request.dateTo) params.append('dateTo', request.dateTo.toISOString());
      if (request.responsiblePerson) params.append('responsiblePerson', request.responsiblePerson);
      if (request.orderNumber) params.append('orderNumber', request.orderNumber);
      if (request.productCode) params.append('productCode', request.productCode);
      if (request.erpDocumentNumber) params.append('erpDocumentNumber', request.erpDocumentNumber);
      if (request.manualActionRequired !== undefined && request.manualActionRequired !== null) {
        params.append('manualActionRequired', request.manualActionRequired.toString());
      }
      
      const urlWithParams = params.toString() ? `${fullUrl}?${params.toString()}` : fullUrl;
      const response = await (apiClient as any).http.fetch(urlWithParams, {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json'
        }
      });
      
      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }
      
      return await response.json();
    },
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
};

export const useManufactureOrderDetailQuery = (id: number | null, enabled: boolean = true) => {
  return useQuery({
    queryKey: manufactureOrderKeys.detail(id!),
    queryFn: async () => {
      if (!id) return null;
      const apiClient = getManufactureOrdersClient();
      return await apiClient.manufactureOrder_GetOrder(id);
    },
    enabled: enabled && !!id,
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
};

export const useCreateManufactureOrderMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request: CreateManufactureOrderRequest): Promise<CreateManufactureOrderResponse> => {
      const apiClient = getManufactureOrdersClient();
      return await apiClient.manufactureOrder_CreateOrder(request);
    },
    onSuccess: () => {
      // Invalidate and refetch manufacture orders
      queryClient.invalidateQueries({ queryKey: manufactureOrderKeys.all });
    },
  });
};

export const useUpdateManufactureOrderMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request: UpdateManufactureOrderRequest): Promise<UpdateManufactureOrderResponse> => {
      const apiClient = getManufactureOrdersClient();
      return await apiClient.manufactureOrder_UpdateOrder(request.id!, request);
    },
    onSuccess: (data, variables) => {
      // Invalidate and refetch manufacture orders
      queryClient.invalidateQueries({ queryKey: manufactureOrderKeys.all });
      
      // Also invalidate specific order detail
      queryClient.invalidateQueries({
        queryKey: manufactureOrderKeys.detail(variables.id!),
      });

      // Also invalidate all calendar queries (including those with date parameters)
      queryClient.invalidateQueries({
        predicate: (query) => {
          return query.queryKey.length >= 2 && 
                 query.queryKey[0] === "manufacture-orders" &&
                 query.queryKey[1] === "calendar";
        },
      });
    },
  });
};

export const useUpdateManufactureOrderStatusMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request: UpdateManufactureOrderStatusRequest): Promise<UpdateManufactureOrderStatusResponse> => {
      const apiClient = getManufactureOrdersClient();
      return await apiClient.manufactureOrder_UpdateOrderStatus(request.id!, request);
    },
    onSuccess: (data, variables) => {
      // Invalidate and refetch manufacture orders
      queryClient.invalidateQueries({ queryKey: manufactureOrderKeys.all });
      
      // Also invalidate specific order detail
      queryClient.invalidateQueries({
        queryKey: manufactureOrderKeys.detail(variables.id!),
      });

      // Also invalidate all calendar queries (including those with date parameters)
      queryClient.invalidateQueries({
        predicate: (query) => {
          return query.queryKey.length >= 2 && 
                 query.queryKey[0] === "manufacture-orders" &&
                 query.queryKey[1] === "calendar";
        },
      });
    },
  });
};

export const useDuplicateManufactureOrder = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (id: number): Promise<DuplicateManufactureOrderResponse> => {
      const apiClient = getManufactureOrdersClient();
      return await apiClient.manufactureOrder_DuplicateOrder(id);
    },
    onSuccess: () => {
      // Invalidate and refetch manufacture orders after duplication
      queryClient.invalidateQueries({
        queryKey: manufactureOrderKeys.all,
      });

      // Also invalidate all calendar queries
      queryClient.invalidateQueries({
        predicate: (query) => {
          return query.queryKey.length >= 2 && 
                 query.queryKey[0] === "manufacture-orders" &&
                 query.queryKey[1] === "calendar";
        },
      });
    },
  });
};

// Type exports
export type { 
  ManufactureOrderState,
  CreateManufactureOrderRequest,
  CreateManufactureOrderResponse,
  UpdateManufactureOrderRequest,
  UpdateManufactureOrderResponse,
  UpdateManufactureOrderStatusRequest,
  UpdateManufactureOrderStatusResponse,
  ConfirmSemiProductManufactureRequest,
  ConfirmSemiProductManufactureResponse,
  ConfirmProductCompletionRequest,
  ConfirmProductCompletionResponse,
  DuplicateManufactureOrderResponse
} from "../generated/api-client";

// Re-export useful DTOs
export type { GetManufactureOrdersResponse, GetManufactureOrderResponse, ManufactureOrderDto } from "../generated/api-client";

// Re-export calendar view related types
export type { GetCalendarViewResponse, CalendarEventDto } from "../generated/api-client";

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

// Confirm semi-product manufacture mutation hook using generated API client
export const useConfirmSemiProductManufacture = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (request: ConfirmSemiProductManufactureRequest): Promise<ConfirmSemiProductManufactureResponse> => {
      const apiClient = getManufactureOrdersClient();
      return await apiClient.manufactureOrder_ConfirmSemiProductManufacture(request.id!, request);
    },
    onSuccess: (data, variables) => {
      // Invalidate and refetch manufacture orders
      queryClient.invalidateQueries({
        queryKey: manufactureOrderKeys.all,
      });
      
      // Also invalidate specific order detail
      queryClient.invalidateQueries({
        queryKey: manufactureOrderKeys.detail(variables.id!),
      });

      // Also invalidate all calendar queries (including those with date parameters)
      queryClient.invalidateQueries({
        predicate: (query) => {
          return query.queryKey.length >= 2 && 
                 query.queryKey[0] === "manufacture-orders" &&
                 query.queryKey[1] === "calendar";
        },
      });
    },
  });
};

// Confirm product completion mutation hook using generated API client
export const useConfirmProductCompletion = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (request: ConfirmProductCompletionRequest): Promise<ConfirmProductCompletionResponse> => {
      const apiClient = getManufactureOrdersClient();
      return await apiClient.manufactureOrder_ConfirmProductCompletion(request.id!, request);
    },
    onSuccess: (data, variables) => {
      // Invalidate and refetch manufacture orders
      queryClient.invalidateQueries({
        queryKey: manufactureOrderKeys.all,
      });
      
      // Also invalidate specific order detail
      queryClient.invalidateQueries({
        queryKey: manufactureOrderKeys.detail(variables.id!),
      });

      // Also invalidate all calendar queries (including those with date parameters)
      queryClient.invalidateQueries({
        predicate: (query) => {
          return query.queryKey.length >= 2 && 
                 query.queryKey[0] === "manufacture-orders" &&
                 query.queryKey[1] === "calendar";
        },
      });
    },
  });
};

// Resolve manual action mutation hook using generated API client
export const useResolveManualAction = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (request: ResolveManualActionRequest): Promise<ResolveManualActionResponse> => {
      const apiClient = getManufactureOrdersClient();
      return await apiClient.manufactureOrder_ResolveManualAction(request.orderId!, request);
    },
    onSuccess: (data, variables) => {
      // Invalidate and refetch manufacture orders
      queryClient.invalidateQueries({
        queryKey: manufactureOrderKeys.all,
      });
      
      // Also invalidate specific order detail
      queryClient.invalidateQueries({
        queryKey: manufactureOrderKeys.detail(variables.orderId!),
      });

      // Also invalidate all calendar queries (including those with date parameters)
      queryClient.invalidateQueries({
        predicate: (query) => {
          return query.queryKey.length >= 2 && 
                 query.queryKey[0] === "manufacture-orders" &&
                 query.queryKey[1] === "calendar";
        },
      });
    },
  });
};

// Update schedule mutation hook for drag & drop functionality
export const useUpdateManufactureOrderSchedule = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (request: { 
      id: number, 
      plannedDate?: Date, 
      changeReason?: string 
    }): Promise<UpdateManufactureOrderScheduleResponse> => {
      const apiClient = getManufactureOrdersClient();
      
      // Create proper request instance
      const scheduleRequest = new UpdateManufactureOrderScheduleRequest({
        id: request.id,
        plannedDate: request.plannedDate,
        changeReason: request.changeReason || "Schedule updated via drag & drop"
      });
      
      return await apiClient.manufactureOrder_UpdateOrderSchedule(request.id, scheduleRequest);
    },
    onSuccess: (data, variables) => {
      // Invalidate and refetch manufacture orders
      queryClient.invalidateQueries({ queryKey: manufactureOrderKeys.all });
      
      // Also invalidate specific order detail
      queryClient.invalidateQueries({
        queryKey: manufactureOrderKeys.detail(variables.id),
      });

      // Also invalidate all calendar queries (including those with date parameters)
      queryClient.invalidateQueries({
        predicate: (query) => {
          return query.queryKey.length >= 2 && 
                 query.queryKey[0] === "manufacture-orders" &&
                 query.queryKey[1] === "calendar";
        },
      });
    },
  });
};

// Aliases for backwards compatibility
export const useCreateManufactureOrder = useCreateManufactureOrderMutation;
export const useUpdateManufactureOrder = useUpdateManufactureOrderMutation;
export const useUpdateManufactureOrderStatus = useUpdateManufactureOrderStatusMutation;
