// Ad performance is reported in Metabase only (ADR-007/008, spec 8). No ads dashboard exists yet
// (docs/architecture/metabase.md), so this points at the instance root. When the dashboard on the
// v_ads_* views is built, replace this with its URL.
export const METABASE_ADS_DASHBOARD_URL =
  'https://anelametabase-dwejace7e3hfadfd.germanywestcentral-01.azurewebsites.net';
