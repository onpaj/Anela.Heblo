---
process: calc-catalog-merge
kind: calculation
module: catalog
summary: Assembles the in-memory product catalog (CatalogAggregate per product) from ~20 separately refreshed source caches — Flexi, Shoptet, Heureka feed and Heblo's own tables — that almost every Heblo page, report and MCP tool reads.
owns:
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeScheduler.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/ICatalogMergeScheduler.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeCallbackWiring.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogCacheStore.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogCacheOptions.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogResilienceService.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/BundleProductRule.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockData.cs
verified_at: "5e993f9e2"
related: [sync-catalog-stock, sync-catalog-history, sync-catalog-prices, sync-catalog-master-data, calc-bundle-sales-expansion, calc-margins]
---

# Catalog merge (the product cache)

## Purpose
Heblo has no product table of its own. The "catalog" (Katalog) is an in-memory list of
`CatalogAggregate` objects — one per product, material, semi-product or goods code — that
combines what Flexi (ERP), Shoptet (e-shop) and Heblo's own tables know about that code: name,
type, stock in every place, prices, sales/purchase/manufacture history, lots, attributes,
manufacture difficulty and margins.

It answers "what do we know about product X right now?" for almost every screen: catalog list and
detail (`/catalog`), product statistics, inventory/stock taking, warehouse statistics, stock
analysis, purchase planning, manufacture planning, margins, pricing, analytics, dashboard tiles,
and the MCP tools `GetCatalogList`, `GetCatalogDetail`, `GetProductMargins`, … (see
`module-catalog`). Nothing is persisted; a restart rebuilds it.

## Trigger
- Each data family is loaded by its own BackgroundRefresh task (see the sync docs in
  `related`). Every load calls `CatalogCacheStore.Set*Data`, which records a load date and
  **schedules a merge** (`CatalogMergeScheduler.ScheduleMerge`).
- The scheduler **debounces**: the merge runs 5 s after the last source landed
  (`CatalogCache:DebounceDelay`), but at most 30 min after the first pending change
  (`MaxMergeInterval`). Only one merge runs at a time; a merge that fires while one is running is
  skipped.
- A read that finds no valid cache runs a synchronous **priority merge** (below).
- At start-up the BackgroundRefresh tiers run in order (tier 1 → 2 → 3, tasks in a tier run in
  parallel), then every task repeats on its own interval. Any task can be run by hand:
  `POST /api/backgroundrefresh/tasks/ICatalogRepository.<Task>/force-refresh`.

## Data flow
1. **Source caches** (`IMemoryCache`, one key each, written whole by their refresh task):

   | Cache key | Filled by (task) | Source | Doc |
   |---|---|---|---|
   | `CachedErpStockData` | `RefreshErpStockData` | Flexi stock-to-date, warehouses 5/20/4 | sync-catalog-stock |
   | `CachedEshopStockData` | `RefreshEshopStockData` | Shoptet product CSV export | sync-catalog-stock |
   | `CachedInTransportData`, `CachedInReserveData`, `CachedInQuarantineData` | `RefreshTransportData`, `RefreshReserveData` | Heblo `TransportBoxes` | sync-catalog-stock |
   | `CachedOrderedData` | `RefreshOrderedData` | Heblo purchase orders | sync-catalog-stock |
   | `CachedPlannedData`, `CachedManufacturedData` | `RefreshPlannedData`, `RefreshManufacturedData` | Heblo manufacture orders / sklad výroby | sync-catalog-stock |
   | `CachedSalesData`, `CachedSetPartsData` | `RefreshSalesData`, `RefreshSetPartsData` | Flexi user query 37, bundle parts | calc-bundle-sales-expansion |
   | `CachedPurchaseHistoryData`, `CachedConsumedData`, `CachedManufactureHistoryData` | `RefreshPurchaseHistoryData`, `RefreshConsumedHistoryData`, `RefreshManufactureHistoryData` | Flexi queries 19, 21, stock movements | sync-catalog-history |
   | `CachedEshopPriceData`, `CachedErpPriceData`, `CachedEshopUrlData` | `RefreshEshopPricesData`, `RefreshErpPricesData`, `RefreshEshopUrlData` | Shoptet price list, Flexi query 41, Heureka XML | sync-catalog-prices |
   | `CachedCatalogAttributesData`, `CachedLotsData`, `CachedStockTakingData`, `CachedManufactureDifficultySettingsData` | `RefreshAttributesData`, `RefreshLotsData`, `RefreshStockTakingData`, `RefreshManufactureDifficultySettingsData` | Flexi query 38, Flexi lots, Heblo tables | sync-catalog-master-data |

2. **Merge** (`CatalogMergeService.Merge`):
   - Product list: on the very first merge (empty catalog) one aggregate per ERP stock row.
     Afterwards the **existing list is cloned** and re-filled (see quirks).
   - Sales are first passed through `BundleSalesExpander` (gift-package sales become component
     rows).
   - For each product, in this order: ERP data, sales history, attributes, transport/reserve/
     quarantine/ordered/planned/manufactured, e-shop data, consumed/purchase/manufacture/
     stock-taking history, lots, prices, URL, manufacture difficulty.
   - Stamps `LastMergeDateTime`.
3. **Install** (`CatalogCacheStore.ReplaceCacheAtomicallyAsync`): the current list
   (`CatalogData_Current`) is demoted to `CatalogData_Stale` (kept 5 min), the new list becomes
   current, and — only if the four *required* sources have each loaded at least once — the
   validity stamp `CatalogData_LastUpdate` is set (valid 4 h).
4. **Margins**: tier-3 task `RefreshMarginData` (every 2 h) waits for a running merge, then writes
   `CatalogAggregate.Margins` on the current objects (see `calc-margins`).
5. **Reads** (`CatalogRepository`):
   - `GetAllAsync`: valid cache → serve it. Required sources never loaded → serve what is there.
     Merge running and `AllowStaleDataDuringMerge` → serve the stale list if it was itself complete.
     Otherwise run a synchronous priority merge.
   - `GetByIdAsync`, `GetByIdsAsync`, `FindAsync`, `CountAsync`, `GetProductsWithSalesInPeriod`
     read current → stale → empty (and schedule a merge) without checking validity.

## Logic & formulas
Field origin per aggregate (`CatalogMergeService`):

| Aggregate field | Source | Rule |
|---|---|---|
| `ProductName`, `ProductNameSuffix`, `ErpId`, `MinimalOrderQuantity`, `HasLots`, `HasExpiration`, `Volume`, `NetWeight`, `Note`, `SupplierCode`, `SupplierName` | Flexi ERP stock row | overwritten when the code is in ERP stock |
| `Type` | ERP `ProductTypeId` via `BundleProductRule.Resolve` | ERP Product with code `BAL*`/`SET*` → `Set` (99); else the ERP type |
| `Stock.Erp` | ERP stock row `Stock` | — |
| `Stock.StockPrice` | ERP `Price` (Flexi average stock price) | only when ERP stock > 0 and price > 0, else null |
| `Stock.Eshop`, `Location`, `Image`, `DefaultImage`, `GrossWeight`, `Height`, `Width`, `Depth`, `AtypicalShipping` | Shoptet CSV row | only when the code is in the CSV; also sets `PrimaryStockSource = Eshop` |
| `Stock.Transport`, `Reserve`, `Quarantine`, `Ordered`, `Planned`, `Manufactured` | Heblo | always set, 0 when the code is absent |
| `SalesHistory` | query 37 + bundle expansion | replaced when present |
| `PurchaseHistory`, `ConsumedHistory`, `ManufactureHistory` | Flexi | replaced when present |
| `StockTakingHistory` | Heblo `StockTakingRecords` | replaced when present, newest first |
| `Stock.Lots` | Flexi lots | replaced when present |
| `Properties.*` (`OptimalStockDaysSetup`, `StockMinSetup`, `BatchSize`, `ExpirationMonths`, `SeasonMonths`, `AllowedResiduePercentage`, `Cooling`), `MinimalManufactureQuantity` | Flexi query 38 | replaced when present |
| `EshopPrice`, `ErpPrice`, `Url` | Shoptet price list, Flexi query 41, Heureka feed | replaced when present |
| `ManufactureDifficultySettings` | Heblo `ManufactureDifficultySettings` | replaced when present |
| `Margins` | `RefreshMarginData` | see `calc-margins` |

Derived stock figures (`StockData`):
- `WarehouseStock` = `Eshop` if `PrimaryStockSource` is Eshop (the product is in the Shoptet CSV),
  else `Erp`.
- `Available` = `WarehouseStock + Transport + Manufactured`. `Total` = `Available + Reserve`
  (quarantine is excluded since 2025-05-12). `EffectiveStock` = `Available + Ordered`.
- `Planned` is disjoint from `Total` (only in-flight manufacture orders), so `Total + Planned` is
  not a double count.

Prices on the aggregate:
- `PriceWithVat` / `PriceWithoutVat`: e-shop price if > 0, else ERP selling price.
- `CurrentSellingPrice` (API `PriceDto`): e-shop price **with VAT**, else ERP price **without VAT**.
- `CurrentPurchasePrice`: ERP purchase price (the e-shop purchase price is always null).

Validity: required sources = `CachedErpStockData`, `CachedSalesData`,
`CachedPurchaseHistoryData`, `CachedManufactureHistoryData`. A merge before all four have loaded
once is installed but not stamped, so the next `GetAllAsync` re-merges once they arrive.

Source-call resilience (`CatalogResilienceService`, used by ERP stock, e-shop stock, attributes,
sales and set parts): up to 3 retries with exponential back-off from 1 s on
`HttpRequestException`/timeouts, 30 s timeout per attempt, and a circuit breaker that opens for
30 s when ≥ 50 % of ≥ 3 calls in a minute fail.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `CatalogCache:DebounceDelay` | 00:00:05 | Wait after the last source change before merging |
| `CatalogCache:MaxMergeInterval` | 00:30:00 | Force a merge this long after the first pending change |
| `CatalogCache:CacheValidityPeriod` | 04:00:00 | Validity of a stamped merge and of each source load date |
| `CatalogCache:AllowStaleDataDuringMerge` | true | Serve the previous complete list while a merge runs |
| `CatalogCache:StaleDataRetentionPeriod` | 00:05:00 | How long the demoted list is kept |
| `CatalogCache:EnableBackgroundMerge` | true | false = drop the merged cache on every source change and merge on the next read |
| `BackgroundRefresh:ICatalogRepository:*` | see sync docs | Interval and tier per source |
| `BackgroundRefresh:ICatalogRepository:RefreshMarginData` | every 02:00:00, tier 3 | Margin write-back |

## Runtime facts
- Before PR #4254 a merge that ran ~40–90 s after every restart, before sales had loaded, was
  stamped valid for 4 h and zeroed M2 catalogue-wide; seen 1:1 against restarts over 10 days —
  agent memory `gotcha_premature_merge_poisons_catalog_cache` — 2026-09-21. Fixed by the
  required-source gate described above.

## Known quirks
- **The product list is fixed at the first merge.** Only an empty catalog is seeded from ERP
  stock; later merges clone the existing list. A code added in Flexi after start-up is not in the
  catalog until the app restarts, and a code that drops out of ERP stock (archived, renamed)
  stays with its last values. Read from code; the carry-forward behaviour is pinned by
  `CatalogMergeServiceTests`.
- **Missing source rows carry forward old values.** Fields marked "when present" keep their
  previous value when the code disappears from that source — e.g. a product removed from the
  Shoptet CSV keeps its last e-shop stock and `PrimaryStockSource = Eshop`.
- **A product exists only if Flexi lists it in warehouse 4/5/20 with an accepted type.** Flexi's
  product group (`skupZboz`) drives the type; an item with an empty group is dropped with its
  sales, margins and stock (SA016005, fixed in Flexi 2026-10-01) — agent memory
  `gotcha_catalog_universe_is_skupzboz`. Names containing "archiv" are dropped too.
- **Only `GetAllAsync` honours the validity stamp.** Single-product and filtered reads return
  whatever list is current, stamped or not.
- **`CurrentSellingPrice` mixes VAT bases**: with VAT from the e-shop, without VAT from the ERP
  fallback.
- **One shared circuit breaker.** `CatalogResilienceService` is a singleton with one pipeline,
  so failures of one source (e.g. set parts) can open the breaker for ERP stock, e-shop stock,
  attributes and sales for 30 s; those refreshes then fail and keep their previous data.
- **A failed refresh keeps the old data silently**, except where the sync docs say otherwise.
  Load dates expire after 4 h, after which `ChangesPendingForMerge` reports pending.
- **Some writers patch cached objects in place** (stock taking `SyncStockTaking`, weight
  recalculation `NetWeight`), against the clone-and-swap rule; the next merge overwrites those
  patches from the source caches.
- Staging/Test windows are 100 days for every history source, so YoY views look empty there —
  agent memory `gotcha_saleshistorydays_100_on_staging`.
- `BackgroundRefresh:IManufactureCostCalculationService:Reload` and
  `ISalesCostCalculationService:Reload` are configured (disabled) but no code registers them.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs` — merge rules per field
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogCacheStore.cs` — cache keys, required sources, validity, stock-taking patch
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeScheduler.cs` — debounce and single-flight
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs` — read paths, `RefreshMarginData`
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — every refresh task registration
- `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockData.cs` — Available/Total formulas
- `backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs` — derived properties (prices, summaries)
- `backend/src/Anela.Heblo.API/appsettings.json` — `CatalogCache`, `BackgroundRefresh`, `DataSourceOptions`
