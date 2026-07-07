# Design: Inject `TimeProvider` into `GetBankStatementImportStatisticsHandler`

## Component Design

### `GetBankStatementImportStatisticsHandler`
Location: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetBankStatementImportStatistics/GetBankStatementImportStatisticsHandler.cs`

Responsibility: `IRequestHandler<GetBankStatementImportStatisticsRequest, GetBankStatementImportStatisticsResponse>` — resolves the effective `startDate`/`endDate` window (defaulting to "today minus 30 days" through "today" when not supplied in the request) and delegates to `IAnalyticsRepository` to fetch bank statement import statistics.

Change: add `TimeProvider` as a second constructor dependency, alongside the existing `IAnalyticsRepository`, and use it to resolve "now" instead of calling the static `DateTime.UtcNow` directly. This brings the handler in line with the pattern already used by `InvoiceImportStatisticsTile` and `TimeWindowParser` in the same module.

```csharp
public class GetBankStatementImportStatisticsHandler
    : IRequestHandler<GetBankStatementImportStatisticsRequest, GetBankStatementImportStatisticsResponse>
{
    private readonly IAnalyticsRepository _analyticsRepository;   // existing, unchanged
    private readonly TimeProvider _timeProvider;                  // new

    public GetBankStatementImportStatisticsHandler(
        IAnalyticsRepository analyticsRepository,
        TimeProvider timeProvider)
    {
        _analyticsRepository = analyticsRepository;
        _timeProvider = timeProvider;
    }

    public async Task<GetBankStatementImportStatisticsResponse> Handle(
        GetBankStatementImportStatisticsRequest request,
        CancellationToken cancellationToken)
    {
        var endDate = request.EndDate ?? _timeProvider.GetUtcNow().Date; // was: DateTime.UtcNow.Date
        var startDate = request.StartDate ?? endDate.AddDays(-30);       // unchanged

        // DateTimeKind normalization block (unchanged) — still required, since
        // GetUtcNow().Date yields Kind == Unspecified, same as UtcNow.Date did.
        ...
    }
}
```

No new interfaces, no new abstractions. `TimeProvider` resolves via the existing application-wide singleton registration (`services.AddSingleton(TimeProvider.System)` in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:131`) — no DI registration changes required.

### `GetBankStatementImportStatisticsHandlerTests` (new)
Location: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetBankStatementImportStatisticsHandlerTests.cs`

Responsibility: unit-test coverage for the handler's date-resolution logic, using the mocking pattern from `InvoiceImportStatisticsTileTests.cs` (`Mock<TimeProvider>` + `.Setup(x => x.GetUtcNow()).Returns(fixedDateTimeOffset)`) for the clock, and the `Handle(...)` + `_mockRepository.Verify(...)` structure from `GetInvoiceImportStatisticsHandlerTests.cs` for handler-level assertions.

Test cases:
1. `EndDate == null` and `StartDate == null` → `IAnalyticsRepository.GetBankStatementImportStatisticsAsync` is invoked with `endDate` equal to the mocked fixed date and `startDate` equal to `fixedDate.AddDays(-30)`, proving the branch is driven by the injected `TimeProvider` rather than the real wall clock.
2. `EndDate` (and/or `StartDate`) explicitly supplied → the mocked `TimeProvider.GetUtcNow()` is not what drives the resulting dates; repository is called with the supplied values (pass-through behavior unchanged).

No other components are added, removed, or restructured. `IAnalyticsRepository`, the MediatR request/response contract, and the controller endpoint are all unaffected.

## Data Schemas

No schema changes. `GetBankStatementImportStatisticsRequest`, `GetBankStatementImportStatisticsResponse`, and `DailyBankStatementStatistics` are unchanged — this is purely a constructor-dependency and internal-logic change inside the handler. No database, API contract, or event-payload changes are introduced.
