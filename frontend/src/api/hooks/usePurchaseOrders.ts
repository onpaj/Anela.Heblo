import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getConfig, shouldUseMockAuth } from '../../config/runtimeConfig';
import { mockAuthService } from '../../auth/mockAuth';

// API interfaces matching backend DTOs
export interface GetPurchaseOrdersRequest {
  searchTerm?: string;
  status?: string;
  fromDate?: Date;
  toDate?: Date;
  supplierId?: string;
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
}

export interface PurchaseOrderSummaryDto {
  id: string;
  orderNumber: string;
  supplierId: string;
  supplierName: string;
  orderDate: string;
  expectedDeliveryDate?: string;
  status: string;
  totalAmount: number;
  lineItemCount: number;
  createdAt: string;
  createdBy: string;
}

export interface GetPurchaseOrdersResponse {
  orders: PurchaseOrderSummaryDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}

export interface PurchaseOrderLineDto {
  id: string;
  materialId: string;
  materialName?: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
  notes?: string;
}

export interface GetPurchaseOrderByIdResponse {
  id: string;
  orderNumber: string;
  supplierId: string;
  supplierName: string;
  orderDate: string;
  expectedDeliveryDate?: string;
  status: string;
  notes?: string;
  totalAmount: number;
  lines?: PurchaseOrderLineDto[];
  createdBy: string;
  createdAt: string;
  updatedBy?: string;
  updatedAt?: string;
}

export interface PurchaseOrderHistoryDto {
  id: string;
  action: string;
  oldValue?: string;
  newValue?: string;
  changedBy: string;
  changedAt: string;
}

export interface CreatePurchaseOrderRequest {
  supplierName: string;
  orderDate: string;
  expectedDeliveryDate?: string;
  notes?: string;
  lines?: CreatePurchaseOrderLineRequest[];
}

export interface CreatePurchaseOrderLineRequest {
  materialId: string;
  quantity: number;
  unitPrice: number;
  notes?: string;
}

export interface CreatePurchaseOrderResponse {
  id: string;
  orderNumber: string;
}

export interface UpdatePurchaseOrderRequest {
  id: string;
  supplierName: string;
  expectedDeliveryDate?: string;
  notes?: string;
  lines: UpdatePurchaseOrderLineRequest[];
}

export interface UpdatePurchaseOrderLineRequest {
  id?: string;
  materialId: string;
  quantity: number;
  unitPrice: number;
  notes?: string;
}

export interface UpdatePurchaseOrderResponse {
  id: string;
  orderNumber: string;
}

export interface UpdatePurchaseOrderStatusRequest {
  id: string;
  status: string;
}

export interface UpdatePurchaseOrderStatusResponse {
  id: string;
  status: string;
}

// Real API client 
class PurchaseOrdersApiClient {
  private baseUrl: string;

  constructor(baseUrl: string) {
    this.baseUrl = baseUrl.replace(/\/$/, ''); // Remove trailing slash
  }

  private async getAuthHeaders(): Promise<HeadersInit> {
    const headers: HeadersInit = {
      'Content-Type': 'application/json',
    };

    if (shouldUseMockAuth()) {
      // Mock authentication
      const token = mockAuthService.getAccessToken();
      if (token) {
        headers['Authorization'] = `Bearer ${token}`;
      }
    } else {
      // Real authentication would go here
      console.warn('Real authentication not implemented for Purchase Orders yet');
    }

    return headers;
  }

  private async makeRequest<T>(url: string, options: RequestInit = {}): Promise<T> {
    const authHeaders = await this.getAuthHeaders();
    
    const response = await fetch(`${this.baseUrl}${url}`, {
      headers: {
        ...authHeaders,
        ...options.headers,
      },
      ...options,
    });

    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`);
    }

    return response.json();
  }

  async getPurchaseOrders(request: GetPurchaseOrdersRequest): Promise<GetPurchaseOrdersResponse> {
    const params = new URLSearchParams();
    
    if (request.searchTerm) params.append('searchTerm', request.searchTerm);
    if (request.status) params.append('status', request.status);
    if (request.fromDate) params.append('fromDate', request.fromDate.toISOString());
    if (request.toDate) params.append('toDate', request.toDate.toISOString());
    if (request.supplierId) params.append('supplierId', request.supplierId);
    if (request.pageNumber) params.append('pageNumber', request.pageNumber.toString());
    if (request.pageSize) params.append('pageSize', request.pageSize.toString());
    if (request.sortBy) params.append('sortBy', request.sortBy);
    if (request.sortDescending !== undefined) params.append('sortDescending', request.sortDescending.toString());

    const queryString = params.toString();
    const url = `/api/purchase-orders${queryString ? `?${queryString}` : ''}`;
    
    return this.makeRequest<GetPurchaseOrdersResponse>(url);
  }

  async getPurchaseOrderById(id: string): Promise<GetPurchaseOrderByIdResponse> {
    return this.makeRequest<GetPurchaseOrderByIdResponse>(`/api/purchase-orders/${id}`);
  }

  async getPurchaseOrderHistory(id: string): Promise<PurchaseOrderHistoryDto[]> {
    return this.makeRequest<PurchaseOrderHistoryDto[]>(`/api/purchase-orders/${id}/history`);
  }

  async createPurchaseOrder(request: CreatePurchaseOrderRequest): Promise<CreatePurchaseOrderResponse> {
    return this.makeRequest<CreatePurchaseOrderResponse>('/api/purchase-orders', {
      method: 'POST',
      body: JSON.stringify(request),
    });
  }

  async updatePurchaseOrder(id: string, request: UpdatePurchaseOrderRequest): Promise<UpdatePurchaseOrderResponse> {
    return this.makeRequest<UpdatePurchaseOrderResponse>(`/api/purchase-orders/${id}`, {
      method: 'PUT',
      body: JSON.stringify(request),
    });
  }

  async updatePurchaseOrderStatus(id: string, request: UpdatePurchaseOrderStatusRequest): Promise<UpdatePurchaseOrderStatusResponse> {
    return this.makeRequest<UpdatePurchaseOrderStatusResponse>(`/api/purchase-orders/${id}/status`, {
      method: 'PUT',
      body: JSON.stringify(request),
    });
  }
}

// Create client instance using runtime configuration
const getClient = (): PurchaseOrdersApiClient => {
  const config = getConfig();
  return new PurchaseOrdersApiClient(config.apiUrl);
};

// Query keys
const purchaseOrderKeys = {
  all: ['purchase-orders'] as const,
  lists: () => [...purchaseOrderKeys.all, 'list'] as const,
  list: (filters: GetPurchaseOrdersRequest) => [...purchaseOrderKeys.lists(), filters] as const,
  details: () => [...purchaseOrderKeys.all, 'detail'] as const,
  detail: (id: string) => [...purchaseOrderKeys.details(), id] as const,
  history: (id: string) => [...purchaseOrderKeys.detail(id), 'history'] as const,
};

// Hooks
export const usePurchaseOrdersQuery = (request: GetPurchaseOrdersRequest) => {
  return useQuery({
    queryKey: purchaseOrderKeys.list(request),
    queryFn: () => getClient().getPurchaseOrders(request),
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
};

export const usePurchaseOrderDetailQuery = (id: string) => {
  return useQuery({
    queryKey: purchaseOrderKeys.detail(id),
    queryFn: () => getClient().getPurchaseOrderById(id),
    enabled: !!id,
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
};

export const usePurchaseOrderHistoryQuery = (id: string) => {
  return useQuery({
    queryKey: purchaseOrderKeys.history(id),
    queryFn: () => getClient().getPurchaseOrderHistory(id),
    enabled: !!id,
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
};

export const useCreatePurchaseOrderMutation = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (request: CreatePurchaseOrderRequest) => getClient().createPurchaseOrder(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: purchaseOrderKeys.lists() });
    },
  });
};

export const useUpdatePurchaseOrderMutation = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: ({ id, request }: { id: string; request: UpdatePurchaseOrderRequest }) => 
      getClient().updatePurchaseOrder(id, request),
    onSuccess: (_, { id }) => {
      queryClient.invalidateQueries({ queryKey: purchaseOrderKeys.detail(id) });
      queryClient.invalidateQueries({ queryKey: purchaseOrderKeys.lists() });
    },
  });
};

export const useUpdatePurchaseOrderStatusMutation = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: ({ id, request }: { id: string; request: UpdatePurchaseOrderStatusRequest }) => 
      getClient().updatePurchaseOrderStatus(id, request),
    onSuccess: (_, { id }) => {
      queryClient.invalidateQueries({ queryKey: purchaseOrderKeys.detail(id) });
      queryClient.invalidateQueries({ queryKey: purchaseOrderKeys.lists() });
      queryClient.invalidateQueries({ queryKey: purchaseOrderKeys.history(id) });
    },
  });
};

// Export types - already exported above as interfaces