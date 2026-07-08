## Module
Analytics

## Finding
`GetInvoiceImportStatisticsHandler` (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs:36`) calls `DateTime.UtcNow` directly to determine the query end date:

```csharp
var endDate = DateTime.UtcNow.Date;
endDate = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);
var startDate = endDate.AddDays(-daysBack);
```

Every other time-aware component in the same module uses the injected `TimeProvider`:
- `TimeWindowParser` (line 8–9): `TimeProvider _timeProvider` → `_timeProvider.GetLocalNow()`
- `InvoiceImportStatisticsTile` (line 10–11): `TimeProvider _timeProvider` → `_timeProvider.GetUtcNow()`

`TimeProvider` is already registered in the DI container (both components prove this).

## Why it matters
Hard-coding `DateTime.UtcNow` makes the date range in this handler non-deterministic in tests. Unit tests for this handler cannot control "today" without static shims or other workarounds, breaking testability. It is also inconsistent with the established pattern in the module — `InvoiceImportStatisticsTile` (which calls the same repository method on the same data) uses `TimeProvider` correctly.

## Suggested fix
Inject `TimeProvider` and replace the direct call:

```csharp
public GetInvoiceImportStatisticsHandler(
    IAnalyticsRepository analyticsRepository,
    IOptions invoiceImportOptions,
    TimeProvider timeProvider)               // ← add this
{
    _analyticsRepository = analyticsRepository;
    _options = invoiceImportOptions.Value;
    _timeProvider = timeProvider;
}

// In Handle():
var endDate = _timeProvider.GetUtcNow().Date;
```

No DI registration change is needed — `TimeProvider` is already in the container.

---
_Filed by daily arch-review routine on 2026-07-05._
