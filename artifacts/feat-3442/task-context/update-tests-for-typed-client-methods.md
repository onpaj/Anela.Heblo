### task: update-tests-for-typed-client-methods

**Files:**
- Modify: `frontend/src/api/hooks/__tests__/useDashboard.test.tsx`

**Context:**

The current test file mocks `getAuthenticatedApiClient` to return `{ baseUrl, http: { fetch: mockFetch } }` and asserts on raw fetch URLs and payloads. After the hook rewrite in the previous task, the test must mock the six typed methods directly on the object returned by `getAuthenticatedApiClient`, following the same restructuring `useBankStatements.test.ts` underwent in commit `2e178ff`. The 403-fallback tests (new, not present in the current file) must throw a `SwaggerException`-shaped plain object (`{ status: 403 }`) from the mocked method to exercise the `isForbidden` catch path, per the design's structural-check rationale.

The rewritten test data (`mockTiles`, `mockSettings`, `mockTileData`) omits fields at the raw mock level to exercise mapping-function defaulting where useful, but this plan keeps them fully populated (matching current test data) since FR-7 only requires preserving existing test intent, not adding new mapping-default coverage — that is out of scope for this refactor.

- [ ] Step 1: Replace the mock setup block. Replace:
  ```typescript
  // Import the mocked client module
  import * as clientModule from '../../client';

  // Mock the API client
  const mockFetch = jest.fn();
  const mockApiClient = {
    baseUrl: 'http://localhost:5001',
    http: {
      fetch: mockFetch
    }
  };

  jest.mock('../../client');
  ```
  with:
  ```typescript
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
  ```

- [ ] Step 2: Update the `beforeEach` block. Replace:
  ```typescript
  describe('useDashboard hooks', () => {
    beforeEach(() => {
      jest.clearAllMocks();
      mockFetch.mockClear();

      // Set up the mock implementation
      (clientModule.getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockApiClient);
    });
  ```
  with:
  ```typescript
  describe('useDashboard hooks', () => {
    beforeEach(() => {
      jest.clearAllMocks();

      // Set up the mock implementation
      (clientModule.getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockApiClient);
    });
  ```

- [ ] Step 3: Rewrite the `useAvailableTiles` describe block. Replace:
  ```typescript
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
  ```
  with:
  ```typescript
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
  ```

- [ ] Step 4: Rewrite the `useUserDashboardSettings` describe block, adding a 403-fallback test. Replace:
  ```typescript
  describe('useUserDashboardSettings', () => {
    it('should fetch user dashboard settings successfully', async () => {
      const mockSettings = {
        tiles: [
          { tileId: 'tile-1', isVisible: true, displayOrder: 0 }
        ],
        lastModified: '2024-01-01T00:00:00Z'
      };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
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
  ```
  with:
  ```typescript
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
  ```

- [ ] Step 5: Rewrite the `useTileData` describe block, adding a 403-fallback test. Replace:
  ```typescript
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
        ok: true,
        status: 200,
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
  ```
  with:
  ```typescript
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
  ```

- [ ] Step 6: Rewrite the `useSaveDashboardSettings` describe block. Replace:
  ```typescript
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
  ```
  with:
  ```typescript
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
  ```

- [ ] Step 7: Rewrite the `useEnableTile` describe block. Replace:
  ```typescript
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
  ```
  with:
  ```typescript
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
  ```

- [ ] Step 8: Rewrite the `useDisableTile` describe block. Replace:
  ```typescript
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
  ```
  with:
  ```typescript
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
  ```

- [ ] Step 9: Replace the `API URL construction` describe block (URLs are no longer observable at this layer since calls go through typed methods, not raw fetch) with method-call assertions covering the same six hooks. Replace:
  ```typescript
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
  ```
  with:
  ```typescript
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
  ```

- [ ] Step 10: Confirm no remaining references to the deleted raw-fetch mock plumbing:
  ```
  grep -n "mockFetch\|baseUrl\|http\.fetch" frontend/src/api/hooks/__tests__/useDashboard.test.tsx
  ```
  Expected: no matches.

- [ ] Step 11: Run the tests to confirm they pass:
  ```
  cd frontend && npx jest src/api/hooks/__tests__/useDashboard.test.tsx --no-coverage
  ```
  Fix any failures. If a test fails because `result.current.isSuccess` is false, check that the relevant `mockApiClient.dashboard_*` method is configured with `mockResolvedValueOnce`/`mockResolvedValue` in that test (not left as an unconfigured `jest.fn()`, which resolves to `undefined` and may fail hook-internal `.map(...)` calls).
