# Specification: Inject TimeProvider into TimeWindowParser

## Summary

`TimeWindowParser` currently uses the static ambient `DateTime.Today`, making any test that relies on the computed date range clock-sensitive and non-deterministic. This change converts `TimeWindowParser` to an instance class that accepts the project-standard `System.TimeProvider` abstraction, aligns it with `InvoiceImportStatisticsTile` (the existing correct pattern), and replaces the silent fallback for unrecognised time-window strings with an explicit `ArgumentException`.

## Background

The Analytics module contains two competing patterns for obtaining "today":

- `InvoiceImportStatisticsTile` (correct): uses injected `TimeProvider` via `_timeProvider.GetUtcNow().Date`.
- `TimeWindowParser` (incorrect): calls `DateTime.Today` — a static ambient that cannot be controlled in tests.

`GetProductMarginSummaryHandler` calls `TimeWindowParser.ParseTimeWindow` at line 31. The corresponding test file (`GetProductMarginSummaryHandlerTests.cs`) reproduces `DateTime.Today` inline in every test case, meaning assertions such as `result.FromDate.Should().Be(fromDate)` pass only when the test and the production code happen to call `DateTime.Today` within the same day. A test run spanning midnight, or a mocked test environment that freezes time, will silently produce wrong results.

Additionally, the `_` (default) arm of the switch in `TimeWindowParser` silently falls back to `"current-year"` for any unrecognised input string, swallowing typos without any diagnostic signal.

`System.TimeProvider` is already registered in DI (used by `InvoiceImportStatisticsTile` and other tiles). No new dependency needs to be introduced.

## Functional Requirements

### FR-1: Convert TimeWindowParser to an injectable instance class

`TimeWindowParser` must be refactored from a `static class` with a `static` method to a non-static class that accepts `TimeProvider` via constructor injection.

The instance method `ParseTimeWindow(string timeWindow)` must obtain the current date through `_timeProvider.GetLocalNow().Date` (matching the existing local-date semantics of `DateTime.Today`).

**Acceptance criteria:**
- `TimeWindowParser` is no longer `static`.
- Constructor signature is `TimeWindowParser(TimeProvider timeProvider)`.
- `ParseTimeWindow` no longer references `DateTime.Today` anywhere in its body.
- All five named time windows (`current-year`, `current-and-previous-year`, `last-6-months`, `last-12-months`, `last-24-months`) continue to return the same date ranges as before when the real clock is used.

### FR-2: Register TimeWindowParser in AnalyticsModule

`TimeWindowParser` must be registered in the DI container inside `AnalyticsModule.AddAnalyticsModule` so that it can be injected into handlers.

**Acceptance criteria:**
- `AnalyticsModule.cs` contains `services.AddScoped<TimeWindowParser>()` (or equivalent lifetime; `Scoped` matches other services in this module).
- The application starts without a `System.InvalidOperationException` about an unresolvable `TimeWindowParser` dependency.

### FR-3: Inject TimeWindowParser into GetProductMarginSummaryHandler

`GetProductMarginSummaryHandler` must receive `TimeWindowParser` as a constructor-injected dependency and call the instance method instead of the static one.

**Acceptance criteria:**
- `GetProductMarginSummaryHandler` constructor declares a `TimeWindowParser` parameter.
- The static call `TimeWindowParser.ParseTimeWindow(request.TimeWindow)` at line 31 is replaced by `_timeWindowParser.ParseTimeWindow(request.TimeWindow)`.
- No other callers of the static `TimeWindowParser.ParseTimeWindow` exist in the codebase after the change.

### FR-4: Replace silent fallback with ArgumentException

The `_` (default) arm of the `switch` in `ParseTimeWindow` must throw `ArgumentException` instead of silently falling back to `current-year`.

**Acceptance criteria:**
- Passing any string not in the recognised set throws `ArgumentException` with a message that includes the offending value.
- All five recognised strings continue to return the correct date range.
- Existing tests that pass valid `timeWindow` strings do not throw.

### FR-5: Update GetProductMarginSummaryHandlerTests to use a frozen TimeProvider

Existing tests in `GetProductMarginSummaryHandlerTests` must be updated to construct `TimeWindowParser` with a `FakeTimeProvider` set to a fixed, known date. Tests must no longer call `DateTime.Today` to derive expected values; expected dates must be hardcoded from the frozen instant.

**Acceptance criteria:**
- No occurrence of `DateTime.Today` remains in `GetProductMarginSummaryHandlerTests.cs`.
- All existing test cases continue to pass deterministically regardless of when they are run.
- The `Handle_DifferentTimeWindows_ParsesCorrectly` theory asserts `result.FromDate` and `result.ToDate` against dates derived from the frozen instant, not the system clock.
- A new test case asserts that passing an unrecognised `timeWindow` string causes `ParseTimeWindow` to throw `ArgumentException`.

## Non-Functional Requirements

### NFR-1: Performance

No performance impact. `TimeWindowParser` performs only in-memory date arithmetic; switching from static to instance invocation adds negligible overhead.

### NFR-2: Security

No security surface changes. The class handles only date computation.

### NFR-3: Testability

After this change, any test that needs a deterministic date range must supply a `FakeTimeProvider` (from `Microsoft.Extensions.TimeProvider.Testing` or equivalent). The real-clock `TimeProvider.System` remains available for production registration.

### NFR-4: Consistency

The implementation must match the pattern established by `InvoiceImportStatisticsTile`: inject `TimeProvider`, call `GetUtcNow()` or `GetLocalNow()` as appropriate for the semantic (local date for time-window parsing, same as the previous `DateTime.Today` behaviour).

## Data Model

No data model changes. `TimeWindowParser` computes a `(DateTime fromDate, DateTime toDate)` tuple and returns it; the shape of that tuple is unchanged.

## API / Interface Design

No public API or contract changes. `TimeWindowParser` is an internal application-layer service, not exposed via any controller or OpenAPI endpoint. The `GetProductMarginSummaryResponse.FromDate` and `ToDate` fields continue to be populated by the same logic.

**Resulting class signature:**

```csharp
// backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs
public class TimeWindowParser
{
    private readonly TimeProvider _timeProvider;

    public TimeWindowParser(TimeProvider timeProvider)
        => _timeProvider = timeProvider;

    public (DateTime fromDate, DateTime toDate) ParseTimeWindow(string timeWindow)
    {
        var today = _timeProvider.GetLocalNow().Date;
        return timeWindow switch
        {
            "current-year"              => (new DateTime(today.Year, 1, 1), today),
            "current-and-previous-year" => (new DateTime(today.Year - 1, 1, 1), today),
            "last-6-months"             => (today.AddMonths(-6), today),
            "last-12-months"            => (today.AddMonths(-12), today),
            "last-24-months"            => (today.AddMonths(-24), today),
            _ => throw new ArgumentException(
                     $"Unrecognised time window: '{timeWindow}'.", nameof(timeWindow))
        };
    }
}
```

**DI registration addition in `AnalyticsModule.cs`:**

```csharp
services.AddScoped<TimeWindowParser>();
```

**Handler constructor change:**

```csharp
public GetProductMarginSummaryHandler(
    IAnalyticsRepository analyticsRepository,
    IMarginCalculator marginCalculator,
    IMonthlyBreakdownGenerator monthlyBreakdownGenerator,
    TimeWindowParser timeWindowParser)   // added
```

## Dependencies

- `System.TimeProvider` (.NET 8 built-in) — already registered in DI; no new package required.
- `Microsoft.Extensions.TimeProvider.Testing` — already present in the test project (assumption based on its use elsewhere in the codebase; verify with `dotnet list package` if uncertain). Provides `FakeTimeProvider` for deterministic tests.

## Out of Scope

- Changing `GetProductMarginAnalysisHandler`, which accepts explicit `StartDate`/`EndDate` from the request and does not call `TimeWindowParser`.
- Changing `GetMarginReportHandler` or `GetBankStatementImportStatisticsHandler` unless they are found to call `TimeWindowParser.ParseTimeWindow` (they do not, based on current code review).
- Extracting `TimeWindowParser` behind an interface (`ITimeWindowParser`). A concrete class is sufficient for DI and test injection with `FakeTimeProvider`; adding an interface would be premature.
- Changing the set of recognised `timeWindow` strings or their date-range semantics.
- Frontend changes.

## Open Questions

None.

## Status: COMPLETE
