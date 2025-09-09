import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import { ApiClient as GeneratedApiClient } from "../generated/api-client";
import {
  GetPurchaseOrdersResponse,
  GetPurchaseOrderByIdResponse,
  PurchaseOrderHistoryDto,
  CreatePurchaseOrderRequest,
  CreatePurchaseOrderResponse,
  UpdatePurchaseOrderRequest,
  UpdatePurchaseOrderResponse,
  UpdatePurchaseOrderStatusRequest,
  UpdatePurchaseOrderStatusResponse,
  UpdatePurchaseOrderInvoiceAcquiredRequest,
  UpdatePurchaseOrderInvoiceAcquiredResponse,
} from "../generated/api-client";

// Define request interface matching the old API
export interface GetPurchaseOrdersRequest {
  searchTerm?: string;
  status?: string;
  fromDate?: Date;
  toDate?: Date;
  supplierId?: number;
  activeOrdersOnly?: boolean;
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
}

// Query keys
const purchaseOrderKeys = {
  all: ["purchase-orders"] as const,
  lists: () => [...purchaseOrderKeys.all, "list"] as const,
  list: (filters: GetPurchaseOrdersRequest) =>
    [...purchaseOrderKeys.lists(), filters] as const,
  details: () => [...purchaseOrderKeys.all, "detail"] as const,
  detail: (id: number) => [...purchaseOrderKeys.details(), id] as const,
  history: (id: number) =>
    [...purchaseOrderKeys.detail(id), "history"] as const,
};

// Helper to get the correct API client instance from generated file
const getPurchaseOrdersClient = (): GeneratedApiClient => {
  const apiClient = getAuthenticatedApiClient();
  // The generated API client has the same structure, so we can use it directly
  return apiClient as any as GeneratedApiClient;
};

// Hooks
export const usePurchaseOrdersQuery = (request: GetPurchaseOrdersRequest) => {
  return useQuery({
    queryKey: purchaseOrderKeys.list(request),
    queryFn: async () => {
      const apiClient = getPurchaseOrdersClient();
      const relativeUrl = `/api/purchase-orders`;
      const params = new URLSearchParams();

      if (request.searchTerm) params.append("SearchTerm", request.searchTerm);
      if (request.status) params.append("Status", request.status);
      if (request.fromDate)
        params.append("FromDate", request.fromDate.toISOString());
      if (request.toDate) params.append("ToDate", request.toDate.toISOString());
      if (request.supplierId)
        params.append("SupplierId", request.supplierId.toString());
      if (request.activeOrdersOnly !== undefined)
        params.append("ActiveOrdersOnly", request.activeOrdersOnly.toString());
      if (request.pageNumber)
        params.append("PageNumber", request.pageNumber.toString());
      if (request.pageSize)
        params.append("PageSize", request.pageSize.toString());
      if (request.sortBy) params.append("SortBy", request.sortBy);
      if (request.sortDescending !== undefined)
        params.append("SortDescending", request.sortDescending.toString());

      const queryString = params.toString();
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}${queryString ? `?${queryString}` : ""}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "GET",
        headers: {
          Accept: "application/json",
        },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<GetPurchaseOrdersResponse>;
    },
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
};

export const usePurchaseOrderDetailQuery = (id: number) => {
  return useQuery({
    queryKey: purchaseOrderKeys.detail(id),
    queryFn: async () => {
      const apiClient = getPurchaseOrdersClient();
      const relativeUrl = `/api/purchase-orders/${id}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "GET",
        headers: {
          Accept: "application/json",
        },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<GetPurchaseOrderByIdResponse>;
    },
    enabled: !!id,
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
};

export const usePurchaseOrderHistoryQuery = (id: number) => {
  return useQuery({
    queryKey: purchaseOrderKeys.history(id),
    queryFn: async () => {
      const apiClient = getPurchaseOrdersClient();
      const relativeUrl = `/api/purchase-orders/${id}/history`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "GET",
        headers: {
          Accept: "application/json",
        },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<PurchaseOrderHistoryDto[]>;
    },
    enabled: !!id,
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
};

export const useCreatePurchaseOrderMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request: CreatePurchaseOrderRequest) => {
      const apiClient = getPurchaseOrdersClient();
      const relativeUrl = `/api/purchase-orders`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "POST",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json",
        },
        body: JSON.stringify(request),
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<CreatePurchaseOrderResponse>;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: purchaseOrderKeys.lists() });
    },
  });
};

export const useUpdatePurchaseOrderMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      id,
      request,
    }: {
      id: number;
      request: UpdatePurchaseOrderRequest;
    }) => {
      const apiClient = getPurchaseOrdersClient();
      const relativeUrl = `/api/purchase-orders/${id}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "PUT",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json",
        },
        body: JSON.stringify(request),
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<UpdatePurchaseOrderResponse>;
    },
    onSuccess: (_, { id }) => {
      queryClient.invalidateQueries({ queryKey: purchaseOrderKeys.detail(id) });
      queryClient.invalidateQueries({ queryKey: purchaseOrderKeys.lists() });
    },
  });
};

export const useUpdatePurchaseOrderStatusMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      id,
      request,
    }: {
      id: number;
      request: UpdatePurchaseOrderStatusRequest;
    }) => {
      const apiClient = getPurchaseOrdersClient();
      const relativeUrl = `/api/purchase-orders/${id}/status`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "PUT",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json",
        },
        body: JSON.stringify(request),
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<UpdatePurchaseOrderStatusResponse>;
    },
    onSuccess: (_, { id }) => {
      queryClient.invalidateQueries({ queryKey: purchaseOrderKeys.detail(id) });
      queryClient.invalidateQueries({ queryKey: purchaseOrderKeys.lists() });
      queryClient.invalidateQueries({
        queryKey: purchaseOrderKeys.history(id),
      });
    },
  });
};

export const useUpdatePurchaseOrderInvoiceAcquiredMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      id,
      request,
    }: {
      id: number;
      request: UpdatePurchaseOrderInvoiceAcquiredRequest;
    }) => {
      const apiClient = getPurchaseOrdersClient();
      const relativeUrl = `/api/purchase-orders/${id}/invoice-acquired`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "PUT",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json",
        },
        body: JSON.stringify(request),
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<UpdatePurchaseOrderInvoiceAcquiredResponse>;
    },
    onSuccess: (_, { id }) => {
      queryClient.invalidateQueries({ queryKey: purchaseOrderKeys.detail(id) });
      queryClient.invalidateQueries({ queryKey: purchaseOrderKeys.lists() });
    },
  });
};

// Re-export types from generated client for backward compatibility
export type {
  GetPurchaseOrdersResponse,
  PurchaseOrderSummaryDto,
  GetPurchaseOrderByIdResponse,
  PurchaseOrderLineDto,
  PurchaseOrderHistoryDto,
  CreatePurchaseOrderRequest,
  CreatePurchaseOrderLineRequest,
  CreatePurchaseOrderResponse,
  UpdatePurchaseOrderRequest,
  UpdatePurchaseOrderLineRequest,
  UpdatePurchaseOrderResponse,
  UpdatePurchaseOrderStatusRequest,
  UpdatePurchaseOrderStatusResponse,
  UpdatePurchaseOrderInvoiceAcquiredRequest,
  UpdatePurchaseOrderInvoiceAcquiredResponse,
} from "../generated/api-client";
