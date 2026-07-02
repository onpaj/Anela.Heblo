import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import {
  DashboardTileDto,
  UserDashboardSettingsDto,
  SaveUserSettingsRequest,
  UserDashboardTileDto,
} from '../generated/api-client';

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

export interface UserDashboardTile {
  tileId: string;
  isVisible: boolean;
  displayOrder: number;
}

export interface UserDashboardSettings {
  tiles: UserDashboardTile[];
  lastModified: string;
}

export interface SaveDashboardSettingsRequest {
  tiles: UserDashboardTile[];
}

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