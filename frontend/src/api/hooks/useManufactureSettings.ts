import { useQuery, UseQueryResult } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import { GetManufactureSettingsResponse } from '../generated/api-client';

const MANUFACTURE_SETTINGS_QUERY_KEY = ['manufacture-settings'] as const;

export const useManufactureSettingsQuery = (): UseQueryResult<GetManufactureSettingsResponse> =>
  useQuery({
    queryKey: MANUFACTURE_SETTINGS_QUERY_KEY,
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      return apiClient.manufactureSettings_GetSettings();
    },
    staleTime: Infinity,
    gcTime: Infinity,
    retry: 1,
  });
