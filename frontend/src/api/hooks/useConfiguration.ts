import { useQuery, UseQueryResult } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import { GetConfigurationResponse } from '../generated/api-client';

const CONFIGURATION_QUERY_KEY = ['configuration'] as const;

export const useConfigurationQuery = (): UseQueryResult<GetConfigurationResponse> =>
  useQuery({
    queryKey: CONFIGURATION_QUERY_KEY,
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      return apiClient.configuration_GetConfiguration();
    },
    staleTime: Infinity,
    gcTime: Infinity,
    retry: 1,
  });
