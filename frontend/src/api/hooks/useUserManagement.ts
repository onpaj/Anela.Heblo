import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

export interface UserDto {
  id: string;
  displayName: string;
  email: string;
}

export interface GetGroupMembersResponse {
  success: boolean;
  errorCode?: number;
  params?: Record<string, string>;
  members: UserDto[];
}

export const useResponsiblePersonsQuery = () => {
  return useQuery({
    queryKey: ['responsible-persons'],
    queryFn: async (): Promise<GetGroupMembersResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      const relativeUrl = '/api/ManufactureOrder/responsible-persons';
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { 'Content-Type': 'application/json' }
      });
      
      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }
      
      return response.json();
    },
    staleTime: 15 * 60 * 1000, // 15 minutes cache
    retry: 2,
    retryDelay: 1000,
  });
};