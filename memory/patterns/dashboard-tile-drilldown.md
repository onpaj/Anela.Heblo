# Dashboard tile drill-down: two shapes

As of 2026-06-02, the dashboard tile system supports **two** drill-down payload shapes. Use the route-key shape for any new tile.

## Preferred — route-key shape

Use `DashboardTileDrillDown` (backend: `Application/Features/Dashboard/Contracts/DashboardTileDrillDown.cs`; frontend mirror: `components/dashboard/drillDownRoutes.ts`).

- Backend emits `{ routeKey, enabled, parameters? }`. No URL strings.
- Add the route key to `DASHBOARD_DRILLDOWN_ROUTES` in `drillDownRoutes.ts` with either `type: 'react-router'` or `type: 'external'` (backend-mounted admin UI).
- Tile component calls `resolveDrillDown(data.drillDown)` and dispatches by `strategy`. Unknown / disabled / undefined → null → tile renders non-interactive.

Tiles using this shape: `DataQualityStatusTile`, `DqtYesterdayStatusTile`, `FailedJobsTile`.

## Legacy — filter shape

Used by `PurchaseOrdersInTransitTile`, `LowStockAlertTile`, `BackgroundTasksTile`, `CountTile`, `InventorySummaryTile`.

- Backend emits `{ filters, enabled, tooltip }`. No URL strings either.
- Frontend tile component hardcodes the base path and uses `createFilteredUrl(basePath, drillDown.filters)` (`frontend/src/utils/urlUtils.ts`).

Do **not** add new filter-shape tiles. If a new tile needs filter parameters in the URL, use the route-key shape with `parameters` and have the tile component build the filter URL from the resolver result.

## Future unification

A later pass should migrate filter-shape tiles onto the route-key shape (route key + parameters dict + frontend builds the URL from a registry entry). Out of scope for this iteration.
