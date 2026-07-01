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
