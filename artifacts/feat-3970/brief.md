## Module
DataQuality

## Finding
`ProductPairingDqtJob` in `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs` imports and applies a Hangfire-specific attribute:

```csharp
using Hangfire;              // line 5
// ...
[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]  // line 40
public async Task ExecuteAsync(CancellationToken cancellationToken = default)
```

The other three DQT jobs (`InvoiceDqtJob`, `StockWriteBackDqtJob`, `LotStockReconciliationDqtJob`) contain no Hangfire references at all.

## Why it matters
Application-layer classes should not depend on infrastructure libraries like Hangfire. The `[AutomaticRetry]` attribute is a Hangfire-specific decorator that configures job-runner behaviour; it couples the Application class to the scheduler's programming model. If the scheduler is ever swapped, or if this class is tested outside a Hangfire context, the attribute is dead weight (and its import forces a Hangfire package dependency on the Application project). The inconsistency with the other three jobs is also a maintenance hazard.

## Suggested fix
Remove the `using Hangfire;` import and the `[AutomaticRetry]` attribute from `ProductPairingDqtJob`. If disabling Hangfire's default retry is required for this job, configure it when registering the job with Hangfire — e.g. in the infrastructure/adapter layer where `RecurringJob.AddOrUpdate` is called, or via a `JobFilterAttribute` applied at registration time — not inside the Application class.

---
_Filed by daily arch-review routine on 2026-08-28._
