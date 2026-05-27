import { useQuery } from '@tanstack/react-query';
import type { GetGroupMembersResponse } from '../generated/api-client';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

export const useResponsiblePersonsQuery = (groupId: string) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.userManagement, 'group-members', groupId],
    enabled: Boolean(groupId),
    queryFn: async (): Promise<GetGroupMembersResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      const relativeUrl = `/api/UserManagement/group-members?groupId=${encodeURIComponent(groupId)}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { 'Content-Type': 'application/json' },
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
