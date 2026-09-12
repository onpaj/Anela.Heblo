# Implementation: code-review-fixes r1

## What was implemented
Fixed a Blocking code-review finding in `RecurringJobNextRunCalculatorTests.cs`: two warning-log assertions used `It.Is<It.IsAnyType>((state, t) => true)`, which matches ANY logged state and would still pass even if the warning message were empty or unrelated. Replaced both with predicates that assert the logged message contains the relevant input value (`timeZoneId` / `cronExpression`) AND the `jobName`, matching the idiom already used in `GetRecurringJobsListHandlerTests.cs`.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` — tightened two `logger.Verify` predicates (timezone-unknown and cron-invalid tests) to check the logged state string contains the specific bad input and the job name; added one `// Assert`-style comment line above each verify explaining what the warning must name.

## Tests
- `Calculate_ReturnsNull_AndLogsWarning_WhenTimezoneUnknown` — now asserts the Warning-level log message contains `"Not/A/Real/Zone"` and `"test-job"`.
- `Calculate_ReturnsNull_AndLogsWarning_WhenCronInvalid` — now asserts the Warning-level log message contains `"not a cron"` and `"test-job"`.
- All 7 tests in the file pass (no other tests touched).

## How to verify
```
cd backend/test/Anela.Heblo.Tests
dotnet build Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~RecurringJobNextRunCalculatorTests"
```
Expected: `Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7`.

## Deliberate-failure check
Changed the timezone test's expected substring from `"Not/A/Real/Zone"` to `"ZZZ_CANNOT_MATCH_ZZZ"` (a value the log message cannot contain), rebuilt, and re-ran the filtered tests. The test failed as expected with Moq reporting the expected invocation was never performed, and it printed the actual performed invocation containing the real message text:

```
[xUnit.net 00:00:01.43]     Anela.Heblo.Tests.Features.BackgroundJobs.RecurringJobNextRunCalculatorTests.Calculate_ReturnsNull_AndLogsWarning_WhenTimezoneUnknown [FAIL]
  Failed Anela.Heblo.Tests.Features.BackgroundJobs.RecurringJobNextRunCalculatorTests.Calculate_ReturnsNull_AndLogsWarning_WhenTimezoneUnknown [9 ms]
  Error Message:
   Moq.MockException :
Expected invocation on the mock once, but was 0 times: x => x.Log<It.IsAnyType>(LogLevel.Warning, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("ZZZ_CANNOT_MATCH_ZZZ") && v.ToString().Contains("test-job")), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>())

Performed invocations:

   Mock<ILogger:3> (x):

      ILogger.Log<FormattedLogValues>(LogLevel.Warning, 0, Timezone 'Not/A/Real/Zone' not found on host, NextRunAt will be null for job 'test-job', System.TimeZoneNotFoundException: The time zone ID 'Not/A/Real/Zone' was not found on the local computer.
 ...
Failed!  - Failed:     1, Passed:     6, Skipped:     0, Total:     7, Duration: 136 ms
```

Restored the substring to `"Not/A/Real/Zone"`, rebuilt, and re-ran the filtered tests: all 7 passed again (`Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7, Duration: 69 ms`). This confirms the new assertions are not vacuous — they actually bite on a wrong/missing message.

## Notes
No production code was touched. `RecurringJobNextRunCalculator.cs` log call sites were only read for reference, not modified. No other tests in the file were changed. Did not commit or push per orchestrator instructions.

## PR Summary
Addressed a Blocking code-review finding on feat-4092 by making two warning-log test assertions in `RecurringJobNextRunCalculatorTests.cs` actually verify the log content (timezone/cron value + job name) instead of matching any logged state unconditionally.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` — tightened `It.Is<It.IsAnyType>` predicates in the timezone-unknown and cron-invalid warning tests to assert on actual message content; added clarifying assert comments.

## Status
DONE

## Orchestrator-level independent verification

The orchestrator re-ran the deliberate-failure check independently, probing the
*other* test and each conjunct separately, to confirm both halves of every
predicate are load-bearing:

1. **Cron value conjunct** — `Contains("not a cron")` -> `Contains("XXX_IMPOSSIBLE_XXX")`.
   `Calculate_ReturnsNull_AndLogsWarning_WhenCronInvalid` FAILED. Moq dumped the
   real performed invocation:
   `ILogger.Log<FormattedLogValues>(LogLevel.Warning, 0, Invalid CRON expression 'not a cron' for job 'test-job', NextRunAt will be null, ...)`
   Result: `Failed: 1, Passed: 6, Total: 7`.

2. **jobName conjunct** — in the timezone test, `Contains("test-job")` ->
   `Contains("NO_SUCH_JOB_NAME")` while leaving the timezone substring correct.
   `Calculate_ReturnsNull_AndLogsWarning_WhenTimezoneUnknown` FAILED
   (`Failed: 1, Passed: 6, Total: 7`), proving the second conjunct is not
   redundant.

3. **Restored** both mutations; confirmed no mutation markers remain in the file.
   Re-ran with the broader `FullyQualifiedName~RecurringJob` filter (covers the
   sibling handler tests, so any regression there would surface):
   `Passed! - Failed: 0, Passed: 94, Skipped: 0, Total: 94`.

Also verified `dotnet format --verify-no-changes` on the modified file: exit 0,
no formatting drift. `dotnet build` of the test project: 0 errors.
