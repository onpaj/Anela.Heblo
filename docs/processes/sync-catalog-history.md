---
process: sync-catalog-history
kind: sync
module: catalog
summary: Loads each product's purchase history, material consumption and manufacture receipts from Flexi into the catalog cache, where they drive purchase planning, material cost (M0) and manufacture statistics.
owns:
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogHistoryRefreshService.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Purchase/FlexiPurchaseHistoryQueryClient.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Purchase/PurchaseHistoryFlexiDto.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Materials/FlexiConsumedMaterialsQueryClient.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureHistoryClient.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/PurchaseHistory/**
  - backend/src/Anela.Heblo.Domain/Features/Catalog/ConsumedMaterials/**
  - backend/src/Anela.Heblo.Domain/Features/Catalog/ManufactureHistory/**
  - backend/src/Anela.Heblo.Application/Common/DataSourceOptions.cs
verified_at: "5e993f9e2"
related: [calc-catalog-merge, calc-bundle-sales-expansion, calc-margins]
---

# Catalog purchase, consumption and manufacture history

## Purpose
Gives every product and material a dated history of what Anela bought (from whom, how much, at
what price), how much material production consumed, and how many pieces each manufacture receipt
produced at what cost. People see it in the catalog detail (purchase / consumption / manufacture
tabs and charts) and in product statistics. It also feeds calculations: purchase planning reads
consumption rates and last purchases, the margin cascade reads manufacture receipts and purchase
prices for M0 and manufactured pieces for M1 (`calc-margins`).

Sales history is loaded by the same service (`RefreshSalesData`, Flexi user query 37) and is
documented, with the bundle expansion, in `calc-bundle-sales-expansion`.

## Trigger
BackgroundRefresh tasks, tier 1, every 1 h, first run at start-up:
- `ICatalogRepository.RefreshPurchaseHistoryData` → cache `CachedPurchaseHistoryData`
- `ICatalogRepository.RefreshConsumedHistoryData` → `CachedConsumedData`
- `ICatalogRepository.RefreshManufactureHistoryData` → `CachedManufactureHistoryData`

Purchase and manufacture history are *required sources*: until both have loaded once the merged
catalog is not stamped valid (`calc-catalog-merge`).

## Data flow
1. **Purchase history** — Flexi user query **19** (`uzivatelsky-dotaz/19`) with `DATUM_OD` =
   today − `PurchaseHistoryDays`, `DATUM_DO` = today, no product filter. Row: product code and
   name, date, purchase document number, amount, warehouse id, price, supplier name and id. The
   query SQL lives in Flexi, not in this repo. The raw result is also memory-cached for 1 h under
   `PurchaseHistory_{from}_{to}_{limit}`. Mapped to `CatalogPurchaseRecord`
   (`SupplierName`, `Date`, `Amount`, `PricePerPiece`, `PriceTotal`, `DocumentNumber`).
2. **Consumed material** — Flexi user query **21** with `DATUM_OD` = today −
   `ConsumedHistoryDays`, `DATUM_DO` = today → `ConsumedMaterialRecord` (code, name, amount,
   date).
3. **Manufacture history** (`FlexiManufactureHistoryClient`) — Flexi stock-movement lines
   (`skladovy-pohyb-polozka`) of direction **In** for each document type in
   `ManufactureDocumentTypeIds` (one call per type, in parallel; retries 502/503/504 twice), over
   today − `ManufactureHistoryDays` .. today. Lines are grouped by **day + product code** (code
   prefix stripped): `Amount` = sum, `PriceTotal` = sum, `PricePerPiece` = average unit price →
   `CatalogManufactureRecord`.
4. Each list replaces its cache and schedules a merge; the merge attaches the rows to the
   product with the same code (`PurchaseHistory`, `ConsumedHistory`, `ManufactureHistory`).

## Logic & formulas
- Windows are rolling, counted in days back from today (UTC date).
- Manufacture history aggregates to one row per product per day, so two receipts on the same day
  become one record; `PricePerPiece` is an unweighted mean of the line unit prices.
- VAT basis of the purchase price (query 19) cannot be determined from the repo: the column
  comes from the Flexi-side query definition (readable with
  `GET uzivatelsky-dotaz/19.json?detail=full`). Manufacture prices are Flexi stock-movement unit
  prices (stock valuation, no VAT).
- `CatalogAggregate.PurchaseHistorySummary` / `ConsumedHistorySummary` group these rows by month
  for the UI.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `DataSourceOptions:PurchaseHistoryDays` | 3650 (class 400; Development 365; Staging/Test 100) | Purchase history window |
| `DataSourceOptions:ConsumedHistoryDays` | 730 (class 720; Staging/Test 100) | Consumption window |
| `DataSourceOptions:ManufactureHistoryDays` | 730 (class 400; Staging/Test 100) | Manufacture receipts loaded into the catalog |
| `DataSourceOptions:ManufactureDocumentTypeIds` | [54, 56, 65, 67] (class empty) | Flexi `typ-skladovy-pohyb` ids counted as manufacture receipts |
| `BackgroundRefresh:ICatalogRepository:Refresh{PurchaseHistory,ConsumedHistory,ManufactureHistory}Data` | every 01:00:00, tier 1 | Intervals |

## Runtime facts
None.

## Known quirks
- **Empty `ManufactureDocumentTypeIds` fails loudly** (`InvalidOperationException`) instead of
  returning no history.
- **Array settings merge, they don't replace.** An environment or Key Vault override of
  `ManufactureDocumentTypeIds` with fewer elements is merged index-wise onto the repo array; the
  client de-duplicates the ids, but the result may still contain ids you meant to remove — agent
  memory `gotcha_config_binder_appends_arrays`.
- **Types 54 and 65 are semi-product receipts** (grams of bulk). They are history here, but they
  inflated the M1 denominator until excluded there — agent memory
  `gotcha_semiproducts_inflate_m1a_denominator`.
- **No resilience wrapper and no stale-cache guard** on these three refreshes: a failed Flexi call
  logs a BackgroundRefresh failure and the previous list stays.
- **Purchase history can be up to ~2 h old**: the adapter's own 1 h memory cache sits behind the
  1 h refresh interval.
- `ManufactureHistoryDays` controls only what the catalog holds; margins use their own
  `ManufactureCostHistoryDays` window (`calc-margins`).
- Staging/Test load 100 days of everything, so long-range charts look empty there.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogHistoryRefreshService.cs` — windows and cache writes
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Purchase/FlexiPurchaseHistoryQueryClient.cs` — query 19, 1 h cache
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Materials/FlexiConsumedMaterialsQueryClient.cs` — query 21
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureHistoryClient.cs` — receipts by document type, daily grouping
- `backend/src/Anela.Heblo.Application/Common/DataSourceOptions.cs` — window defaults
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/PurchaseMaterialCatalogAdapter.cs` — how Purchase reads consumption
