## Module
Analytics

## Finding
`GetBankStatementImportStatisticsHandler` (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetBankStatementImportStatistics/GetBankStatementImportStatisticsHandler.cs:23`) accesses system time directly:

```csharp
var endDate = request.EndDate ?? DateTime.UtcNow.Date;
```

The module already established the correct pattern: `InvoiceImportStatisticsTile` (line 46) and `TimeWindowParser` both inject `TimeProvider` and call `_timeProvider.GetUtcNow()`. Issue #3488 filed the same finding for `GetInvoiceImportStatisticsHandler` — this is its sibling handler with an identical gap.

## Why it matters
When `request.EndDate` is null (the common path — the frontend does not pass explicit end dates for this endpoint), the handler falls back to `DateTime.UtcNow.Date`. Unit tests cannot control "today" without static shims, making the default-date branch untestable in isolation. It is also inconsistent with the established module pattern.

## Suggested fix
Inject `TimeProvider` (no DI registration change needed — it is already in the container):

```csharp
public GetBankStatementImportStatisticsHandler(
    IAnalyticsRepository analyticsRepository,
    TimeProvider timeProvider)               // ← add this
{
    _analyticsRepository = analyticsRepository;
    _timeProvider = timeProvider;
}

// In Handle():
var endDate = request.EndDate ?? _timeProvider.GetUtcNow().Date;
```

---
_Filed by daily arch-review routine on 2026-07-06._
