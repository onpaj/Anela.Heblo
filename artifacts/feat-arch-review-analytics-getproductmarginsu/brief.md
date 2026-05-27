## Module
Analytics

## Finding

`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs:14–28`

```csharp
public class GetProductMarginSummaryHandler : IRequestHandler<...>
{
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly MarginCalculator _marginCalculator;           // ← concrete class
    private readonly MonthlyBreakdownGenerator _monthlyBreakdownGenerator;  // ← concrete class
```

`MarginCalculator` lives in the **Domain layer** (`Anela.Heblo.Domain.Features.Analytics.MarginCalculator`) while the handler is in the **Application layer** — the Application layer takes a direct dependency on a Domain layer concrete class with no interface. `MonthlyBreakdownGenerator` is in the Application layer itself but is also injected as a concrete type with no interface.

Both are registered as concrete types in `AnalyticsModule.cs` (lines 37–38), labelled "Legacy services (keeping for backward compatibility)" — but they are the **active primary path** for every `GetProductMarginSummary` request; there is no newer replacement. The `IProductFilterService` and `IReportBuilderService` injected into the sibling handlers were extracted behind interfaces; `MarginCalculator` and `MonthlyBreakdownGenerator` were not.

## Why it matters

- **DIP**: The handler depends on concrete implementations, not abstractions. The Application layer's handler cannot be tested without constructing a real `MarginCalculator`.
- **Inconsistency**: `IProductFilterService` and `IReportBuilderService` follow the correct interface pattern in the same module. `MarginCalculator` and `MonthlyBreakdownGenerator` are exceptions with no documented reason.
- **Misleading label**: "Legacy services" implies they will be removed or replaced, but they are load-bearing. The comment creates confusion about whether this code path is intentional.

## Suggested fix

Extract interfaces for both classes in `Application/Features/Analytics/Services/`:

```csharp
public interface IMarginCalculator
{
    Task<MarginCalculationResult> CalculateAsync(...);
    string GetGroupKey(AnalyticsProduct product, ProductGroupingMode groupingMode);
    string GetGroupDisplayName(string groupKey, ProductGroupingMode mode, List<AnalyticsProduct> products);
    decimal GetMarginAmountForLevel(AnalyticsProduct product, string marginLevel);
}

public interface IMonthlyBreakdownGenerator
{
    List<MonthlyProductMarginDto> Generate(
        MarginCalculationResult result, DateRange dateRange,
        ProductGroupingMode groupingMode, string marginLevel = "M2");
}
```

Register them behind their interfaces in `AnalyticsModule` and update the handler to inject `IMarginCalculator` and `IMonthlyBreakdownGenerator`. Remove the "Legacy services" comment, which is inaccurate. Move `MarginCalculator` from the Domain layer to the Application layer — it is an application-level calculator service with async streaming logic, not a domain entity or value object.

---
_Filed by daily arch-review routine on 2026-05-26._