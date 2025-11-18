import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";

// Define types based on our backend DTOs
export interface PackingMaterialDto {
  id: number;
  name: string;
  consumptionRate: number;
  consumptionType: ConsumptionType;
  consumptionTypeText: string;
  currentQuantity: number;
  forecastedDays?: number;
  createdAt: string;
  updatedAt?: string;
}

export enum ConsumptionType {
  PerOrder = 1,
  PerProduct = 2,
  PerDay = 3
}

export interface GetPackingMaterialsListResponse {
  materials: PackingMaterialDto[];
}

export interface CreatePackingMaterialRequest {
  name: string;
  consumptionRate: number;
  consumptionType: ConsumptionType;
  currentQuantity: number;
}

export interface CreatePackingMaterialResponse {
  id: number;
  material: PackingMaterialDto;
}

export interface UpdatePackingMaterialRequest {
  id: number;
  name: string;
  consumptionRate: number;
  consumptionType: ConsumptionType;
}

export interface UpdatePackingMaterialResponse {
  material: PackingMaterialDto;
}

export interface UpdateQuantityRequest {
  newQuantity: number;
  date: string; // DateOnly as ISO string
}

export interface UpdatePackingMaterialQuantityResponse {
  material: PackingMaterialDto;
}

// API client class
class PackingMaterialsApiClient {
  private baseUrl: string;

  constructor(baseUrl: string) {
    this.baseUrl = baseUrl;
  }

  async makeRequest<T>(url: string, options: RequestInit = {}): Promise<T> {
    const apiClient = getAuthenticatedApiClient();
    const fullUrl = `${this.baseUrl}${url}`;
    
    // Use the API client's fetch method which handles authentication automatically
    const response = await (apiClient as any).http.fetch(fullUrl, {
      method: options.method || 'GET',
      headers: {
        'Content-Type': 'application/json',
        ...options.headers,
      },
      body: options.body,
    });

    if (!response.ok) {
      throw new Error(`API request failed: ${response.statusText}`);
    }

    return response.json();
  }

  async getPackingMaterials(): Promise<GetPackingMaterialsListResponse> {
    return this.makeRequest<GetPackingMaterialsListResponse>('/api/packing-materials');
  }

  async createPackingMaterial(request: CreatePackingMaterialRequest): Promise<CreatePackingMaterialResponse> {
    return this.makeRequest<CreatePackingMaterialResponse>('/api/packing-materials', {
      method: 'POST',
      body: JSON.stringify(request),
    });
  }

  async updatePackingMaterial(id: number, request: Omit<UpdatePackingMaterialRequest, 'id'>): Promise<UpdatePackingMaterialResponse> {
    return this.makeRequest<UpdatePackingMaterialResponse>(`/api/packing-materials/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ ...request, id }),
    });
  }

  async updatePackingMaterialQuantity(id: number, request: UpdateQuantityRequest): Promise<UpdatePackingMaterialQuantityResponse> {
    return this.makeRequest<UpdatePackingMaterialQuantityResponse>(`/api/packing-materials/${id}/quantity`, {
      method: 'POST',
      body: JSON.stringify(request),
    });
  }

  async deletePackingMaterial(id: number): Promise<void> {
    return this.makeRequest<void>(`/api/packing-materials/${id}`, {
      method: 'DELETE',
    });
  }
}

// Create API client instance
const createApiClient = (): PackingMaterialsApiClient => {
  const apiClient = getAuthenticatedApiClient();
  return new PackingMaterialsApiClient((apiClient as any).baseUrl);
};

// Query keys
const QUERY_KEYS = {
  packingMaterials: ['packingMaterials'] as const,
};

// Hooks
export const usePackingMaterials = () => {
  return useQuery({
    queryKey: QUERY_KEYS.packingMaterials,
    queryFn: async () => {
      const client = createApiClient();
      return client.getPackingMaterials();
    },
  });
};

export const useCreatePackingMaterial = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (request: CreatePackingMaterialRequest) => {
      const client = createApiClient();
      return client.createPackingMaterial(request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.packingMaterials });
    },
  });
};

export const useUpdatePackingMaterial = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async ({ id, ...request }: UpdatePackingMaterialRequest) => {
      const client = createApiClient();
      return client.updatePackingMaterial(id, request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.packingMaterials });
    },
  });
};

export const useUpdatePackingMaterialQuantity = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async ({ id, ...request }: { id: number } & UpdateQuantityRequest) => {
      const client = createApiClient();
      return client.updatePackingMaterialQuantity(id, request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.packingMaterials });
    },
  });
};

export const useDeletePackingMaterial = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (id: number) => {
      const client = createApiClient();
      return client.deletePackingMaterial(id);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.packingMaterials });
    },
  });
};