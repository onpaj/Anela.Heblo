---
process: calc-product-margin-summary
kind: calculation
module: analytics
summary: Margin-analysis report (Analýza marže) that multiplies each product's latest per-unit M0/M1/M2 margin by its units sold in a time window and groups the result by product, family or category, month by month.
owns:
  - backend/src/Anela.Heblo.Application/Features/Analytics/Services/**
  - backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/**
  - backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/**
  - backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/**
  - backend/src/Anela.Heblo.Application/Features/Analytics/Validators/**
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs
  - backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct*.cs
  - backend/src/Anela.Heblo.Domain/Features/Analytics/MarginLevel.cs
  - backend/src/Anela.Heblo.Domain/Features/Analytics/ProductGroupingMode.cs
verified_at: "5e993f9e2"
related: [calc-margins, calc-bundle-sales-expansion]
---

# Margin analysis (Analýza marže)

## Purpose
Answers "which products, product families or categories earned us the most margin over a period,
and how did that develop month by month?". Management uses it on the page **Analýza marže**
(`/analytics/product-margin-summary`, Finance section of the sidebar). The page shows a stacked
monthly chart of the top 15 groups plus "others", and a table of every group with M0, M1 and M2
amounts and percentages.

This report **does not compute margins itself**. It reuses the per-unit margins from
`calc-margins`, and the sales from the catalog's sales history, and multiplies them together. The
access permission is `finance.margin_analysis.read` (`Feature.Finance_MarginAnalysis`). No MCP tool
exposes this report; MCP `GetProductMargins` belongs to `calc-margins`.

## Trigger
On demand, with nothing cached in the analytics layer:
- `GET /api/analytics/product-margin-summary?timeWindow=&groupingMode=&marginLevel=&sortBy=&sortDescending=`
  — the page.
- `GET /api/analytics/margin-report?startDate=&endDate=&productFilter=&categoryFilter=&maxProducts=`
  and `GET /api/analytics/margin-analysis?productId=&startDate=&endDate=&includeBreakdown=` exist in
  the API and the generated client, but **no frontend page calls them**.

## Data flow
1. `CatalogRepository.GetProductsWithSalesInPeriod` reads the **in-memory catalog cache**, with no
   database query. It keeps products of type `Product` and `Goods` that have at least one real
   (non-bundle-expanded) sale in the window.
2. `CatalogAnalyticsSourceAdapter` projects each product to an `AnalyticsProduct`:
   - `SalesHistory` = catalog sales rows inside the window with `SourceBundleCode == null`, as
     B2B and B2C pieces per day;
   - per-unit margins = the **latest** `Margins.MonthlyData` month inside the window (if there is
     none in the window, the latest month overall): `M0Amount`, `M1Amount`, `M2Amount` and their
     percentages, `MaterialCost = M0.CostLevel`, `HandlingCost = M1.CostLevel`;
   - `MarginAmount` = that M0 amount (or `Margins.Averages.M0.Amount` when there is no monthly
     data at all);
   - `SellingPrice` = catalog e-shop price excl. VAT (`EshopPrice.PriceWithoutVat`);
     `PurchasePrice` = the newest `PurchaseHistory.PricePerPiece`.
3. The handler streams these through `MarginCalculator`, `MonthlyBreakdownGenerator` and
   `TopProductSorter`, and returns JSON. Nothing is stored.

## Logic & formulas
**Time window** (`TimeWindowParser`, relative to today in the server's local time):
`current-year` (1 Jan → today, the default), `current-and-previous-year`, `last-6-months`,
`last-12-months`, `last-24-months`. Any other value throws.

**Grouping** (`groupingMode`): `Products` (by product code, shown with the product name),
`ProductFamily` ("Rodina X"), `ProductCategory` ("Kategorie X"). A missing family or category
becomes `Unknown`.

**Margin level** (`marginLevel`): `M0`, `M1` or `M2` (default M2, labelled "M2R" in the UI).
M3 from `calc-margins` is **not** offered.

**Totals** (`MarginCalculator.CalculateAsync`):
- skip any product whose `MarginAmount` (M0) is ≤ 0;
- `units` = Σ (AmountB2B + AmountB2C) over the window;
- `contribution` = `units × per-unit margin at the selected level`;
- `group total` = Σ contributions; `TotalMargin` = Σ over all groups.
- Amounts are CZK excl. VAT, because the per-unit margins are excl. VAT.

**Monthly breakdown** (`MonthlyBreakdownGenerator`). For every calendar month from the window's
start month to its end month, each group's contribution is `units sold that month × the same
per-unit margin`. Products whose margin at the selected level is ≤ 0 are skipped, and groups with a
total ≤ 0 are dropped. The month's segments are sorted by contribution, and each one's `Percentage`
is its share of that month's total. Each segment also carries plain (unweighted) averages over the
group's products: margin per piece, e-shop price excl. VAT, material cost and handling cost.

**Group table columns** (`GetGroupAggregatedMarginData`). M0, M1 and M2 amounts and percentages,
selling price and purchase price, each **weighted by the units sold** in the window. If the group
sold nothing, a simple average is used.

**Sorting.** The default is total margin descending. `sortBy` accepts groupkey/productcode,
displayname/productname, totalmargin, m0amount, m1amount, m2amount, m0percentage, m1percentage,
m2percentage, sellingprice and purchaseprice. `Rank` = the position after sorting. The response
carries **every** group; the "top 15 + others" cut is made in the frontend.

**`margin-report` and `margin-analysis`** (not used by the UI). Both are M0 only, regardless of
level:
- `revenue = units × e-shop price excl. VAT`;
- `cost = units × (price − M0 amount)`;
- `margin = revenue − cost`;
- `margin % = margin / revenue × 100`.

`margin-report` keeps the first `maxProducts` (default 50, maximum 1000) products that match a
case-insensitive name or name-suffix filter and an exact category filter. It sorts them by M2%
descending and adds per-category totals (null category → `Uncategorized`). Validation: start ≤ end,
the period must be 1 to 730 days, and a product id is required for `margin-analysis`.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `AnalyticsConstants.MAX_REPORT_PERIOD_DAYS` (code) | 730 | Longest period for margin-report / margin-analysis. |
| `AnalyticsConstants.DEFAULT_MAX_PRODUCTS` / `ABSOLUTE_MAX_PRODUCTS` (code) | 100 / 1000 | Product caps (the request default is 50). |
| `DataSourceOptions:SalesHistoryDays` | 400 (`appsettings.json`), 100 on Staging/Test | How far back the catalog keeps sales. This silently caps every window. |

## Runtime facts
- Staging keeps only 100 days of sales history (`SalesHistoryDays` = 100), so year-to-date and
  multi-year windows look nearly empty there, while production keeps 400 days — agent memory
  `gotcha_saleshistorydays_100_on_staging` and the appsettings files — 2026-10-02.

## Known quirks
- **Past months use today's margin.** The report applies one per-unit margin — the latest month in
  the window — to the units of **every** month. A month in January is valued at September's margin,
  so the chart shows sales volume × the current margin, not the margin actually earned at the time.
- **The longest windows are silently cut.** `last-24-months` and `current-and-previous-year` reach
  further back than the catalog's 400-day sales history, so the earliest months show nothing.
- **The total and the chart can disagree.** `CalculateAsync` filters on M0 > 0 only, so a product
  with a positive M0 and a negative M1/M2 *reduces* `TotalMargin` and the group totals. The monthly
  chart skips such products, so with M1 or M2 selected the chart's months can add up to more than
  the header total.
- **Products without margin data vanish.** A product with no margin month at all has M0, M1 and M2
  = 0, and is dropped by the M0 > 0 filter unless its `Averages.M0` is positive. Even then its
  level amounts are 0.
- **Sets are never included.** Bundles and sets (`ProductType.Set`) cannot be projected. Sales of
  set components that came from bundle expansion (`SourceBundleCode` set) are excluded, so a
  component is credited only with its own direct sales (see `calc-bundle-sales-expansion`).
- **Selling price follows the current e-shop price.** That includes a running Shoptet action price
  since #4373. It only affects the averages and the `margin-report` revenue, not the contributions,
  which use the margins from `calc-margins`.
- **"Latest month" depends on insertion order.** `MonthlyData` is a plain `Dictionary`, and the
  adapter takes `.LastOrDefault()`. That is the newest month only because the margin history is
  built in chronological order; nothing sorts it here.
- **The window uses the server's local date.** `TimeWindowParser` uses the server's local today,
  not Europe/Prague explicitly.
- **Two endpoints look dead.** `margin-report` and `margin-analysis` have no UI caller. Their
  "margin" is M0 even though `margin-report` sorts by M2%.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` — the page's pipeline
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs` — the M0 > 0 filter, contributions, weighted group averages
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MonthlyBreakdownGenerator.cs` — monthly segments
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs` — window keywords
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs` — which margin month, price and sales are used
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs`, `GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs` — the unused endpoints
- `backend/src/Anela.Heblo.API/Controllers/AnalyticsController.cs` — routes and permission
- `frontend/src/components/pages/ProductMarginSummary.tsx` — the page (top 15 + others, level labels)
