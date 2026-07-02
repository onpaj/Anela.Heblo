## Module
MarketingInvoices

## Finding
There is a contradiction between the handler's stated intent and the service's actual behavior when a batch-level DB flush fails.

**Handler comment** (`backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/ImportMarketingInvoicesHandler.cs`, lines 50–52):
```csharp
// Import-time exceptions are intentionally NOT caught here — they must
// propagate to the job's catch-log-rethrow so Hangfire can retry.
var result = await _importService.ImportAsync(source, request.From, request.To, cancellationToken);
```

**Service behavior** (`backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs`, lines 93–109):
```csharp
try
{
    await _repository.SaveChangesAsync(ct);
    result.Imported = stagedCount;
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to persist {Count} marketing transactions for {Platform}", ...);
    result.Failed += stagedCount;
    // result.Imported intentionally stays 0 — nothing was committed.
}
```

The service catches `SaveChangesAsync` exceptions (transient DB connectivity, deadlock, constraint violation) and swallows them. It returns a `MarketingImportResult` with `Imported=0` and `Failed=N` instead of rethrowing.

Consequence: the handler never sees an exception, so the Hangfire job (`MetaAdsInvoiceImportJob` / `GoogleAdsInvoiceImportJob`) logs `"completed. Imported=0, Skipped=0, Failed=N"` at `Information` level and exits normally — Hangfire records a **success** and schedules no retry.

The per-transaction catch (lines 82–90, swallowing individual `AddAsync` errors) is intentional — partial failure should not abort the run. But a `SaveChangesAsync` failure is categorically different: it means **no** staged transaction was persisted and the failure is likely transient (DB blip, connection reset). This is exactly the case the handler comment says should trigger a Hangfire retry.

## Why it matters
- A transient DB failure during flush silently drops all transactions staged in that run. They will only be re-imported on the next scheduled run (up to 12 hours later for the twice-daily jobs).
- Hangfire's job history shows a successful execution with `Failed=N`, obscuring the fact that a system-level error occurred.
- The architectural intent (comment in handler) and the runtime behaviour are directly contradictory, making the failure mode invisible to operators.

## Suggested fix
Rethrow in the `SaveChangesAsync` catch block after logging:

```csharp
catch (Exception ex)
{
    _logger.LogError(
        ex,
        "Failed to persist {Count} marketing transactions for {Platform}",
        stagedCount, source.Platform);
    result.Failed += stagedCount;
    throw; // propagate so the job can catch-log-rethrow and Hangfire schedules a retry
}
```

This ensures a DB flush failure produces a Hangfire job failure, surfaces properly in monitoring, and triggers automatic retry — which is exactly what the handler comment documents as the intended design.

---
_Filed by daily arch-review routine on 2026-06-29._
