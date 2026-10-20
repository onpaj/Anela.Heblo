import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import {
  SetAdKillSwitchRequest,
  UpdateAdAutonomySettingRequest,
  UpdateAdLimitSettingRequest,
} from '../generated/api-client';
import { marketingAdsKeys } from './marketingAds/keys';
import { mapSettings } from './marketingAds/mappers';
import type { AdsSettings } from './marketingAds/types';

export interface AutonomyChange {
  platform: string;
  actionType: string;
  mode: string;
}

export interface LimitChange {
  platform: string;
  actionType: string;
  key: string;
  value: number;
}

export interface KillSwitchChange {
  platform: string | null;
  executionEnabled: boolean;
}

export const useAdsSettingsQuery = (enabled: boolean) =>
  useQuery<AdsSettings, Error>({
    queryKey: marketingAdsKeys.settings(),
    queryFn: async () => mapSettings(await getAuthenticatedApiClient().marketingAdsSettings_GetSettings()),
    enabled,
  });

const useInvalidateSettings = () => {
  const queryClient = useQueryClient();
  // Returned from onSuccess, so the mutation stays pending until the new settings have loaded.
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: marketingAdsKeys.settings() }),
      queryClient.invalidateQueries({ queryKey: marketingAdsKeys.auditAll() }),
    ]);
};

export const useUpdateAdAutonomyModeMutation = () => {
  const invalidate = useInvalidateSettings();
  return useMutation<void, unknown, AutonomyChange>({
    mutationFn: async ({ platform, actionType, mode }) => {
      await getAuthenticatedApiClient(false).marketingAdsSettings_UpdateAutonomy(
        new UpdateAdAutonomySettingRequest({
          platform: platform as UpdateAdAutonomySettingRequest['platform'],
          actionType: actionType as UpdateAdAutonomySettingRequest['actionType'],
          mode: mode as UpdateAdAutonomySettingRequest['mode'],
        }),
      );
    },
    onSuccess: invalidate,
  });
};

export const useUpdateAdLimitMutation = () => {
  const invalidate = useInvalidateSettings();
  return useMutation<void, unknown, LimitChange>({
    mutationFn: async ({ platform, actionType, key, value }) => {
      await getAuthenticatedApiClient(false).marketingAdsSettings_UpdateLimit(
        new UpdateAdLimitSettingRequest({
          platform: platform as UpdateAdLimitSettingRequest['platform'],
          actionType: actionType as UpdateAdLimitSettingRequest['actionType'],
          key,
          value,
        }),
      );
    },
    onSuccess: invalidate,
  });
};

export const useSetAdKillSwitchMutation = () => {
  const invalidate = useInvalidateSettings();
  return useMutation<void, unknown, KillSwitchChange>({
    mutationFn: async ({ platform, executionEnabled }) => {
      await getAuthenticatedApiClient(false).marketingAdsSettings_SetKillSwitch(
        new SetAdKillSwitchRequest({
          platform: (platform ?? undefined) as SetAdKillSwitchRequest['platform'],
          executionEnabled,
        }),
      );
    },
    onSuccess: invalidate,
  });
};
