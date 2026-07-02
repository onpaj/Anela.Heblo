# Dashboard Hooks — Remove Manual DTOs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Replace the untyped `(apiClient as any).baseUrl` / `(apiClient as any).http.fetch` pattern in all six hooks of `useDashboard.ts` with typed generated client methods, reconciling local interfaces with generated DTOs via mapping functions, and preserving the two 403-fallback behaviors via a structural `isForbidden` check.

**Architecture:** Pure frontend refactor — no backend changes. Only `frontend/src/api/hooks/useDashboard.ts` and its test file change. All required generated types and methods already exist in `api-client.ts`. This mirrors the identical migration already merged for `useBankStatements.ts` in commit `2e178ff`.

**Tech Stack:** TypeScript, React Query, NSwag-generated client

---

### task: replace-raw-fetch-with-typed-client-and-mapping-functions

**Files:**
- Modify: `frontend/src/api/hooks/useDashboard.ts`

**Context:**

The file currently defines four local interfaces (`DashboardTile`, `UserDashboardTile`, `UserDashboardSettings`, `SaveDashboardSettingsRequest`) and six hooks that all reach into `(apiClient as any).baseUrl` + `(apiClient as any).http.fetch(...)` instead of calling the generated client's typed methods (`dashboard_GetAvailableTiles`, `dashboard_GetUserSettings`, `dashboard_SaveUserSettings`, `dashboard_GetTileData`, `dashboard_EnableTile`, `dashboard_DisableTile`), all already present in `frontend/src/api/generated/api-client.ts`.

Per the design doc, the local interfaces are **kept** (not deleted) with required fields, because `DashboardSettings.tsx` and `Dashboard.tsx` consume `tile.title`, `tile.size`, etc. directly with no `undefined` handling, while the generated DTOs (`DashboardTileDto`, `UserDashboardSettingsDto`, `UserDashboardTileDto`) mark every field optional. Two small mapping functions (`toDashboardTile`, `toUserDashboardSettings`) reconcile this at the hook boundary. `DashboardTile.size` is widened from `'Small' | 'Medium' | 'Large'` to plain `string` (confirmed safe: the only consumer, `DashboardTile.tsx`'s `getSizeClasses()`, is a `switch` with a `default` case, not an exhaustive union switch).

The two mutation-only-adjacent read hooks needing 403-fallback (`useUserDashboardSettings`, `useTileData`) use a structural `isForbidden(error)` check rather than `instanceof SwaggerException`, per the design's explicit rationale (robust across the test-mock boundary where thrown errors may be plain objects, not real class instances).

The three mutation hooks (`useSaveDashboardSettings`, `useEnableTile`, `useDisableTile`) resolve to `Promise<FileResponse>` post-refactor (confirmed unconsumed by both callers — `DashboardSettings.tsx`'s `enableTile.mutateAsync(tile.tileId)` and `Dashboard.tsx`'s `saveDashboardSettings.mutateAsync({ tiles: updatedTiles })` both discard the resolved value).

- [ ] Step 1: Replace the import line and add generated-type imports plus mapping/helper functions. Replace:
  ```typescript
  import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
  import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
  ```
  with:
  ```typescript
  import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
  import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
  import {
    DashboardTileDto,
    UserDashboardSettingsDto,
    SaveUserSettingsRequest,
    UserDashboardTileDto,
  } from '../generated/api-client';
  ```

- [ ] Step 2: Widen `DashboardTile.size` from the literal union to `string`. Replace:
  ```typescript
  export interface DashboardTile {
    tileId: string;
    title: string;
    description: string;
    size: 'Small' | 'Medium' | 'Large';
    category: string;
    defaultEnabled: boolean;
    autoShow: boolean;
    requiredPermissions: string[];
    isUnauthorized?: boolean;
    data?: any;
  }
  ```
  with:
  ```typescript
  export interface DashboardTile {
    tileId: string;
    title: string;
    description: string;
    size: string;
    category: string;
    defaultEnabled: boolean;
    autoShow: boolean;
    requiredPermissions: string[];
    isUnauthorized?: boolean;
    data?: any;
  }
  ```
  Leave `UserDashboardTile`, `UserDashboardSettings`, `SaveDashboardSettingsRequest` interfaces unchanged.

- [ ] Step 3: Add the two mapping functions and the `isForbidden` helper immediately after the interface block (after `SaveDashboardSettingsRequest`'s closing brace, before the `useAvailableTiles` hook):
  ```typescript
  const toDashboardTile = (dto: DashboardTileDto): DashboardTile => ({
    tileId: dto.tileId ?? '',
    title: dto.title ?? '',
    description: dto.description ?? '',
    size: dto.size ?? 'Medium',
    category: dto.category ?? '',
    defaultEnabled: dto.defaultEnabled ?? false,
    autoShow: dto.autoShow ?? false,
    requiredPermissions: dto.requiredPermissions ?? [],
    isUnauthorized: dto.isUnauthorized,
    data: dto.data,
  });

  const toUserDashboardSettings = (dto: UserDashboardSettingsDto): UserDashboardSettings => ({
    tiles: (dto.tiles ?? []).map((t) => ({
      tileId: t.tileId ?? '',
      isVisible: t.isVisible ?? false,
      displayOrder: t.displayOrder ?? 0,
    })),
    lastModified: dto.lastModified?.toISOString() ?? new Date().toISOString(),
  });

  const isForbidden = (error: unknown): boolean =>
    typeof error === 'object' &&
    error !== null &&
    'status' in error &&
    (error as { status?: number }).status === 403;
  ```

- [ ] Step 4: Replace the `useAvailableTiles` hook body. Replace:
  ```typescript
  // Hook to get all available tiles
  export const useAvailableTiles = () => {
    return useQuery({
      queryKey: [...QUERY_KEYS.dashboard, 'tiles'],
      queryFn: async (): Promise<DashboardTile[]> => {
        const apiClient = await getAuthenticatedApiClient();
        const relativeUrl = `/api/dashboard/tiles`;
        const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
        const response = await (apiClient as any).http.fetch(fullUrl, {
          method: 'GET',
        });
        return response.json();
      },
    });
  };
  ```
  with:
  ```typescript
  // Hook to get all available tiles
  export const useAvailableTiles = () => {
    return useQuery({
      queryKey: [...QUERY_KEYS.dashboard, 'tiles'],
      queryFn: async (): Promise<DashboardTile[]> => {
        const apiClient = getAuthenticatedApiClient();
        const tiles = await apiClient.dashboard_GetAvailableTiles();
        return tiles.map(toDashboardTile);
      },
    });
  };
  ```
  Note: `getAuthenticatedApiClient()` is synchronous — the previous `await` on it is removed (this was the one outlier hook that had it).

- [ ] Step 5: Replace the `useUserDashboardSettings` hook body. Replace:
  ```typescript
  // Hook to get user dashboard settings
  export const useUserDashboardSettings = () => {
    return useQuery({
      queryKey: [...QUERY_KEYS.dashboard, 'settings'],
      queryFn: async (): Promise<UserDashboardSettings> => {
        const apiClient = getAuthenticatedApiClient(false);
        const relativeUrl = `/api/dashboard/settings`;
        const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
        const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });
        if (response.status === 403) return { tiles: [], lastModified: new Date().toISOString() };
        if (!response.ok) throw new Error(`API Call Error (${response.status})`);
        return response.json();
      },
    });
  };
  ```
  with:
  ```typescript
  // Hook to get user dashboard settings
  export const useUserDashboardSettings = () => {
    return useQuery({
      queryKey: [...QUERY_KEYS.dashboard, 'settings'],
      queryFn: async (): Promise<UserDashboardSettings> => {
        const apiClient = getAuthenticatedApiClient(false);
        try {
          const settings = await apiClient.dashboard_GetUserSettings();
          return toUserDashboardSettings(settings);
        } catch (error) {
          if (isForbidden(error)) {
            return { tiles: [], lastModified: new Date().toISOString() };
          }
          throw error;
        }
      },
    });
  };
  ```

- [ ] Step 6: Replace the `useTileData` hook body. Replace:
  ```typescript
  // Hook to get tile data
  export const useTileData = () => {
    return useQuery({
      queryKey: [...QUERY_KEYS.dashboard, 'data'],
      queryFn: async (): Promise<DashboardTile[]> => {
        const apiClient = getAuthenticatedApiClient(false);
        const relativeUrl = `/api/dashboard/data`;
        const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
        const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });
        if (response.status === 403) return [];
        if (!response.ok) throw new Error(`API Call Error (${response.status})`);
        return response.json();
      },
      refetchInterval: 30000,
    });
  };
  ```
  with:
  ```typescript
  // Hook to get tile data
  export const useTileData = () => {
    return useQuery({
      queryKey: [...QUERY_KEYS.dashboard, 'data'],
      queryFn: async (): Promise<DashboardTile[]> => {
        const apiClient = getAuthenticatedApiClient(false);
        try {
          const tiles = await apiClient.dashboard_GetTileData(undefined);
          return tiles.map(toDashboardTile);
        } catch (error) {
          if (isForbidden(error)) {
            return [];
          }
          throw error;
        }
      },
      refetchInterval: 30000,
    });
  };
  ```

- [ ] Step 7: Replace the `useSaveDashboardSettings` hook body. Replace:
  ```typescript
  // Hook to save user dashboard settings
  export const useSaveDashboardSettings = () => {
    const queryClient = useQueryClient();

    return useMutation({
      mutationFn: async (settings: SaveDashboardSettingsRequest) => {
        const apiClient = await getAuthenticatedApiClient();
        const relativeUrl = `/api/dashboard/settings`;
        const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

        await (apiClient as any).http.fetch(fullUrl, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(settings),
        });
      },
      onSuccess: () => {
        // Invalidate both settings and data queries to refetch
        queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.dashboard, 'settings'] });
        queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.dashboard, 'data'] });
      },
    });
  };
  ```
  with:
  ```typescript
  // Hook to save user dashboard settings
  export const useSaveDashboardSettings = () => {
    const queryClient = useQueryClient();

    return useMutation({
      mutationFn: async (settings: SaveDashboardSettingsRequest) => {
        const apiClient = getAuthenticatedApiClient();
        const request = new SaveUserSettingsRequest({
          tiles: settings.tiles.map((t) => new UserDashboardTileDto(t)),
        });
        return apiClient.dashboard_SaveUserSettings(request);
      },
      onSuccess: () => {
        // Invalidate both settings and data queries to refetch
        queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.dashboard, 'settings'] });
        queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.dashboard, 'data'] });
      },
    });
  };
  ```
  Note: the redundant `await` on `getAuthenticatedApiClient()` is removed; the boolean argument (and therefore `showErrorToasts`, defaulting to `true`) is left unspecified exactly as before, so this hook continues to show global error toasts on failure.

- [ ] Step 8: Replace the `useEnableTile` hook body. Replace:
  ```typescript
  // Hook to enable a tile
  export const useEnableTile = () => {
    const queryClient = useQueryClient();

    return useMutation({
      mutationFn: async (tileId: string) => {
        const apiClient = await getAuthenticatedApiClient();
        const relativeUrl = `/api/dashboard/tiles/${tileId}/enable`;
        const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

        await (apiClient as any).http.fetch(fullUrl, {
          method: 'POST',
        });
      },
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.dashboard, 'settings'] });
        queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.dashboard, 'data'] });
      },
    });
  };
  ```
  with:
  ```typescript
  // Hook to enable a tile
  export const useEnableTile = () => {
    const queryClient = useQueryClient();

    return useMutation({
      mutationFn: async (tileId: string) => {
        const apiClient = getAuthenticatedApiClient();
        return apiClient.dashboard_EnableTile(tileId);
      },
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.dashboard, 'settings'] });
        queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.dashboard, 'data'] });
      },
    });
  };
  ```

- [ ] Step 9: Replace the `useDisableTile` hook body. Replace:
  ```typescript
  // Hook to disable a tile
  export const useDisableTile = () => {
    const queryClient = useQueryClient();

    return useMutation({
      mutationFn: async (tileId: string) => {
        const apiClient = await getAuthenticatedApiClient();
        const relativeUrl = `/api/dashboard/tiles/${tileId}/disable`;
        const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

        await (apiClient as any).http.fetch(fullUrl, {
          method: 'POST',
        });
      },
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.dashboard, 'settings'] });
        queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.dashboard, 'data'] });
      },
    });
  };
  ```
  with:
  ```typescript
  // Hook to disable a tile
  export const useDisableTile = () => {
    const queryClient = useQueryClient();

    return useMutation({
      mutationFn: async (tileId: string) => {
        const apiClient = getAuthenticatedApiClient();
        return apiClient.dashboard_DisableTile(tileId);
      },
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.dashboard, 'settings'] });
        queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.dashboard, 'data'] });
      },
    });
  };
  ```

- [ ] Step 10: Verify the file compiles without errors. Run:
  ```
  cd frontend && npx tsc --noEmit
  ```
  Fix any type errors before proceeding. Do not modify `frontend/src/api/generated/api-client.ts`. Expected: no errors from `useDashboard.ts` itself. If a type error surfaces in `DashboardSettings.tsx`, `Dashboard.tsx`, `DashboardGrid.tsx`, or `DashboardTile.tsx`, it means a caller genuinely needs a follow-up change — do not silently cast to `any`; instead confirm the mismatch against `toDashboardTile`/`toUserDashboardSettings` first, since both mapping functions guarantee non-optional required fields matching the pre-refactor local interfaces exactly.

- [ ] Step 11: Confirm no `as any` remains in the file:
  ```
  grep -n "as any" frontend/src/api/hooks/useDashboard.ts
  ```
  Expected: no matches (the pre-existing `data?: any` field on `DashboardTile`/`toDashboardTile` is a type annotation, not an `as any` cast, and is unaffected — only `as any` casts are in scope for removal).

---

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

---

### task: build-and-lint-verification

**Files:**
- No file changes — verification only

**Context:**

The project rules require `npm run build` and `npm run lint` to pass before a task is declared done. The TypeScript compile check in task 1 step 10 catches type errors early; this task runs the full build to catch any issues missed by `tsc --noEmit` alone (e.g. Vite/CRA transform errors) and the ESLint pass to catch any `as any` accidentally left behind. It also re-checks the two consumer components and their existing tests, since the arch review and design flagged `DashboardSettings.tsx` / `Dashboard.tsx` / `DashboardGrid.tsx` / `DashboardTile.tsx` as requiring no changes but this must be verified, not assumed.

- [ ] Step 1: Run the frontend build:
  ```
  cd frontend && npm run build
  ```
  Resolve any errors. Do not modify `frontend/src/api/generated/api-client.ts`. If a type error surfaces from consumer components (`DashboardSettings.tsx`, `Dashboard.tsx`, `DashboardGrid.tsx`, `DashboardTile.tsx`), the fix belongs in the mapping functions in `useDashboard.ts` (ensuring all required local-interface fields stay non-optional), not in the consumer files, per the design's Decision 1.

- [ ] Step 2: Run the linter:
  ```
  cd frontend && npm run lint
  ```
  Resolve any `@typescript-eslint/no-explicit-any` violations introduced by this change. Do not introduce new `as any` casts. The pre-existing `data?: any` field on `DashboardTile` (and the corresponding `dto.data` passthrough in `toDashboardTile`) is not an `as any` cast and predates this refactor — leave it unchanged.

- [ ] Step 3: Run the full existing test suites for the two consumer components to confirm no regressions from the widened `DashboardTile.size` type or the `Promise<FileResponse>` mutation return type:
  ```
  cd frontend && npx jest src/components/dashboard/__tests__/DashboardSettings.test.tsx src/components/dashboard/__tests__/DashboardTile.test.tsx src/components/dashboard/__tests__/DashboardGrid.test.tsx src/components/pages/__tests__/Dashboard.test.tsx --no-coverage
  ```
  Expected: all pass with no changes required to these test files. If any fail, diagnose whether the failure stems from the `size` widening (unlikely, since `DashboardTile.tsx`'s `getSizeClasses()` has a `default` case) or from a mutation return-type mismatch (unlikely, since neither caller consumes the mutation's resolved value) before making any test-file edits — do not silently adjust assertions to force a pass.

- [ ] Step 4: Run the full dashboard hook test file one more time alongside the two verification steps above to confirm the whole slice is green:
  ```
  cd frontend && npx jest src/api/hooks/__tests__/useDashboard.test.tsx --no-coverage
  ```
