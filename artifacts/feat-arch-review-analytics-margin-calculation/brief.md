## Module
Analytics

## Finding

The core per-product margin formula (`revenue = totalSales × SellingPrice; cost = totalSales × (SellingPrice − MarginAmount); margin = revenue − cost`) is duplicated in three places:

1. `GetMarginReportHandler.ProcessProductsForReport()` — lines 113–117:
   ```csharp
   var revenue = (decimal)totalSales * product.SellingPrice;
   var cost = (decimal)totalSales * (product.SellingPrice - product.MarginAmount);
   var margin = revenue - cost;
   var marginPercentage = revenue > 0 ? (margin / revenue) * 100 : 0;
   ```

2. `GetProductMarginAnalysisHandler.CalculateProductMargins()` — lines 134–138 (same formula):
   ```csharp
   var revenue = (decimal)totalSales * product.SellingPrice;
   var cost = (decimal)totalSales * (product.SellingPrice - product.MarginAmount);
   var margin = revenue - cost;
   var marginPercentage = revenue > 0 ? (margin / revenue) * 100 : 0;
   ```

3. `ReportBuilderService.BuildMonthlyBreakdown()` — lines 44–46:
   ```csharp
   var monthlyRevenue = (decimal)monthlyUnitsSold * productData.SellingPrice;
   var monthlyCost = (decimal)monthlyUnitsSold * (productData.SellingPrice - productData.MarginAmount);
   var monthlyMargin = monthlyRevenue - monthlyCost;
   ```

`IMarginCalculator` and `MarginCalculator` already exist in `Services/MarginCalculator.cs` and are registered in `AnalyticsModule`. `GetProductMarginSummaryHandler` uses `IMarginCalculator` correctly. However, `GetMarginReportHandler` does not inject `IMarginCalculator` at all and re-implements the formula inline; `GetProductMarginAnalysisHandler` likewise has a private static `CalculateProductMargins()` method that reimplements it.

## Why it matters

The formula `margin = units × SellingPrice − units × (SellingPrice − MarginAmount)` simplifies to `units × MarginAmount`. If the business definition of margin changes (e.g. a different cost component is included), it must be updated in three places — and the existing `ReportBuilderService` deviation (it doesn't filter `SalesHistory` before summing, unlike the handler which sums all history) shows the copies have already drifted semantically.

## Suggested fix

Add a method to `IMarginCalculator` (or a companion static/extension helper) for single-product margin calculation:

```csharp
AnalysisMarginData CalculateForProduct(AnalyticsProduct product, IEnumerable<SalesDataPoint> salesInPeriod);
```

Implement it once in `MarginCalculator`. Update `GetMarginReportHandler` to inject `IMarginCalculator` and call this method instead of the inline formula. Update `GetProductMarginAnalysisHandler` the same way (removing the private `CalculateProductMargins` method). Update `ReportBuilderService.BuildMonthlyBreakdown()` to accept `AnalysisMarginData` per month rather than recalculating it internally.

---
_Filed by daily arch-review routine on 2026-05-28._