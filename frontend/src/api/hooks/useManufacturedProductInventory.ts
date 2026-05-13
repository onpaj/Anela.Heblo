import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";

export enum InventoryChangeType {
  InitialWriteDown = 1,
  ConsumedByTransportBox = 2,
  RestoredFromTransportBox = 3,
  ManualAdjustment = 4,
  ManualRemoval = 5,
  ManualAddition = 6,
}

export interface ManufacturedProductInventoryLog {
  id: number;
  inventoryItemId: number;
  changeType: InventoryChangeType;
  amountDelta: number;
  amountAfter: number;
  referenceType?: string;
  referenceId?: string;
  note?: string;
  timestamp: string;
  user: string;
}

export interface ManufacturedProductInventoryItem {
  id: number;
  productCode: string;
  productName: string;
  lotNumber?: string;
  expirationDate?: string;
  amount: number;
  manufactureOrderId?: number;
  createdAt: string;
  createdBy: string;
  lastModifiedAt?: string;
  lastModifiedBy?: string;
  log: ManufacturedProductInventoryLog[];
}

export interface ManufacturedInventoryFilters {
  search?: string;
  onlyWithStock?: boolean;
  manufactureOrderId?: number;
  page?: number;
  pageSize?: number;
}

export interface CreateManufacturedInventoryItemInput {
  productCode: string;
  productName: string;
  amount: number;
  lotNumber?: string;
  expirationDate?: string;
  manufactureOrderId?: number;
}

export interface UpdateManufacturedInventoryItemInput {
  id: number;
  newAmount: number;
  note?: string;
}

interface ManufacturedInventoryResponse {
  items: ManufacturedProductInventoryItem[];
  totalCount: number;
}

const QUERY_KEY = "manufactured-product-inventory";

const getBaseUrl = (): string => {
  const apiClient = getAuthenticatedApiClient();
  return `${(apiClient as unknown as { baseUrl: string }).baseUrl}/api/manufactured-inventory`;
};

const buildFilterParams = (filters: ManufacturedInventoryFilters): URLSearchParams => {
  const params = new URLSearchParams();
  if (filters.search) params.append("search", filters.search);
  if (filters.onlyWithStock !== undefined) params.append("onlyWithStock", filters.onlyWithStock.toString());
  if (filters.manufactureOrderId !== undefined) params.append("manufactureOrderId", filters.manufactureOrderId.toString());
  if (filters.page !== undefined) params.append("page", filters.page.toString());
  if (filters.pageSize !== undefined) params.append("pageSize", filters.pageSize.toString());
  return params;
};

export const useManufacturedProductInventoryQuery = (filters: ManufacturedInventoryFilters = {}) => {
  const apiClient = getAuthenticatedApiClient();

  return useQuery<ManufacturedInventoryResponse>({
    queryKey: [QUERY_KEY, filters],
    queryFn: async () => {
      const baseUrl = getBaseUrl();
      const params = buildFilterParams(filters);
      const url = params.toString() ? `${baseUrl}?${params.toString()}` : baseUrl;

      const response = await (apiClient as unknown as { http: { fetch: (url: string, init?: RequestInit) => Promise<Response> } }).http.fetch(url, {
        method: "GET",
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<ManufacturedInventoryResponse>;
    },
    staleTime: 1000 * 60 * 5,
  });
};

export const useCreateManufacturedProductInventoryItem = () => {
  const queryClient = useQueryClient();
  const apiClient = getAuthenticatedApiClient();

  return useMutation({
    mutationFn: async (input: CreateManufacturedInventoryItemInput): Promise<ManufacturedProductInventoryItem> => {
      const url = getBaseUrl();
      const response = await (apiClient as unknown as { http: { fetch: (url: string, init?: RequestInit) => Promise<Response> } }).http.fetch(url, {
        method: "POST",
        body: JSON.stringify(input),
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<ManufacturedProductInventoryItem>;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [QUERY_KEY] });
    },
  });
};

export const useUpdateManufacturedProductInventoryItem = () => {
  const queryClient = useQueryClient();
  const apiClient = getAuthenticatedApiClient();

  return useMutation({
    mutationFn: async (input: UpdateManufacturedInventoryItemInput): Promise<ManufacturedProductInventoryItem> => {
      const url = `${getBaseUrl()}/${input.id}`;
      const response = await (apiClient as unknown as { http: { fetch: (url: string, init?: RequestInit) => Promise<Response> } }).http.fetch(url, {
        method: "PUT",
        body: JSON.stringify({ newAmount: input.newAmount, note: input.note }),
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<ManufacturedProductInventoryItem>;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [QUERY_KEY] });
    },
  });
};

export const useDeleteManufacturedProductInventoryItem = () => {
  const queryClient = useQueryClient();
  const apiClient = getAuthenticatedApiClient();

  return useMutation({
    mutationFn: async ({ id, note }: { id: number; note?: string }): Promise<void> => {
      const baseUrl = `${getBaseUrl()}/${id}`;
      const params = new URLSearchParams();
      if (note) params.append("note", note);
      const url = params.toString() ? `${baseUrl}?${params.toString()}` : baseUrl;

      const response = await (apiClient as unknown as { http: { fetch: (url: string, init?: RequestInit) => Promise<Response> } }).http.fetch(url, {
        method: "DELETE",
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [QUERY_KEY] });
    },
  });
};
