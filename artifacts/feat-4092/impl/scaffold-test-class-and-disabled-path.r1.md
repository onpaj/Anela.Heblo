# Implementation: scaffold-test-class-and-disabled-path (r1)

## Summary

Created `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs`
with the test class scaffold and the first test case covering the "job disabled" path
(`Calculate_ReturnsNull_WhenJobDisabled`).

## What it verifies

- `RecurringJobNextRunCalculator.Calculate` returns `null` immediately when `isEnabled` is `false`,
  without attempting any timezone lookup or CRON parsing.
- The logger is never invoked for this path (`Times.Never`), matching the source: the disabled-job
  branch returns before any `logger.LogWarning` call.

## Verification

Ran:

```
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobNextRunCalculatorTests"
```

Result: `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1`

## Files changed

- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` (new)
