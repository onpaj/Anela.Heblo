## Module
Manufacture

## Finding
`ProductionActivityAnalyzer` uses `DateTime.UtcNow` directly for two business-logic time cutoffs, with no `TimeProvider` injection:

```
backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ProductionActivityAnalyzer.cs
  line 17: var cutoffDate = DateTime.UtcNow.AddDays(-dayThreshold);
  line 45: var analysisStartDate = DateTime.UtcNow.AddMonths(-analysisMonths);
```

These cutoffs determine whether a product is classified as "in active production" (`IsInActiveProduction`) and compute average production frequency (`CalculateAverageProductionFrequency`). Both results feed into the severity classifications shown on the stock analysis dashboard, and the service has dedicated tests in `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ProductionActivityAnalyzerTests.cs`.

## Why it matters
The two methods implement time-windowed business logic: "was there a manufacture run in the last N days?" and "what is the average interval over the last M months?". Because the cutoff is hard-wired to wall-clock time, the tests cannot set up deterministic scenarios (e.g. "a run 25 days ago is active with a 30-day threshold but not with a 20-day threshold"). Any test that seeds history records with relative dates is valid only on the day it was written; it will silently drift as time passes. This is inconsistent with every other time-aware handler in the module, which all inject `TimeProvider` for exactly this reason.

## Suggested fix
1. Add `TimeProvider _timeProvider` to the constructor.
2. Replace `DateTime.UtcNow` with `_timeProvider.GetUtcNow().DateTime` on lines 17 and 45.
3. In `ManufactureModule.AddManufactureModule` (already registers `IProductionActivityAnalyzer`), no DI change is needed — `TimeProvider` is registered as a singleton by the framework.

```csharp
public ProductionActivityAnalyzer(ILogger<ProductionActivityAnalyzer> logger, TimeProvider timeProvider)
{
    _logger = logger;
    _timeProvider = timeProvider;
}

// line 17
var cutoffDate = _timeProvider.GetUtcNow().DateTime.AddDays(-dayThreshold);

// line 45
var analysisStartDate = _timeProvider.GetUtcNow().DateTime.AddMonths(-analysisMonths);
```

Test fixtures can then pass `FakeTimeProvider` and seed history records relative to the frozen "now".

---
_Filed by daily arch-review routine on 2026-06-06._