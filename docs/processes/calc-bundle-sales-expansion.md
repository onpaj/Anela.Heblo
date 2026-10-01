---
process: calc-bundle-sales-expansion
kind: calculation
summary: Turns every sold gift package (BAL…/SET…) into quantity-only sale rows for its components, so manufacture, purchase and stock planning see bundle demand while revenue and margins stay on the bundle.
owns:
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/BundleSalesExpander.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/BundleProductRule.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Sales/CatalogSetPart.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Sales/CatalogSaleRecord.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/Sales/ICatalogSetPartsClient.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Sales/FlexiCatalogSetPartsClient.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Sales/FlexiCatalogSalesClient.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Sales/CatalogSalesFlexiDto.cs
verified_at: "abfbe9d40"
related: [calc-margins, feed-stock-up]
---

# Bundle sales expansion (sales history)

## Purpose
Answers "how many pieces of this product did customers take, including the ones that left inside
a gift package?". A bundle is invoiced as one line under its own code, so without this its
components would show no demand for those sales. Planning reads the expanded figure: the
manufacturing stock analysis, batch planning, purchase stock analysis (goods), the low-stock
dashboard tile and the catalog detail's monthly sales (page + MCP `GetCatalogDetail`).
Money stays on the bundle's own row. Margins, analytics and cost allocation skip the synthetic
rows (see the consumer table).

Introduced by #3966. The design and its accepted trade-offs are in
`docs/superpowers/specs/2026-08-26-bundle-sales-expansion-design.md`.

## Trigger
No Hangfire job. Two in-process BackgroundRefresh tasks fill two caches, and the expansion runs
inside every catalog merge (`CatalogMergeService.Merge`), which any source refresh schedules:

1. `ICatalogRepository.RefreshSalesData`: every 1 h, tier 1. Reads Flexi user query 37.
2. `ICatalogRepository.RefreshSetPartsData`: every 1 h, tier 2. It derives the bundle codes from
   the ERP stock cache, which tier 1 fills.

## Data flow
1. **Sales.** `FlexiCatalogSalesClient` calls Flexi user query **37** (`PRODEJE-SUM`) with
   `DATUM_OD`/`DATUM_DO` = the last `SalesHistoryDays`. The SQL lives in Flexi, not in this
   repo; read it with `GET uzivatelsky-dotaz/37.json?detail=full` (field `dotaz`). It sums every
   line (`dpolfak`) of issued invoices of document type 6 (`FAKTURA`), grouped by issue date
   and ceník code:
   `Mnozstvi = Σ mnozmj`, `Suma = Σ SumZkl` (excl. VAT), split into VO (invoice has a DIČ) and
   MO (no DIČ). The result is one `CatalogSaleRecord` per product per day, kept in cache key
   `CachedSalesData`.
2. **Composition.** `RefreshSetPartsData` takes every ERP stock item that
   `BundleProductRule.Resolve` turns into `ProductType.Set`. For each, `FlexiCatalogSetPartsClient`
   reads Flexi `sady-a-komplety` (`cenikSada = code:<bundle>`). That is **not** the kusovník
   (BoM): sets have no kusovník. One `CatalogSetPart(SetCode, ComponentCode, ComponentName,
   Amount = mnozMj)` is created per row. The result goes to cache key `CachedSetPartsData`.
3. **Expansion at merge.** `BundleSalesExpander.Expand(sales, setParts)` returns all original rows
   plus one synthetic row per component per bundle sale. The rows are grouped by product code
   into `CatalogAggregate.SalesHistory`, which also rebuilds `SaleHistorySummary` (monthly).
4. The raw sales cache is never modified. The expansion is redone on every merge, so it always
   combines the latest sales with the latest composition.

## Logic & formulas
**Which codes are bundles** (`BundleProductRule`): an ERP `Product` whose code starts `BAL` or
`SET` (ordinal, case-sensitive) becomes `ProductType.Set`. Nothing else is expanded. Shoptet
product sets coded `SA…` stay `Product` and are **not** expanded (see quirks).

**Synthetic row**, for a bundle sale row `s` and a part `p` of that bundle:

| Field | Value |
|---|---|
| `Date` | `s.Date` |
| `ProductCode` / `ProductName` | `p.ComponentCode` / `p.ComponentName` |
| `AmountB2B` / `AmountB2C` | `s.AmountB2B × p.Amount` / `s.AmountB2C × p.Amount` |
| `AmountTotal` | `AmountB2B + AmountB2C` |
| `SumB2B` / `SumB2C` / `SumTotal` | `0`. The bundle row keeps all the revenue |
| `SourceBundleCode` | `s.ProductCode`. Null on every row that came from Flexi |

Rules:
- **One level only.** Rows that already have a `SourceBundleCode` are not expanded again, so a
  bundle inside a bundle contributes only its own code.
- **Today's composition is applied to the whole history window.** Flexi does not version
  `sady-a-komplety`.
- The client drops part rows with a blank component code (an archived product) or
  `mnozMj ≤ 0`. A bundle with no remaining parts is logged and skipped.
- Each bundle is fetched in its own resilience call. The cache is then replaced by the parts that
  were fetched. It is **kept unchanged** if no bundle code is found in ERP stock, if every fetch
  fails, or if the task throws.

**Consumers of `SalesHistory`** and whether they count bundle pieces:

| Consumer | Reads | Bundle pieces |
|---|---|---|
| Manufacturing stock analysis (`GetManufacturingStockAnalysisHandler`, `ProductType.Product` only) | daily rate + `GetTotalSold` | counted |
| Batch planning (`BatchPlanningService`) | daily rate | counted |
| Purchase stock analysis (`PurchaseMaterialCatalogAdapter`) | `GetTotalSold` for Goods; consumption for Material | counted for Goods |
| Low-stock tile (`LowStockAlertTile`, Product + Goods) | Σ `AmountTotal` | counted |
| Catalog detail / product statistics / MCP `GetCatalogDetail` | `SaleHistorySummary` | pieces counted, revenue not; `TransactionCount` includes synthetic rows |
| Gift package screen (`LogisticsCatalogSourceAdapter`) | the bundle's own `GetTotalSold` | n/a |
| Margins list `OnlyWithSales` filter (`GetProductMarginsHandler`) | `GetTotalSold` | counted |
| Pricing simulator baseline quantity (`PricingBaselineBuilder`, trailing 12 months) | `GetTotalSold` | **counted (defect, see quirks)** |
| M2/M3 revenue allocation (`SalesRevenueAllocation`) | Σ `SumTotal` / `AmountTotal` | excluded (`SourceBundleCode != null`) |
| Analytics margin reports (`GetProductsWithSalesInPeriod`, `CatalogAnalyticsSourceAdapter`) | sales points | excluded (`SourceBundleCode == null`) |

Any new consumer that derives money from quantities must apply the same filter.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `DataSourceOptions:SalesHistoryDays` | 400 (Staging/Test 100) | Sales window loaded from query 37, and therefore the expansion window |
| `BackgroundRefresh:ICatalogRepository:RefreshSalesData` | every 01:00:00, tier 1 | Query 37 reload |
| `BackgroundRefresh:ICatalogRepository:RefreshSetPartsData` | every 01:00:00, tier 2 | Bundle composition reload, one Flexi call per bundle |
| `CatalogCache:EnableBackgroundMerge` | true | A refreshed source schedules a re-merge (and so a re-expansion) |

## Runtime facts
- Query 37 SQL as described in Data flow — read live from prod Flexi
  (`uzivatelsky-dotaz/37`, `lastUpdate` 2026-08-26) — 2026-10-01.
- **No double counting from Flexi**: on Aug–Sep 2026 invoices, 64 BAL lines and 70 SA lines
  were checked. No invoice carries zero-priced component lines next to a set line, so Flexi
  never splits a set into its components. 3 + 3 invoices had a component line, but each was
  priced as a separate purchase. Query 37 therefore returns only the set's own code for a set
  sale — read-only Flexi queries — 2026-10-01.
- Sales 2025-10-01..2026-09-30 (query 37, excl. VAT): BAL 1 450 pcs / 991 526 Kč (expanded);
  SA 1 763 pcs / 1 071 154 Kč (**not** expanded); MAS009X50 80 pcs / 17 851 Kč and
  SLU000001 364 pcs / 2 Kč (not expanded) — 2026-10-01.
- `sady-a-komplety` defines 114 set codes:
  - 61 BAL. 38 of them have an active ceník row; 22 sold in Aug–Sep 2026.
  - 23 SA (the ceník has 23 `SA0…` items).
  - DAR002–DAR029 and DARX01, none of them active.
  - `MAS009X50` (2 × MAS009050, "1+1 ZDARMA"), `SEZ001100D` (SEZ001100 + SEZ002005),
    `SLU000001` (a free gift containing TON002030) and `DEZ001200S` (no parts).
  - There is **no** `SET…` ceník item — the `SET` prefix in the rule matches nothing.
  - Read-only Flexi queries — 2026-10-01.
- Every BAL bundle contains a gift box as a component, `DAR0010` (mini, `…M`) or `DAR001`
  (large, `…V`). Both are VYROBEK ceník items, so the boxes get synthetic sales and appear as
  products in the manufacturing stock analysis — 2026-10-01.

## Known quirks
- **SA… sets are not expanded (defect).** `BundleProductRule` knows only `BAL`/`SET`. The 23
  Shoptet product sets (`SA…`) have `sady-a-komplety` definitions exactly like BAL, but they
  resolve to `Product`, so their composition is never fetched. That is about 1 760 sets a year of
  component demand (more than BAL) missing from manufacture and purchase planning. Before adding
  `SA` to the rule, note that it also changes the type. As a `Set`, SA would:
  - drop out of the default margins, pricing and analytics lists (Product + Goods only);
  - appear on the gift package screen, where assembling it would fail because Shoptet refuses
    stock changes on product sets (`stock-change-not-allowed`, shoptet-api §8.5).

  Separating "expand sales" from "is a gift package" avoids both. Expansion fits SA even better
  than BAL: Shoptet deducts SA components at the moment of sale, so there is no assembly lag.
- **Other sets are not expanded either:** `MAS009X50` ("1+1 zdarma"; its 80 pieces a year are
  really 160 × MAS009050) and `SLU000001` (364 free TON002030 a year).
- **Pricing simulator counts bundle pieces as the component's own sales (defect).**
  `PricingBaselineBuilder` takes `Quantity = GetTotalSold(12 months)`, and the scenario totals
  multiply it by the component's own price (`revenueBefore/After`, M0 totals in
  `PricingSimulationCalculator`). Pieces that left inside a bundle are credited with revenue and
  margin they never earned, so the scenario overstates every component that sells in bundles.
- **Margins `OnlyWithSales`** counts synthetic rows, so a product sold only inside bundles passes
  the filter and is listed with its own-price margin.
- **Assembly lag (BAL).** Gift package assembly takes component stock out at assembly time
  (`GPM-` operations, feed-stock-up), but demand is counted at sale time. After a large assembly
  batch, the planner looks more urgent than reality until the bundles sell.
- **Composition changes rewrite history.** Every past bundle sale is re-expanded with today's
  parts on the next merge.
- **Set parts are not a required catalog source** (`CatalogCacheStore.RequiredSourceKeys`). A
  merge that runs before the first successful `RefreshSetPartsData` has no synthetic rows. The
  later refresh schedules a re-merge, but a cold-start failure leaves expansion off until the next
  hourly success, with only a warning log.
- **A partly failed refresh drops the failed bundles** for that hour. The cache is replaced with
  the parts of the bundles that were fetched, so the failed bundles' sales are not expanded until
  the next run.
- Set codes come from the **ERP stock cache**, so a bundle without a ZBOZI stock card or with an
  empty ceník product group (`skupZboz`) is never expanded. Agent memory
  `gotcha_catalog_universe_is_skupzboz`.

## Code entry points
- `backend/src/Anela.Heblo.Domain/Features/Catalog/BundleProductRule.cs` — which codes are bundles (`BAL`/`SET`)
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/BundleSalesExpander.cs` — the expansion
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogHistoryRefreshService.cs` — `RefreshSetPartsData`, `RefreshSalesData`
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs` — where `Expand` is called; `GetProductType`
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Sales/FlexiCatalogSetPartsClient.cs` — `sady-a-komplety` read and row filtering
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Sales/FlexiCatalogSalesClient.cs` — user query 37 call, `CatalogSalesFlexiDto` column mapping
- `backend/src/Anela.Heblo.Domain/Features/Catalog/Sales/CatalogSaleRecord.cs` — `SourceBundleCode`
- `backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/SalesRevenueAllocation.cs` — exclusion from M2/M3
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs` — exclusion from analytics
- `backend/src/Anela.Heblo.Application/Features/Pricing/Services/PricingBaselineBuilder.cs` — baseline quantity (includes bundle pieces)
- `backend/src/Anela.Heblo.API/appsettings.json` — `BackgroundRefresh` schedules, `SalesHistoryDays`
