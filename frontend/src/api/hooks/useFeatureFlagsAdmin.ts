import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  FlagStatusDto,
  UpsertFlagOverrideBodyDto,
} from "../generated/api-client";

const featureFlagsAdminKeys = {
  admin: () => [...QUERY_KEYS.featureFlags, "admin"] as const,
};

export const useFeatureFlagsAdmin = () => {
  return useQuery({
    queryKey: featureFlagsAdminKeys.admin(),
    queryFn: async () => {
      const client = getAuthenticatedApiClient();
      const response = await client.featureFlags_GetAdmin();
      return response?.flags ?? ([] as FlagStatusDto[]);
    },
  });
};

export const useUpsertFlagOverride = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({
      key,
      isEnabled,
    }: {
      key: string;
      isEnabled: boolean;
    }) => {
      const client = getAuthenticatedApiClient();
      const body = new UpsertFlagOverrideBodyDto({ isEnabled });
      await client.featureFlags_Put(key, body);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: featureFlagsAdminKeys.admin() });
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.featureFlags });
    },
  });
};

export const useClearFlagOverride = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (key: string) => {
      const client = getAuthenticatedApiClient();
      await client.featureFlags_Delete(key);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: featureFlagsAdminKeys.admin() });
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.featureFlags });
    },
  });
};

export type { FlagStatusDto };
