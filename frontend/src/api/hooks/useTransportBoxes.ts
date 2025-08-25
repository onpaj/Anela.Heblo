import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import { ApiClient as GeneratedApiClient } from '../generated/api-client';
import { 
  GetTransportBoxesResponse,
  GetTransportBoxByIdResponse,
  ChangeTransportBoxStateRequest,
  ChangeTransportBoxStateResponse
} from '../generated/api-client';

// Define request interface matching the backend contract
export interface GetTransportBoxesRequest {
  skip?: number;
  take?: number;
  code?: string;
  state?: string;
  fromDate?: Date;
  toDate?: Date;
  sortBy?: string;
  sortDescending?: boolean;
}

// Query keys
const transportBoxKeys = {
  all: ['transport-boxes'] as const,
  lists: () => [...transportBoxKeys.all, 'list'] as const,
  list: (filters: GetTransportBoxesRequest) => [...transportBoxKeys.lists(), filters] as const,
  details: () => [...transportBoxKeys.all, 'detail'] as const,
  detail: (id: number) => [...transportBoxKeys.details(), id] as const,
};

// Helper to get the correct API client instance from generated file
const getTransportBoxClient = (): GeneratedApiClient => {
  const apiClient = getAuthenticatedApiClient();
  return apiClient as any as GeneratedApiClient;
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
        request.fromDate || null,
        request.toDate || null,
        request.sortBy || null,
        request.sortDescending
      );
    },
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
};

export const useTransportBoxByIdQuery = (id: number, enabled: boolean = true) => {
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

// Convenience hook for getting all transport boxes with default pagination
export const useTransportBoxesList = (filters?: Omit<GetTransportBoxesRequest, 'skip' | 'take'>) => {
  return useTransportBoxesQuery({
    skip: 0,
    take: 50,
    sortBy: 'id',
    sortDescending: true,
    ...filters,
  });
};

// Hook for transport box summary
export const useTransportBoxSummaryQuery = (request: Omit<GetTransportBoxesRequest, 'skip' | 'take' | 'sortBy' | 'sortDescending'>) => {
  return useQuery({
    queryKey: [...transportBoxKeys.all, 'summary', request],
    queryFn: async () => {
      const client = getTransportBoxClient();
      return await client.transportBox_GetTransportBoxSummary(
        request.code || null,
        request.fromDate || null,
        request.toDate || null
      );
    },
    staleTime: 1000 * 60 * 2, // 2 minutes for summary
  });
};

// Mutation hook for changing transport box state
export const useChangeTransportBoxState = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (params: { boxId: number; newState: string; description?: string }): Promise<ChangeTransportBoxStateResponse> => {
      const client = getTransportBoxClient();
      const request = new ChangeTransportBoxStateRequest({
        boxId: params.boxId,
        newState: params.newState,
        description: params.description
      });
      return await client.transportBox_ChangeTransportBoxState(params.boxId, request);
    },
    onSuccess: (data, variables) => {
      // Invalidate and refetch related queries
      queryClient.invalidateQueries({ queryKey: transportBoxKeys.detail(variables.boxId) });
      queryClient.invalidateQueries({ queryKey: transportBoxKeys.lists() });
      queryClient.invalidateQueries({ queryKey: [...transportBoxKeys.all, 'summary'] });
    },
  });
};

export { transportBoxKeys };