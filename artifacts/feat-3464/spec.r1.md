# Specification: Extract `ITimeWindowParser` interface in Analytics module

## Summary
`GetProductMarginSummaryHandler` currently depends on the concrete class `TimeWindowParser` instead of an abstraction, breaking the Dependency Inversion Principle and the interface-per-service convention already followed by every other collaborator in the Analytics module (`IProductFilterService`, `IReportBuilderService`, `IMarginCalculator`, `IMonthlyBreakdownGenerator`). This change extracts an `ITimeWindowParser` interface, has `TimeWindowParser` implement it, updates DI registration and the handler to depend on the interface, and updates the existing unit test accordingly. This is a pure refactor with no behavioral change.

## Background
This item was filed by the automated daily arch-review routine (2026-07-03) as an architecture-consistency finding, not a functional bug. `TimeWindowParser` (`backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs`) is a small stateless-per-call service that converts a named time window string (e.g. `"last-6-months"`) into a `(fromDate, toDate)` tuple, using `TimeProvider` for "now". It is consumed by exactly one place: `GetProductMarginSummaryHandler` (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`), and registered as a concrete type in `AnalyticsModule.cs:47` (`services.AddScoped<TimeWindowParser>();`).

Every sibling service injected into the same handler is already interface-based:
- `IProductFilterService` / `ProductFilterService`
- `IReportBuilderService` / `ReportBuilderService`
- `IMarginCalculator` / `MarginCalculator`
- `IMonthlyBreakdownGenerator` / `MonthlyBreakdownGenerator`

`TimeWindowParser` is the sole exception. Injecting the concrete class makes the handler's dependency graph inconsistent and nudges future test/mocking code toward instantiating the real implementation (as the existing test currently does) rather than using a test double, and it violates the established module convention enforced nowhere but by eyeballing.

A full repo search confirms `TimeWindowParser` has exactly three usage sites: the handler, the module registration, and one existing test file (`backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`). No other production code references it.

## Functional Requirements

### FR-1: Extract `ITimeWindowParser` interface
Add a public interface `ITimeWindowParser` in the `Anela.Heblo.Application.Features.Analytics.Services` namespace, colocated in the same file as `TimeWindowParser` (matching the pattern used in `MarginCalculator.cs`, where `IMarginCalculator` and `MarginCalculator` live in the same file), i.e. `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs`.

The interface must expose exactly the one public member currently on `TimeWindowParser`:

```csharp
public interface ITimeWindowParser
{
    (DateTime fromDate, DateTime toDate) ParseTimeWindow(string timeWindow);
}
```

**Acceptance criteria:**
- `ITimeWindowParser` is defined in `Anela.Heblo.Application.Features.Analytics.Services`.
- The method signature (name, parameter type/name, return tuple shape and element names) is identical to the existing `TimeWindowParser.ParseTimeWindow`.
- `public class TimeWindowParser : ITimeWindowParser` — the class body, constructor, and `ParseTimeWindow` implementation are otherwise unchanged (no behavioral change).

### FR-2: Update DI registration
Change the registration in `AnalyticsModule.cs` from a concrete-type registration to an interface-to-implementation registration, consistent with the neighboring lines for `IMarginCalculator` and `IMonthlyBreakdownGenerator`.

**Acceptance criteria:**
- `services.AddScoped<TimeWindowParser>();` is replaced with `services.AddScoped<ITimeWindowParser, TimeWindowParser>();` at `AnalyticsModule.cs:47` (or the resulting line after the edit).
- Lifetime remains `Scoped` (unchanged from today).
- No other registrations in `AnalyticsModule.cs` are touched.

### FR-3: Update `GetProductMarginSummaryHandler` to depend on the abstraction
Change the constructor-injected field and parameter type from `TimeWindowParser` to `ITimeWindowParser`.

**Acceptance criteria:**
- The private field is declared as `private readonly ITimeWindowParser _timeWindowParser;`.
- The constructor parameter is declared as `ITimeWindowParser timeWindowParser`.
- All internal usages (`_timeWindowParser.ParseTimeWindow(request.TimeWindow)`) are unchanged in behavior.
- No other constructor parameters, field types, or handler logic are modified.

### FR-4: Update existing unit test
`GetProductMarginSummaryHandlerTests.cs` currently does `private readonly TimeWindowParser _timeWindowParser;` and constructs it via `new TimeWindowParser(timeProvider)` before passing it into the handler. Since the handler now depends on `ITimeWindowParser`, the test must compile and pass without modification to its behavioral intent.

**Acceptance criteria:**
- The test continues to construct the real `TimeWindowParser` (via `new TimeWindowParser(timeProvider)` — no mocking framework introduced by this change) and pass it wherever `ITimeWindowParser` is expected; the field type may be updated to `ITimeWindowParser` or left as `TimeWindowParser` (either compiles, since `TimeWindowParser` implements `ITimeWindowParser`), whichever requires the smaller diff.
- Existing test cases in this file continue to pass unmodified in their assertions.
- No new test cases are required by this change (this is a structural refactor, not new behavior), though adding a trivial interface-substitutability test is optional and left to implementer discretion (see Out of Scope).

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected or required. This is a compile-time abstraction change; DI resolution cost for a `Scoped` service is unchanged whether registered as concrete type or interface-to-implementation.

### NFR-2: Security
Not applicable. No security-sensitive surface is touched (no auth, no data exposure, no external I/O change).

### NFR-3: Backward compatibility
This is an internal (non-public-API) refactor confined to the Application layer's DI wiring and one handler's constructor signature. It must not change:
- The `GetProductMarginSummaryRequest`/`GetProductMarginSummaryResponse` contracts.
- Any HTTP endpoint behavior, request/response shape, or OpenAPI-generated client surface.
- The parsing behavior/output of `ParseTimeWindow` for any input string.

## Data Model
Not applicable — no persistent or transient data model changes. `ParseTimeWindow`'s return type remains the value tuple `(DateTime fromDate, DateTime toDate)`.

## API / Interface Design

**New interface** (`backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs`):
```csharp
public interface ITimeWindowParser
{
    (DateTime fromDate, DateTime toDate) ParseTimeWindow(string timeWindow);
}
```

**Implementation** (same file, existing class updated to implement the interface):
```csharp
public class TimeWindowParser : ITimeWindowParser
{
    // constructor and ParseTimeWindow body unchanged
}
```

**DI registration** (`backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs`):
```csharp
services.AddScoped<ITimeWindowParser, TimeWindowParser>();
```

**Handler constructor** (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`):
```csharp
public GetProductMarginSummaryHandler(
    IAnalyticsRepository analyticsRepository,
    IMarginCalculator marginCalculator,
    IMonthlyBreakdownGenerator monthlyBreakdownGenerator,
    ITimeWindowParser timeWindowParser)
```

No HTTP endpoints, MediatR request/response contracts, or frontend-facing surfaces change.

## Dependencies
- No new external libraries or services.
- Depends on the existing `TimeProvider` abstraction already injected into `TimeWindowParser` (unchanged).
- Depends on `AnalyticsModule.cs` being the sole DI composition root for this module (confirmed — no other module or test-fixture DI container separately registers `TimeWindowParser`, per repo-wide `grep` for `TimeWindowParser`).

## Out of Scope
- Any change to `ParseTimeWindow`'s parsing logic, supported time-window values, or error handling for unknown values.
- Adding a mocking-framework-based unit test double for `ITimeWindowParser` (e.g. a Moq/NSubstitute mock) to replace the real object currently constructed in `GetProductMarginSummaryHandlerTests.cs` — optional nice-to-have, not required by this fix.
- Refactoring any other concrete-class dependency in the codebase outside the Analytics module's `GetProductMarginSummaryHandler`.
- Renaming the file, moving the interface to a separate file, or changing the namespace structure of the `Services/` folder.
- Any change to the `AnalyticsModule.cs` comment blocks or the ordering/grouping of unrelated registrations beyond the single line being changed.

## Open Questions
None.

## Status: COMPLETE
