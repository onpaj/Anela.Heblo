# Design: Inject TimeProvider into GetInvoiceImportStatisticsHandler

## Component Design

No new components. Existing MediatR handler `GetInvoiceImportStatisticsHandler`
(`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs`)
gains one additional constructor dependency: the already-registered singleton `TimeProvider`
(`services.AddSingleton(TimeProvider.System)`), consistent with `TimeWindowParser`,
`InvoiceImportStatisticsTile`, and `GetBankStatementImportStatisticsHandler`.

**Constructor contract change:**

```csharp
public GetInvoiceImportStatisticsHandler(
    IAnalyticsRepository analyticsRepository,
    IOptions<InvoiceImportOptions> invoiceImportOptions,
    TimeProvider timeProvider)
```

- New field: `private readonly TimeProvider _timeProvider;`, assigned in the constructor,
  matching field-ordering/assignment style in `GetBankStatementImportStatisticsHandler`.
- No interface (`IRequestHandler<TRequest, TResponse>`) or module registration changes —
  MediatR resolves the handler by assembly scan; DI resolves `TimeProvider` from the
  existing singleton registration.

**`Handle()` body change:**

- Replace `var endDate = DateTime.UtcNow.Date;` with `var endDate = _timeProvider.GetUtcNow().Date;`.
- `endDate = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);`, `startDate = endDate.AddDays(-daysBack)`,
  the repository call (`GetInvoiceImportStatisticsAsync(startDate, endDate, request.DateType, cancellationToken)`),
  threshold logic, and response projection are all unchanged.

**Test contract change:**

`backend/test/Anela.Heblo.Tests/Features/Analytics/GetInvoiceImportStatisticsHandlerTests.cs`
follows the `GetBankStatementImportStatisticsHandlerTests.cs` pattern: a `Mock<TimeProvider>`
field stubbed with a fixed `DateTimeOffset` via `GetUtcNow()`, passed as the third constructor
argument at all four `new GetInvoiceImportStatisticsHandler(...)` call sites (shared `_handler`
plus the three ad-hoc handlers in `Handle_ShouldUseDefaultThresholdWhenNotConfigured`,
`Handle_ShouldUseConfigurableDefaultDaysBack`, `Handle_ShouldUseDefaultValuesWhenOptionsAreParameterless`).
Assertions that previously read `DateTime.UtcNow.Date` are rebased on the mock's fixed date.

## Data Schemas

No schema changes. `GetInvoiceImportStatisticsRequest`, `GetInvoiceImportStatisticsResponse`,
and the `DailyInvoiceCountDto` projection are byte-for-byte unchanged. No new API endpoints,
database schema, or event payloads are introduced — this is an internal time-source
substitution only.
