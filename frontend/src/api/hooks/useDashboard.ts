import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

export interface DashboardTile {
  tileId: string;
  title: string;
  description: string;
  size: 'Small' | 'Medium' | 'Large';
  category: string;
  defaultEnabled: boolean;
  autoShow: boolean;
  requiredPermissions: string[];
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

// Hook to get all available tiles
export const useAvailableTiles = () => {
  return useQuery({
    queryKey: ['dashboard', 'tiles'],
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

// Hook to get user dashboard settings
export const useUserDashboardSettings = () => {
  return useQuery({
    queryKey: ['dashboard', 'settings'],
    queryFn: async (): Promise<UserDashboardSettings> => {
      const apiClient = await getAuthenticatedApiClient();
      const relativeUrl = `/api/dashboard/settings`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
      });
      return response.json();
    },
  });
};

// Hook to get tile data
export const useTileData = () => {
  return useQuery({
    queryKey: ['dashboard', 'data'],
    queryFn: async (): Promise<DashboardTile[]> => {
      const apiClient = await getAuthenticatedApiClient();
      const relativeUrl = `/api/dashboard/data`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
      });
      return response.json();
    },
    refetchInterval: 30000, // Refresh every 30 seconds
  });
};

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
      queryClient.invalidateQueries({ queryKey: ['dashboard', 'settings'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard', 'data'] });
    },
  });
};

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
      queryClient.invalidateQueries({ queryKey: ['dashboard', 'settings'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard', 'data'] });
    },
  });
};

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
      queryClient.invalidateQueries({ queryKey: ['dashboard', 'settings'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard', 'data'] });
    },
  });
};