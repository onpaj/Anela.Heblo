---
process: calc-purchase-stock-analysis
kind: calculation
module: purchase
summary: Per-material stock coverage for purchasing - consumption rate, days until stock-out, stock efficiency (NS%), severity and a recommended order quantity for every material and goods item, shown on Zásoby materiálu and the dashboard.
owns:
  - backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/**
  - backend/src/Anela.Heblo.Application/Features/Purchase/Services/**
  - backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/LowStockEfficiencyTile.cs
  - backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialStockSnapshot.cs
  - backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialStockLevels.cs
  - backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialPurchaseSnapshot.cs
  - backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialCategoryFilter.cs
  - backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockSeverity.cs
  - backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockStatusFilter.cs
  - backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockAnalysisSortBy.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/PurchaseMaterialCatalogAdapter.cs
  - backend/src/Anela.Heblo.API/Controllers/PurchaseStockAnalysisController.cs
  - frontend/src/components/pages/PurchaseStockAnalysis.tsx
  - frontend/src/api/hooks/usePurchaseStockAnalysis.ts
verified_at: "5e993f9e2"
related:
  - calc-bundle-sales-expansion
  - flow-purchase-order
---

# Purchase stock analysis (Zásoby materiálu)

## Purpose
Answers the buyer's question **"what do I need to order, and how urgently?"** For every material
(materiál: raw materials, labels, packaging) and every goods item (zboží: bought-in products for resale), it shows
the stock, how fast it is used up, how many days it will last, how full it is against its
configured target (**NS%**, stock efficiency), a severity (Kritický / Nízký / Optimální / Přeskladněno /
Nenastaveno) and a suggested order quantity.

Seen on:
- Page **Nákup → Zásoby materiálu** (`/purchase/stock-analysis`). The defaults are category *Suroviny*, only configured items, and the last 12 months.
  From there, items can be put on a client-side purchase planning list (`PurchasePlanningListContext`, max 20 items, kept only in the browser tab) and carried into a new purchase order.
- Dashboard tile **"Materiál NS < 20%"** (`lowstockefficiency`, auto-shown).
- API `GET /api/purchase-stock-analysis` (permission `Purchase_PurchaseStock`).

Finished products are not here: their coverage is in the manufacturing stock analysis (Manufacture module).

## Trigger
On demand: each page load, filter change, export or dashboard tile load computes the result from the in-memory
catalog. Nothing is stored. The inputs are refreshed by the Catalog module's BackgroundRefresh tasks (see
Configuration).

## Data flow
1. `PurchaseMaterialCatalogAdapter.GetStockAnalysisSnapshotsAsync` reads the merged catalog
   (`ICatalogRepository.GetAllAsync`) and keeps items of type **Material** or **Goods**. For each item it builds a
   snapshot from these catalog fields:
   | Field | Source (all via the Catalog module's caches) |
   |---|---|
   | Consumption in period | Material: `ConsumedHistory` (Flexi user query 21, consumed materials). Goods: `SalesHistory` B2B + B2C pieces (Flexi sales, including component rows from bundle expansion, see `calc-bundle-sales-expansion`). Only rows with `fromDate ≤ Date ≤ toDate` count. |
   | Available | `Stock.Available` = warehouse stock + in transport + manufacture warehouse (sklad výroby) |
   | Ordered | `Stock.Ordered` = open purchase-order quantities (see `flow-purchase-order`) |
   | Effective stock | `Available + Ordered` |
   | Min stock (`StockMinSetup`), optimal stock days (`OptimalStockDaysSetup`) | Flexi product attributes, user query 38 |
   | Supplier name, MOQ (`MinimalOrderQuantity`) | Flexi `stav-skladu-k-datu` row (`SupplierName`, `MoqName`) |
   | Last purchase | Newest row of `PurchaseHistory`, Flexi user query 19 (date, supplier, amount, unit price, total) |
2. `GetPurchaseStockAnalysisHandler` keeps the items whose code matches the requested category. It analyses each item
   (`StockAnalysisCalculator.AnalyzeItem`), builds the summary from **all** analysed items, and then applies the
   status filter, the "only configured" filter and the search. It then sorts and pages the rows (an export returns all rows).
3. The dashboard tile runs the same request (all statuses, category *All*, export) and counts the items with NS% < 20
   that are configured.

## Logic & formulas
Default window: `fromDate` = now − 1 year, `toDate` = now (UTC). `fromDate > toDate` returns `InvalidDateRange`.

- `days` = whole days between `fromDate` and `toDate` (minimum 1).
- `dailyConsumption` = consumption in period / `days`.
- `daysUntilStockout` = ⌊EffectiveStock / dailyConsumption⌋, or empty when there is no consumption.
- `minStock` = `StockMinSetup`. `optimalStock` = `dailyConsumption × OptimalStockDaysSetup` (0 when the days are not set).
- An item is **configured** when `StockMinSetup > 0` or `OptimalStockDaysSetup > 0`.
- **NS% (stock efficiency)** = EffectiveStock / optimalStock × 100. If `optimalStock` is 0, it is
  EffectiveStock / minStock × 100, and 0 when neither is set.
- **Severity**, checked in this order, with stock = EffectiveStock:
  | Severity | Rule |
  |---|---|
  | NotConfigured (Nenastaveno) | neither min nor optimal days set |
  | Critical (Kritický) | min set and stock < min, **or** optimal days set and stock < 20 % of optimalStock |
  | Low (Nízký) | optimal days set and 20 % ≤ stock < 70 % of optimalStock |
  | Overstocked (Přeskladněno) | optimal days set and stock > 150 % of optimalStock |
  | Optimal (Optimální) | everything else |
- **Recommended order quantity**: target = optimalStock if > 0, otherwise 2 × minStock. needed = target −
  **Available**. If needed > 0, the result is max(needed, MOQ) when MOQ parses as a number, otherwise needed. If
  needed ≤ 0 or nothing is configured, there is no recommendation.
- **Category** by product-code prefix: `ETI…` = labels (Etikety); `VIC`, `LAH`, `KEL`, `UZA` = packaging (Obaly:
  caps, bottles, beakers, closures); anything else = *Other* (shown as Suroviny, which includes goods).
- **Summary**: counts per severity, plus `TotalInventoryValue` = Σ EffectiveStock × last purchase unit price
  (0 for items with no purchase history). The summary covers every item in the chosen category, whatever the
  other filters are.
- Search matches the product code, the normalised name or name suffix, the supplier name, or the last-purchase supplier.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `DataSourceOptions:ConsumedHistoryDays` | 730 | History loaded for material consumption |
| `DataSourceOptions:SalesHistoryDays` | 400 | History loaded for goods sales (consumption of goods) |
| `DataSourceOptions:PurchaseHistoryDays` | 3650 | History loaded for "last purchase" |
| `BackgroundRefresh:ICatalogRepository:RefreshConsumedHistoryData` | every 01:00:00 | Consumption refresh |
| `BackgroundRefresh:ICatalogRepository:RefreshSalesData` | every 01:00:00 | Sales refresh |
| `BackgroundRefresh:ICatalogRepository:RefreshPurchaseHistoryData` | every 01:00:00 | Purchase history refresh |
| `BackgroundRefresh:ICatalogRepository:RefreshAttributesData` | every 01:00:00 | Min stock / optimal days refresh |
| `BackgroundRefresh:ICatalogRepository:RefreshErpStockData` | every 00:10:00 | Flexi stock refresh |
| `BackgroundRefresh:ICatalogRepository:RefreshOrderedData` | every 00:05:00 | Open purchase-order quantities |
| Severity thresholds (code constants) | 20 % / 70 % / 150 % of optimal | `StockSeverityCalculator` |

`appsettings.Staging.json` and `appsettings.Test.json` set all three history windows to **100 days**
(production inherits the defaults above), so on staging a 12-month analysis understates consumption about 3.6×.

## Runtime facts
None.

## Known quirks
- **The recommended quantity ignores what is already on order.** It uses `Available`, while severity, NS% and
  days-to-stock-out use `EffectiveStock` (Available + Ordered). An item already ordered can show as Optimal and
  still recommend ordering the same quantity again.
- **"Ordered" is broader than "in transit".** A purchase order's lines count as ordered while the order is Draft or
  InTransit, **or whenever "invoice acquired" is not ticked, whatever its status**. A Completed order with no
  invoice flag keeps adding to EffectiveStock after the goods are already in stock in Flexi. See
  `flow-purchase-order`.
- **Optimal days set but no consumption → Overstocked.** If an item has optimal days configured but no consumption,
  optimalStock is 0, so any positive stock reads as Overstocked, and NS% falls back to the min-stock ratio or 0.
- **The dashboard tile and its drill-down show different lists.** The tile counts configured items with NS% < 20,
  but its drill-down opens the page filtered to *Critical*. Critical also includes items below min stock with a
  higher NS%, and the page keeps its "only configured" default.
- **MOQ comes from Flexi's MOQ name (`MoqName`), which is text.** It is parsed with the server culture. A
  non-numeric or comma-decimal value is ignored silently.
- **The window is limited by the loaded history.** A window longer than `ConsumedHistoryDays` /
  `SalesHistoryDays` divides a truncated consumption by the full day count, so it understates the rate.
- `TotalInventoryValue` includes ordered quantities and values them at the last purchase price, not at stock value.
- Items not in the catalog (for example a Flexi card with an empty product group) are missing here too.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs` — window, category, filter/search/sort/paging, summary scope
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs` — daily rate, NS%, recommended quantity, summary
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockSeverityCalculator.cs` — severity thresholds
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/MaterialCategoryResolver.cs` — code-prefix categories
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/PurchaseMaterialCatalogAdapter.cs` — snapshot from the catalog (consumption vs sales, last purchase)
- `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/LowStockEfficiencyTile.cs` — dashboard tile
- `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockData.cs` — Available / EffectiveStock definitions
- `frontend/src/components/pages/PurchaseStockAnalysis.tsx` — page defaults, planning list
