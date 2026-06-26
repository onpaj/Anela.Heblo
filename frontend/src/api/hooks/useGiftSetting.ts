import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

export interface GiftSettingDto {
  isEnabled: boolean;
  thresholdCzk: number;
  text: string;
  modifiedAt: string | null;
  modifiedBy: string | null;
}

export interface SetGiftSettingRequest {
  isEnabled: boolean;
  thresholdCzk: number;
  text: string;
}

const QUERY_KEYS = {
  setting: ['giftSettings', 'setting'] as const,
};

const getSetting = async (): Promise<GiftSettingDto> => {
  const apiClient = getAuthenticatedApiClient();
  const fullUrl = `${(apiClient as any).baseUrl}/api/gift-settings`;
  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'GET',
    headers: { Accept: 'application/json' },
  });
  if (!response.ok) {
    throw new Error(`Failed to fetch gift setting: ${response.status}`);
  }
  return response.json();
};

const setSetting = async (request: SetGiftSettingRequest): Promise<void> => {
  const apiClient = getAuthenticatedApiClient();
  const fullUrl = `${(apiClient as any).baseUrl}/api/gift-settings`;
  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json',
    },
    body: JSON.stringify(request),
  });
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new Error(body?.params?.message ?? `Failed to save gift setting: ${response.status}`);
  }
};

export const useGiftSetting = () => {
  return useQuery({
    queryKey: QUERY_KEYS.setting,
    queryFn: getSetting,
  });
};

export const useSetGiftSetting = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: setSetting,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.setting });
    },
  });
};
