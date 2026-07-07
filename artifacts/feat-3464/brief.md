## Module
Analytics

## Finding
`GetProductMarginSummaryHandler` (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs:22`) takes a concrete `TimeWindowParser` dependency:

```csharp
public GetProductMarginSummaryHandler(
    IAnalyticsRepository analyticsRepository,
    IMarginCalculator marginCalculator,
    IMonthlyBreakdownGenerator monthlyBreakdownGenerator,
    TimeWindowParser timeWindowParser)   // ← concrete class, not an interface
```

Every other service in this module has an interface: `IProductFilterService`, `IReportBuilderService`, `IMarginCalculator`, `IMonthlyBreakdownGenerator`. `TimeWindowParser` is the sole exception. It is also registered as a concrete type in `AnalyticsModule.cs:47`:

```csharp
services.AddScoped<TimeWindowParser>();
```

## Why it matters
Injecting a concrete class instead of an abstraction (DIP) makes the handler harder to unit-test — tests must construct the real `TimeWindowParser` (which depends on `TimeProvider`) rather than a test double. It is also inconsistent with the pattern established by the other four services in the same module.

## Suggested fix
Extract an interface in `Services/`:

```csharp
public interface ITimeWindowParser
{
    (DateTime fromDate, DateTime toDate) ParseTimeWindow(string timeWindow);
}
```

Update the registration in `AnalyticsModule.cs`:

```csharp
services.AddScoped<ITimeWindowParser, TimeWindowParser>();
```

Update the handler constructor to inject `ITimeWindowParser`.

---
_Filed by daily arch-review routine on 2026-07-03._
