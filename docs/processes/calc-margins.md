---
process: calc-margins
kind: calculation
summary: Per-product monthly margin cascade M0-M3 (material, manufacturing labour, warehouse+marketing, overhead) derived from catalog history and the Flexi ledger, shown on the Marže pages and via MCP.
owns:
  - backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/**
  - backend/src/Anela.Heblo.Application/Features/Catalog/Services/MarginCalculationService.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/**
  - backend/src/Anela.Heblo.Application/Shared/CostPools/**
  - backend/src/Anela.Heblo.Domain/Features/Catalog/MonthlyMarginHistory.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/MarginLevel.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Ledger/LedgerService.cs
verified_at: "a008e2306"
related: []
---

# Product margins (M0-M3)

## Purpose
Answers "how much does one piece of this product earn after each layer of cost?". Four
cumulative levels, each subtracting one more cost layer from the selling price excl. VAT:

| Level | UI label | Cost layer added | Cost source |
|---|---|---|---|
| M0 | M0 | material / purchase price | manufacture receipts or ERP purchase price |
| M1 | M1 | manufacturing labour (cost centre VYROBA) | Flexi ledger, allocated by manufacture difficulty |
| M2 | M2R | warehouse + marketing (SKLAD, MARKETING) | Flexi ledger, allocated by revenue |
| M3 | M3R | overhead: every other cost centre | Flexi ledger, allocated by revenue |

Consumers: page `/products/margins` (list), the Margins tab of the catalog detail, MCP tool
`GetProductMargins`, `GET /api/ProductMargins`; the latest month is also read by Pricing
(`PricingBaselineBuilder`) and Analytics (`CatalogAnalyticsSourceAdapter`). The user-facing
definition of the levels (Czech) is `frontend/public/docs/margin-levels.md`, served under the
"?" on the margins page.

## Trigger
No Hangfire job. In-process BackgroundRefresh tasks, run in hydration tiers at startup and then
on their own interval (config `BackgroundRefresh:<Owner>:<Method>`):

1. Tier 1 — catalog source loads (sales, manufacture history, ERP stock, prices …) and
   `ICostPoolService.RefreshCache` (every 4 h).
2. Tier 2 — `IMaterialCostProvider`, `IFlatManufactureCostProvider`, `ISalesCostProvider`,
   `IOverheadCostProvider` `.RefreshCache` (every 1 h each).
3. Tier 3 — `ICatalogRepository.RefreshMarginData` (every 2 h) writes `CatalogAggregate.Margins`.

The request path (`GetProductMarginsHandler`) only reads the precomputed `Margins`; it never
recalculates.

## Data flow
1. Catalog merge builds `CatalogAggregate`s (sales history from Flexi user query 37,
   manufacture history from Flexi receipts of `ManufactureDocumentTypeIds`, ERP + e-shop prices,
   `ManufactureDifficultySettings`).
2. Each cost provider waits for the current catalog merge, computes a per-product list of
   `MonthlyCost` for its window and stores it in its in-memory cache
   (`IMaterialCostCache`, `IFlatManufactureCostCache`, `ISalesCostCache`, `IOverheadCostCache`).
   M1/M2 read the ledger via `ILedgerService` (FlexiBee `ucetni-denik` REST query, 15-minute
   memory cache in `LedgerService`); M3 reads monthly pool totals from `ICostPoolService`.
3. `RefreshMarginData` calls `MarginCalculationService.GetMarginAsync` per product, which reads
   the four caches and builds `MonthlyMarginHistory.MonthlyData` (one `MarginData` per month).
4. `GetProductMarginsHandler` filters/sorts/pages the catalog, returns `Margins.Averages`
   as the headline numbers and the monthly rows from the last 13 months.

Everything lives in process memory; nothing is persisted. A restart recomputes it all.

## Logic & formulas
**Selling price** `P` = e-shop price excl. VAT if > 0, else ERP price excl. VAT
(`CatalogAggregate.PriceWithoutVat`). It is today's price, applied to every month. No price
(or no product code) → empty margin history, so every level reads 0 in the list.

**Per month**, with `c0..c3` the product's cost per piece for that month (0 if none):
```
CostTotal(Mk) = c0 + … + ck          CostLevel(Mk) = ck
Amount(Mk)    = P − CostTotal(Mk)
Percentage(Mk)= Amount(Mk) / P × 100
```
All four values are rounded to 2 decimals per month (`MarginLevel.Create`).

**Averages** (headline numbers, sort keys): a plain arithmetic mean over every month in
`MonthlyData` of Percentage, Amount, CostTotal and CostLevel separately — a mean of monthly
percentages, not total margin / total price.

**Margin window**: months from the month of `today − ManufactureCostHistoryDays` to
`today − 1 month` (the current month is excluded as incomplete). With 365 days that is 12
months. Cost providers use the same `ManufactureCostHistoryDays`, but their window runs from the
1st of that start month to the end of the *current* month, so pools and allocation denominators
include the current partial month.

**M0 — material** (`ManufactureBasedMaterialCostProvider`):
- Product, SemiProduct, Set with any manufacture history loaded (the catalog loads
  `ManufactureHistoryDays` of it, 730 in `appsettings.json`): walk the M0 window month by month,
  starting with no known price.
  1. Month with receipts → amount-weighted average `PricePerPiece` of that month's receipts;
     it becomes the last known price.
  2. Month without receipts, after an in-window receipt → the last known price is carried forward.
  3. Month without receipts, before the first in-window receipt → the price of the **next**
     receipt month after it (in the window). Receipts from before the window are never used.
  4. No later receipt either → **no M0 cost for that month** (the purchase price is not used).
- Product, SemiProduct, Set with an **empty** manufacture history → purchase price rule below.
- Goods, Material (and manufactured items with no receipts): `ErpPrice.PurchasePrice`
  (excl. VAT), same value every month; 0 or missing → no M0 cost.

**M1 — manufacturing labour** (`FlatManufactureCostProvider`):
```
pool          = Σ ledger debits on accounts 51x+52x, cost centre VYROBA, in window
points(p)     = Σ over p's receipts in window of amount × difficulty(receipt date)   (difficulty default 1)
costPerPoint  = pool / Σ points(p) over cost-bearing products
M1 per piece  = points(p) / Σ amount(p) × costPerPoint
```
Cost-bearing = `ProductType.Product` or `ProductType.Set` only. SemiProduct, Goods and Material
get M1 = 0. A product with no receipt in the window gets M1 = 0. The value is flat across months.
If Σ points = 0 every product gets 0 and a warning is logged.

**M2 and M3 — revenue allocation** (`SalesRevenueAllocation`, shared by both):
```
rate         = pool / Σ allocatable revenue of all products in window
M2|M3 piece  = rate × (product revenue in window / product pieces in window)
```
Revenue = `SumTotal`, pieces = `AmountTotal` of `SalesHistory` rows in window, excluding rows
with `SourceBundleCode` (synthetic bundle-component rows carry pieces but no revenue). A product
is allocatable only if pieces > 0.001 and revenue > 0; otherwise it gets 0 and is also left out
of the denominator. Revenue per piece is realized, so discounts and B2B lower it. Flat across
months. Total revenue ≤ 0 → everyone gets 0 with a warning.

**Pools** (`CostPoolDefinition`):
- M2 pool = cost centres SKLAD + MARKETING, debit accounts **50x, 51x, 52x** (50x counts only
  here: shipping packaging and marketing print).
- M3 pool = accounts 51x + 52x of every other cost centre (unknown/blank centre included — M3 is
  the catch-all), excluding VYROBA, SKLAD, MARKETING and the separate activity **BUVOL**.
- Outside every level by design: accounts 53x–57x, 58x/59x, 50x outside SKLAD/MARKETING.

**Handler defaults**: without a `ProductType` filter only Product and Goods are listed;
`OnlyWithSales` keeps products with sales in the last 365 days.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `DataSourceOptions:ManufactureCostHistoryDays` | 365 in `appsettings.json` (class default 400; Development 730; Staging/Test 100) | Window for all four cost levels and for the margin months |
| `DataSourceOptions:ManufactureDocumentTypeIds` | `[54, 56, 65, 67]` | Flexi receipt types read as manufacture history (M0 prices, M1 points) |
| `DataSourceOptions:ManufactureHistoryDays` | 730 (class default 400; Staging 100) | Manufacture history loaded into the catalog; wider than the M0 window, but pre-window receipts are ignored (see quirks) |
| `DataSourceOptions:SalesHistoryDays` | 400 (Staging 100) | Sales history loaded into the catalog; must cover the cost window for M2/M3 |
| `BackgroundRefresh:ICatalogRepository:RefreshMarginData` | every 02:00:00, tier 3 | Margin recompute |
| `BackgroundRefresh:I{Material,FlatManufacture,Sales,Overhead}CostProvider:RefreshCache` | every 01:00:00, tier 2 | Cost cache refresh |
| `BackgroundRefresh:ICostPoolService:RefreshCache` | every 04:00:00, tier 1 | Monthly pool totals used by M3 |
| `CatalogCache:CacheValidityPeriod` | 04:00:00 | How long a merged catalog counts as valid |

## Runtime facts
- Production has no override of `ManufactureCostHistoryDays` (no App Setting on `heblo`, no
  Key Vault secret), so prod runs the repo value (365 since #4250) — agent memory
  `project_prod_cost_window_365_appsetting` — checked 2026-09-21.
- Pool sizes over 2025-09-01..2026-09-30: M1 (VYROBA 51+52) 2 785 052 Kč; M2
  (SKLAD+MARKETING 50+51+52) 11 893 202 Kč; M3 (other centres 51+52, BUVOL excluded)
  6 016 907 Kč; left outside all levels: 53x–57x 569 356 Kč, 58x/59x 2 870 483 Kč — measured
  live against FlexiBee, agent memory `gotcha_m2_pool_is_59pct_of_overhead` — 2026-09-22.

## Known quirks
- **Averages include zero-cost months.** The mean runs over every month in the window, so any
  month where a provider emits no cost pulls the displayed cost down and inflates the %. The
  window is aligned to `ManufactureCostHistoryDays` in `RefreshMarginData` for exactly this
  reason: before the alignment, #4250 (730 → 365 days) left 8 of 20 margin months without cost
  and scaled every displayed cost by 0.6 (MAS009180 M0 90.79 → 47.37). The user doc's claim that
  "a month without data does not dilute the average" holds only because of that alignment.
- **M0 ignores receipts from before the window (defect, read from code, not observed in
  prod).** `CalculateFromManufactureHistory` falls back to the purchase price only when the
  manufacture history is completely empty. The catalog loads 730 days of history but the M0
  window is 365, and the month loop starts with no last-known price. So (a) a product whose
  receipts all fall 13–24 months back gets **no M0 cost in any month**: M0 reads ~100 %, and
  M1–M3 are overstated by the missing material cost; (b) window months before the first in-window
  receipt take the *next* in-window price instead of the most recent pre-window one.
- **Changing the window moves every level at once**, not just manufacturing — one key drives
  M0, M1, M2 and M3.
- **Semi-products must stay out of the M1 denominator.** Their receipts are grams of bulk with
  no difficulty (scored 1 per gram); when #4249 started ingesting types 54/65 they inflated the
  points ×7.07 and moved ~70 % of VYROBA onto bulk that is never sold. Fixed by `IsCostBearing`.
- **Sets are manufactured.** `ProductType.Set` is an ERP Product whose code starts `BAL`/`SET`
  (`BundleProductRule`), assembled in-house; excluding it from M1 zeroes every set's labour and
  hands its share to everything else.
- **A catalog merge before sources load used to zero M2 catalogue-wide for 4 h after each
  restart.** Fixed in #4254: the cache is stamped valid only once `ErpStock`, `Sales`,
  `PurchaseHistory` and `ManufactureHistory` have loaded (`CatalogCacheStore.RequiredSourceKeys`,
  all tier 1). Still open: a *partially* loaded catalog gives a too-small revenue denominator
  and inflated M2/M3 with no log line; only a fully empty one triggers `No sales revenue found`.
- **Cold caches read as zero cost.** A provider whose cache is not hydrated returns no costs
  (warning `…Cache not hydrated yet`), and `GetMarginAsync` catches all exceptions and returns an
  empty history — both look like cheap products rather than errors.
- **M1 = 0** means "not manufactured in the window", **M2 = M3 = 0** means "no net revenue in
  the window" — neither means the product costs nothing.
- **Historical months use today's selling price**, so a past month's % reflects the current price
  against that month's cost.
- **ConfigurationBinder merges arrays index-wise**, so an env/KV override of
  `ManufactureDocumentTypeIds` can duplicate ids; `FlexiManufactureHistoryClient` de-duplicates.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Catalog/Services/MarginCalculationService.cs` — the cascade per month
- `backend/src/Anela.Heblo.Domain/Features/Catalog/MarginLevel.cs` — Amount/Percentage formula and rounding
- `backend/src/Anela.Heblo.Domain/Features/Catalog/MonthlyMarginHistory.cs` — how `Averages` is computed
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs` — `RefreshMarginData`: margin window
- `backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProvider.cs` — M0
- `backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/FlatManufactureCostProvider.cs` — M1 pool, points, `IsCostBearing`
- `backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/SalesCostProvider.cs` — M2 pool
- `backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/OverheadCostProvider.cs` — M3 pool
- `backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/SalesRevenueAllocation.cs` — revenue allocation shared by M2/M3
- `backend/src/Anela.Heblo.Application/Shared/CostPools/CostPoolDefinition.cs` — which cost centre/account goes to which pool
- `backend/src/Anela.Heblo.Application/Shared/CostPools/CostPoolService.cs` — monthly pool totals (M3 input)
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Ledger/LedgerService.cs` — ledger query, `GetDirectCosts` = 51+52
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs` — list filters, sorting, 13-month history
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — refresh task registration
- `backend/src/Anela.Heblo.API/appsettings.json` — `DataSourceOptions`, `BackgroundRefresh` schedules
