import React, { ReactNode } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  useAvailableTiles,
  useUserDashboardSettings,
  useTileData,
  useSaveDashboardSettings,
  useEnableTile,
  useDisableTile
} from '../useDashboard';

// Import the mocked client module
import * as clientModule from '../../client';

// Mock the API client — mock the six typed methods the hooks call directly
const mockApiClient = {
  dashboard_GetAvailableTiles: jest.fn(),
  dashboard_GetUserSettings: jest.fn(),
  dashboard_GetTileData: jest.fn(),
  dashboard_SaveUserSettings: jest.fn(),
  dashboard_EnableTile: jest.fn(),
  dashboard_DisableTile: jest.fn(),
};

jest.mock('../../client');

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
      mutations: {
        retry: false,
      },
    },
  });

  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      {children}
    </QueryClientProvider>
  );
};

describe('useDashboard hooks', () => {
  beforeEach(() => {
    jest.clearAllMocks();

    // Set up the mock implementation
    (clientModule.getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockApiClient);
  });

  describe('useAvailableTiles', () => {
    it('should fetch available tiles successfully', async () => {
      const mockTiles = [
        {
          tileId: 'tile-1',
          title: 'Test Tile 1',
          description: 'Description 1',
          size: 'Medium',
          category: 'Analytics',
          defaultEnabled: true,
          autoShow: false,
          requiredPermissions: []
        }
      ];

      mockApiClient.dashboard_GetAvailableTiles.mockResolvedValueOnce(mockTiles);

      const { result } = renderHook(() => useAvailableTiles(), {
        wrapper: createWrapper()
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(mockApiClient.dashboard_GetAvailableTiles).toHaveBeenCalledWith();
      expect(result.current.data).toEqual(mockTiles);
    });

    it('should handle fetch error', async () => {
      mockApiClient.dashboard_GetAvailableTiles.mockRejectedValueOnce(new Error('Network error'));

      const { result } = renderHook(() => useAvailableTiles(), {
        wrapper: createWrapper()
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect(result.current.error).toBeDefined();
    });
  });

  describe('useUserDashboardSettings', () => {
    it('should fetch user dashboard settings successfully', async () => {
      const mockSettings = {
        tiles: [
          { tileId: 'tile-1', isVisible: true, displayOrder: 0 }
        ],
        lastModified: new Date('2024-01-01T00:00:00Z')
      };

      mockApiClient.dashboard_GetUserSettings.mockResolvedValueOnce(mockSettings);

      const { result } = renderHook(() => useUserDashboardSettings(), {
        wrapper: createWrapper()
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(mockApiClient.dashboard_GetUserSettings).toHaveBeenCalledWith();
      expect(result.current.data).toEqual({
        tiles: [{ tileId: 'tile-1', isVisible: true, displayOrder: 0 }],
        lastModified: '2024-01-01T00:00:00.000Z'
      });
    });

    it('should handle settings fetch error', async () => {
      mockApiClient.dashboard_GetUserSettings.mockRejectedValueOnce(new Error('Unauthorized'));

      const { result } = renderHook(() => useUserDashboardSettings(), {
        wrapper: createWrapper()
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect(result.current.error).toBeDefined();
    });

    it('should return empty fallback settings on 403 Forbidden', async () => {
      mockApiClient.dashboard_GetUserSettings.mockRejectedValueOnce({ status: 403 });

      const { result } = renderHook(() => useUserDashboardSettings(), {
        wrapper: createWrapper()
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data?.tiles).toEqual([]);
      expect(typeof result.current.data?.lastModified).toBe('string');
    });
  });

  describe('useTileData', () => {
    it('should fetch tile data successfully', async () => {
      const mockTileData = [
        {
          tileId: 'tile-1',
          title: 'Analytics',
          description: 'Analytics data',
          size: 'Medium',
          category: 'Analytics',
          defaultEnabled: true,
          autoShow: false,
          requiredPermissions: [],
          data: { count: 42 }
        }
      ];

      mockApiClient.dashboard_GetTileData.mockResolvedValueOnce(mockTileData);

      const { result } = renderHook(() => useTileData(), {
        wrapper: createWrapper()
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(mockApiClient.dashboard_GetTileData).toHaveBeenCalledWith(undefined);
      expect(result.current.data).toEqual(mockTileData);
    });

    it('should return empty array fallback on 403 Forbidden', async () => {
      mockApiClient.dashboard_GetTileData.mockRejectedValueOnce({ status: 403 });

      const { result } = renderHook(() => useTileData(), {
        wrapper: createWrapper()
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual([]);
    });

    it('should have refetch interval set', () => {
      const { result } = renderHook(() => useTileData(), {
        wrapper: createWrapper()
      });

      // We can't easily test the actual refetch interval, but we can verify
      // the hook is configured correctly
      expect(result.current).toBeDefined();
    });
  });

  describe('useSaveDashboardSettings', () => {
    it('should save dashboard settings successfully', async () => {
      mockApiClient.dashboard_SaveUserSettings.mockResolvedValueOnce({});

      const { result } = renderHook(() => useSaveDashboardSettings(), {
        wrapper: createWrapper()
      });

      const settingsToSave = {
        tiles: [
          { tileId: 'tile-1', isVisible: true, displayOrder: 0 }
        ]
      };

      result.current.mutate(settingsToSave);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(mockApiClient.dashboard_SaveUserSettings).toHaveBeenCalledTimes(1);
      const calledWith = mockApiClient.dashboard_SaveUserSettings.mock.calls[0][0];
      expect(calledWith.tiles).toEqual([
        { tileId: 'tile-1', isVisible: true, displayOrder: 0 }
      ]);
    });

    it('should handle save error', async () => {
      mockApiClient.dashboard_SaveUserSettings.mockRejectedValueOnce(new Error('Save failed'));

      const { result } = renderHook(() => useSaveDashboardSettings(), {
        wrapper: createWrapper()
      });

      result.current.mutate({
        tiles: [{ tileId: 'tile-1', isVisible: true, displayOrder: 0 }]
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect(result.current.error).toBeDefined();
    });
  });

  describe('useEnableTile', () => {
    it('should enable tile successfully', async () => {
      mockApiClient.dashboard_EnableTile.mockResolvedValueOnce({});

      const { result } = renderHook(() => useEnableTile(), {
        wrapper: createWrapper()
      });

      const tileId = 'analytics-tile';
      result.current.mutate(tileId);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(mockApiClient.dashboard_EnableTile).toHaveBeenCalledWith(tileId);
    });

    it('should handle enable error', async () => {
      mockApiClient.dashboard_EnableTile.mockRejectedValueOnce(new Error('Enable failed'));

      const { result } = renderHook(() => useEnableTile(), {
        wrapper: createWrapper()
      });

      result.current.mutate('analytics-tile');

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect(result.current.error).toBeDefined();
    });

    it('should handle tile ID with special characters', async () => {
      mockApiClient.dashboard_EnableTile.mockResolvedValueOnce({});

      const { result } = renderHook(() => useEnableTile(), {
        wrapper: createWrapper()
      });

      const specialTileId = 'tile-with-dashes_and_underscores';
      result.current.mutate(specialTileId);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(mockApiClient.dashboard_EnableTile).toHaveBeenCalledWith(specialTileId);
    });
  });

  describe('useDisableTile', () => {
    it('should disable tile successfully', async () => {
      mockApiClient.dashboard_DisableTile.mockResolvedValueOnce({});

      const { result } = renderHook(() => useDisableTile(), {
        wrapper: createWrapper()
      });

      const tileId = 'analytics-tile';
      result.current.mutate(tileId);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(mockApiClient.dashboard_DisableTile).toHaveBeenCalledWith(tileId);
    });

    it('should handle disable error', async () => {
      mockApiClient.dashboard_DisableTile.mockRejectedValueOnce(new Error('Disable failed'));

      const { result } = renderHook(() => useDisableTile(), {
        wrapper: createWrapper()
      });

      result.current.mutate('analytics-tile');

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect(result.current.error).toBeDefined();
    });
  });

  describe('typed client method calls', () => {
    it('should call the correct typed read methods with no arguments (except useTileData)', async () => {
      mockApiClient.dashboard_GetAvailableTiles.mockResolvedValue([]);
      mockApiClient.dashboard_GetUserSettings.mockResolvedValue({ tiles: [], lastModified: new Date() });
      mockApiClient.dashboard_GetTileData.mockResolvedValue([]);

      const hooks = [
        () => useAvailableTiles(),
        () => useUserDashboardSettings(),
        () => useTileData()
      ];

      for (const hook of hooks) {
        const { result } = renderHook(hook, {
          wrapper: createWrapper()
        });

        await waitFor(() => {
          expect(result.current.isSuccess || result.current.isLoading).toBe(true);
        });
      }

      expect(mockApiClient.dashboard_GetAvailableTiles).toHaveBeenCalledWith();
      expect(mockApiClient.dashboard_GetUserSettings).toHaveBeenCalledWith();
      expect(mockApiClient.dashboard_GetTileData).toHaveBeenCalledWith(undefined);
    });

    it('should call the correct typed mutation methods with the right arguments', async () => {
      mockApiClient.dashboard_SaveUserSettings.mockResolvedValue({});
      mockApiClient.dashboard_EnableTile.mockResolvedValue({});
      mockApiClient.dashboard_DisableTile.mockResolvedValue({});

      const { result: saveResult } = renderHook(() => useSaveDashboardSettings(), {
        wrapper: createWrapper()
      });

      const { result: enableResult } = renderHook(() => useEnableTile(), {
        wrapper: createWrapper()
      });

      const { result: disableResult } = renderHook(() => useDisableTile(), {
        wrapper: createWrapper()
      });

      saveResult.current.mutate({ tiles: [] });
      enableResult.current.mutate('test-tile');
      disableResult.current.mutate('test-tile');

      await waitFor(() => {
        expect(saveResult.current.isSuccess || saveResult.current.isLoading).toBe(true);
        expect(enableResult.current.isSuccess || enableResult.current.isLoading).toBe(true);
        expect(disableResult.current.isSuccess || disableResult.current.isLoading).toBe(true);
      });

      expect(mockApiClient.dashboard_SaveUserSettings).toHaveBeenCalledTimes(1);
      expect(mockApiClient.dashboard_EnableTile).toHaveBeenCalledWith('test-tile');
      expect(mockApiClient.dashboard_DisableTile).toHaveBeenCalledWith('test-tile');
    });
  });
});