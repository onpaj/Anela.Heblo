## Module
Invoices

## Finding
`IssuedInvoiceRepository.GetSyncStatsAsync` (lines 35–57 of `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs`) applies the same `WHERE InvoiceDate BETWEEN @from AND @to` predicate five times and emits five separate SQL round-trips:

```csharp
var totalInvoices       = await query.CountAsync(cancellationToken);             // 1
var syncedInvoices      = await query.CountAsync(x => x.IsSynced, ...);         // 2
// unsyncedInvoices is arithmetic on the above two
var invoicesWithErrors  = await query.CountAsync(x => x.ErrorType.HasValue, ...) ; // 3
var criticalErrors      = await query.CountAsync(x => x.ErrorType.HasValue && x.ErrorType != IssuedInvoiceErrorType.InvoicePaired, ...); // 4
var lastSyncTime        = await query...MaxAsync(...);                           // 5
```

The frontend `useIssuedInvoiceSyncStats` hook calls this endpoint with `staleTime: 5m`, which means it can fire multiple times per session across the InvoiceImportStatistics and sync-stats pages.

## Why it matters
Five DB round-trips where one would do. Under a 30-day window this scans the same index/table slice five times sequentially. As the `issued_invoices` table grows (every daily import adds rows), latency compounds and the endpoint becomes the slowest call in the module.

## Suggested fix
Replace the five `CountAsync` / `MaxAsync` calls with a single projection into an anonymous aggregate:

```csharp
var stats = await query
    .GroupBy(_ => 1)
    .Select(g => new
    {
        Total          = g.Count(),
        Synced         = g.Count(x => x.IsSynced),
        WithErrors     = g.Count(x => x.ErrorType.HasValue),
        Critical       = g.Count(x => x.ErrorType.HasValue && x.ErrorType != IssuedInvoiceErrorType.InvoicePaired),
        LastSyncTime   = g.Max(x => (DateTime?)x.LastSyncTime)
    })
    .FirstOrDefaultAsync(cancellationToken);
```

EF Core translates this into a single `SELECT COUNT(*) ... COUNT(CASE ...) ...` statement.

---
_Filed by daily arch-review routine on 2026-07-08._
