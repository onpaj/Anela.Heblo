import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

// Type-safe interface for accessing API client internals
interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

export interface AllowedTransition {
  state: string;
  label: string;
  requiresCondition: boolean;
  conditionDescription?: string;
}

export interface GetAllowedTransitionsResponse {
  success: boolean;
  errorMessage?: string;
  currentState?: string;
  allowedTransitions: AllowedTransition[];
}

export const useAllowedTransitionsQuery = (boxId: number, enabled: boolean = true) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.transportBoxTransitions, boxId],
    queryFn: async (): Promise<GetAllowedTransitionsResponse> => {
      const apiClient = getAuthenticatedApiClient() as unknown as ApiClientWithInternals;
      const relativeUrl = `/api/transport-boxes/${boxId}/allowed-transitions`;
      const fullUrl = `${apiClient.baseUrl}${relativeUrl}`;
      const response = await apiClient.http.fetch(fullUrl, {
        method: 'GET',
      });
      
      if (!response.ok) {
        throw new Error(`Failed to get allowed transitions: ${response.statusText}`);
      }
      
      return response.json();
    },
    enabled: enabled && boxId > 0,
  });
};