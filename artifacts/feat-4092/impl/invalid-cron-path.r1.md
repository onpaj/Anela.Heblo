# Implementation: invalid-cron-path

## What was implemented
Added one new `[Fact]` test method, `Calculate_ReturnsNull_AndLogsWarning_WhenCronInvalid`, to `RecurringJobNextRunCalculatorTests.cs`, covering the invalid-CRON-expression path of `RecurringJobNextRunCalculator.Calculate`. The test asserts that an unparseable cron string (`"not a cron"`) causes `Calculate` to return `null` and to log exactly one `Warning`-level message, matching the existing pattern used by the sibling `Calculate_ReturnsNull_AndLogsWarning_WhenTimezoneUnknown` test. The method was inserted immediately after that sibling test, exactly as specified in the task context, with no other changes.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` — added `Calculate_ReturnsNull_AndLogsWarning_WhenCronInvalid` test method after `Calculate_ReturnsNull_AndLogsWarning_WhenTimezoneUnknown`. No other file touched, no production code changed.

## Tests
- `RecurringJobNextRunCalculatorTests.cs` now has 3 `[Fact]` tests:
  - `Calculate_ReturnsNull_WhenJobDisabled` (pre-existing)
  - `Calculate_ReturnsNull_AndLogsWarning_WhenTimezoneUnknown` (pre-existing)
  - `Calculate_ReturnsNull_AndLogsWarning_WhenCronInvalid` (new) — verifies `Calculate` returns `null` and logs a single `Warning` when the cron expression `"not a cron"` cannot be parsed.

## How to verify
```
cd backend
dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~RecurringJobNextRunCalculatorTests"
```
Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0`.

Actual result obtained in this run:
```
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 74 ms - Anela.Heblo.Tests.dll (net8.0)
```
Build also succeeded: 0 errors (240 pre-existing warnings across the test project, unrelated to this change).

## Notes
- Followed the task context verbatim — the test body, name, and placement match exactly what was specified.
- Did not modify production code, per the task instructions and CLAUDE.md's surgical-changes rule.
- Only the test file was committed; `artifacts/feat-4092/state.json` shows as modified in the working tree (git status) but was intentionally left uncommitted — the orchestrator/pipeline handles artifact commits separately, per the assignment instructions.
- Did not push, per instructions.

## PR Summary
This change closes a coverage gap for `RecurringJobNextRunCalculator` by adding a unit test for the invalid-CRON-expression path (FR-3). Previously the calculator's behavior when handed a malformed cron string (`CrontabSchedule.Parse` throwing `NCrontab.Advanced.Exceptions.CrontabException`) was exercised implicitly but not asserted directly in a dedicated test. The new test confirms `Calculate` catches the parse failure, returns `null`, and logs exactly one `Warning`-level message — mirroring the existing assertion style used for the "unknown timezone" case. No production code was touched; this is a test-only change.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` — added `Calculate_ReturnsNull_AndLogsWarning_WhenCronInvalid` test method

## Status
DONE
