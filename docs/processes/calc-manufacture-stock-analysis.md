---
process: calc-manufacture-stock-analysis
kind: calculation
module: manufacture
summary: Ranks every finished product by how many days of sales its current stock covers against its configured optimal days, flagging what production should make next (Critical / Major / Adequate / Unconfigured).
owns:
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetStockAnalysis/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureSeverityCalculator.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IManufactureSeverityCalculator.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureAnalysisMapper.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IManufactureAnalysisMapper.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ItemFilterService.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IItemFilterService.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ProductionActivityAnalyzer.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IProductionActivityAnalyzer.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Configuration/ManufactureAnalysisOptions.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Configuration/ManufactureAnalysisConstants.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Validators/GetManufacturingStockAnalysisRequestValidator.cs
  - backend/src/Anela.Heblo.API/Controllers/ManufacturingStockAnalysisController.cs
verified_at: "5e993f9e2"
related:
  - calc-batch-planning
  - flow-manufacture-order
---

# Manufacturing stock analysis

## Purpose
Page `/manufacturing/stock-analysis` ("Zásoby výrobků") tells production which finished
products are running low relative to how fast they sell, so the next batches can be chosen.
Each product gets a colour: red **Critical** (stock covers less than its optimal number of
days), orange **Major** (below its minimum stock), green **Adequate**, grey **Unconfigured** (no
optimal days set — hidden unless asked for). Supports search, product-family filter, sorting,
paging and an export of all rows.

## Trigger
On demand: `GET /api/manufacturing-stock-analysis` (permission `Manufacture_ManufactureStock`).
Query: `timePeriod` (default `Q9M`), `customFromDate`/`customToDate`, `productFamily`,
severity check-boxes, `searchTerm`, `pageNumber`/`pageSize` (default 1/20), `sortBy` (default
StockDaysAvailable), `sortDescending`, `salesMultiplier` (default 1.0), `isExport`.

## Data flow
1. Catalog cache (`IManufactureCatalogSource.GetAllAsync`) → items with `Type == Product`.
2. Per item: sales history, stock, properties (`OptimalStockDaysSetup`, `StockMinSetup`,
   `BatchSize`), product family, manufacture history — all already merged by the catalog module
   from Flexi, Shoptet and Heblo tables.
3. Compute metrics → filter → sort → page; summary counts are over **all** analysed items.

## Logic & formulas
- **Sales window** (`TimePeriodResolver`): `Q9M` = last 6 months **plus** the 3 months starting one
  year ago (two ranges, i.e. recent trend + next quarter last year); also PreviousQuarter, FutureQuarter
  (same 3 months last year), Y2Y (last 12 full months), PreviousSeason (1 Oct last year – 31 Jan this year), CustomPeriod.
- **Daily sales** = Σ (B2B + B2C pieces) over the ranges ÷ Σ days, × `salesMultiplier`.
  `SalesInPeriod` = Σ sold in the ranges × multiplier.
- **Stock days available** = `Stock.Total` ÷ daily sales; daily sales < 0.000001 → 999999.
  `Stock.Total` = warehouse (Flexi or e-shop, per primary source) + transport boxes + sklad výroby + reserve; quarantine excluded.
- **Overstock %** = stock days ÷ `OptimalStockDaysSetup` × 100 (0 when optimal days ≤ 0).
- **Severity**: optimal days ≤ 0 → Unconfigured; else overstock % < 100 and daily sales > 0 →
  Critical; else `StockMinSetup` > 0 and `Stock.Total` < it → Major; else Adequate.
- The **CurrentStock** column shows `Stock.Available` (without reserve), while stock days use
  `Stock.Total` (with reserve). ERP, e-shop, transport, manufactured (sklad výroby), reserve,
  quarantine and planned (open manufacture orders) are returned as separate columns.
- `IsInProduction` = any manufacture-history record with amount > 0 in the last 30 days.
- Search matches code, diacritics-normalised name or product family.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `ManufactureAnalysis:InfiniteStockIndicator` | 999999 | Shown when stock days are infinite |
| `ManufactureAnalysis:DefaultMonthsBack`, `MaxMonthsBack`, `ProductionActivityDays`, `CriticalStockMultiplier`, `HighStockMultiplier`, `MediumStockMultiplier` | 12, 60, 30, 1.0, 1.5, 2.0 | **Not read by any code** (see quirks) |
| Catalog `OptimalStockDaysSetup`, `StockMinSetup` | per product | Thresholds (product settings in the catalog) |

## Runtime facts
- Staging's catalog keeps only 100 days of sales history (`SalesHistoryDays`), so `Q9M`,
  `FutureQuarter`, `Y2Y` and `PreviousSeason` read mostly empty there; production keeps 400 days —
  memory note gotcha_saleshistorydays_100_on_staging.

## Known quirks
- **Most `ManufactureAnalysis` options are dead.** Only `InfiniteStockIndicator` is used; the
  severity uses a hard 100 % threshold and the 30-day activity window is a parameter default,
  so changing those settings has no effect. The `Minor` severity is counted in the summary but never assigned.
- **Infinite stock is 999999, not ∞.** With no sales the stock days are 999999, so the overstock %
  becomes huge and the item reads Adequate (or Major if below minimum stock).
- **CustomPeriod without both dates fails**: the resolver returns no ranges and the handler's
  `Min()` over them throws, so the request errors instead of returning an empty list.
- **`GetManufacturingStockAnalysisRequestValidator` is dead code** (never registered).
- Stock days include sklad výroby and transport boxes; a product can look covered while the
  shippable warehouse stock is low — memory note gotcha_stock_available_includes_manufacture_warehouse.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetStockAnalysis/GetManufacturingStockAnalysisHandler.cs` — pipeline
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureSeverityCalculator.cs` — severity rules
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureAnalysisMapper.cs` — returned columns
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ItemFilterService.cs` — filters, sorting, summary
- `backend/src/Anela.Heblo.Application/Common/TimePeriods/TimePeriodResolver.cs` — period presets
- `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockData.cs` — Total / Available definitions
