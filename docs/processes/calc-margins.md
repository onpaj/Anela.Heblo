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
verified_at: "abfbe9d40"
related: [calc-bundle-sales-expansion]
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

### Bundles and sets
Flexi has two kinds of set. Both are VYROBEK ceník items whose composition is in the
`sady-a-komplety` evidence. **Neither has a kusovník** (BoM), and neither ever gets a Flexi
manufacture receipt:

| | Gift packages `BAL…` | Shoptet product sets `SA…` |
|---|---|---|
| What it is | Physically assembled in-house (Dárkové balíčky screen); its own stock in Shoptet | Shoptet `product-set`; Shoptet derives its stock from the components; never assembled |
| Heblo type (`BundleProductRule`) | `ProductType.Set` (prefix `BAL` or `SET`) | `ProductType.Product` (prefix not recognised) |
| M0 | purchase-price fallback (below) | same |
| M1 | 0 (cost-bearing, but no receipts) | 0 (same reason) |
| M2 / M3 | revenue allocation on the bundle's own invoice lines, like any product | same |
| Listed by default (margins page, MCP, pricing, analytics) | no: Product + Goods only; analytics cannot project a Set at all | yes |

**How a set's M0 is formed:**
1. Assembly writes no Flexi document. `GiftPackageManufactureService` books only `GPM-`/`GPD-`
   stock operations in Shoptet (feed-stock-up), and SA sets are never assembled. Flexi shows no
   stock movement for BAL/SA since 2023. So `ManufactureHistory` is empty and
   `CalculateFromManufactureHistory` takes the purchase-price fallback.
2. The fallback is `ErpPrice.PurchasePrice`: the Flexi ceník `nakupCena` from user query 41
   (`cenanakup`), excl. VAT since #4284. The **same value applies to every month**.
3. **Nothing in Heblo maintains that `nakupCena`.** It is typed by hand in Flexi. The nightly
   `purchase-price-recalculation` job (02:00) touches only:
   - in phase 1 (stock price → `nakupCena`), Material and Goods;
   - in phases 2–3 (Flexi `prepocti-nakupni-cenu`), items with a kusovník (`HasBoM`/`BoMId`).

   Sets have no kusovník, so the job never reaches them. Its "sets after products" ordering is
   only a safeguard (`docs/integrations/flexi-api.md`). The set's M0 therefore stays frozen at
   whatever was last entered and does not follow its components' cost (defect, see quirks).
4. **The composition is loaded but not used for cost.** `CatalogSetPart` (from
   `sady-a-komplety`) serves only bundle sales expansion (calc-bundle-sales-expansion). No cost
   provider reads it, and no Σ component cost × quantity roll-up exists anywhere.
5. **Packaging.** The gift box *is* a component (`DAR0010` mini, `DAR001` large, with ceník
   prices 11.02 / 15.72). A roll-up would include it, but the frozen `nakupCena` does not track
   it. Shipping cartons and other packing materials have no per-product cost: `PackingMaterial`
   has no price field, and their purchases (50x on SKLAD) reach margins only through the M2 pool,
   allocated by revenue. Assembly labour reaches a set only through M1, which is 0 for sets.
6. `nakupCena = 0` gives **no M0 at all**: the set reads M0 ≈ 100 % (SA016005).

**Components of a bundle.** Bundle sales add quantity-only rows to each component's
`SalesHistory` (`SourceBundleCode` set, `Sum* = 0`). Margins ignore those rows for M2/M3
revenue, for the revenue denominator and for analytics, so a component's margin depends only on
its own sales. The one leak is the `OnlyWithSales` filter, which counts them.

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
- Sets in prod Flexi — read-only queries — 2026-10-01:
  - 38 `BAL…` and 23 `SA…` ceník items, all `typZasoby.vyrobek`, none with a kusovník; no
    `SET…` item exists.
  - None has a stock movement after 2023, and every stock card is 0.
  - Ceník rows were last updated 2025-09-11/24 (BAL, two exceptions in 2026) and 2025-08-28 /
    2025-09-10 (SA). `lastUpdate` is an upper bound for the last `nakupCena` change.
- Stored `nakupCena` vs today's component roll-up (Σ component `nakupCena` × `mnozMj` from
  `sady-a-komplety`, gift box included) — read-only queries — 2026-10-01:
  - BAL: stored is **above** the roll-up for 36 of 38, median +29 %, up to +92 % (BAL0003M
    134.30 vs 69.79; BAL0005V 276.60 vs 143.90).
  - SA: 14 of 23 within ±10 %; outliers SA001 +56 %, SA002 +52 %, SA012 −29 %.
  - Not established: whether the BAL premium is deliberate (labour or extra packaging priced in)
    or stale (the components got cheaper, e.g. after #4291 synced component `nakupCena` to
    stock prices). Owner question.

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
- **Sets are manufactured, but not in Flexi.** `ProductType.Set` is an ERP Product whose code
  starts `BAL`/`SET` (`BundleProductRule`), assembled in-house, so it stays cost-bearing in M1.
  The comment on `IsCostBearing` says sets are "receipted like any other product". In practice
  they are not: assembly writes only Shoptet stock moves. Every set therefore has M1 = 0, and
  the labour of assembling gift packages is spread over manufactured products.
- **Bundle and set cost is frozen (defect).** M0 for BAL/SA is the hand-entered ceník
  `nakupCena`, which no job recalculates (no kusovník). Component price changes never reach the
  bundle. Measured drift is up to +92 % against today's component roll-up (Runtime facts). The
  composition needed for a roll-up is already loaded as `CatalogSetPart`, but only sales
  expansion uses it.
- **SA… sets are not `Set` (defect).** Only `BAL`/`SET` prefixes are recognised, so the 23
  Shoptet product sets are typed `Product`. Their margins come out the same as BAL (purchase-price
  M0, M1 = 0), but their component sales are not expanded (calc-bundle-sales-expansion). The
  `SET` prefix matches no Flexi item.
- **BAL bundles are hidden by default.** The margins list, MCP `GetProductMargins` and the
  pricing simulator list only Product + Goods without a type filter. The analytics margin reports
  never include Sets. About 1 M Kč a year of BAL revenue (excl. VAT) is visible only with
  `ProductType = Set`.
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
- `backend/src/Anela.Heblo.Domain/Features/Catalog/BundleProductRule.cs` — which codes become `ProductType.Set`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/RecalculatePurchasePrice/RecalculatePurchasePriceHandler.cs` — nightly `nakupCena` maintenance; shows why sets are never recalculated
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
