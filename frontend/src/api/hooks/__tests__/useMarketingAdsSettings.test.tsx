import React from 'react';
import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  useAdsSettingsQuery,
  useSetAdKillSwitchMutation,
  useUpdateAdAutonomyModeMutation,
  useUpdateAdLimitMutation,
} from '../useMarketingAdsSettings';
import { marketingAdsKeys } from '../marketingAds/keys';

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

const createQueryClient = () =>
  new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });

const wrapperFor = (client: QueryClient) => {
  const Wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return Wrapper;
};

const createWrapper = () => wrapperFor(createQueryClient());

type Wrapper = ReturnType<typeof wrapperFor>;

const runMutation = async <T,>(useHook: () => { mutateAsync: (v: T) => Promise<void> }, variables: T, wrapper: Wrapper) => {
  const { result } = renderHook(() => useHook(), { wrapper });
  await act(async () => {
    await result.current.mutateAsync(variables);
  });
};

const SETTINGS_RESPONSE = { autonomy: [], limits: [], killSwitches: [], isExecutionEnabledGlobally: false, approvalRates: [] };

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

  describe('what a settings change refreshes', () => {
    const changes = [
      {
        name: 'autonomy mode',
        change: (wrapper: Wrapper) => {
          mockUpdateAutonomy.mockResolvedValue({ success: true });
          return runMutation(useUpdateAdAutonomyModeMutation, { platform: 'GoogleAds', actionType: 'PauseAd', mode: 'Auto' }, wrapper);
        },
      },
      {
        name: 'limit',
        change: (wrapper: Wrapper) => {
          mockUpdateLimit.mockResolvedValue({ success: true });
          return runMutation(useUpdateAdLimitMutation, { platform: 'GoogleAds', actionType: 'PauseAd', key: 'K', value: 1 }, wrapper);
        },
      },
      {
        name: 'kill switch',
        change: (wrapper: Wrapper) => {
          mockSetKillSwitch.mockResolvedValue({ success: true });
          return runMutation(useSetAdKillSwitchMutation, { platform: null, executionEnabled: false }, wrapper);
        },
      },
    ];

    it.each(changes)('changing the $name invalidates the settings and the audit log', async ({ change }) => {
      // Arrange
      const client = createQueryClient();
      const spy = jest.spyOn(client, 'invalidateQueries');

      // Act
      await change(wrapperFor(client));

      // Assert
      expect(spy.mock.calls.map(([filters]) => filters?.queryKey)).toEqual([marketingAdsKeys.settings(), marketingAdsKeys.auditAll()]);
    });

    it('a limit change stays pending until the settings have reloaded', async () => {
      // Arrange
      mockUpdateLimit.mockResolvedValue({ success: true });
      mockGetSettings.mockResolvedValueOnce(SETTINGS_RESPONSE);
      const { result } = renderHook(
        () => ({ settings: useAdsSettingsQuery(true), update: useUpdateAdLimitMutation() }),
        { wrapper: createWrapper() },
      );
      await waitFor(() => expect(result.current.settings.isSuccess).toBe(true));
      let resolveReload: (value: typeof SETTINGS_RESPONSE) => void = () => undefined;
      mockGetSettings.mockReturnValueOnce(new Promise((resolve) => (resolveReload = resolve)));

      // Act
      act(() => result.current.update.mutate({ platform: 'GoogleAds', actionType: 'PauseAd', key: 'K', value: 1 }));
      await waitFor(() => expect(mockGetSettings).toHaveBeenCalledTimes(2));

      // Assert
      expect(result.current.update.isPending).toBe(true);
      await act(async () => resolveReload(SETTINGS_RESPONSE));
      await waitFor(() => expect(result.current.update.isPending).toBe(false));
    });
  });
});
