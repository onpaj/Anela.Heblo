# Architecture Review: Inject TimeProvider into GetInvoiceImportStatisticsHandler

## Skip Design: true

## Architectural Fit Assessment
This is a one-line-of-substance fix inside an existing MediatR handler; it introduces no new component, no new module, and no new contract. `TimeProvider` is already registered as a process-wide singleton (`services.AddSingleton(TimeProvider.System)` in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:131`) and is already consumed via constructor injection by three other components in the same Analytics module:

- `TimeWindowParser` (`Services/TimeWindowParser.cs`)
- `InvoiceImportStatisticsTile` (`DashboardTiles/InvoiceImportStatisticsTile.cs`)
- `GetBankStatementImportStatisticsHandler` (`UseCases/GetBankStatementImportStatistics/GetBankStatementImportStatisticsHandler.cs`)

`GetInvoiceImportStatisticsHandler` is the only time-aware class in the module still calling `DateTime.UtcNow` directly. `GetBankStatementImportStatisticsHandler` is structurally the closest sibling — same module, same layer (`IRequestHandler<TRequest,TResponse>`), same repository dependency (`IAnalyticsRepository`), same "compute a UTC date range, call repository" shape. It is the correct template to copy, and its test file (`GetBankStatementImportStatisticsHandlerTests.cs`) is the correct testing template. No new integration points, no DI registration changes, no cross-module impact.

## Proposed Architecture

### Component Overview
No new components. One existing node in the dependency graph gains one existing, already-available dependency:

```
DI container
  └─ TimeProvider.System (singleton, registered once in ServiceCollectionExtensions.cs)
       ├─ TimeWindowParser                          (already injected)
       ├─ InvoiceImportStatisticsTile                (already injected)
       ├─ GetBankStatementImportStatisticsHandler     (already injected)
       └─ GetInvoiceImportStatisticsHandler           (← add injection here)
```

### Key Design Decisions

#### Decision 1: Reuse the already-registered singleton TimeProvider vs. introduce a new abstraction
**Options considered:**
1. Add a constructor parameter of type `TimeProvider` and use `_timeProvider.GetUtcNow()`.
2. Introduce a module-specific clock abstraction (e.g. `IClock`) to decouple from BCL `TimeProvider`.

**Chosen approach:** Option 1 — inject the BCL `TimeProvider` directly, exactly as `GetBankStatementImportStatisticsHandler` does.

**Rationale:** The module has already standardized on `TimeProvider` (three existing consumers, one existing DI registration). Introducing a second abstraction would fragment the pattern for zero benefit — `TimeProvider` already supports deterministic testing via `Mock<TimeProvider>` / `FakeTimeProvider`, which is all this fix needs. This is also consistent with the spec's explicit instruction to match the sibling handler and not introduce a new testing convention.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Edit in place:

- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetInvoiceImportStatisticsHandlerTests.cs`

### Interfaces and Contracts
Constructor changes from:
```csharp
public GetInvoiceImportStatisticsHandler(
    IAnalyticsRepository analyticsRepository,
    IOptions<InvoiceImportOptions> invoiceImportOptions)
```
to:
```csharp
public GetInvoiceImportStatisticsHandler(
    IAnalyticsRepository analyticsRepository,
    IOptions<InvoiceImportOptions> invoiceImportOptions,
    TimeProvider timeProvider)
```
with a new `private readonly TimeProvider _timeProvider;` field assigned in the constructor body, matching the field-ordering and assignment style already used in `GetBankStatementImportStatisticsHandler`.

Body change — replace:
```csharp
var endDate = DateTime.UtcNow.Date;
endDate = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);
```
with:
```csharp
var endDate = _timeProvider.GetUtcNow().Date;
endDate = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);
```
Everything downstream (`startDate`, repository call, response projection) is untouched, per spec.

No public API contract (`GetInvoiceImportStatisticsRequest` / `GetInvoiceImportStatisticsResponse`) changes. MediatR auto-registers the handler by scanning, so no DI wiring change in `AnalyticsModule.cs` is needed — confirmed no explicit `services.AddScoped<GetInvoiceImportStatisticsHandler>` (or similar) registration exists anywhere in that file to touch.

### Data Flow
Unchanged except for the source of "now": `Handle()` still computes `[startDate, endDate]` from `daysBack`, calls `IAnalyticsRepository.GetInvoiceImportStatisticsAsync`, and projects results to `DailyInvoiceCountDto` with the threshold flag. The only difference is that "now" comes from the injected, mockable `TimeProvider.GetUtcNow()` instead of the static, unmockable `DateTime.UtcNow`.

### Test Update
`GetInvoiceImportStatisticsHandlerTests.cs` currently constructs the handler with two arguments in four places and asserts against live `DateTime.UtcNow.Date` in two tests (`Handle_ShouldUseConfigurableDefaultDaysBack`, `Handle_ShouldUseDefaultValuesWhenOptionsAreParameterless`), which is itself a latent flakiness risk (midnight rollover between `Handle()`'s internal clock read and the test's own `DateTime.UtcNow.Date` read). Follow `GetBankStatementImportStatisticsHandlerTests.cs` exactly:
```csharp
private readonly Mock<TimeProvider> _timeProviderMock;
private readonly DateTime _fixedDateTime = new DateTime(2025, 10, 14, 10, 0, 0, DateTimeKind.Utc);
...
_timeProviderMock = new Mock<TimeProvider>();
_timeProviderMock.Setup(x => x.GetUtcNow()).Returns(_fixedDateTime);
_handler = new GetInvoiceImportStatisticsHandler(_mockRepository.Object, options, _timeProviderMock.Object);
```
Pass `_timeProviderMock.Object` at all four construction sites (the shared `_handler` in the constructor, plus the three ad-hoc handlers built inside `Handle_ShouldUseDefaultThresholdWhenNotConfigured`, `Handle_ShouldUseConfigurableDefaultDaysBack`, `Handle_ShouldUseDefaultValuesWhenOptionsAreParameterless`), and replace their `DateTime.UtcNow.Date`-based expected values with `_fixedDateTime.Date`-based ones.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Missing a `new GetInvoiceImportStatisticsHandler(...)` call site during the constructor signature change, breaking the build | Low | Only two call sites exist in production code (none — MediatR auto-resolves) and four in the single test file; a build-wide grep for `new GetInvoiceImportStatisticsHandler(` (as the spec's FR-2 acceptance criteria requires) is sufficient to catch all of them before considering the change complete |
| Test-only regression: forgetting to update one of the two date-flakiness tests, leaving a hidden dependency on wall-clock time | Low | Both flagged tests (`Handle_ShouldUseConfigurableDefaultDaysBack`, `Handle_ShouldUseDefaultValuesWhenOptionsAreParameterless`) must be updated per FR-2; run the full test file after the change and confirm no `DateTime.UtcNow` reference remains in it |

## Specification Amendments
None. The spec is complete, correctly scoped, and its acceptance criteria already reference the exact sibling files and testing convention this review independently confirmed by reading the code. No additions needed.

## Prerequisites
None. `TimeProvider` is already registered in the DI container (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:131`); no migration, config, or infrastructure work is required before implementation starts.
