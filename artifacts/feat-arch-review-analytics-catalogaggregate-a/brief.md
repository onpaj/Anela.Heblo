## Module
Analytics

## Finding

`backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs`

The logic that converts a `CatalogAggregate` to `AnalyticsProduct` is duplicated verbatim in two methods:

- **`StreamProductsWithSalesAsync`**: lines 52–116 (inside the `foreach`)
- **`GetProductAnalysisDataAsync`**: lines 168–231

Both blocks perform identical steps in the same order:
1. Extract `marginData = product.Margins`
2. Filter `relevantMargins` by date range (omitted in `GetProductAnalysisDataAsync`)
3. Resolve `latestMarginEntry` with a fallback to the last available entry
4. Apply the same `latestMarginEntry.Equals(default(KeyValuePair<DateTime, MarginData>))` ternary **six times** to guard each field (`marginAmount`, `materialCost`, `handlingCost`, `M0Amount`, `M1Amount`, `M2Amount`, and the percentages)
5. Resolve `purchasePrice` from `PurchaseHistory`
6. Construct `new AnalyticsProduct { ... }` with the same property mapping

The only difference between the two blocks is that `GetProductAnalysisDataAsync` does not filter `SalesHistory` by date range when populating `SalesHistory` (line 222 vs line 107–115).

## Why it matters

Any future change to the `CatalogAggregate` → `AnalyticsProduct` mapping (e.g. adding a new margin field, changing how `PurchasePrice` is computed, fixing a margin fallback bug) must be applied in two places. The existing deviation (unfiltered `SalesHistory` in `GetProductAnalysisDataAsync`) suggests the two copies have already drifted. This is compounded by the upcoming cross-module refactor (#1805), where the mapping will need to move into a `CatalogAnalyticsSourceAdapter` — having it duplicated doubles the migration surface.

## Suggested fix

Extract a private helper method:

```csharp
private AnalyticsProduct MapToAnalyticsProduct(
    CatalogAggregate product,
    DateTime fromDate,
    DateTime toDate)
{
    var marginData = product.Margins;
    var relevantMargins = marginData.MonthlyData
        .Where(m => m.Key >= fromDate && m.Key <= toDate)
        .ToList();

    var latestMarginEntry = relevantMargins.LastOrDefault();
    if (latestMarginEntry.Equals(default(KeyValuePair<DateTime, MarginData>)))
        latestMarginEntry = marginData.MonthlyData.LastOrDefault();

    var hasEntry = !latestMarginEntry.Equals(default(KeyValuePair<DateTime, MarginData>));

    var latestPurchase = product.PurchaseHistory?.OrderByDescending(p => p.Date).FirstOrDefault();

    return new AnalyticsProduct
    {
        ProductCode    = product.ProductCode,
        ProductName    = product.ProductName,
        Type           = product.Type,
        ProductFamily  = product.ProductFamily,
        ProductCategory = product.ProductCategory,
        MarginAmount   = hasEntry ? latestMarginEntry.Value.M0.Amount : marginData.Averages.M0.Amount,
        M0Amount       = hasEntry ? latestMarginEntry.Value.M0.Amount : 0,
        M1Amount       = hasEntry ? latestMarginEntry.Value.M1.Amount : 0,
        M2Amount       = hasEntry ? latestMarginEntry.Value.M2.Amount : 0,
        M0Percentage   = hasEntry ? latestMarginEntry.Value.M0.Percentage : 0,
        M1Percentage   = hasEntry ? latestMarginEntry.Value.M1.Percentage : 0,
        M2Percentage   = hasEntry ? latestMarginEntry.Value.M2.Percentage : 0,
        MaterialCost   = hasEntry ? latestMarginEntry.Value.M0.CostLevel : 0,
        HandlingCost   = hasEntry ? latestMarginEntry.Value.M1_A.CostLevel : 0,
        SellingPrice   = product.EshopPrice?.PriceWithoutVat ?? 0,
        EshopPriceWithoutVat = product.EshopPrice?.PriceWithoutVat,
        PurchasePrice  = latestPurchase?.PricePerPiece ?? 0,
        SalesHistory   = product.SalesHistory
            .Where(s => s.Date >= fromDate && s.Date <= toDate)
            .Select(s => new SalesDataPoint { Date = s.Date, AmountB2B = s.AmountB2B, AmountB2C = s.AmountB2C })
            .ToList()
    };
}
```

Replace both duplicated blocks with a call to `MapToAnalyticsProduct(product, fromDate, toDate)`. Also fixes the latent drift bug: `GetProductAnalysisDataAsync` currently does not filter `SalesHistory` by date range (unlike `StreamProductsWithSalesAsync`), so after extraction both paths will consistently filter.

Note: this helper will naturally move into `CatalogAnalyticsSourceAdapter` when #1805 is resolved.

---
_Filed by daily arch-review routine on 2026-05-27._