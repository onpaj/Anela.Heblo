## Module
DataQuality

## Finding
All three scheduled DQT jobs call `AddAsync` but **not** `SaveChangesAsync` before passing control to the job runner. The run record only reaches the database when the runner's `finally` block fires at the end of execution.

**`InvoiceDqtJob.ExecuteAsync`** (`backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/InvoiceDqtJob.cs`, lines 48–51):
```csharp
var run = DqtRun.Start(DqtTestType.IssuedInvoiceComparison, yesterday, yesterday, DqtTriggerType.Scheduled);
await _repository.AddAsync(run, cancellationToken);
// ❌ SaveChangesAsync missing here
await _jobRunner.RunAsync(run.Id, cancellationToken);
```

Same pattern in `ProductPairingDqtJob.cs` (lines 48–50) and `StockWriteBackDqtJob.cs`.

By contrast, the **manual trigger** (`RunDqtHandler.cs`, lines 40–42) correctly persists before handing off:
```csharp
await _repository.AddAsync(run, cancellationToken);
await _repository.SaveChangesAsync(cancellationToken);  // ✅ run is in DB before fire-and-forget
```

This works in the happy path (because the job runner shares the same EF context scope and will call `SaveChangesAsync` in its `finally` block), but breaks on failure:
- If the application crashes or the process is killed during execution, the `finally` block never runs and **there is no record in the database** that the run was ever attempted.
- A monitoring operator would see a gap in run history with no indication of whether the job ran, failed, or was never scheduled.

## Why it matters
- **Data integrity**: failed or crashed runs leave no audit trail in scheduled mode.
- **Debuggability**: without a persisted `DqtRunStatus.Running` record, diagnosing why a nightly run didn't complete requires log archaeology instead of a simple database query.
- **Inconsistency**: the manual and scheduled paths behave differently under the same failure scenario.

## Suggested fix
Add `SaveChangesAsync` immediately after `AddAsync` in each scheduled job, before calling `RunAsync`:

```csharp
var run = DqtRun.Start(...);
await _repository.AddAsync(run, cancellationToken);
await _repository.SaveChangesAsync(cancellationToken); // ensure run is recorded before execution
await _jobRunner.RunAsync(run.Id, cancellationToken);
```

Applies to `InvoiceDqtJob.cs`, `ProductPairingDqtJob.cs`, and `StockWriteBackDqtJob.cs` — identical one-line fix in each.

---
_Filed by daily arch-review routine on 2026-06-01._