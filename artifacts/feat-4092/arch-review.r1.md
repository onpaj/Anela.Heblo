# Architecture Review: Unit test coverage for RecurringJobNextRunCalculator

## Skip Design: true

## Architectural Fit Assessment
This is a pure coverage-gap remediation: one new xUnit test class, zero production code changes, zero new interfaces or dependencies. The codebase already has an established, consistent pattern for exactly this shape of test — `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/` mirrors `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/` file-for-file (e.g. `RecurringJobStatusCheckerTests.cs`, `HangfireJobRegistrationHelperTests.cs`), uses xUnit `[Fact]`/`[Theory]`, Moq, FluentAssertions, and either `NullLogger<T>.Instance` or `Mock<ILogger<T>>` depending on whether a log call needs verifying — confirmed by direct inspection of `docs/architecture/testing-strategy.md` and multiple existing test files. `RecurringJobNextRunCalculator.Calculate` takes a plain (non-generic) `ILogger`, not `ILogger<T>`, which simplifies mocking (`new Mock<ILogger>()`, no generic parameter). No architectural decision is required beyond "follow the existing convention," because the convention already exists and directly covers this case.

## Proposed Architecture

### Component Overview
```
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/
  RecurringJobNextRunCalculator.cs        <- unchanged (SUT, static class)

backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/
  RecurringJobNextRunCalculatorTests.cs   <- NEW, only file added
```
No DI container, no MediatR pipeline, no database, no Hangfire runtime involved. The test class calls the static `Calculate` method directly with literal arguments and a mocked/null `ILogger`.

### Key Design Decisions

#### Decision 1: Logger test double per test case
**Options considered:**
- `NullLogger.Instance` everywhere (simplest, but can't assert `LogWarning` happened for FR-2/FR-3).
- `Mock<ILogger>` everywhere (uniform, but adds unnecessary Moq setup/verify noise to tests that don't care about logging).
**Chosen approach:** `NullLogger.Instance` (non-generic `Microsoft.Extensions.Logging.Abstractions.NullLogger`, since `Calculate` takes `ILogger` not `ILogger<T>`) for FR-1 (disabled), FR-4 (happy path), FR-5 (DST); `Mock<ILogger>` with a `Verify(x => x.Log(LogLevel.Warning, ...), Times.Once)` call for FR-2 (unknown timezone) and FR-3 (invalid CRON), where the log call itself is part of the acceptance criteria.
**Rationale:** Matches the codebase's existing split (compare `RecurringJobStatusCheckerTests.cs`, which uses `NullLogger<T>.Instance` because it never asserts on logging, versus handler tests that inject `Mock<ILogger<T>>` specifically to verify warnings/errors were logged). Keeps each test's Arrange section proportional to what it actually asserts.

#### Decision 2: How to verify `ILogger.LogWarning` with Moq
**Options considered:**
- Verify the extension method `LogWarning(...)` directly — not mockable, since it's a `static` extension over `ILogger.Log`.
- Verify the underlying `ILogger.Log<TState>(LogLevel, EventId, TState, Exception?, Func<TState,Exception?,string>)` call with loose `It.Is`/`It.IsAny` matchers.
**Chosen approach:** Verify the underlying `Log` method:
```csharp
_logger.Verify(
    x => x.Log(
        LogLevel.Warning,
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((state, t) => true),
        It.IsAny<Exception>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
    Times.Once);
```
Optionally narrow the `It.Is<It.IsAnyType>` predicate to check `state.ToString()` contains the timezone id / cron expression, if a tighter assertion is wanted — this is a minor developer choice, not an architectural one.
**Rationale:** This is the standard, well-known Moq pattern for verifying `ILogger` extension-method calls (`LogWarning`, `LogError`, etc. all funnel through `ILogger.Log`); it requires no new package and matches how `Mock<ILogger<T>>` is already used elsewhere in this codebase for dependency injection, just applied to the non-generic `ILogger` here.

#### Decision 3: DST test does not pin exact resolution semantics
**Options considered:**
- Assert the exact returned UTC instant across a DST transition.
- Assert only "does not throw" and accept either a concrete `DateTime` or `null` as passing, per spec FR-5.
**Chosen approach:** The latter, as already specified in `spec.r1.md` FR-5.
**Rationale:** `TimeZoneInfo.ConvertTimeToUtc` for an ambiguous local time uses the standard (non-DST) offset by convention, and for a skipped (spring-forward gap) local time it does not throw either — .NET resolves out-of-range local times leniently rather than throwing `InvalidTimeZoneException` here (that exception is thrown by `ConvertTimeBySystemTimeZoneId`/`TimeZoneInfo.ConvertTime` overloads with validation, not by the two methods used in `Calculate`). Pinning an exact expected instant risks a brittle, .NET-runtime-version-coupled assertion for a case the ticket explicitly says should be "sensible, not a throw." Confirmed by reading the production code: `Calculate` does not catch `ArgumentException`/`InvalidTimeZoneException` around the DST conversion — if a future .NET/NCrontab combination did start throwing here, this test's "does not throw" assertion is exactly the regression detector wanted, and pinning an exact value would fail for the wrong reason (offset drift) instead of the right one (an exception escaping).

## Implementation Guidance

### Directory / Module Structure
Single new file: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs`, class `RecurringJobNextRunCalculatorTests`, no constructor-injected fixture needed (static SUT, no shared mutable state) — plain `[Fact]`/`[Theory]` methods are sufficient; a constructor or `private readonly` fields are unnecessary since there is no shared per-test setup beyond local Arrange blocks.

### Interfaces and Contracts
No new or changed interfaces. Test-only usings needed:
```csharp
using Anela.Heblo.Application.Features.BackgroundJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NCrontab.Advanced.Exceptions;   // only if asserting exception types directly; not required for these tests
using FluentAssertions;
using Xunit;
```
(`NCrontab.Advanced.Exceptions` is not actually needed by the tests since `Calculate` already swallows `CrontabException` — listed here only to flag that no new NCrontab-specific test dependency is required.)

### Data Flow
Each test is a direct call: `RecurringJobNextRunCalculator.Calculate(cron, isEnabled, timeZoneId, utcNow, logger, jobName)` → assert on the returned `DateTime?` and, where applicable, verify the logger mock. No async, no `CancellationToken`, no ambient state.

Recommended test cases (mapping 1:1 to spec FRs):
1. `Calculate_ReturnsNull_WhenJobDisabled` (FR-1) — also assert `_logger.Verify(..., Times.Never)` to lock in "no other work done."
2. `Calculate_ReturnsNull_AndLogsWarning_WhenTimezoneUnknown` (FR-2).
3. `Calculate_ReturnsNull_AndLogsWarning_WhenCronInvalid` (FR-3).
4. `Calculate_ReturnsExpectedUtcInstant_ForUtcTimezone` (FR-4, UTC case) — e.g. `utcNow = 2026-01-01T00:00:00Z`, cron `"0 6 * * *"`, `timeZoneId = "UTC"` → expect `2026-01-01T06:00:00Z`, `Kind == Utc`.
5. `Calculate_ReturnsExpectedUtcInstant_ForNonUtcTimezone` (FR-4, non-UTC case) — use `RecurringJobMetadata.DefaultTimeZoneId` ("Europe/Prague", confirmed as the codebase's standard non-UTC test timezone — see `HangfireJobRegistrationHelperTests.cs`) with a `utcNow` in winter (CET, UTC+1) to keep the expected offset unambiguous and independent of DST, e.g. `utcNow = 2026-01-01T05:00:00Z` (06:00 local), cron `"0 6 * * *"` → next occurrence is the *following* day at 06:00 local = `2026-01-02T05:00:00Z` UTC (since 06:00 local already occurred at exactly `utcNow`; use `NCrontab.Advanced`'s actual semantics — verify with a quick manual trace or a slightly-before instant like `04:59` local to avoid an off-by-one edge case in the test itself).
6. `Calculate_DoesNotThrow_AroundDstSpringForwardGap` (FR-5) — `timeZoneId = "Europe/Prague"`, `utcNow` shortly before the last Sunday of March 02:00→03:00 local transition, cron expression that lands `nextLocal` inside `02:00`–`03:00` (e.g. `"30 2 * * *"`). Assert only `act.Should().NotThrow()` (wrap the call in a `Func<DateTime?>` / `FluentAssertions` `.Should().NotThrow()` or a plain try/no-exception pattern — developer's choice).
7. (Optional but recommended, still within FR-5's intent) `Calculate_DoesNotThrow_AroundDstAutumnAmbiguousHour` — same idea, last Sunday of October, cron landing `nextLocal` inside the repeated 02:00–03:00 hour.

Use `[Theory]`/`[InlineData]` only if it clearly reduces duplication without hurting readability of the DST edge-case reasoning (Decision 3) — plain `[Fact]`s are equally acceptable here given each case has distinct fixture setup (spec does not mandate one over the other).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Hand-computing the "expected" happy-path UTC instant is wrong (off-by-one due to CRON `GetNextOccurrence` being exclusive of the exact current instant) | Medium | Choose `utcNow` values clearly *before* the expected occurrence (e.g. a minute or more prior) rather than exactly on a boundary, so the test's expected value is unambiguous regardless of inclusive/exclusive semantics. |
| DST test asserts something IANA tzdata / .NET version actually changes over time (e.g. Europe/Prague transition dates) | Low | Test asserts "does not throw," not a specific date; transition *dates* only matter for choosing a `utcNow` that lands inside a transition window this year — if wrong, worst case the DST test degenerates into a normal happy-path test, it doesn't fail. Consider using a fixed historical or clearly-documented transition date (e.g. last Sunday of March 2026) rather than deriving it dynamically, so the test is deterministic and independent of "today." |
| CI/test host missing IANA tzdata for `"Europe/Prague"` | Low | Already a live assumption elsewhere in this codebase (`RecurringJobMetadata.DefaultTimeZoneId`, existing `HangfireJobRegistrationHelperTests.cs` use the same id) — no new risk introduced. |
| Over-scoping into testing `Calculate`'s callers or adding upstream CRON validation (mentioned in the brief but explicitly Out of Scope in spec.r1.md) | Low | Spec already excludes this; architect reaffirms: do not touch `RecurringJobDiscoveryService`, `HangfireJobRegistrationHelper`, or any caller in this PR. |

## Specification Amendments
None required. `spec.r1.md` is implementable as written; this review only adds the concrete Moq-verification pattern (Decision 2) and a recommended non-UTC test fixture (`"Europe/Prague"`, winter date) for FR-4's second acceptance criterion, both of which are implementation detail within the spec's existing acceptance criteria rather than changes to them.

## Prerequisites
None. No migrations, no config, no new package references — `Moq`, `FluentAssertions`, `xunit`, and `Microsoft.Extensions.Logging.Abstractions` are already referenced by `Anela.Heblo.Tests.csproj`.
