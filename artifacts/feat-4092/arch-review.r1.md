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

#### Decision 3: DST test asserts verified, actual behavior — not an assumption
**Options considered:**
- Assume both DST edge cases degrade gracefully (no throw) and assert that uniformly, as `spec.r1.md`'s first draft (r1, pre-correction) did.
- Actually run the production code (`TimeZoneInfo.ConvertTimeFromUtc`/`ConvertTimeToUtc` + `NCrontab.Advanced.CrontabSchedule`, exact versions used by this repo: net8.0, NCrontab.Advanced 1.3.28) against both DST edge cases before writing the test, and assert whatever it actually does.
**Chosen approach:** The latter. Verified by direct experimentation (a throwaway console program referencing the same `NCrontab.Advanced 1.3.28` package from the local NuGet cache, run under net8.0):
  - **Autumn ambiguous hour** (e.g. `Europe/Prague`, last Sunday of October, local `02:00`–`03:00` occurs twice): `TimeZoneInfo.ConvertTimeToUtc` resolves the ambiguous unspecified-kind `DateTime` using the zone's **standard** (winter/CET) offset — documented .NET behavior. No exception; `Calculate` returns a concrete UTC `DateTime`.
  - **Spring-forward gap** (e.g. `Europe/Prague`, last Sunday of March, local `02:00`–`03:00` does not exist): `TimeZoneInfo.ConvertTimeToUtc` throws `System.ArgumentException: The supplied DateTime represents an invalid time.` This is **not** caught by `Calculate` — it only catches `TimeZoneNotFoundException` (timezone lookup) and `CrontabException` (CRON parsing), not the `ArgumentException` thrown by the DST conversion. The exception propagates to the caller.
**Rationale:** The original assumption (spec r1, pre-correction: "must not throw, either case") was wrong for the spring-forward gap — verifying against the real dependency versions caught this before it shipped as a false test assertion. This is exactly the kind of thing a coverage-only ticket should surface, not paper over: `spec.r1.md` FR-5 has been corrected to assert the two cases separately (autumn: returns a value; spring-forward: throws `ArgumentException`, asserted via `.Should().Throw<ArgumentException>()`), and the throw is flagged in Out of Scope / Risks below as a legitimate follow-up bug for a *separate* issue — this ticket adds coverage for `Calculate` as it exists today, it does not change its behavior.

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
| Hand-computing the "expected" happy-path UTC instant is wrong (off-by-one due to CRON `GetNextOccurrence` semantics) | Medium | Confirmed by direct experimentation: `GetNextOccurrence(nowLocal)` is exclusive of the exact `nowLocal` instant (a `utcNow` landing exactly on a CRON occurrence yields the *next* one, not that instant). Choose `utcNow` values clearly *before* the expected occurrence (e.g. a minute or more prior) rather than exactly on a boundary, so the test's expected value is unambiguous. |
| Uncaught `ArgumentException` on DST spring-forward gap is a real latent production bug, not just a test-writing detail | Medium | Explicitly called out in spec.r1.md FR-5 / Out of Scope and this review's Decision 3: this ticket documents the behavior with a test (`.Should().Throw<ArgumentException>()`) rather than fixing it. Recommend the developer file a follow-up issue ("RecurringJobNextRunCalculator throws for jobs scheduled to fire inside a DST spring-forward gap, instead of returning null like its other error paths") after this PR merges — do not fix it in this PR. |
| DST test transition dates (e.g. Europe/Prague's specific spring-forward/autumn dates) could differ if IANA tzdata rules change in the future | Low | Use a fixed calendar year (e.g. 2026, already verified above: spring-forward March 29 2026, autumn fallback October 25 2026) rather than deriving "this year" dynamically, so the test is deterministic and does not depend on when it runs. |
| CI/test host missing IANA tzdata for `"Europe/Prague"` | Low | Already a live assumption elsewhere in this codebase (`RecurringJobMetadata.DefaultTimeZoneId`, existing `HangfireJobRegistrationHelperTests.cs` use the same id) — no new risk introduced. |
| Over-scoping into testing `Calculate`'s callers or adding upstream CRON validation (mentioned in the brief but explicitly Out of Scope in spec.r1.md) | Low | Spec already excludes this; architect reaffirms: do not touch `RecurringJobDiscoveryService`, `HangfireJobRegistrationHelper`, or any caller in this PR. |

## Specification Amendments
`spec.r1.md` FR-5 was corrected in place (same file, no new revision) after this architecture review's active-exploration step verified actual DST behavior against the real dependency versions: the original FR-5 assumed both DST edge cases degrade gracefully without throwing; verification showed the spring-forward gap case actually throws an uncaught `System.ArgumentException`. FR-5 and Out of Scope now reflect the two cases separately (see Decision 3). This review also adds the concrete Moq-verification pattern (Decision 2) and a recommended non-UTC test fixture (`"Europe/Prague"`, winter date) for FR-4's second acceptance criterion — implementation detail within the spec's acceptance criteria, not a further change to them.

## Prerequisites
None. No migrations, no config, no new package references — `Moq`, `FluentAssertions`, `xunit`, and `Microsoft.Extensions.Logging.Abstractions` are already referenced by `Anela.Heblo.Tests.csproj`.
