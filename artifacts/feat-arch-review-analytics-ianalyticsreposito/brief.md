## Module
Analytics

## Finding

`IAnalyticsRepository` declares `GetGroupMarginTotalsAsync` (line 26 of `Infrastructure/IAnalyticsRepository.cs`):

```csharp
Task<Dictionary<string, decimal>> GetGroupMarginTotalsAsync(
    DateTime fromDate,
    DateTime toDate,
    AnalyticsProductType[] productTypes,
    ProductGroupingMode groupingMode,
    CancellationToken cancellationToken = default);
```

The implementation is in `AnalyticsRepository.GetGroupMarginTotalsAsync()` (lines 41–68). No handler, service, or test calls this method — a grep across the entire `backend/` tree shows it is referenced only in the interface declaration and the implementation.

The implementation itself contains a `TODO` on line 49:
```csharp
// TODO: This would be optimized SQL query in real database implementation
// For now, use existing logic but avoid loading full objects
```

Additionally, `AnalyticsRepository.GetGroupKey()` (lines 79–88) is a private helper used only by `GetGroupMarginTotalsAsync` and is byte-for-byte identical to `MarginCalculator.GetGroupKey()` (lines 78–88 of `Services/MarginCalculator.cs`):

```csharp
return groupingMode switch
{
    ProductGroupingMode.Products => product.ProductCode,
    ProductGroupingMode.ProductFamily => product.ProductFamily ?? "Unknown",
    ProductGroupingMode.ProductCategory => product.ProductCategory ?? "Unknown",
    _ => product.ProductCode
};
```

## Why it matters

- **YAGNI**: the method is speculative ("would be optimized SQL query") and has no consumer. It adds interface surface area that must be implemented by any future `IAnalyticsRepository` mock or alternative implementation.
- **Duplication**: the private `GetGroupKey` copy in the repository will drift from `MarginCalculator.GetGroupKey()` independently (e.g. if a new `ProductGroupingMode` value is added, both must be updated).
- **Interface bloat**: `IAnalyticsRepository` exposes a method that no handler uses; this is an Interface Segregation violation.

## Suggested fix

Delete `GetGroupMarginTotalsAsync` from `IAnalyticsRepository` and its implementation from `AnalyticsRepository` (lines 41–68), along with the private `GetGroupKey` helper (lines 79–88). When a genuinely optimized aggregation query is needed in the future, it can be added at that time with a real caller. If the grouping key logic is ever needed in a repository, delegate to `IMarginCalculator.GetGroupKey()` rather than copying it.

---
_Filed by daily arch-review routine on 2026-05-28._