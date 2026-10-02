---
process: sync-catalog-master-data
kind: sync
module: catalog
summary: Loads product planning attributes (Flexi query 38), Flexi lots with expirations, Heblo stock-taking records and Heblo manufacture-difficulty settings into the catalog cache.
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
  - backend/src/Anela.Heblo.Persistence/Catalog/ManufactureDifficulty/**
verified_at: "5e993f9e2"
related: [calc-catalog-merge, calc-margins, flow-stock-taking]
---

# Catalog master data (attributes, lots, stock takings, difficulty)

## Purpose
Brings in the slow-changing facts that planning and costing need per product:
- **Planning attributes** kept in Flexi as product parameters: optimal stock days, minimum
  stock, batch size, minimal manufacture quantity (MMQ), shelf life in months, season months,
  allowed residue % and cooling. Stock analysis, purchase/manufacture planning, low-stock alerts
  and expedition cooling read them.
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
| Flexi attribute ids | 80–89 (constants in `FlexiProductAttributesQueryClient`) | Parameter → field map |

## Runtime facts
None.

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

## Code entry points
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/ProductAttributes/FlexiProductAttributesQueryClient.cs` — query 38 and the attribute-id map
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Lots/FlexiLotsClient.cs` — Flexi lots
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogReferenceRefreshService.cs` — stock takings, difficulty (all / single product)
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMetaRefreshService.cs` — `RefreshAttributesData`, `RefreshLotsData`
- `backend/src/Anela.Heblo.Domain/Features/Catalog/ManufactureDifficultyConfiguration.cs` — current-value rule
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/CreateManufactureDifficulty/CreateManufactureDifficultyHandler.cs` — overlap handling
