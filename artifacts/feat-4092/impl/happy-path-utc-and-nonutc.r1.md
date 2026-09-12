# Implementation: happy-path-utc-and-nonutc

## What was implemented
Added two happy-path unit tests to `RecurringJobNextRunCalculatorTests` covering `RecurringJobNextRunCalculator.Calculate` when the job is enabled and the cron expression is valid, for both a UTC timezone and a non-UTC timezone (Europe/Prague, CET winter, no DST). Both tests were inserted verbatim per the task context, placed immediately after the existing `Calculate_ReturnsNull_AndLogsWarning_WhenCronInvalid` method.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` — added `Calculate_ReturnsExpectedUtcInstant_ForUtcTimezone` and `Calculate_ReturnsExpectedUtcInstant_ForNonUtcTimezone`.

## Tests
- `Calculate_ReturnsExpectedUtcInstant_ForUtcTimezone` — utcNow = 2026-01-01T00:00:00Z, cron `"0 6 * * *"`, timezone `UTC` → expects next run `2026-01-01T06:00:00Z` with `DateTimeKind.Utc`.
- `Calculate_ReturnsExpectedUtcInstant_ForNonUtcTimezone` — utcNow = 2026-01-01T04:59:00Z (05:59 local Europe/Prague, CET winter), cron `"0 6 * * *"`, timezone `Europe/Prague` → expects next run `2026-01-01T05:00:00Z` (UTC) with `DateTimeKind.Utc`.

Test run output:
```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 425 ms - Anela.Heblo.Tests.dll (net8.0)
```
All 5 tests in the class (3 pre-existing + 2 new) pass.

## How to verify
```bash
cd /Users/pajgrtondrej/Work/GitHub/worktrees/feature-4092-Coverage-Gap-Backgroundjobs-Recurringjobnextruncal
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~RecurringJobNextRunCalculatorTests"
```

## Notes
- No production code was touched, per the hard constraint — only the test file listed above.
- Used `NullLogger.Instance` per the task snippet (the `Microsoft.Extensions.Logging.Abstractions` using was already present in the file).
- `dotnet format` verification was skipped as instructed unless trivially fast; the change is a small, style-consistent addition matching the existing test method conventions (AAA comments, same parameter style), so no formatting risk expected.
- Commit created on the existing checked-out branch; no push, no PR, no worktree/branch creation.

## PR Summary
Adds the two missing happy-path unit tests for `RecurringJobNextRunCalculator.Calculate`, closing a coverage gap for the UTC and non-UTC (Europe/Prague, winter/no-DST) timezone conversion cases when the job is enabled and the cron expression is valid.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` — added `Calculate_ReturnsExpectedUtcInstant_ForUtcTimezone` and `Calculate_ReturnsExpectedUtcInstant_ForNonUtcTimezone`.

## Status
DONE
