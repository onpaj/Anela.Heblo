# Architecture Review: Inject TimeProvider into TimeWindowParser

## Skip Design: true

## Architectural Fit Assessment

This change is a textbook testability refactor. The pattern already exists in this codebase — `InvoiceImportStatisticsTile` shows the exact model: `TimeProvider` injected via constructor, stored as `private readonly TimeProvider _timeProvider`, consumed via `_timeProvider.GetUtcNow()` or `_timeProvider.GetLocalNow()`. The spec asks us to replicate that pattern for `TimeWindowParser`.

The one genuine architectural question the spec raises — silently — is whether `TimeWindowParser` should remain a standalone service or be folded inline into its single caller. Given the spec explicitly scopes out `GetProductMarginAnalysisHandler` and `GetMarginReportHandler`, and the class name `TimeWindowParser` implies shared utility, keeping it as a distinct injectable service is the right call. Future handlers can reuse it without reaching back through another handler.

The main integration points are:
1. `TimeWindowParser` (source of the ambient date dependency)
2. `GetProductMarginSummaryHandler` (sole current consumer, uses static call at line 31)
3. `AnalyticsModule` (DI registration)
4. `GetProductMarginSummaryHandlerTests` (tests that mirror `DateTime.Today` and must be frozen)

`TimeProvider.System` is already registered as a singleton in `ServiceCollectionExtensions.cs` at line 132. No infrastructure change is needed.

## Proposed Architecture

### Component Overview

```
ServiceCollectionExtensions
  └─ AddSingleton(TimeProvider.System)           [already exists, no change]

AnalyticsModule.AddAnalyticsModule()
  └─ AddScoped<TimeWindowParser>()               [new]

GetProductMarginSummaryHandler
  ├─ IAnalyticsRepository          (existing)
  ├─ IMarginCalculator             (existing)
  ├─ IMonthlyBreakdownGenerator    (existing)
  └─ TimeWindowParser              (new injection)
       └─ TimeProvider             (singleton, resolved transitively)

GetProductMarginSummaryHandlerTests
  └─ FakeTimeProvider(frozenDate)  [replaces DateTime.Today usage in test body]
```

### Key Design Decisions

#### Decision 1: Scoped vs Singleton lifetime for TimeWindowParser

**Options considered:**
- `AddSingleton<TimeWindowParser>()` — simplest, stateless class holds only an injected singleton
- `AddScoped<TimeWindowParser>()` — consistent with other services registered in `AnalyticsModule`

**Chosen approach:** `AddScoped<TimeWindowParser>()`

**Rationale:** All other services in `AnalyticsModule` (`IProductFilterService`, `IReportBuilderService`, `IMarginCalculator`, `IMonthlyBreakdownGenerator`) are scoped. A singleton would also work given `TimeWindowParser` holds no mutable state, but matching the module's established lifetime convention avoids a subtle registration mismatch if the handler or its dependencies are ever scoped-chain-validated. `TimeProvider.System` is a singleton and is safe to inject into a scoped consumer.

#### Decision 2: GetLocalNow vs GetUtcNow

**Options considered:**
- `_timeProvider.GetUtcNow().Date` — UTC date
- `_timeProvider.GetLocalNow().Date` — local server date

**Chosen approach:** `_timeProvider.GetLocalNow().Date`

**Rationale:** The existing `TimeWindowParser` uses `DateTime.Today`, which returns the **local** date. The spec explicitly prescribes `_timeProvider.GetLocalNow().Date` (FR-1). The computed date ranges (year boundaries, month offsets) are relative to a business calendar, not a UTC timestamp. Using `GetLocalNow()` preserves the original semantics while gaining testability. This matches the correct call to use here — `InvoiceImportStatisticsTile` uses `GetUtcNow()` only because it computes UTC ranges for database queries; `TimeWindowParser` computes business date ranges, so local is appropriate.

#### Decision 3: FakeTimeProvider vs Mock\<TimeProvider\>

**Options considered:**
- `Mock<TimeProvider>` via Moq — used in older tests (`GetProductMarginsHandlerTests` plan from 2026-06-03)
- `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` — used in newer tests (`UpcomingProductionTileTests`)

**Chosen approach:** `FakeTimeProvider`

**Rationale:** `Microsoft.Extensions.TimeProvider.Testing` is already referenced in `Anela.Heblo.Tests.csproj` (version 8.1.0) and `UpcomingProductionTileTests` uses it as the current pattern. `FakeTimeProvider` requires no `Setup()` boilerplate — `new FakeTimeProvider(frozenDate)` is all you need. It is the more recent and ergonomic standard for this project; use it consistently going forward.

#### Decision 4: ArgumentException on unrecognised time window

**Options considered:**
- Keep the silent fallback `_ => (new DateTime(today.Year, 1, 1), today)` — hides bugs
- Throw `ArgumentException` — fails fast, surfaces bad input immediately

**Chosen approach:** Throw `ArgumentException` with the offending value

**Rationale:** The silent fallback makes invalid `TimeWindow` strings indistinguishable from `"current-year"` at the call site. The spec (FR-4) is correct: fail loudly. The handler already owns the `TimeWindow` value from the request; if it reaches `TimeWindowParser` with an unrecognised string it is a programming error, not a user-recoverable condition. `ArgumentException` is appropriate (not `ArgumentOutOfRangeException`, since the value is a string not a numeric range).

## Implementation Guidance

### Directory / Module Structure

No new files. Modify only these four existing files:

```
backend/src/Anela.Heblo.Application/
  Features/Analytics/
    Services/
      TimeWindowParser.cs                          ← convert static → instance, inject TimeProvider, throw on fallback
    AnalyticsModule.cs                             ← add AddScoped<TimeWindowParser>()
    UseCases/GetProductMarginSummary/
      GetProductMarginSummaryHandler.cs            ← inject TimeWindowParser, replace static call

backend/test/Anela.Heblo.Tests/
  Features/Analytics/
    GetProductMarginSummaryHandlerTests.cs         ← inject FakeTimeProvider + TimeWindowParser, freeze date
```

### Interfaces and Contracts

`TimeWindowParser` is not given an interface — the spec explicitly excludes interface extraction (Out of Scope). Inject the concrete type directly, consistent with other concrete services in this module (`MarginCalculator`, `MonthlyBreakdownGenerator`).

**New constructor for TimeWindowParser:**
```csharp
public TimeWindowParser(TimeProvider timeProvider)
{
    _timeProvider = timeProvider;
}
```

**New method signature (unchanged externally):**
```csharp
public (DateTime fromDate, DateTime toDate) ParseTimeWindow(string timeWindow)
```

**New constructor for GetProductMarginSummaryHandler** — add `TimeWindowParser` as the last parameter, after existing three, to minimise test churn:
```csharp
public GetProductMarginSummaryHandler(
    IAnalyticsRepository analyticsRepository,
    IMarginCalculator marginCalculator,
    IMonthlyBreakdownGenerator monthlyBreakdownGenerator,
    TimeWindowParser timeWindowParser)
```

The call site at line 31 changes from:
```csharp
var (fromDate, toDate) = TimeWindowParser.ParseTimeWindow(request.TimeWindow);
```
to:
```csharp
var (fromDate, toDate) = _timeWindowParser.ParseTimeWindow(request.TimeWindow);
```

### Data Flow

```
HTTP request → GetProductMarginSummaryHandler.Handle()
                  │
                  ▼
           _timeWindowParser.ParseTimeWindow(request.TimeWindow)
                  │
                  ▼
           _timeProvider.GetLocalNow().Date   ← frozen in tests via FakeTimeProvider
                  │
                  ▼
           (fromDate, toDate) tuple
                  │
                  ▼
           _analyticsRepository.StreamProductsWithSalesAsync(fromDate, toDate, ...)
```

### Test Changes

The existing `GetProductMarginSummaryHandlerTests` constructor must be updated. The handler's constructor gains `TimeWindowParser`, which itself needs a `TimeProvider`.

**New test setup pattern:**
```csharp
private static readonly DateTimeOffset FrozenDate = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

public GetProductMarginSummaryHandlerTests()
{
    _analyticsRepositoryMock = new Mock<IAnalyticsRepository>();
    _marginCalculator = new MarginCalculator();
    _monthlyBreakdownGenerator = new MonthlyBreakdownGenerator(_marginCalculator);
    var timeProvider = new FakeTimeProvider(FrozenDate);
    var timeWindowParser = new TimeWindowParser(timeProvider);
    _handler = new GetProductMarginSummaryHandler(
        _analyticsRepositoryMock.Object,
        _marginCalculator,
        _monthlyBreakdownGenerator,
        timeWindowParser);
}
```

All test methods that reference `DateTime.Today` must replace those references with the frozen values derived from `FrozenDate`. For example, `Handle_ValidRequest_ReturnsCorrectResponse` currently captures `var today = DateTime.Today` and uses it to build `fromDate`/`toDate` expectations and fixture data. These must change to `FrozenDate.LocalDateTime.Date` (or a literal `new DateTime(2026, 1, 15)` aligned to the frozen date), so that mock `Setup()` matchers and assertions agree with what the frozen `TimeWindowParser` actually computes.

The `Handle_DifferentTimeWindows_ParsesCorrectly` theory also mirrors `DateTime.Today` inline — replace with the frozen equivalent.

Add one new test for the `ArgumentException` path:
```csharp
[Fact]
public async Task Handle_UnknownTimeWindow_ThrowsArgumentException()
{
    var request = new GetProductMarginSummaryRequest
    {
        TimeWindow = "not-a-real-window",
        GroupingMode = ProductGroupingMode.Products
    };

    Func<Task> act = () => _handler.Handle(request, CancellationToken.None);
    await act.Should().ThrowAsync<ArgumentException>()
        .WithMessage("*not-a-real-window*");
}
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Test mock `Setup()` matchers that pass `fromDate`/`toDate` exactly will break if the frozen date differs from the clock at the time the test ran | High | Freeze to a fixed date (`2026-01-15`) in the test constructor; derive all fixture values from that constant, not from `DateTime.Today`. |
| `GetLocalNow()` vs `GetUtcNow()` produces different dates at midnight near UTC offset boundaries | Low | The frozen test date should be set to noon UTC to avoid midnight edge cases in unit tests. `FrozenDate` should not be `00:00:00`. |
| Handlers out of scope (`GetProductMarginAnalysisHandler`, `GetMarginReportHandler`) also call `TimeWindowParser.ParseTimeWindow()` statically — compile error if they are not updated | Medium | After converting `TimeWindowParser` from static to instance, the compiler will flag every static call site. Check those handlers — they are out of scope for behavior change but must be updated to accept an injected instance or continue to call a refactored version. See Specification Amendment #1 below. |

## Specification Amendments

### Amendment 1: Out-of-scope handlers will not compile after the static class is removed

The spec lists `GetProductMarginAnalysisHandler` and `GetMarginReportHandler` as out of scope. However, if they call `TimeWindowParser.ParseTimeWindow()` as a static method today, removing `static` from the class will cause a compile error in those handlers. The implementer must verify whether these handlers use `TimeWindowParser` and, if so, inject it (without any behavior change to the handlers themselves). This is mechanical injection only — no logic change, no test authoring required for those handlers as part of this task. Verify with a grep before starting.

### Amendment 2: Clarify ArgumentException message format

The spec says throw `ArgumentException` with "the offending value" but does not specify the message string. Use:
```csharp
throw new ArgumentException($"Unknown time window value: '{timeWindow}'", nameof(timeWindow));
```
This makes the exception message testable (`WithMessage("*not-a-real-window*")`) and follows .NET conventions for `ArgumentException`.

## Prerequisites

- No migrations required.
- No infrastructure changes required.
- `TimeProvider.System` is already registered as a singleton (`ServiceCollectionExtensions.cs:132`).
- `Microsoft.Extensions.TimeProvider.Testing` is already in `Anela.Heblo.Tests.csproj`.
- Before starting: grep for all callers of `TimeWindowParser.ParseTimeWindow` to surface any call sites not mentioned in the spec.
