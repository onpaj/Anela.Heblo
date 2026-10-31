---
process: sync-catalog-master-data
kind: sync
module: catalog
summary: Loads product planning attributes (Flexi query 38), Flexi lots with expirations, Heblo stock-taking records and Heblo manufacture-difficulty settings into the catalog cache; also writes the manual top-product flag back to Flexi.
owns:
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogReferenceRefreshService.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/ProductAttributes/**
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Lots/FlexiLotsClient.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Attributes/**
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Lots/**
  - backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogProperties.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/ManufactureDifficultySetting.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/ManufactureDifficultyConfiguration.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/IManufactureDifficultyRepository.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/*ManufactureDifficulty*/**
  - backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/SetTopProduct/**
  - backend/src/Anela.Heblo.Persistence/Catalog/ManufactureDifficulty/**
verified_at: "6b5578b47"
related: [calc-catalog-merge, calc-margins, flow-stock-taking]
---

# Catalog master data (attributes, lots, stock takings, difficulty)

## Purpose
Brings in the slow-changing facts that planning and costing need per product:
- **Planning attributes** kept in Flexi as product parameters: optimal stock days, minimum
  stock, batch size, minimal manufacture quantity (MMQ), shelf life in months, season months,
  allowed residue %, cooling and the manual "top product" flag. Stock analysis,
  purchase/manufacture planning, low-stock alerts and expedition cooling read them; the top flag
  is shown as a star next to the product name in the catalog list, stock analysis and batch planning.
- **Lots** (šarže) with amount and expiration from Flexi — shown in the catalog detail and used
  for expiration tiles and lot-stock checks.
- **Stock-taking history** (inventury) from Heblo — the "last stock taking" date and history.
- **Manufacture difficulty** (náročnost výroby), set by staff per product with validity
  periods — the weight that spreads manufacturing labour cost in M1 (`calc-margins`) and the
  basis of manufacture capacity views.

## Trigger
BackgroundRefresh tasks, tier 1, first run at start-up:

| Task id | Every | Cache key | Source |
|---|---|---|---|
| `ICatalogRepository.RefreshAttributesData` | 1 h | `CachedCatalogAttributesData` | Flexi query 38 |
| `ICatalogRepository.RefreshLotsData` | 1 h | `CachedLotsData` | Flexi lots |
| `ICatalogRepository.RefreshStockTakingData` | 5 min | `CachedStockTakingData` | Heblo `StockTakingRecords` |
| `ICatalogRepository.RefreshManufactureDifficultySettingsData` | 1 h | `CachedManufactureDifficultySettingsData` | Heblo `ManufactureDifficultySettings` |

On demand: creating, editing or deleting a difficulty setting (catalog detail, `POST/PUT/DELETE
/api/Catalog/manufacture-difficulty…`) reloads that one product's settings and swaps a patched
clone of the product into the live cache. An ERP stock taking reloads one product's Flexi lots
(`flow-stock-taking`).

Ticking or unticking "Top produkt" in the catalog detail (`PUT /api/Catalog/{productCode}/top-product`,
`products.catalog.write`) writes the flag to Flexi and then patches that one product in the catalog caches (no full refresh).

## Data flow
1. **Attributes** — Flexi user query **38** (all rows, through `CatalogResilienceService`), one
   row per product × attribute, grouped per product. Attribute ids → fields:

   | Flexi attribute id | Field | Parse |
   |---|---|---|
   | 80 | `OptimalStockDays` (`Properties.OptimalStockDaysSetup`) | int, else 0 |
   | 81 | `StockMin` (`StockMinSetup`) | int, else 0 |
   | 82 | `BatchSize` | int, else 0 |
   | 84 | `SeasonMonths` | parsed month list (`ISeasonalDataParser`) |
   | 85 | `MinimalManufactureQuantity` | int, else 0 |
   | 86 | `ExpirationMonths` | int, else 0 |
   | 87 | `AllowedResiduePercentage` | double, else 0 |
   | 89 | `Cooling` | enum `Cooling` by name, else None |
   | 90 (`TOPPRODUKT`) | `IsTopProduct` | true for `true`/`1`/`ano`/`yes` (case-insensitive), else false |

2. **Lots** — Flexi SDK lots client (all lots): id, product code, amount, expiration (date),
   lot code → `CatalogLot`. Flexi's "Entity LotsItem not found" is treated as no lots.
3. **Stock takings** — every row of `public."StockTakingRecords"` (type Eshop/Erp, code, old and
   new amount, date, user, error).
4. **Manufacture difficulty** — every row of `public."ManufactureDifficultySettings"`
   (product code, difficulty value, `ValidFrom`, `ValidTo`, created at/by), grouped per product,
   newest `ValidFrom` first.
5. Each list replaces its cache and schedules a merge; the merge copies attributes into
   `Properties`, lots into `Stock.Lots`, stock takings into `StockTakingHistory` (newest first)
   and difficulty into `ManufactureDifficultySettings`.
6. **Top product write (Heblo → Flexi)** — `SetTopProductHandler` checks the product exists in
   the catalog, then `FlexiTopProductWriter` calls the SDK `IProductAttributeClient` for attribute
   **90**: ticking upserts boolean `true`, unticking **deletes** the attribute row (so "not set"
   and "false" both read back as not top). A Flexi failure returns `TopProductWriteFailed` (1317,
   503) and the caches are left alone. After a successful write `ICatalogRepository.ApplyTopProductFlagAsync`
   (`CatalogCacheStore.ApplyTopProductAsync`) swaps a patched clone of the product into the merged
   catalog (current and stale) **and** into the attributes source cache, and schedules a debounced merge so a racing in-flight merge converges.
   Both are needed: the merged catalog only gets the flag through a catalog-wide merge that runs after
   a 5 s debounce, so the UI refetch right after a toggle would read the old value, and a merge reads
   the attributes source cache, so patching only the merged catalog is reverted by the next merge. A
   product with no attributes row (its only row was the Top flag) is patched in the merged catalog
   only; the next hourly refresh finds the new row. If the patch throws, the request still succeeds
   and the hourly refresh catches up.

## Logic & formulas
- **Current difficulty**: the setting whose `[ValidFrom, ValidTo]` (null = open) contains the
  reference date (merge time for the displayed value), latest `ValidFrom` wins. M1 looks it up
  per manufacture-receipt date and uses 1 when none applies (`FlatManufactureCostProvider`).
- **Saving a difficulty** (`CreateManufactureDifficultyHandler`): `ValidFrom` must be before
  `ValidTo`; overlapping existing periods of the same product are adjusted to make room
  (`ResolveOverlapsAsync`); then the single-product cache refresh above.
- `CatalogAggregate.IsInSeason` returns true when season months are set and the month is *not*
  among them; no production code calls it.
- `IsUnderStocked` = `Stock.Available < StockMinSetup` when a minimum is set.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `BackgroundRefresh:ICatalogRepository:Refresh{Attributes,Lots,ManufactureDifficultySettings}Data` | every 01:00:00, tier 1 | |
| `BackgroundRefresh:ICatalogRepository:RefreshStockTakingData` | every 00:05:00, tier 1 | |
| Flexi attribute ids | 80–90 (constants in `FlexiProductAttributesQueryClient`) | Parameter → field map |

## Runtime facts
- `RefreshLotsData` logs `Catalog.LotsRefreshCompleted rows={Rows} elapsedMs={ElapsedMs}` (Information) after the Flexi lots call (`sarze-expirace.json`) succeeds; nothing is logged on failure. Use it to track the endpoint's latency (p95 ~19 s observed, FlexiBee-side; see `docs/integrations/flexi-api.md`, Known slow endpoints).

## Known quirks
- **Unparseable attribute values become 0/None silently** (e.g. "10 ks" for stock min) — the
  product then looks unconfigured to stock analysis.
- **Query 38 product type only knows Product and Material**; everything else maps to UNDEFINED.
  The aggregate's type comes from ERP stock, not from here.
- **Stock-taking records are loaded unbounded** every 5 minutes (whole table, no date window).
- **Two "lots" exist**: these Flexi lots (stock per lot) are not the Heblo `Lots` table used for
  label printing (`flow-lots-and-material-containers`).
- **`IsInSeason` reads inverted** (true outside the listed months) and is unused; check the
  meaning of attribute 84 before building on it.
- Lots and stock takings have no resilience wrapper; a failed load keeps the previous list.
- **The top-product write is not idempotent against duplicate attribute rows**: if Flexi already
  holds several rows for product × attribute 90 the SDK answers 409 and the save fails with
  `TopProductWriteFailed`; fix the duplicate in Flexi.
- **Query 38 must return boolean attributes in `hodnota`.** The SDK stores attribute 90 (type
  `typAtribut.boolean`) in the row's `valBoolean` column and leaves `hodnota` empty, while the other
  attributes are string-typed and use `hodnota`. The query's SQL lives only in Flexi (read it with
  `GET uzivatelsky-dotaz/38.json?detail=full`); on 2026-10-31 it was changed to select
  `COALESCE(NULLIF(A.hodnota,''), CASE WHEN A.valboolean THEN 'true' END) as hodnota`. Without it
  every Top flag reads back as false even though the write succeeded (HTTP 200, no log line). If
  the query is ever recreated, keep the `COALESCE`.

## Code entry points
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/ProductAttributes/FlexiProductAttributesQueryClient.cs` — query 38 and the attribute-id map
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/ProductAttributes/FlexiTopProductWriter.cs` — set/clear attribute 90 through the SDK
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/SetTopProduct/SetTopProductHandler.cs` — validation, write, single-product cache patch
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogCacheStore.cs` — `ApplyTopProductAsync`, the cache patch
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Lots/FlexiLotsClient.cs` — Flexi lots
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogReferenceRefreshService.cs` — stock takings, difficulty (all / single product)
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMetaRefreshService.cs` — `RefreshAttributesData`, `RefreshLotsData`
- `backend/src/Anela.Heblo.Domain/Features/Catalog/ManufactureDifficultyConfiguration.cs` — current-value rule
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/CreateManufactureDifficulty/CreateManufactureDifficultyHandler.cs` — overlap handling
