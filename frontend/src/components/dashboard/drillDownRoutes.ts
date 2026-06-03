import { getConfig } from '../../config/runtimeConfig';

export type DashboardDrillDownRouteKey = 'dataQuality' | 'hangfireFailedJobs';

export type DrillDownTarget =
  | { type: 'react-router'; path: string }
  | { type: 'external'; path: string };

export interface DashboardTileDrillDown {
  routeKey: string;
  enabled: boolean;
  parameters?: Record<string, string>;
}

export interface DrillDownResolution {
  url: string;
  strategy: DrillDownTarget['type'];
}

// Closed set: adding a backend route key without a frontend entry is a build error
// at the consuming tile component (the union type narrows away unknowns).
export const DASHBOARD_DRILLDOWN_ROUTES: Record<DashboardDrillDownRouteKey, DrillDownTarget> = {
  dataQuality: { type: 'react-router', path: '/automation/data-quality' },
  hangfireFailedJobs: { type: 'external', path: '/hangfire/jobs/failed' },
};

const isKnownRouteKey = (key: string): key is DashboardDrillDownRouteKey =>
  Object.prototype.hasOwnProperty.call(DASHBOARD_DRILLDOWN_ROUTES, key);

export function resolveDrillDown(
  drillDown: DashboardTileDrillDown | undefined,
): DrillDownResolution | null {
  if (!drillDown || !drillDown.enabled) {
    return null;
  }

  if (!isKnownRouteKey(drillDown.routeKey)) {
    // Backend deployed ahead of frontend: leave the tile visible but non-interactive.
    console.warn(`[dashboard] Unknown drill-down route key: ${drillDown.routeKey}`);
    return null;
  }

  const target = DASHBOARD_DRILLDOWN_ROUTES[drillDown.routeKey];

  if (target.type === 'external') {
    const { apiUrl } = getConfig();
    return { url: `${apiUrl}${target.path}`, strategy: 'external' };
  }

  return { url: target.path, strategy: 'react-router' };
}
