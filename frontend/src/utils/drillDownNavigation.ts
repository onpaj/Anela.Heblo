/**
 * Utility functions for drill-down navigation from dashboard tiles to filtered list views
 */

import { NavigateFunction } from 'react-router-dom';

// Type definitions for drill-down data
export interface DrillDownInfo {
  url: string;
  enabled: boolean;
  tooltip?: string;
}

export interface TileDataWithDrillDown {
  status?: string;
  data?: {
    count?: number;
    [key: string]: any;
  };
  error?: string;
  drillDown?: DrillDownInfo;
  [key: string]: any;
}

/**
 * Handles tile click navigation using the drillDown URL
 */
export const handleTileClick = (
  tileData: TileDataWithDrillDown,
  navigate: NavigateFunction
): void => {
  if (tileData.drillDown?.enabled && tileData.drillDown.url) {
    navigate(tileData.drillDown.url);
  }
};

/**
 * Checks if a tile is clickable (has drill-down enabled)
 */
export const isTileClickable = (tileData: TileDataWithDrillDown): boolean => {
  return tileData.drillDown?.enabled === true && Boolean(tileData.drillDown.url);
};

/**
 * Gets tooltip text for clickable tiles
 */
export const getTileTooltip = (tileData: TileDataWithDrillDown): string | undefined => {
  return isTileClickable(tileData) ? tileData.drillDown?.tooltip : undefined;
};

/**
 * Manual URL builders for specific transport box states (fallback if backend doesn't provide drillDown)
 */
export const createTransportBoxUrls = {
  byState: (state: string) => `/logistics/transport-boxes?state=${state}`,
  errorBoxes: () => `/logistics/transport-boxes?state=Error`,
  inTransitBoxes: () => `/logistics/transport-boxes?state=InTransit`,
  receivedBoxes: () => `/logistics/transport-boxes?state=Received`,
  activeBoxes: () => `/logistics/transport-boxes?state=ACTIVE`,
  allBoxes: () => `/logistics/transport-boxes`,
};

/**
 * Generic URL builder for creating filtered URLs
 */
export const createFilteredUrl = (baseUrl: string, filters: Record<string, any>): string => {
  const params = new URLSearchParams();
  
  Object.entries(filters).forEach(([key, value]) => {
    if (value !== null && value !== undefined && value !== '') {
      params.append(key, String(value));
    }
  });
  
  return params.toString() ? `${baseUrl}?${params.toString()}` : baseUrl;
};