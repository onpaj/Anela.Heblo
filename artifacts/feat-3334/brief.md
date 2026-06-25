## Module
Analytics

## Finding
`backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs:9`

```csharp
public static class TimeWindowParser
{
    public static (DateTime fromDate, DateTime toDate) ParseTimeWindow(string timeWindow)
    {
        var today = DateTime.Today;  // ← static ambient date
        return timeWindow switch
        {
            "current-year" => (new DateTime(today.Year, 1, 1), today),
            ...
        };
    }
}
```

`DateTime.Today` is a static ambient value. Any unit test that exercises `GetProductMarginSummaryHandler` — which calls `TimeWindowParser.ParseTimeWindow` directly at line 31 — cannot control or freeze the date, making test assertions about the computed date range fragile and time-dependent.

Compare with `InvoiceImportStatisticsTile`, which correctly uses the injected `TimeProvider` abstraction (`backend/src/Anela.Heblo.Application/Features/Analytics/DashboardTiles/InvoiceImportStatisticsTile.cs:10,42`):
```csharp
var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().Date);
```

The project already has `TimeProvider` registered in DI (it's a `System.TimeProvider` — a .NET 8 built-in).

## Why it matters
- `GetProductMarginSummaryHandlerTests.cs` exists but any test that depends on a specific `fromDate`/`toDate` pair for a given `TimeWindow` string is clock-sensitive and will produce different results run on different days
- The inconsistency with `InvoiceImportStatisticsTile` shows two competing patterns in the same module
- Silent fallback to `"current-year"` for unrecognized `timeWindow` strings (the `_` arm) swallows typos silently — a logged warning or thrown `ArgumentException` would be safer

## Suggested fix
The smallest fix: convert `TimeWindowParser` from a `static class` to an instance class injected with `TimeProvider`:

```csharp
public class TimeWindowParser
{
    private readonly TimeProvider _timeProvider;
    public TimeWindowParser(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public (DateTime fromDate, DateTime toDate) ParseTimeWindow(string timeWindow)
    {
        var today = _timeProvider.GetLocalNow().Date;
        return timeWindow switch { ... };
    }
}
```

Register it in `AnalyticsModule.cs` and inject it into `GetProductMarginSummaryHandler`. `TimeProvider.System` can be used in tests for the real clock, or a `FakeTimeProvider` for deterministic date tests.

---
_Filed by daily arch-review routine on 2026-06-24._
