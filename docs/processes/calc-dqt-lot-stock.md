---
process: calc-dqt-lot-stock
kind: calculation
module: data-quality
summary: Every morning reconciles, for each material with expiration, the sum of its Flexi lots (šarže) against its Flexi on-hand stock from the catalog cache, reporting sum mismatches, missing lots and orphan lots.
owns:
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/LotStockReconciliationDqtJob.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Services/LotStockReconciliationComparer.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IMaterialLotStockQuery.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/MaterialLotStockSnapshot.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityMaterialLotStockQueryAdapter.cs
  - backend/src/Anela.Heblo.Domain/Features/DataQuality/LotStockReconciliationMismatch.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtJobRunner.cs
  - backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftComparisonResult.cs
verified_at: "5e993f9e2"
related: []
---

# Lots vs ERP stock (Šarže vs. ERP sklad)

## Purpose
Materials with an expiration date are tracked in Flexi by lot (*šarže*): each lot has an
amount and an expiry. The lots should add up to the material's on-hand stock. When they
don't — a lot was not written off, a receipt was booked without a lot, a stock-taking fixed
the total but not the lots — manufacturing picks the wrong lots and expiry planning is wrong.
This check surfaces that drift between explicit stock-takings. Results appear on the
**Kvalita dat** page (`/automation/data-quality`, test *Šarže vs. ERP sklad*, columns
"ERP" and "Šarže"); there is no dashboard tile.

## Trigger
- Hangfire recurring job `daily-lot-stock-dqt` (`LotStockReconciliationDqtJob`), cron
  `0 8 * * *` (08:00 Europe/Prague), enabled by default. Run labelled with today's date.
- Manual: **Spustit DQT** → *Šarže vs. ERP sklad* (`TestType = LotSumVsErpStock`). The date
  range is ignored — snapshot of the current state.

## Data flow
1. Create a `DqtRun` (`TestType` 4, Running) in `public."DqtRuns"`.
2. Read the **in-memory catalog cache** (`ICatalogRepository.GetAllAsync`) — no direct call to
   Flexi. Keep items with type **Material** and `HasExpiration` (from Flexi product data).
   For each: on-hand stock = `Stock.Erp`, lots = `Stock.Lots[].Amount`.
   - `Stock.Erp` comes from BackgroundRefresh `RefreshErpStockData` (Flexi stock-to-date,
     materials warehouse 5), every 10 min.
   - `Stock.Lots` comes from BackgroundRefresh `RefreshLotsData` (Flexi lots via the FlexiBee
     SDK `ILotsClient`), every 1 h.
3. Compare (`LotStockReconciliationComparer`) and store findings in
   `public."DqtDriftResults"`; run Completed with `TotalChecked` = materials with expiration,
   `TotalMismatches` = findings.

## Logic & formulas
- Units are the material's Flexi stock unit (typically grams or pieces); no conversion.
- `lotSum = Σ lot amounts`. If `|lotSum − erp| ≤ 0.01` → OK, no row.
- Otherwise exactly one code (`LotStockReconciliationMismatch`):

  | Code | Label (UI) | Rule |
  |---|---|---|
  | 2 `MissingLots` | Chybí šarže | `lotSum ≤ 0.01` (stock exists, no lots) |
  | 4 `OrphanLots` | Šarže bez skladu | `erp ≤ 0.01` (lots exist, no stock) |
  | 1 `SumMismatch` | Nesouhlasí součet | both non-zero but differ by more than 0.01 |

- Row content: key = material code, `HebloValue` = ERP stock (2 dp, column "ERP"),
  `ShoptetValue` = lot sum (2 dp, column "Šarže"), `Details` =
  `ERP: x | Šarže: y | Rozdíl: y−x`.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| Recurring job `daily-lot-stock-dqt` | `0 8 * * *`, enabled | Schedule |
| `BackgroundRefresh:ICatalogRepository:RefreshErpStockData` | every 00:10:00, tier 1 | Freshness of the ERP stock side |
| `BackgroundRefresh:ICatalogRepository:RefreshLotsData` | every 01:00:00, tier 1 | Freshness of the lot side |

Tolerance 0.01 is a code constant.

## Runtime facts
None.

## Known quirks
- **The two sides refresh at different speeds** (stock every 10 min, lots every hour), so a
  stock movement in Flexi within the hour before 08:00 can show up as a one-day
  `SumMismatch` that disappears on the next run. (Read from code, not observed.)
- **Both values come from the catalog cache, not a fresh Flexi read.** If the cache is not yet
  hydrated (e.g. right after an app restart) a manual run can report wrong findings.
- **Which Flexi warehouses the lot read covers is decided inside the FlexiBee SDK** (not
  visible in this repo); the ERP stock side is materials warehouse 5 only. A material with
  lots in another warehouse would show as a mismatch.
- The drift row reuses the `ShoptetValue` column for the lot sum — nothing here involves Shoptet.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/LotStockReconciliationComparer.cs` — tolerance, classification
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/LotStockReconciliationDqtJob.cs` — schedule
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityMaterialLotStockQueryAdapter.cs` — material filter, cache read
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs` — where `Stock.Erp`, `Stock.Lots`, `HasExpiration` are filled
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMetaRefreshService.cs` — lots refresh
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Lots/FlexiLotsClient.cs` — Flexi lot read
