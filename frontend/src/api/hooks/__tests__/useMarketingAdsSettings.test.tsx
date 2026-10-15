import React from 'react';
import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  useAdsSettingsQuery,
  useSetAdKillSwitchMutation,
  useUpdateAdAutonomyModeMutation,
  useUpdateAdLimitMutation,
} from '../useMarketingAdsSettings';

const mockGetSettings = jest.fn();
const mockUpdateAutonomy = jest.fn();
const mockUpdateLimit = jest.fn();
const mockSetKillSwitch = jest.fn();
jest.mock('../../client', () => ({
  getAuthenticatedApiClient: () => ({
    marketingAdsSettings_GetSettings: (...args: unknown[]) => mockGetSettings(...args),
    marketingAdsSettings_UpdateAutonomy: (...args: unknown[]) => mockUpdateAutonomy(...args),
    marketingAdsSettings_UpdateLimit: (...args: unknown[]) => mockUpdateLimit(...args),
    marketingAdsSettings_SetKillSwitch: (...args: unknown[]) => mockSetKillSwitch(...args),
  }),
}));

const createWrapper = () => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const Wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return Wrapper;
};

describe('useMarketingAdsSettings', () => {
  it('does not load settings for a non-admin (query disabled)', () => {
    renderHook(() => useAdsSettingsQuery(false), { wrapper: createWrapper() });

    expect(mockGetSettings).not.toHaveBeenCalled();
  });

  it('loads and maps settings, joining approval rates and deriving the kill switch', async () => {
    mockGetSettings.mockResolvedValue({
      autonomy: [{ platform: 'GoogleAds', actionType: 'PauseAd', mode: 'ProposeOnly' }],
      limits: [{ platform: 'GoogleAds', actionType: 'PauseAd', key: 'MaxActionsPerProposal', value: 10 }],
      killSwitches: [
        { scope: 'Global', executionEnabled: true },
        { scope: 'Sklik', executionEnabled: false },
      ],
      isExecutionEnabledGlobally: true,
      approvalRates: [{ platform: 'GoogleAds', actionType: 'PauseAd', decided: 4, approvedWithoutEdit: 2, rate: 0.5 }],
    });

    const { result } = renderHook(() => useAdsSettingsQuery(true), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.killSwitch.executionEnabled).toBe(true);
    expect(result.current.data?.killSwitch.platformOverrides).toEqual([{ platform: 'Sklik', executionEnabled: false }]);
    expect(result.current.data?.limits[0].value).toBe(10);
    expect(result.current.data?.autonomy[0]).toEqual(expect.objectContaining({ approvedWithoutEditRate: 0.5, decidedCount: 4 }));
  });

  it('sends an autonomy change', async () => {
    mockUpdateAutonomy.mockResolvedValue({ success: true });
    const { result } = renderHook(() => useUpdateAdAutonomyModeMutation(), { wrapper: createWrapper() });

    await act(async () => {
      await result.current.mutateAsync({ platform: 'GoogleAds', actionType: 'PauseAd', mode: 'AutoWithinLimits' });
    });

    expect(mockUpdateAutonomy).toHaveBeenCalledWith(
      expect.objectContaining({ platform: 'GoogleAds', actionType: 'PauseAd', mode: 'AutoWithinLimits' }),
    );
  });

  it('sends a limit change with platform and action type', async () => {
    mockUpdateLimit.mockResolvedValue({ success: true });
    const { result } = renderHook(() => useUpdateAdLimitMutation(), { wrapper: createWrapper() });

    await act(async () => {
      await result.current.mutateAsync({ platform: 'GoogleAds', actionType: 'PauseAd', key: 'MaxPausedAdsPer7Days', value: 20 });
    });

    expect(mockUpdateLimit).toHaveBeenCalledWith(
      expect.objectContaining({ platform: 'GoogleAds', actionType: 'PauseAd', key: 'MaxPausedAdsPer7Days', value: 20 }),
    );
  });

  it('sends a kill-switch flip for one platform', async () => {
    mockSetKillSwitch.mockResolvedValue({ success: true });
    const { result } = renderHook(() => useSetAdKillSwitchMutation(), { wrapper: createWrapper() });

    await act(async () => {
      await result.current.mutateAsync({ platform: 'Sklik', executionEnabled: false });
    });

    expect(mockSetKillSwitch).toHaveBeenCalledWith(expect.objectContaining({ platform: 'Sklik', executionEnabled: false }));
  });

  it('sends a global kill-switch flip with an undefined platform', async () => {
    mockSetKillSwitch.mockResolvedValue({ success: true });
    const { result } = renderHook(() => useSetAdKillSwitchMutation(), { wrapper: createWrapper() });

    await act(async () => {
      await result.current.mutateAsync({ platform: null, executionEnabled: true });
    });

    expect(mockSetKillSwitch).toHaveBeenCalledWith(expect.objectContaining({ platform: undefined, executionEnabled: true }));
  });
});
