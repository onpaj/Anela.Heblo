## Module
Analytics

## Finding

`backend/src/Anela.Heblo.Application/Features/Analytics/Services/ReportBuilderService.cs`, lines 8–23:

```csharp
public interface IReportBuilderService
{
    List<GetProductMarginAnalysisResponse.MonthlyMarginBreakdown> BuildMonthlyBreakdown(
        List<SalesDataPoint> salesData,
        AnalyticsProduct productData,
        DateTime startDate,
        DateTime endDate);

    List<GetMarginReportResponse.CategoryMarginSummary> BuildCategorySummaries(
        Dictionary<string, CategoryData> categoryTotals);

    GetMarginReportResponse.ProductMarginSummary BuildProductSummary(
        AnalyticsProduct product,
        AnalysisMarginData marginData);
}
```

All three return types are **nested classes defined inside specific use-case response objects** (`GetProductMarginAnalysisResponse.MonthlyMarginBreakdown`, `GetMarginReportResponse.CategoryMarginSummary`, `GetMarginReportResponse.ProductMarginSummary`). The interface lives in `Features/Analytics/Services/` but its signatures hard-code the shapes of two specific use-case responses located in `Features/Analytics/UseCases/GetProductMarginAnalysis/` and `Features/Analytics/UseCases/GetMarginReport/`.

This means:
- The `Services/` layer has a compile-time dependency on specific `UseCases/` types, inverting the expected dependency direction (`Services` should be depended on by use cases, not the other way around).
- A new use case that needs monthly margin breakdown cannot use `IReportBuilderService` without receiving a type named `GetProductMarginAnalysisResponse.MonthlyMarginBreakdown`, which is semantically incorrect.
- Renaming or restructuring either response class forces a change to the shared service interface and its implementation.

## Why it matters

Per the filesystem guidelines, `Services/` contains "Domain services and business logic" shared across use cases, while `UseCases/` contains per-request handlers with their request/response shapes. The interface's direction of dependency is reversed: `Services` should not import `UseCases`. This is an SRP and OCP violation — the service is closed to extension for new callers that need the same computations but with different response shapes.

## Suggested fix

Define intermediate data types in `Analytics/Contracts/` (which is already the home for shared DTOs like `AnalysisMarginData`, `TopProductDto`, `MonthlyProductMarginDto`):

```csharp
// Analytics/Contracts/MonthlyMarginBreakdownDto.cs
public class MonthlyMarginBreakdownDto
{
    public DateTime Month { get; set; }
    public decimal MarginAmount { get; set; }
    public decimal Revenue { get; set; }
    public decimal Cost { get; set; }
    public int UnitsSold { get; set; }
}

// Analytics/Contracts/CategoryMarginSummaryDto.cs
public class CategoryMarginSummaryDto
{
    public string Category { get; set; } = string.Empty;
    public decimal TotalMargin { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal AverageMarginPercentage { get; set; }
    public int ProductCount { get; set; }
    public int TotalUnitsSold { get; set; }
}

// Analytics/Contracts/ProductMarginSummaryDto.cs  (if not already duplicating TopProductDto)
public class ProductMarginSummaryDto { ... }
```

Update `IReportBuilderService` to return these contract types. The use-case handlers then map from the service's contract types to their own response nested types — a one-liner projection in each handler. This keeps the dependency arrows pointing inward (use cases → services → contracts), not outward.

---
_Filed by daily arch-review routine on 2026-05-27._