## Module
DataQuality

## Finding
The `ExecuteAsync` implementation is nearly identical across all five scheduled DQT job classes:

- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/InvoiceDqtJob.cs:40-57`
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs:43-57`
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/StockWriteBackDqtJob.cs:43-57`
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/LotStockReconciliationDqtJob.cs:42-57`
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/PriceComparisonDqtJob.cs:43-62`

Each method does the same five steps: check job-enabled flag -> compute target date -> log start -> create and persist `DqtRun` -> call `runner.RunAsync`. The only differences are the `DqtTestType` value and whether the target date is "today" or "yesterday".

Across 5 classes this is ~100 lines of near-identical logic. Any change to this flow (adding retry logic, changing the trigger-type, altering how the run is persisted) must be applied in five places.

## Why it matters

**DRY / KISS**: five copies of the same orchestration logic means five places to update and five places to make mistakes. It also hides the real variation (test type and date offset) behind identical boilerplate.

## Suggested fix

Extract a `DqtJobBase` abstract class that owns the common `ExecuteAsync` body and exposes two abstract members: the `DqtTestType` and a `DateOnly GetTargetDate(DateOnly today)` (or an `int DateOffsetDays` property). Each concrete job overrides only those two members plus `Metadata`. Since all five concrete jobs implement `IRecurringJob`, the base class can implement `IRecurringJob` and the subclasses remain compatible:

```csharp
public abstract class DqtJobBase : IRecurringJob
{
    protected abstract DqtTestType TestType { get; }
    protected abstract DateOnly GetTargetDate(DateOnly today);
    // ... shared fields from ctor ...

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName)) { ... }
        var date = GetTargetDate(DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime));
        var run = DqtRun.Start(TestType, date, date, DqtTriggerType.Scheduled, _timeProvider.GetUtcNow().DateTime);
        await _repository.AddAsync(run, ct);
        await _repository.SaveChangesAsync(ct);
        await RunAsync(run.Id, ct);
    }
}
```

`InvoiceDqtJob` injects `IInvoiceDqtJobRunner` specifically; it can still do so and override a protected `RunAsync` that delegates to the specific runner. The four drift jobs share the same `IDriftDqtJobRunner` override.

(Issue #4368: "[arch-review] DataQuality: ExecuteAsync body duplicated across all 5 DQT job classes")
