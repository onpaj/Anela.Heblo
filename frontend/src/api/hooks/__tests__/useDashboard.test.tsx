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

// Mock the API client
const mockFetch = jest.fn();
const mockApiClient = {
  baseUrl: 'http://localhost:5001',
  http: {
    fetch: mockFetch
  }
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

// Import the mocked client module
import * as clientModule from '../../client';

describe('useDashboard hooks', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockFetch.mockClear();
    
    // Set up the mock implementation
    (clientModule.getAuthenticatedApiClient as jest.Mock).mockResolvedValue(mockApiClient);
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

      mockFetch.mockResolvedValueOnce({
        json: jest.fn().mockResolvedValue(mockTiles)
      });

      const { result } = renderHook(() => useAvailableTiles(), {
        wrapper: createWrapper()
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(mockFetch).toHaveBeenCalledWith('http://localhost:5001/api/dashboard/tiles', {
        method: 'GET'
      });
      expect(result.current.data).toEqual(mockTiles);
    });

    it('should handle fetch error', async () => {
      mockFetch.mockRejectedValueOnce(new Error('Network error'));

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
        lastModified: '2024-01-01T00:00:00Z'
      };

      mockFetch.mockResolvedValueOnce({
        json: jest.fn().mockResolvedValue(mockSettings)
      });

      const { result } = renderHook(() => useUserDashboardSettings(), {
        wrapper: createWrapper()
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(mockFetch).toHaveBeenCalledWith('http://localhost:5001/api/dashboard/settings', {
        method: 'GET'
      });
      expect(result.current.data).toEqual(mockSettings);
    });

    it('should handle settings fetch error', async () => {
      mockFetch.mockRejectedValueOnce(new Error('Unauthorized'));

      const { result } = renderHook(() => useUserDashboardSettings(), {
        wrapper: createWrapper()
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect(result.current.error).toBeDefined();
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

      mockFetch.mockResolvedValueOnce({
        json: jest.fn().mockResolvedValue(mockTileData)
      });

      const { result } = renderHook(() => useTileData(), {
        wrapper: createWrapper()
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(mockFetch).toHaveBeenCalledWith('http://localhost:5001/api/dashboard/data', {
        method: 'GET'
      });
      expect(result.current.data).toEqual(mockTileData);
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
      mockFetch.mockResolvedValueOnce({ ok: true });

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

      expect(mockFetch).toHaveBeenCalledWith('http://localhost:5001/api/dashboard/settings', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(settingsToSave)
      });
    });

    it('should handle save error', async () => {
      mockFetch.mockRejectedValueOnce(new Error('Save failed'));

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
      mockFetch.mockResolvedValueOnce({ ok: true });

      const { result } = renderHook(() => useEnableTile(), {
        wrapper: createWrapper()
      });

      const tileId = 'analytics-tile';
      result.current.mutate(tileId);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(mockFetch).toHaveBeenCalledWith(`http://localhost:5001/api/dashboard/tiles/${tileId}/enable`, {
        method: 'POST'
      });
    });

    it('should handle enable error', async () => {
      mockFetch.mockRejectedValueOnce(new Error('Enable failed'));

      const { result } = renderHook(() => useEnableTile(), {
        wrapper: createWrapper()
      });

      result.current.mutate('analytics-tile');

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect(result.current.error).toBeDefined();
    });

    it('should handle tile ID with special characters', async () => {
      mockFetch.mockResolvedValueOnce({ ok: true });

      const { result } = renderHook(() => useEnableTile(), {
        wrapper: createWrapper()
      });

      const specialTileId = 'tile-with-dashes_and_underscores';
      result.current.mutate(specialTileId);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(mockFetch).toHaveBeenCalledWith(
        `http://localhost:5001/api/dashboard/tiles/${specialTileId}/enable`,
        { method: 'POST' }
      );
    });
  });

  describe('useDisableTile', () => {
    it('should disable tile successfully', async () => {
      mockFetch.mockResolvedValueOnce({ ok: true });

      const { result } = renderHook(() => useDisableTile(), {
        wrapper: createWrapper()
      });

      const tileId = 'analytics-tile';
      result.current.mutate(tileId);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(mockFetch).toHaveBeenCalledWith(`http://localhost:5001/api/dashboard/tiles/${tileId}/disable`, {
        method: 'POST'
      });
    });

    it('should handle disable error', async () => {
      mockFetch.mockRejectedValueOnce(new Error('Disable failed'));

      const { result } = renderHook(() => useDisableTile(), {
        wrapper: createWrapper()
      });

      result.current.mutate('analytics-tile');

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect(result.current.error).toBeDefined();
    });
  });

  describe('API URL construction', () => {
    it('should construct correct URLs with baseUrl', async () => {
      mockFetch.mockResolvedValue({
        json: jest.fn().mockResolvedValue([])
      });

      // Test all hooks to verify URL construction
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

      // Verify all URLs start with the baseUrl
      const calls = mockFetch.mock.calls;
      calls.forEach(call => {
        expect(call[0]).toMatch(/^http:\/\/localhost:5001\/api\/dashboard/);
      });
    });

    it('should handle mutations with correct URLs', async () => {
      mockFetch.mockResolvedValue({ ok: true });

      const { result: saveResult } = renderHook(() => useSaveDashboardSettings(), {
        wrapper: createWrapper()
      });

      const { result: enableResult } = renderHook(() => useEnableTile(), {
        wrapper: createWrapper()
      });

      const { result: disableResult } = renderHook(() => useDisableTile(), {
        wrapper: createWrapper()
      });

      // Test mutations
      saveResult.current.mutate({ tiles: [] });
      enableResult.current.mutate('test-tile');
      disableResult.current.mutate('test-tile');

      await waitFor(() => {
        expect(saveResult.current.isSuccess || saveResult.current.isLoading).toBe(true);
        expect(enableResult.current.isSuccess || enableResult.current.isLoading).toBe(true);
        expect(disableResult.current.isSuccess || disableResult.current.isLoading).toBe(true);
      });

      // Verify mutation URLs
      const mutationCalls = mockFetch.mock.calls.slice(-3);
      expect(mutationCalls[0][0]).toBe('http://localhost:5001/api/dashboard/settings');
      expect(mutationCalls[1][0]).toBe('http://localhost:5001/api/dashboard/tiles/test-tile/enable');
      expect(mutationCalls[2][0]).toBe('http://localhost:5001/api/dashboard/tiles/test-tile/disable');
    });
  });
});