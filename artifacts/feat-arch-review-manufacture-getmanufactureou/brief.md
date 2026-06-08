## Module
Manufacture

## Finding
Two handlers use `DateTime.Now` (local server time) as the default upper bound for date range calculations — without injecting `TimeProvider`:

| File | Line | Usage |
|------|------|-------|
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOutput/GetManufactureOutputHandler.cs` | 31 | `var toDate = DateTime.Now;` |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs` | 58 | `var endDate = request.ToDate ?? DateTime.Now;` |

`GetManufactureOutputHandler` derives a `[fromDate, toDate]` window from `DateTime.Now` and passes it straight to `IManufactureHistoryClient.GetHistoryAsync`. `CalculateBatchPlanHandler.ResolveSalesRanges` uses `DateTime.Now` as the fallback `endDate` when no `TimePeriod` and no explicit `ToDate` are provided (the common case).

This is distinct from issues #2676–#2680 (which cover handlers that already have `TimeProvider` injected but bypass it for some fields). These two handlers have **no** `TimeProvider` injection at all.

## Why it matters
- **Correctness risk**: `DateTime.Now` reads the OS local clock. On any server configured to a non-UTC timezone (CET, for example), `DateTime.Now` at 23:45 is already the next calendar day in UTC. A date range that should include "today UTC" will be off by one when the server runs in a UTC+ zone.
- **Consistency**: the entire module moved to `TimeProvider` for testability. These two handlers are silent exceptions — there is nothing in the code to indicate they are intentional outliers.
- **Testability**: neither handler can be time-shifted in a unit test; the date range window is always anchored to wall-clock time.

Note: `GetManufactureOutputHandler` also uses `DateTime.Now` on lines 128–129 to re-derive `currentDate`/`endDate` for gap-filling, which has the same issue.

## Suggested fix
Inject `TimeProvider` into both handlers and replace `DateTime.Now` with `_timeProvider.GetUtcNow().DateTime`:

```csharp
// GetManufactureOutputHandler
public GetManufactureOutputHandler(
    IManufactureHistoryClient manufactureHistoryClient,
    IManufactureCatalogSource catalogSource,
    ILogger<GetManufactureOutputHandler> logger,
    TimeProvider timeProvider)          // add
{
    ...
    _timeProvider = timeProvider;
}

// line 31
var toDate = _timeProvider.GetUtcNow().DateTime;

// lines 128–129  (gap-filling loop)
var currentDate = new DateTime(fromDate.Year, fromDate.Month, 1);
var endDate = new DateTime(_timeProvider.GetUtcNow().Year, _timeProvider.GetUtcNow().Month, 1);
```

```csharp
// CalculateBatchPlanHandler — ResolveSalesRanges (line 58)
private IReadOnlyList<DateRange> ResolveSalesRanges(CalculateBatchPlanRequest request)
{
    if (request.TimePeriod.HasValue)
        return _timePeriodResolver.Resolve(...);

    var endDate = request.ToDate ?? _timeProvider.GetUtcNow().DateTime;   // was DateTime.Now
    var startDate = request.FromDate ?? endDate.AddDays(-DefaultFallbackDays);
    return new[] { new DateRange(startDate, endDate) };
}
```

No DI registration changes needed — `TimeProvider` is a framework singleton.

---
_Filed by daily arch-review routine on 2026-06-06._