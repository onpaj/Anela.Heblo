# Implementation: unknown-timezone-path

## What was implemented
Added a unit test covering the FR-2 unknown-timezone path of `RecurringJobNextRunCalculator.Calculate`: when `TimeZoneInfo.FindSystemTimeZoneById` throws `TimeZoneNotFoundException` for an unknown timezone id, `Calculate` catches it, logs exactly one `Warning`, and returns `null`. Verified this against the production source before writing the test — the real behaviour matches the task snippet exactly, so no deviation was needed.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` — added `Calculate_ReturnsNull_AndLogsWarning_WhenTimezoneUnknown`, a new `[Fact]` after `Calculate_ReturnsNull_WhenJobDisabled`, asserting `result.Should().BeNull()` and that `logger.Log` was invoked exactly once at `LogLevel.Warning`.

## Tests
`backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` — now covers both the disabled-job path (pre-existing) and the unknown-timezone path (new).

Test-run output:
```
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 577 ms - Anela.Heblo.Tests.dll (net8.0)
```

## How to verify
```bash
cd /Users/pajgrtondrej/Work/worktrees/feature-4092-Coverage-Gap-Backgroundjobs-Recurringjobnextruncal
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~RecurringJobNextRunCalculatorTests"
```

## Notes
No deviations from the task context. Production source `RecurringJobNextRunCalculator.Calculate` was read prior to writing the test and confirmed to catch `TimeZoneNotFoundException`, call `logger.LogWarning(...)` exactly once, and return `null` — matching the provided test snippet verbatim. No production code was modified.

## PR Summary
Adds unit test coverage for the unknown-timezone branch of `RecurringJobNextRunCalculator.Calculate`, closing a coverage gap identified for issue #4092. The new test confirms that an unresolvable timezone id causes the calculator to log a single warning and return `null` rather than throwing, matching the documented FR-2 behaviour. No production code changed — coverage-only addition, verified against the real `TimeZoneInfo.FindSystemTimeZoneById` exception path.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` — added `Calculate_ReturnsNull_AndLogsWarning_WhenTimezoneUnknown` test

## Status
DONE
