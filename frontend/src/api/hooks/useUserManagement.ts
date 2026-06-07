import { useQuery } from '@tanstack/react-query';
import type { GetGroupMembersResponse } from '../generated/api-client';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

export const useResponsiblePersonsQuery = (groupId: string) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.userManagement, 'group-members', groupId],
    enabled: Boolean(groupId),
    queryFn: async (): Promise<GetGroupMembersResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.userManagement_GetGroupMembers(groupId);
    },
    staleTime: 15 * 60 * 1000, // 15 minutes cache
    retry: 2,
    retryDelay: 1000,
  });
};
