## Module
Analytics

## Finding

`backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct.cs`, lines 59–64:

```csharp
/// <summary>
/// Result object for margin calculations
/// </summary>
public class MarginCalculationResult
{
    public required Dictionary<string, decimal> GroupTotals { get; init; }
    public required Dictionary<string, List<AnalyticsProduct>> GroupProducts { get; init; }
    public required decimal TotalMargin { get; init; }
}
```

`MarginCalculationResult` holds `Dictionary<string, List<AnalyticsProduct>>` — a raw grouping of in-memory product lists keyed by a string group key. This is the output of `IMarginCalculator.CalculateAsync()` (an Application-layer service) and is consumed exclusively by `GetProductMarginSummaryHandler` in the Application layer.

## Why it matters

The Domain layer should model business concepts (entities, value objects, domain services, repository interfaces). `MarginCalculationResult` is not a domain concept — it is a computation result produced by an Application-layer service that groups and totals products for a specific use case. By placing it in `Domain`, the Domain layer now carries knowledge of how the Application layer organizes data, which inverts the dependency rule.

Concretely:
- The Domain layer types (`AnalyticsProduct.cs`) are tightly coupled to a specific Application-layer use-case structure (dictionaries of grouped products).
- A separate use case that needs a differently shaped calculation result must either reuse this type (forcing a fit) or define a second one in a different layer.

The co-located `DateRange` record (line 54) and `SalesDataPoint` class (line 44) are legitimate domain types. `MarginCalculationResult` is not.

## Suggested fix

Move `MarginCalculationResult` to the Application layer, e.g.:

```
backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculationResult.cs
```

or inline it as a nested type of `IMarginCalculator` / `MarginCalculator` in `Services/MarginCalculator.cs`. Remove it from `Domain/Features/Analytics/AnalyticsProduct.cs`. No consumer outside the Application layer references it (verified by grep).

---
_Filed by daily arch-review routine on 2026-05-28._