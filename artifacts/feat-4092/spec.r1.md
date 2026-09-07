# Specification: Unit test coverage for RecurringJobNextRunCalculator

## Summary
`RecurringJobNextRunCalculator.Calculate` is a static, side-effect-free method that computes the next UTC run time for a recurring job from a CRON expression and a timezone id. It currently has 0% line coverage and no test file. This work adds a focused unit test suite covering all four code paths (disabled job, unknown timezone, invalid CRON, happy path) plus a DST boundary case, without touching production code.

## Background
The method is used to populate `NextRunAt`, a value shown to operators and used in scheduling decisions. It fails safe by design: an unrecognized timezone id or a malformed CRON expression is caught internally, logged as a warning, and surfaced as `null` rather than as an exception. Because none of these branches are exercised by tests, a regression that changes this suppression behavior (e.g. lets an exception propagate, or stops logging) would go undetected until it broke production job scheduling. This is a coverage-gap remediation identified by the weekly coverage-gap routine (CI run #33791274852); no behavior change is in scope.

## Functional Requirements

### FR-1: Disabled job returns null immediately
When `isEnabled` is `false`, `Calculate` must return `null` without attempting timezone lookup, CRON parsing, or any logging.
**Acceptance criteria:**
- Given `isEnabled = false` (with otherwise arbitrary/valid arguments), `Calculate` returns `null`.
- No `ILogger` warning (or any log) is invoked in this path.

### FR-2: Unknown timezone id returns null and logs a warning
When `timeZoneId` does not resolve via `TimeZoneInfo.FindSystemTimeZoneById`, the method must catch `TimeZoneNotFoundException`, log a warning containing the timezone id and job name, and return `null`.
**Acceptance criteria:**
- Given `isEnabled = true` and an invalid/unknown `timeZoneId` (e.g. `"Not/A/Real/Zone"`), `Calculate` returns `null`.
- A warning-level log call is made exactly once, referencing the supplied `timeZoneId` and `jobName`.
- No exception propagates out of `Calculate`.

### FR-3: Invalid CRON expression returns null and logs a warning
When `cronExpression` cannot be parsed by `CrontabSchedule.Parse`, the method must catch `CrontabException`, log a warning containing the CRON expression and job name, and return `null`.
**Acceptance criteria:**
- Given `isEnabled = true`, a valid `timeZoneId` (e.g. `"UTC"`), and a malformed `cronExpression` (e.g. `"not a cron"`), `Calculate` returns `null`.
- A warning-level log call is made exactly once, referencing the supplied `cronExpression` and `jobName`.
- No exception propagates out of `Calculate`.

### FR-4: Happy path returns the correct next UTC occurrence
Given valid, enabled inputs, `Calculate` converts `utcNow` into the job's local timezone, computes the next CRON occurrence in local time via `CrontabSchedule.GetNextOccurrence`, converts that local time back to UTC, and returns it with `DateTimeKind.Utc`.
**Acceptance criteria:**
- For a fixed `utcNow`, a simple daily CRON expression (e.g. `"0 6 * * *"`), and `timeZoneId = "UTC"`, the returned value equals the expected next occurrence computed independently (e.g. `utcNow.Date` at 06:00 the same or next day, per CRON semantics) and its `.Kind` is `DateTimeKind.Utc`.
- At least one non-UTC timezone case (e.g. `"Europe/Prague"` or the IANA-equivalent available on the test host) is covered to confirm the local-time round trip, not only the degenerate UTC-to-UTC case.
- No log call is made on the happy path (only warnings are logged on the error branches).

### FR-5: DST boundary behavior does not throw
Around a DST transition (spring-forward gap or autumn-fall ambiguous hour) in a timezone that observes DST, `Calculate` must not throw for a `utcNow`/CRON combination whose computed local next-occurrence falls in or near the transition. The exact returned instant is not asserted beyond being a valid, non-throwing `DateTime?` result (spec intentionally does not pin down .NET's ambiguous/skipped-time resolution semantics — see Open Questions).
**Acceptance criteria:**
- At least one test drives `utcNow` and a CRON expression such that the computed local `nextLocal` falls within a known DST transition window for a DST-observing IANA timezone (e.g. `"Europe/Prague"` spring-forward around late March, or autumn ambiguous hour around late October).
- The call completes without throwing and returns a `DateTime?` (either a concrete value or, if the environment's `TimeZoneInfo`/CRON library combination legitimately cannot resolve it, a graceful `null` is acceptable) — the test asserts "does not throw", not a specific offset.

## Non-Functional Requirements

### NFR-1: Test isolation and speed
Tests must be pure unit tests: no filesystem, network, database, Hangfire, or DI container involvement. `Calculate` is static and takes `utcNow` as a parameter, so no time-provider abstraction or fake clock is needed — literal `DateTime` values are sufficient.

### NFR-2: Logger verification
`ILogger` must be a test double capable of verifying that `LogWarning` was (or was not) invoked, consistent with existing patterns in the codebase (`Mock<ILogger<T>>` with Moq, or `NullLogger<T>.Instance` where verification isn't needed). Use `Moq` where a call must be asserted (FR-2, FR-3) and `NullLogger<T>.Instance` where logging is merely incidental (FR-1, FR-4 happy path, FR-5).

### NFR-3: Consistency with existing conventions
Follow the conventions already established in `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/` (xUnit `[Fact]`/`[Theory]`, FluentAssertions `.Should()`, Moq, `NullLogger<T>.Instance` for incidental logging, `Arrange`/`Act`/`Assert` comment sections). New test file: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs`, class `RecurringJobNextRunCalculatorTests`.

## Data Model
No data model changes. `Calculate`'s signature (unchanged, for reference):
```csharp
public static DateTime? Calculate(
    string cronExpression, bool isEnabled, string timeZoneId,
    DateTime utcNow, ILogger logger, string? jobName = null)
```

## API / Interface Design
Not applicable — this is a test-only addition. No production code, controller, or API surface changes.

## Dependencies
- `NCrontab.Advanced` (already referenced by production code) — `CrontabSchedule`, `CrontabException`.
- `Microsoft.Extensions.Logging` / `Microsoft.Extensions.Logging.Abstractions` — `ILogger`, `NullLogger<T>`.
- `Moq` and `FluentAssertions` and `Xunit` — already used throughout `Anela.Heblo.Tests`.
- The host running the DST test (FR-5) must have the IANA timezone database available (true on Linux CI and typical dev containers per the codebase's existing use of IANA ids like `"Europe/Prague"`).

## Out of Scope
- Any change to `RecurringJobNextRunCalculator.cs` production code or its public contract.
- Testing callers of `Calculate` (e.g. the recurring job scheduler/service that invokes it) — out of scope for this coverage-gap ticket.
- Adding a CRON-validity gate upstream of `Calculate` (mentioned in the brief as a related concern, but not part of this ticket).
- Property-based/fuzz testing of arbitrary CRON expressions or timezones — the suggested approach calls for a small, targeted test class, not exhaustive coverage.

## Open Questions
None.
