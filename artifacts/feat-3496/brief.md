## Module
Analytics

## Finding
The private static method `HasSalesInPeriod` is copy-pasted identically into two handlers:

**`GetMarginReportHandler.cs:125-128`**
```csharp
private static bool HasSalesInPeriod(Domain.Features.Analytics.AnalyticsProduct product, DateTime startDate, DateTime endDate)
{
    return product.SalesHistory.Any(s => s.Date >= startDate && s.Date <= endDate);
}
```

**`GetProductMarginAnalysisHandler.cs:71-74`**
```csharp
private static bool HasSalesInPeriod(AnalyticsProduct productData, DateTime startDate, DateTime endDate)
{
    return productData.SalesHistory.Any(s => s.Date >= startDate && s.Date <= endDate);
}
```

The method bodies are bit-for-bit identical. The only difference is the parameter name (`product` vs `productData`).

## Why it matters
If the date-range semantics ever change (e.g., exclusive end date, UTC normalisation), the fix must be applied in two places. Because both methods are private, the duplication is invisible to consumers and easy to miss in a PR review. It is also inconsistent with the module's pattern of extracting shared logic into named services (`IMarginCalculator`, `IProductFilterService`, etc.).

## Suggested fix
Add an extension method on `AnalyticsProduct` in the Domain layer (or as a static helper in the Analytics application layer):

```csharp
// Domain/Features/Analytics/AnalyticsProductExtensions.cs
public static class AnalyticsProductExtensions
{
    public static bool HasSalesInPeriod(this AnalyticsProduct product, DateTime startDate, DateTime endDate)
        => product.SalesHistory.Any(s => s.Date >= startDate && s.Date <= endDate);
}
```

Both handlers then become:
```csharp
if (!product.HasSalesInPeriod(startDate, endDate))
    continue; // or return error
```

No behaviour change; one canonical implementation.

---
_Filed by daily arch-review routine on 2026-07-06._
