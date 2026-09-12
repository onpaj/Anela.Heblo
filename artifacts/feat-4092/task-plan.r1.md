# RecurringJobNextRunCalculator Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a focused xUnit test class that exercises all four branches of the static `RecurringJobNextRunCalculator.Calculate` method (disabled, unknown timezone, invalid CRON, happy path) plus the two DST-boundary edge cases, raising its coverage from 0% with zero production code changes.

**Architecture:** One new file, `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs`, built up incrementally across five tasks (one per functional requirement in `spec.r1.md`). Every test calls the static `Calculate` method directly with literal arguments; `NullLogger.Instance` is used where logging is incidental, `Mock<ILogger>` where a `LogWarning` call is itself part of the assertion. All expected values below were verified by actually running the exact `NCrontab.Advanced 1.3.28` / net8.0 `TimeZoneInfo` combination this repo uses (see `arch-review.r1.md` Decision 3) — they are not hand-derived guesses.

**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions, `Microsoft.Extensions.Logging.Abstractions.NullLogger`.

**Note on TDD framing:** This is a coverage-only ticket — `Calculate`'s behavior already exists and will not change. So "write failing test" does not apply in the classic red/green sense; each task's test is expected to **pass on first run** because it characterizes existing behavior. The step is still "write test, run it, confirm the result matches what's documented here" — if a test does *not* pass as predicted, stop and treat that as a signal the prediction (or the plan) is wrong, not that production code should be changed to match (production changes are out of scope for this ticket — see `spec.r1.md` Out of Scope).

---

### task: scaffold-test-class-and-disabled-path

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs`

- [ ] **Step 1: Create the test file with usings, class declaration, and the FR-1 (disabled job) test**

```csharp
using System;
using Anela.Heblo.Application.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class RecurringJobNextRunCalculatorTests
{
    [Fact]
    public void Calculate_ReturnsNull_WhenJobDisabled()
    {
        // Arrange
        var logger = new Mock<ILogger>();

        // Act
        var result = RecurringJobNextRunCalculator.Calculate(
            cronExpression: "0 6 * * *",
            isEnabled: false,
            timeZoneId: "UTC",
            utcNow: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            logger: logger.Object,
            jobName: "test-job");

        // Assert
        result.Should().BeNull();
        logger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~RecurringJobNextRunCalculatorTests"`
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs
git commit -m "test(background-jobs): cover RecurringJobNextRunCalculator disabled-job path"
```

---

### task: unknown-timezone-path

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` (add a method inside the existing class, after `Calculate_ReturnsNull_WhenJobDisabled`)

- [ ] **Step 1: Add the FR-2 (unknown timezone) test**

```csharp
    [Fact]
    public void Calculate_ReturnsNull_AndLogsWarning_WhenTimezoneUnknown()
    {
        // Arrange
        var logger = new Mock<ILogger>();

        // Act
        var result = RecurringJobNextRunCalculator.Calculate(
            cronExpression: "0 6 * * *",
            isEnabled: true,
            timeZoneId: "Not/A/Real/Zone",
            utcNow: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            logger: logger.Object,
            jobName: "test-job");

        // Assert
        result.Should().BeNull();
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

(This test was verified against the real `TimeZoneInfo.FindSystemTimeZoneById` — `"Not/A/Real/Zone"` throws `System.TimeZoneNotFoundException: The time zone ID 'Not/A/Real/Zone' was not found on the local computer.`, which `Calculate` catches and logs as a warning before returning `null`.)

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~RecurringJobNextRunCalculatorTests"`
Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs
git commit -m "test(background-jobs): cover RecurringJobNextRunCalculator unknown-timezone path"
```

---

### task: invalid-cron-path

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` (add a method after `Calculate_ReturnsNull_AndLogsWarning_WhenTimezoneUnknown`)

- [ ] **Step 1: Add the FR-3 (invalid CRON) test**

```csharp
    [Fact]
    public void Calculate_ReturnsNull_AndLogsWarning_WhenCronInvalid()
    {
        // Arrange
        var logger = new Mock<ILogger>();

        // Act
        var result = RecurringJobNextRunCalculator.Calculate(
            cronExpression: "not a cron",
            isEnabled: true,
            timeZoneId: "UTC",
            utcNow: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            logger: logger.Object,
            jobName: "test-job");

        // Assert
        result.Should().BeNull();
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

(Verified: `CrontabSchedule.Parse("not a cron")` throws `NCrontab.Advanced.Exceptions.CrontabException: The provided cron string <not a cron> has too few parameters`, which `Calculate` catches and logs as a warning before returning `null`.)

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~RecurringJobNextRunCalculatorTests"`
Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs
git commit -m "test(background-jobs): cover RecurringJobNextRunCalculator invalid-cron path"
```

---

### task: happy-path-utc-and-nonutc

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` (add two methods after `Calculate_ReturnsNull_AndLogsWarning_WhenCronInvalid`)

- [ ] **Step 1: Add the FR-4 happy-path tests (UTC and non-UTC)**

```csharp
    [Fact]
    public void Calculate_ReturnsExpectedUtcInstant_ForUtcTimezone()
    {
        // Arrange
        var utcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var result = RecurringJobNextRunCalculator.Calculate(
            cronExpression: "0 6 * * *",
            isEnabled: true,
            timeZoneId: "UTC",
            utcNow: utcNow,
            logger: NullLogger.Instance,
            jobName: "test-job");

        // Assert
        result.Should().Be(new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc));
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Calculate_ReturnsExpectedUtcInstant_ForNonUtcTimezone()
    {
        // Arrange
        // 2026-01-01T04:59:00Z = 2026-01-01T05:59:00 local Europe/Prague time
        // (CET = UTC+1 in winter, no DST in effect on this date).
        var utcNow = new DateTime(2026, 1, 1, 4, 59, 0, DateTimeKind.Utc);

        // Act
        var result = RecurringJobNextRunCalculator.Calculate(
            cronExpression: "0 6 * * *",
            isEnabled: true,
            timeZoneId: "Europe/Prague",
            utcNow: utcNow,
            logger: NullLogger.Instance,
            jobName: "test-job");

        // Assert
        result.Should().Be(new DateTime(2026, 1, 1, 5, 0, 0, DateTimeKind.Utc));
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }
```

(Both expected values were verified by actually running `RecurringJobNextRunCalculator`-equivalent code against `NCrontab.Advanced 1.3.28` under net8.0: for the UTC case, `utcNow` is exactly midnight and the next `"0 6 * * *"` occurrence is 06:00 the same day. For the Prague case, local time at `utcNow` is 05:59, so the next occurrence is 06:00 local the same day = 05:00 UTC.)

- [ ] **Step 2: Run the tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~RecurringJobNextRunCalculatorTests"`
Expected: `Passed! - Failed: 0, Passed: 5, Skipped: 0`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs
git commit -m "test(background-jobs): cover RecurringJobNextRunCalculator happy path (UTC and Europe/Prague)"
```

---

### task: dst-boundary-cases

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` (add two methods after `Calculate_ReturnsExpectedUtcInstant_ForNonUtcTimezone`, at the end of the class)

- [ ] **Step 1: Add the FR-5 DST-boundary tests (autumn ambiguous hour, spring-forward gap)**

```csharp
    [Fact]
    public void Calculate_ReturnsExpectedUtcInstant_AroundDstAutumnAmbiguousHour()
    {
        // Arrange
        // Europe/Prague DST ends 2026-10-25: local clocks fall back from 03:00 CEST to 02:00 CET,
        // so local 02:00-03:00 occurs twice that day. TimeZoneInfo.ConvertTimeToUtc resolves an
        // ambiguous unspecified-kind DateTime using the zone's standard (CET, UTC+1) offset --
        // verified directly against this repo's NCrontab.Advanced/TimeZoneInfo combination.
        var utcNow = new DateTime(2026, 10, 24, 20, 0, 0, DateTimeKind.Utc);

        // Act
        var result = RecurringJobNextRunCalculator.Calculate(
            cronExpression: "30 2 * * *",
            isEnabled: true,
            timeZoneId: "Europe/Prague",
            utcNow: utcNow,
            logger: NullLogger.Instance,
            jobName: "test-job");

        // Assert
        result.Should().Be(new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc));
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Calculate_Throws_AroundDstSpringForwardGap()
    {
        // Arrange
        // Europe/Prague DST starts 2026-03-29: local clocks jump forward from 02:00 CET to 03:00 CEST,
        // so local 02:00-03:00 never occurs that day. TimeZoneInfo.ConvertTimeToUtc throws
        // ArgumentException for any local time inside this gap. RecurringJobNextRunCalculator.Calculate
        // only catches TimeZoneNotFoundException and CrontabException, so this exception is NOT caught
        // and propagates to the caller. This test documents CURRENT, uncaught behavior -- it is not
        // an endorsement of it. Fixing it (e.g. catching ArgumentException and returning null like the
        // other error paths) is a legitimate follow-up but explicitly out of scope for this coverage-only
        // ticket -- see spec.r1.md Out of Scope. Consider filing a separate issue after this PR merges.
        var utcNow = new DateTime(2026, 3, 28, 20, 0, 0, DateTimeKind.Utc);

        // Act
        Action act = () => RecurringJobNextRunCalculator.Calculate(
            cronExpression: "30 2 * * *",
            isEnabled: true,
            timeZoneId: "Europe/Prague",
            utcNow: utcNow,
            logger: NullLogger.Instance,
            jobName: "test-job");

        // Assert
        act.Should().Throw<ArgumentException>();
    }
```

- [ ] **Step 2: Run the full test class to verify all tests pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~RecurringJobNextRunCalculatorTests"`
Expected: `Passed! - Failed: 0, Passed: 7, Skipped: 0`

- [ ] **Step 3: Run the full backend test suite to confirm no regressions**

Run: `cd backend && dotnet build && dotnet format --verify-no-changes && dotnet test`
Expected: build succeeds, `dotnet format` reports no changes needed, all tests pass (existing tests untouched, 7 new tests added).

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs
git commit -m "test(background-jobs): cover RecurringJobNextRunCalculator DST boundary cases"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (disabled job) → `scaffold-test-class-and-disabled-path` / `Calculate_ReturnsNull_WhenJobDisabled`. ✓
- FR-2 (unknown timezone) → `unknown-timezone-path` / `Calculate_ReturnsNull_AndLogsWarning_WhenTimezoneUnknown`. ✓
- FR-3 (invalid CRON) → `invalid-cron-path` / `Calculate_ReturnsNull_AndLogsWarning_WhenCronInvalid`. ✓
- FR-4 (happy path, UTC + non-UTC) → `happy-path-utc-and-nonutc` / two tests. ✓
- FR-5 (DST boundary, both sub-cases) → `dst-boundary-cases` / two tests. ✓
- NFR-1 (test isolation/speed) → all tests are pure static calls, no I/O, no fixtures. ✓
- NFR-2 (logger verification) → `Mock<ILogger>` used exactly where FR-2/FR-3 need it; `NullLogger.Instance` elsewhere. ✓
- NFR-3 (codebase conventions) → xUnit `[Fact]`, FluentAssertions, Moq, Arrange/Act/Assert comments, file placed in `Features/BackgroundJobs/`, matching existing test files in that directory. ✓
No gaps found.

**2. Placeholder scan:** No "TBD"/"TODO"/"handle edge cases" language; every step shows complete, runnable code with concrete expected values and verified exception types/messages. No `git commit` step lacks a real message. No step says "similar to Task N" without the actual code.

**3. Type consistency:** `RecurringJobNextRunCalculator.Calculate` signature (`cronExpression, isEnabled, timeZoneId, utcNow, logger, jobName`) is used identically, with named arguments, in all seven test methods across all five tasks. `Mock<ILogger>` (non-generic, matching `Calculate`'s `ILogger` parameter type — not `ILogger<T>`) is used consistently in the two tests that need it; `NullLogger.Instance` (non-generic, from `Microsoft.Extensions.Logging.Abstractions`) is used consistently in the other five.

No issues found; plan is ready to execute as written.
