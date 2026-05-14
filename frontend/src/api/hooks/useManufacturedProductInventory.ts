import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";

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


function getClientAndBaseUrl(): { apiClient: ReturnType<typeof getAuthenticatedApiClient>; baseUrl: string } {
  const apiClient = getAuthenticatedApiClient();
  const baseUrl = (apiClient as any).baseUrl as string;
  return { apiClient, baseUrl };
}

async function apiFetch(
  apiClient: ReturnType<typeof getAuthenticatedApiClient>,
  url: string,
  init?: RequestInit,
): Promise<Response> {
  const response = await (apiClient as any).http.fetch(url, init);
  if (!response.ok) {
    throw new Error(`HTTP error! status: ${response.status}`);
  }
  return response;
}

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
  return useQuery<ManufacturedInventoryResponse>({
    queryKey: [...QUERY_KEYS.manufacturedProductInventory, filters],
    queryFn: async () => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const inventoryBaseUrl = `${baseUrl}/api/manufactured-inventory`;
      const params = buildFilterParams(filters);
      const url = params.toString() ? `${inventoryBaseUrl}?${params.toString()}` : inventoryBaseUrl;
      const response = await apiFetch(apiClient, url, { method: "GET" });
      return response.json() as Promise<ManufacturedInventoryResponse>;
    },
    staleTime: 1000 * 60 * 5,
  });
};

export const useCreateManufacturedProductInventoryItem = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (input: CreateManufacturedInventoryItemInput): Promise<ManufacturedProductInventoryItem> => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const url = `${baseUrl}/api/manufactured-inventory`;
      const response = await apiFetch(apiClient, url, {
        method: "POST",
        body: JSON.stringify(input),
      });
      return response.json() as Promise<ManufacturedProductInventoryItem>;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.manufacturedProductInventory });
    },
  });
};

export const useUpdateManufacturedProductInventoryItem = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (input: UpdateManufacturedInventoryItemInput): Promise<ManufacturedProductInventoryItem> => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const url = `${baseUrl}/api/manufactured-inventory/${input.id}`;
      const response = await apiFetch(apiClient, url, {
        method: "PUT",
        body: JSON.stringify({ newAmount: input.newAmount, note: input.note }),
      });
      return response.json() as Promise<ManufacturedProductInventoryItem>;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.manufacturedProductInventory });
    },
  });
};

export const useDeleteManufacturedProductInventoryItem = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, note }: { id: number; note?: string }): Promise<void> => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const itemBaseUrl = `${baseUrl}/api/manufactured-inventory/${id}`;
      const params = new URLSearchParams();
      if (note) params.append("note", note);
      const url = params.toString() ? `${itemBaseUrl}?${params.toString()}` : itemBaseUrl;
      await apiFetch(apiClient, url, { method: "DELETE" });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.manufacturedProductInventory });
    },
  });
};
