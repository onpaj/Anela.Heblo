---
process: calc-dashboard-tiles
kind: calculation
module: dashboard
summary: Builds each user's home-page tile list (auto-provisioning, back-fill, permission check) and derives every tile's number from its owning module's data.
owns:
  - backend/src/Anela.Heblo.Application/Features/Dashboard/**
  - backend/src/Anela.Heblo.Domain/Features/Dashboard/**
  - backend/src/Anela.Heblo.Persistence/Dashboard/**
  - backend/src/Anela.Heblo.Xcc/Services/Dashboard/**
  - backend/src/Anela.Heblo.API/Controllers/DashboardController.cs
  - backend/src/Anela.Heblo.Application/Features/Analytics/DashboardTiles/**
  - backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/**
  - backend/src/Anela.Heblo.Application/Features/Catalog/DashboardTiles/**
  - backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/**
  - backend/src/Anela.Heblo.Application/Features/Logistics/DashboardTiles/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/**
  - backend/src/Anela.Heblo.Application/Features/Packaging/DashboardTiles/**
  - backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/**
verified_at: "5e993f9e2"
related:
  - module-dashboard
  - sync-weather-forecast
---

# Dashboard tiles

## Purpose
Explains what each number on the home page (Dashboard, route `/`) means and where it comes
from, and why a given user sees the tiles they see. The tile figures are always **live
reads** at the moment the page asks — nothing is pre-computed or stored by the Dashboard.
The heavy calculations behind some tiles (purchase stock analysis, gift-package severity, DQT
runs) belong to their own modules; this doc states only what the tile takes from them.

## Trigger
On demand, no Hangfire job, no BackgroundRefresh task.
- `GET api/dashboard/data` — when the Dashboard page opens and then every 30 s while it stays
  open (frontend `useTileData`, `refetchInterval: 30000`).
- `GET api/dashboard/settings` — page open and after every settings save.

## Data flow
1. **Resolve the user's layout** (`GetUserSettingsHandler`, under a per-user in-process lock):
   - load `public."UserDashboardSettings"` + `public."UserDashboardTiles"` for the caller's
     user id (`"anonymous"` if none);
   - **first visit**: create the settings row with one visible tile row per registered tile
     that has `DefaultEnabled && AutoShow`, in registry order (`DisplayOrder` 0, 1, 2, …);
   - **later visits**: any `DefaultEnabled && AutoShow` tile the user has no row for is
     appended as visible at `max(DisplayOrder)+1…`, and saved.
2. **Pick tiles to load** (`GetTileDataHandler`): rows with `IsVisible=true`, ordered by
   `DisplayOrder`.
3. **Permission check**: SuperUser role sees everything; otherwise the caller's permissions
   come from `IPermissionResolver`. A tile whose `RequiredPermissions` are not all held is
   returned as metadata with `IsUnauthorized=true` and no data. (No tile declares permissions
   today, so this never triggers.)
4. **Load tiles in parallel**, at most `Dashboard:MaxConcurrentTileLoads` at once. Each tile
   is resolved from DI in its own scope and runs `LoadDataAsync`. An unknown tile id or an
   exception becomes a small "Error" tile; the other tiles still load.
5. Return the tiles in the user's order. The frontend picks a renderer by tile id
   (`tileRegistry.tsx`) and adds the drill-down link.

## Logic & formulas
"Catalog cache" = `ICatalogRepository.GetAllAsync()` — the in-memory merged catalogue
(stock, sales history, stock-taking dates, lots) built by the Catalog module's
BackgroundRefresh tasks. "Today"/"yesterday" are **UTC** dates unless stated.

**System**
- `backgroundtaskstatus` — `Completed` = registered BackgroundRefresh tasks whose last run is
  Completed; `Total` = all registered tasks. A task that has never run counts as not completed.
- `failedjobs` — Hangfire `MonitoringApi.FailedCount()`: jobs currently in the Failed state.

**Manufacture**
- `todayproduction` / `nextdayproduction` — manufacture orders with `PlannedDate` = reference
  date, excluding `Cancelled`. Reference date: today (UTC) / next day skipping Saturday and
  Sunday (public holidays are **not** skipped). `TotalOrders` = count; list = first 5 orders
  with semi-product name, `ResponsiblePerson`, semi-product `ActualQuantity`, and flags
  "semi-product done" (`SemiProductManufactured` or `Completed`) and "products done"
  (`Completed`).
- `manualactionrequired` — count of orders with `ManualActionRequired = true` (any state).
- `manufactureconditions` — current inner/outer temperature and humidity from Home Assistant
  (`GET /api/states/{entityId}`, entity ids in `HomeAssistant:*EntityId`), cached 5 min
  (`HomeAssistant:ConditionsCacheDurationMinutes`). On failure the tile shows empty values
  with source `Unavailable`.

**Logistics**
- `intransitboxes` / `receivedboxes` / `errorboxes` — count of `public."TransportBoxes"` in
  state `InTransit` / `Received` / `Error`.
- `criticalgiftpackages` — gift packages (Logistics gift-package manufacture list,
  `SalesCoefficient = 1.0`) whose severity is `Critical`, i.e. available stock is below the
  package's minimal stock setting (`StockMinSetup`).

**Catalog** (all from the catalog cache)
- `lowstockalert` — products to re-stock on the e-shop from reserve (S/R/T = Sklad / Rezerva /
  Transport). Scope: `ProductType.Product` and `Goods`. Window = last
  `DataSourceOptions:SalesHistoryDays` days. `averageDailySales = Σ SalesHistory.AmountTotal
  in window / SalesHistoryDays` (pieces/day). Included when `Stock.Reserve > 0` **and**
  either there are no sales in the window, or `Stock.Eshop ≤ averageDailySales ×
  ResupplyThresholdMultiplier`. Sorted by e-shop stock ascending; returns e-shop, reserve and
  transport stock and days of stock remaining (`Eshop / averageDailySales`).
- `productinventorycount` / `materialinventorycount` — items (products+goods / materials)
  whose `LastStockTaking` is within the last 30 days.
- `productinventorysummary` (products+goods), `materialwithexpirationinventorysummary`
  (materials with `HasExpiration`), `materialwithoutexpirationinventorysummary` (materials
  without — packaging and labels): count by days since `LastStockTaking`:
  `recent` < 180, `medium` 180–365, `old` > 365, `never` = no stock-taking; `total` = all.
- `materialexpirationsummary` — materials with `HasExpiration` bucketed by
  `MinimalExpiration` (earliest in-stock lot): `expired` < today, `within30` today…+30 days,
  `within90` +31…+90 days, `ok` beyond 90. Materials without an in-stock expiring lot are
  excluded. `total = expired + within30 + within90` (healthy ones not counted).

**Purchase**
- `lowstockefficiency` — items of the purchase stock analysis (all categories, all statuses)
  with `StockEfficiencyPercentage < 20` **and** min or optimal stock configured.
  Efficiency = effective stock / optimal stock × 100 (or / min stock when no optimal is set).
- `purchaseordersintransit` — count and Σ `TotalAmount` (sum of order line totals, in the
  order's own amounts) of purchase orders with status `InTransit`; also shown as thousands
  with a `k` suffix (one decimal when not whole).

**Finance / Data quality**
- `invoiceimportstatistics` — number of `public."IssuedInvoices"` whose `LastSyncTime` falls
  on yesterday 00:00:00–23:59:59 UTC. The tile accepts a `date` parameter, but the frontend
  never sends one.
- `dataqualitystatus` — latest `DqtRuns` row of type `IssuedInvoiceComparison`: status
  `error` if the run failed, `warning` if it found mismatches, else `success`; shows
  `TotalChecked`, `TotalMismatches`, period.
- `dqtyesterdaystatus` — latest `IssuedInvoiceComparison` run whose period covers yesterday;
  `Running` also counts as `warning`. `no_data` if none.
- `pricecomparisonstatus` — latest `PriceComparison` run; status as above; additionally
  `missingInShoptet` = its `DqtDriftResults` rows with mismatch `MissingInShoptet` (counted only
  for completed runs, because that mismatch is excluded from `TotalMismatches`).

**Orders / Packaging**
- `packingstats` — `ordersBeingPackedCount` / `ordersBeingProcessedCount` = Shoptet order
  totals (`Paginator.TotalCount`, page 1) in status `ShoptetOrders:PackingStateId` (26) and
  `ShoptetOrders:ProcessingStateId` (−2); left empty if Shoptet fails. `totalOrdersPackedToday`
  = distinct `OrderCode` in `public."Packages"` with `PackedAt` in today (server local day);
  per packer = distinct orders per `PackedByUserId`/`PackedBy`, most first.

**Weather** — `weatherforecast`: see `sync-weather-forecast`.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Dashboard:MaxConcurrentTileLoads` | 4 | Tiles loaded in parallel per request. |
| `Dashboard:UserLockSlidingExpirationMinutes` | 10 (code default) | Not used anywhere. |
| `DataSourceOptions:SalesHistoryDays` | 400 (Staging/Test: 100) | Sales window for `lowstockalert`. |
| `DataSourceOptions:ResupplyThresholdMultiplier` | 1.3 | Days-of-sales threshold for `lowstockalert`. |
| `HomeAssistant:ConditionsCacheDurationMinutes` | 5 | Cache for `manufactureconditions`. |
| `ShoptetOrders:PackingStateId` | 26 (code default) | Shoptet status counted as "being packed". |
| `ShoptetOrders:ProcessingStateId` | −2 (code default) | Shoptet status counted as "being processed". |

## Runtime facts
- Staging uses `SalesHistoryDays = 100`, so `lowstockalert` averages over 100 days there vs 400
  in production — memory note `gotcha_saleshistorydays_100_on_staging`, recorded before
  2026-10-02.

## Known quirks
- **UTC day boundaries.** "Today", "tomorrow" and "yesterday" in the production, DQT and
  invoice tiles are UTC dates. Between 00:00 and 01:00/02:00 Prague time the "today" tile still
  shows the previous day, and "yesterday" lags by a day.
- **Reads write.** Every `GET data` first runs the settings provisioning (step 1), which can
  insert rows; a newly released Auto tile shows up for every user on their next refresh.
- **Comments disagree with code** in `InventorySummaryTileBase` and the three summary tiles:
  comments say 120/250 days, the code uses 180/365 days. The code is authoritative.
- `lowstockalert` divides by `SalesHistoryDays` even for products that started selling
  recently, so new products look slower-selling than they are. Products with reserve stock
  but no sales in the window are always listed.
- `materialexpirationsummary.total` excludes the `ok` bucket — it is "materials needing
  attention", not all materials.
- `nextdayproduction` skips weekends only; on the day before a public holiday it shows the
  holiday.
- `backgroundtaskstatus` shows a low ratio right after an app restart because tasks that have
  not yet run count as not completed.
- Several tiles catch exceptions and return `status:"error"` with the raw exception message
  (`lowstockalert`, the inventory tiles, `criticalgiftpackages`, `lowstockefficiency`).
- The per-user lock is process-local; with more than one app instance, concurrent first visits
  could race on the unique `UserId` index.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetUserSettings/GetUserSettingsHandler.cs` — provisioning + back-fill.
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetTileData/GetTileDataHandler.cs` — loading, permissions, error tiles.
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileRegistry.cs`, `TileRegistryExtensions.cs` — registration and duplicate-id validation at startup.
- `backend/src/Anela.Heblo.Application/Features/*/DashboardTiles/*.cs` — one class per tile (formulas above).
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/Tiles/BackgroundTaskStatusTile.cs` — system tile.
- `frontend/src/components/dashboard/tiles/tileRegistry.tsx` — renderer + drill-down URL per tile id.
