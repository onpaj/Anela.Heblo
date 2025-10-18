/**
 * Utility functions for drill-down navigation from dashboard tiles to filtered list views
 */

import { NavigateFunction } from 'react-router-dom';

// Type definitions for drill-down data
export interface DrillDownInfo {
  url?: string; // For backward compatibility
  filters?: Record<string, any>; // New filter-based approach
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
 * Constructs URL from filter parameters for transport boxes
 */
const constructTransportBoxUrl = (filters: Record<string, any>): string => {
  const baseUrl = '/logistics/transport-boxes';
  
  if (!filters || Object.keys(filters).length === 0) {
    return baseUrl;
  }
  
  return createFilteredUrl(baseUrl, filters);
};

/**
 * Maps filter data to appropriate URLs based on tile category and filter content
 */
const constructUrlFromFilters = (filters: Record<string, any>, tileCategory?: string, tileTitle?: string): string => {
  // Transport boxes - logistics category with state filter
  if (filters.state && (tileCategory === 'Warehouse' || tileTitle?.includes('Boxy'))) {
    return constructTransportBoxUrl(filters);
  }
  
  // Manufacturing inventory
  if (filters.type === 'Material' && tileCategory === 'Warehouse') {
    return createFilteredUrl('/manufacturing/inventory', filters);
  }
  
  // Logistics inventory (products)
  if (filters.type === 'Product' && tileCategory === 'Warehouse') {
    return createFilteredUrl('/logistics/inventory', filters);
  }
  
  // Manufacturing orders
  if (tileCategory === 'Manufacture') {
    return createFilteredUrl('/manufacturing/orders', filters);
  }
  
  // Purchase orders
  if (filters.state === 'InTransit' && tileCategory === 'Purchase') {
    return createFilteredUrl('/purchase/orders', filters);
  }
  
  // Purchase stock analysis  
  if (filters.filter === 'kriticke' && tileCategory === 'Purchase') {
    return createFilteredUrl('/purchase/stock-analysis', filters);
  }
  
  // Gift package manufacturing
  if (filters.filter === 'Kriticke' && tileTitle?.includes('balíčky')) {
    return createFilteredUrl('/logistics/gift-package-manufacturing', filters);
  }
  
  // Analytics - invoice import statistics
  if (tileCategory === 'Finance' && tileTitle?.includes('Faktury')) {
    return '/automation/invoice-import-statistics';
  }
  
  // Analytics - bank statements
  if (tileCategory === 'Finance' && tileTitle?.includes('Bankovní')) {
    return '/finance/bank-statements';
  }
  
  // System - background tasks
  if (tileCategory === 'System') {
    return '/automation/background-tasks';
  }
  
  // Default fallback to transport boxes for backward compatibility
  return constructTransportBoxUrl(filters);
};

/**
 * Handles tile click navigation using the drillDown URL or filters
 */
export const handleTileClick = (
  tileData: TileDataWithDrillDown,
  navigate: NavigateFunction,
  tileCategory?: string,
  tileTitle?: string
): void => {
  if (!tileData.drillDown?.enabled) {
    return;
  }

  let targetUrl: string | undefined;

  // Prefer new filter-based approach
  if (tileData.drillDown.filters) {
    targetUrl = constructUrlFromFilters(tileData.drillDown.filters, tileCategory, tileTitle);
  }
  // Fall back to legacy URL approach for backward compatibility
  else if (tileData.drillDown.url) {
    targetUrl = tileData.drillDown.url;
  }

  if (targetUrl) {
    navigate(targetUrl);
  }
};

/**
 * Checks if a tile is clickable (has drill-down enabled)
 */
export const isTileClickable = (tileData: TileDataWithDrillDown): boolean => {
  return tileData.drillDown?.enabled === true && 
         (Boolean(tileData.drillDown.filters) || Boolean(tileData.drillDown.url));
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