/**
 * Utility functions for URL construction with filter parameters
 */

/**
 * Creates a URL with query parameters from filter object
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

/**
 * Interface for drill-down data from backend
 */
export interface DrillDownInfo {
  filters?: Record<string, any>;
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
 * Checks if a tile is clickable (has drill-down enabled)
 */
export const isTileClickable = (tileData: TileDataWithDrillDown): boolean => {
  return tileData.drillDown?.enabled === true && Boolean(tileData.drillDown.filters);
};

/**
 * Gets tooltip text for clickable tiles
 */
export const getTileTooltip = (tileData: TileDataWithDrillDown): string | undefined => {
  return isTileClickable(tileData) ? tileData.drillDown?.tooltip : undefined;
};