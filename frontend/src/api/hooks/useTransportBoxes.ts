import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { ApiClient } from "../generated/api-client";
import {
  GetTransportBoxesResponse,
  GetTransportBoxByIdResponse,
  ChangeTransportBoxStateRequest,
  ChangeTransportBoxStateResponse,
  TransportBoxState,
  TransportBoxDto,
  ErrorCodes,
} from "../generated/api-client";

// Type-safe interface for accessing API client internals
interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

// Extended add-item request — includes optional inventory source fields not yet in the
// generated client (backend AddItemToBoxRequest already supports them from Task 10).
export interface AddItemToBoxInput {
  boxId: number;
  productCode: string;
  productName: string;
  amount: number;
  sourceInventoryId?: number;
  lotNumber?: string;
  expirationDate?: string;
  allowNegativeStock?: boolean;
}

// Define request interface matching the backend contract
export interface GetTransportBoxesRequest {
  skip?: number;
  take?: number;
  code?: string;
  state?: string;
  productCode?: string;
  sortBy?: string;
  sortDescending?: boolean;
}

// Query keys
const transportBoxKeys = {
  all: QUERY_KEYS.transportBox,
  lists: () => [...QUERY_KEYS.transportBox, "list"] as const,
  list: (filters: GetTransportBoxesRequest) =>
    [...QUERY_KEYS.transportBox, "list", filters] as const,
  details: () => [...QUERY_KEYS.transportBox, "detail"] as const,
  detail: (id: number) => [...QUERY_KEYS.transportBox, "detail", id] as const,
};

// Helper to get the correct API client instance from generated file
const getTransportBoxClient = (): ApiClient => {
  return getAuthenticatedApiClient();
};

// Hooks
export const useTransportBoxesQuery = (request: GetTransportBoxesRequest) => {
  return useQuery({
    queryKey: transportBoxKeys.list(request),
    queryFn: async (): Promise<GetTransportBoxesResponse> => {
      const client = getTransportBoxClient();
      return await client.transportBox_GetTransportBoxes(
        request.skip,
        request.take,
        request.code || null,
        request.state || null,
        request.productCode || null,
        request.sortBy || null,
        request.sortDescending,
      );
    },
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
};

export const useTransportBoxByIdQuery = (
  id: number,
  enabled: boolean = true,
) => {
  return useQuery({
    queryKey: transportBoxKeys.detail(id),
    queryFn: async (): Promise<GetTransportBoxByIdResponse> => {
      const client = getTransportBoxClient();
      return await client.transportBox_GetTransportBoxById(id);
    },
    enabled: enabled && id > 0,
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
};

// Look up a transport box by its scannable code (barcode). Returns the box, or
// null when the code is genuinely not found. Throws (sets isError) for any other
// backend error so callers can show a distinct "load error" vs "not found" message.
// staleTime: 0 — scan results must always be fresh (the physical box may have just
// changed state between scans).
export const useTransportBoxByCodeQuery = (code: string | null) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.transportBox, "byCode", code],
    queryFn: async (): Promise<TransportBoxDto | null> => {
      const client = getTransportBoxClient();
      const response = await client.transportBox_GetTransportBoxByCode(code!);
      if (response.success) return response.transportBox ?? null;
      if (response.errorCode === ErrorCodes.TransportBoxNotFound) return null;
      throw new Error(response.errorCode ?? "UnknownError");
    },
    enabled: !!code,
    staleTime: 0,
  });
};

// Convenience hook for getting all transport boxes with default pagination
export const useTransportBoxesList = (
  filters?: Omit<GetTransportBoxesRequest, "skip" | "take">,
) => {
  return useTransportBoxesQuery({
    skip: 0,
    take: 50,
    sortBy: "id",
    sortDescending: true,
    ...filters,
  });
};

// Hook for transport box summary
export const useTransportBoxSummaryQuery = (
  request: Omit<
    GetTransportBoxesRequest,
    "skip" | "take" | "sortBy" | "sortDescending"
  >,
) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.transportBox, "summary", request],
    queryFn: async () => {
      const client = getTransportBoxClient();
      return await client.transportBox_GetTransportBoxSummary(
        request.code || null,
        request.productCode || null,
      );
    },
    staleTime: 1000 * 60 * 2, // 2 minutes for summary
  });
};

// Mutation hook for changing transport box state
export const useChangeTransportBoxState = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (params: {
      boxId: number;
      newState: TransportBoxState;
      description?: string;
      boxNumber?: string;
      location?: string;
    }): Promise<ChangeTransportBoxStateResponse> => {
      const client = getTransportBoxClient();
      const request = new ChangeTransportBoxStateRequest({
        boxId: params.boxId,
        newState: params.newState,
        description: params.description,
        boxCode: params.boxNumber,
        location: params.location,
      });

      const response = await client.transportBox_ChangeTransportBoxState(
        params.boxId,
        request,
      );

      // No need to check for errors here - global error handler shows toasts
      // If there was an error, SwaggerException will be thrown by generated client
      return response;
    },
    onSuccess: (data, variables) => {
      // Invalidate and refetch related queries
      queryClient.invalidateQueries({
        queryKey: transportBoxKeys.detail(variables.boxId),
      });
      queryClient.invalidateQueries({ queryKey: transportBoxKeys.lists() });
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.transportBox, "summary"],
      });

      // Also invalidate any transition-related queries
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.transportBoxTransitions, variables.boxId],
      });

      // Invalidate byCode cache so the scan lookup reflects the new state
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.transportBox, 'byCode'],
      });

      // Force refetch of the specific box detail to ensure fresh data
      queryClient.refetchQueries({
        queryKey: transportBoxKeys.detail(variables.boxId),
      });
    },
  });
};

export { transportBoxKeys };

export const useAddItemToBox = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (input: AddItemToBoxInput): Promise<{ success: boolean; errorMessage?: string }> => {
      const apiClient = getAuthenticatedApiClient() as unknown as ApiClientWithInternals;
      const url = `${apiClient.baseUrl}/api/transport-boxes/${input.boxId}/items`;
      const body: Record<string, unknown> = {
        boxId: input.boxId,
        productCode: input.productCode,
        productName: input.productName,
        amount: input.amount,
      };
      if (input.sourceInventoryId !== undefined) body["sourceInventoryId"] = input.sourceInventoryId;
      if (input.lotNumber !== undefined) body["lotNumber"] = input.lotNumber;
      if (input.expirationDate !== undefined) body["expirationDate"] = input.expirationDate;
      if (input.allowNegativeStock) body["allowNegativeStock"] = true;

      const response = await apiClient.http.fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });

      if (!response.ok) {
        throw new Error(`HTTP error ${response.status}: ${response.statusText}`);
      }

      return response.json() as Promise<{ success: boolean; errorMessage?: string }>;
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: transportBoxKeys.detail(variables.boxId) });
      queryClient.invalidateQueries({ queryKey: transportBoxKeys.lists() });
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.manufacturedProductInventory });
    },
  });
};
