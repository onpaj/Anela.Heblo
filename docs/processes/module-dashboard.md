---
process: module-dashboard
kind: module
module: dashboard
summary: The personal home page (Dashboard) — a per-user, reorderable grid of tiles that each show one live number or short list from another Heblo module.
owns: []
verified_at: "5e993f9e2"
related:
  - calc-dashboard-tiles
  - sync-weather-forecast
---

# Dashboard

## Purpose
The Dashboard is the first page every user sees in Heblo (route `/`). It answers "what needs my
attention today?" at a glance: how many transport boxes are in transit, what is being produced
today, which products are running out on the e-shop, whether last night's invoice check passed,
and so on. Each piece is a **tile** (dlaždice). Every user picks which tiles to see and in what
order; the choice is stored per user.

The module itself owns no business data. It only owns (a) each user's tile layout and (b) the
framework that loads tiles. The numbers come from the module that owns the tile — the Dashboard
module calls the tile and passes the result through. How each tile's number is computed is in
`calc-dashboard-tiles`.

## Users & screens
- **All signed-in users** — page `/` (`frontend/src/components/pages/Dashboard.tsx`). Tiles are
  drag-and-drop reorderable; the settings panel (`DashboardSettings.tsx`) lists every available
  tile with a toggle. Tile data is refetched every 30 s while the page is open
  (`useTileData`, `refetchInterval: 30000`).
- Clicking a tile drills down to the owning page (e.g. `/logistics/transport-boxes`,
  `/manufacturing/inventory`, `/purchase/stock-analysis`, `/baleni`, `/automation/data-quality`,
  `/products/pricing`, Hangfire `/hangfire/jobs/failed`). Targets are hard-coded in the frontend
  (`tiles/tileRegistry.tsx`, `drillDownRoutes.ts`).
- API (`DashboardController`, `api/dashboard`): `GET tiles` (catalogue), `GET settings`,
  `POST settings` (bulk save of order/visibility), `GET data` (all visible tiles with data),
  `POST tiles/{tileId}/enable`, `POST tiles/{tileId}/disable`. No MCP tool.

### Tile catalogue
27 tiles are registered at startup. "Auto" = appears for every user automatically
(`DefaultEnabled && AutoShow`); "Opt-in" = user must switch it on in settings. No tile requires
a permission today (`RequiredPermissions` is empty everywhere).

| Tile id | Czech title | Owner module | Shows | Source | Show |
|---|---|---|---|---|---|
| `backgroundtaskstatus` | Stav background tasků | Xcc (system) | completed / total BackgroundRefresh tasks | in-process `BackgroundRefreshTaskRegistry` | Auto |
| `failedjobs` | Failed background jobs | BackgroundJobs | count of Hangfire jobs in Failed state | Hangfire monitoring API | Opt-in |
| `todayproduction` | Dnešní výroba (date) | Manufacture | manufacture orders planned for today, top 5 | `ManufactureOrders` table | Auto |
| `nextdayproduction` | Zítřejší výroba (date) | Manufacture | same for next working day (Mon–Fri) | `ManufactureOrders` table | Auto |
| `manualactionrequired` | Výrobní příkazy | Manufacture | orders flagged `ManualActionRequired` | `ManufactureOrders` table | Auto |
| `manufactureconditions` | Podmínky ve výrobně | Manufacture | inside/outside temperature + humidity | Home Assistant `/api/states/{entity}` (5 min cache) | Auto |
| `weatherforecast` | Předpověď počasí | weather-forecast | hottest Czech city per day, 7 days | Open-Meteo (see `sync-weather-forecast`) | Opt-in |
| `intransitboxes` | Boxy v přepravě | Logistics | transport boxes in state InTransit | `TransportBoxes` table | Auto |
| `receivedboxes` | Boxy přijaté | Logistics | transport boxes in state Received | `TransportBoxes` table | Auto |
| `errorboxes` | Boxy v chybě | Logistics | transport boxes in state Error | `TransportBoxes` table | Auto |
| `criticalgiftpackages` | Kritické balíčky | Logistics | gift packages with severity Critical | gift-package manufacture calculation | Auto |
| `lowstockalert` | K přeskladnění (S/R/T) | Catalog | products to move from reserve to e-shop stock | catalog cache (stock + sales) | Auto |
| `productinventorycount` | Produkty inventarizované (30dní) | Catalog | products stock-taken in last 30 days | catalog cache | Auto |
| `materialinventorycount` | Materiály inventarizované (30dní) | Catalog | materials stock-taken in last 30 days | catalog cache | Auto |
| `productinventorysummary` | Produkty podle stáří inventury | Catalog | products by age of last stock-taking | catalog cache | Auto |
| `materialwithexpirationinventorysummary` | Inventury surovin | Catalog | raw materials (with expiration) by stock-taking age | catalog cache | Auto |
| `materialwithoutexpirationinventorysummary` | Inventury obalů a etiket | Catalog | packaging/labels (no expiration) by stock-taking age | catalog cache | Auto |
| `materialexpirationsummary` | Expirace surovin | Catalog | raw materials by nearest lot expiry | catalog cache | Auto |
| `lowstockefficiency` | Materiál NS < 20% | Purchase | configured materials with stock efficiency < 20 % | purchase stock analysis | Auto |
| `purchaseordersintransit` | Suma nákupních objednávek | Purchase | count + total of purchase orders InTransit | `PurchaseOrders` table | Opt-in |
| `invoiceimportstatistics` | Faktury importované včera | Analytics | issued invoices synced yesterday (UTC) | `IssuedInvoices.LastSyncTime` | Auto |
| `dataqualitystatus` | Kvalita dat | DataQuality | result of the latest invoice DQT run | `DqtRuns` | Opt-in |
| `dqtyesterdaystatus` | DQT včera | DataQuality | invoice DQT run covering yesterday | `DqtRuns` | Opt-in |
| `pricecomparisonstatus` | Kontrola cen | DataQuality | latest Shoptet-vs-Flexi price check | `DqtRuns` + drift results | Opt-in |
| `packingstats` | Stav balení | Packaging | orders being packed/processed in Shoptet, packed today per packer | Shoptet REST orders + `Packages` table | Auto |

## Processes
- `calc-dashboard-tiles` — how the tile list is provisioned per user and how each tile's number
  is derived. Trigger: every `GET api/dashboard/data` (page open + every 30 s).
- `sync-weather-forecast` (module weather-forecast) — Open-Meteo forecast behind the
  `weatherforecast` tile.

Plain CRUD (no process doc): enable / disable a tile, reorder tiles (bulk save). These only
write the user's own rows in `UserDashboardTiles`.

## Data owned
- `public."UserDashboardSettings"` — one row per user (`UserId` unique, Entra object id or
  `"anonymous"`), `LastModified`.
- `public."UserDashboardTiles"` — one row per user × tile (`UserId`+`TileId` unique):
  `IsVisible`, `DisplayOrder`, `LastModified`. A row with `IsVisible=false` means "the user
  switched this tile off" and stops it being re-added automatically.

No cache, no blob. Tile registry is in memory (`TileRegistry`, filled at startup by
`app.InitializeTileRegistry()` in `Program.cs`).

## External systems
None directly. Tiles reach external systems through their owning modules: Home Assistant
(`manufactureconditions`), Open-Meteo (`weatherforecast`), Shoptet REST API (`packingstats`),
Hangfire storage (`failedjobs`). Each of these is read-only.

## Dependencies
- Reads from (via tiles): Catalog, Manufacture, Logistics, Purchase, Analytics/Invoices,
  DataQuality, Packaging, BackgroundJobs, WeatherForecast, Xcc BackgroundRefresh.
- Uses Authorization (`IPermissionResolver`) to decide whether a tile's data may be loaded.
- Nothing reads from the Dashboard module.

## Known quirks
- **Reading the dashboard writes to the DB.** `GET settings` and `GET data` both run
  `GetUserSettingsHandler`, which creates the user's settings on first visit and back-fills any
  newly-released Auto tile. A new Auto tile therefore appears for every existing user on their
  next page load, appended at the end.
- **`DefaultEnabled` alone does nothing.** Only tiles with `DefaultEnabled && AutoShow` are
  provisioned. The five "Opt-in" tiles above have `DefaultEnabled=true` but `AutoShow=false`,
  so nobody sees them until they switch them on.
- **Removed tiles turn into error tiles.** If a tile class is deleted, users who had it visible
  keep the row and get a small "Error — Tile 'x' not found" tile until they hide it.
- **Dead config key** `Dashboard:UserLockSlidingExpirationMinutes` (default 10, in
  `DashboardOptions`) is never read. The per-user write lock (`UserDashboardSettingsLock`) is a
  process-local `SemaphoreSlim` per user id that is never evicted — correct only while the app
  runs as a single instance.
- **Dead frontend renderer** `bankstatementimportstatistics` in `tileRegistry.tsx` has no backend
  tile, so it never renders.
- Permission gating on tiles is implemented (`IsUnauthorized` placeholder) but dormant because
  no tile declares `RequiredPermissions`. An older frontend role-vs-permission comparison bug
  (memory note `gotcha_role_gate_search`) no longer applies: `Dashboard.tsx` does not filter by
  permissions any more.

## Code entry points
- `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs` — the six endpoints.
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetUserSettings/GetUserSettingsHandler.cs` — provisioning and back-fill.
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetTileData/GetTileDataHandler.cs` — parallel tile loading, permission check, error tiles.
- `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/UserDashboardSettingsMutator.cs` — enable/disable/save scaffold.
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/` — `ITile`, `TileRegistry`, `[TileId]`, `DashboardOptions`.
- `backend/src/Anela.Heblo.Persistence/Dashboard/` — EF configuration and repository.
- `frontend/src/components/pages/Dashboard.tsx`, `frontend/src/components/dashboard/tiles/tileRegistry.tsx` — page and per-tile renderers/drill-downs.
