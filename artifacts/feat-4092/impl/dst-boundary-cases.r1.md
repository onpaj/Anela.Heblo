# Implementation: dst-boundary-cases

## What was implemented
Added the two FR-5 DST-boundary unit tests to `RecurringJobNextRunCalculatorTests`, inserted verbatim from the task context immediately after `Calculate_ReturnsExpectedUtcInstant_ForNonUtcTimezone`, at the end of the class:

- `Calculate_ReturnsExpectedUtcInstant_AroundDstAutumnAmbiguousHour` — Europe/Prague, autumn DST-end ambiguous local hour (2026-10-25, local 02:00–03:00 occurs twice).
- `Calculate_Throws_AroundDstSpringForwardGap` — Europe/Prague, spring DST-start invalid local hour (2026-03-29, local 02:00–03:00 never occurs).

No production code was modified — only the test file, per the hard constraint (P1).

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` — added the two DST-boundary test methods above. The five pre-existing tests were left untouched.

## Tests
- `Calculate_ReturnsExpectedUtcInstant_AroundDstAutumnAmbiguousHour` — utcNow = `2026-10-24T20:00:00Z` (local Prague 22:00 CEST, still pre-fallback), cron `"30 2 * * *"`, timezone `Europe/Prague`. `CrontabSchedule.GetNextOccurrence` lands on local `2026-10-25 02:30:00` (unspecified kind), which is inside the ambiguous hour (occurs both as CEST and CET that day). Asserts the result is exactly `2026-10-25T01:30:00Z` with `DateTimeKind.Utc` — i.e. `TimeZoneInfo.ConvertTimeToUtc` resolved the ambiguous unspecified-kind local time using the zone's **standard** offset (CET, UTC+1), not daylight (CEST, UTC+2, which would have produced `00:30:00Z`).
- `Calculate_Throws_AroundDstSpringForwardGap` — utcNow = `2026-03-28T20:00:00Z` (local Prague 21:00 CET, pre-jump), cron `"30 2 * * *"`, timezone `Europe/Prague`. Next occurrence lands on local `2026-03-29 02:30:00`, which falls inside the spring-forward gap (never exists locally that day). Asserts `TimeZoneInfo.ConvertTimeToUtc` throws `ArgumentException`, uncaught by `RecurringJobNextRunCalculator.Calculate` (which only catches `TimeZoneNotFoundException` and `CrontabException`), and therefore propagates to the caller.

Ran the full test class as instructed (Step 2 of the task context): 7/7 pass (5 pre-existing + 2 new).

### Discrepancy
None. I read `RecurringJobNextRunCalculator.Calculate` in full before writing/confirming the assertions (see below) and treated the task-context comment's claimed UTC values as an unverified hypothesis per P4c/d. I then built and ran the tests exactly as written from the task context (no adjustment), and the actual `dotnet test` run confirms both hypothesized values/behaviors exactly:
- Autumn ambiguous hour resolves to the **standard** offset (CET, UTC+1) → `2026-10-25T01:30:00Z`, matching the task context's asserted value and confirming (per P4b) that the test would fail had the implementation instead resolved via the daylight offset (which would yield `2026-10-25T00:30:00Z`).
- Spring gap causes `TimeZoneInfo.ConvertTimeToUtc` to throw `ArgumentException`, uncaught by `Calculate`, matching the task context's asserted exception type.

No rewriting of assertions was needed; the code as committed is the code as specified in the task context.

### Verbatim test output
Build (`dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false`): `0 Error(s)` (166 pre-existing warnings, none introduced by this change — no warnings reference `RecurringJobNextRunCalculatorTests.cs`).

Filtered test run (`dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~RecurringJobNextRunCalculatorTests"`):
```
Test run for /Users/pajgrtondrej/Work/GitHub/worktrees/feature-4092-Coverage-Gap-Backgroundjobs-Recurringjobnextruncal/backend/test/Anela.Heblo.Tests/bin/Debug/net8.0/Anela.Heblo.Tests.dll (.NETCoreApp,Version=v8.0)
VSTest version 18.0.1 (arm64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7, Duration: 138 ms - Anela.Heblo.Tests.dll (net8.0)
```
Matches the task context's Step 2 expectation exactly: `Passed! - Failed: 0, Passed: 7, Skipped: 0`.

`dotnet format test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --verify-no-changes`: exit clean, no output — no formatting drift.

## How to verify
```bash
cd /Users/pajgrtondrej/Work/GitHub/worktrees/feature-4092-Coverage-Gap-Backgroundjobs-Recurringjobnextruncal/backend
dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~RecurringJobNextRunCalculatorTests"
```

## Notes
- Per P2, did not run a bare/solution-wide `dotnet test`; used the exact scoped build+test sequence given in the pipeline pins.
- Per P1, production code (`RecurringJobNextRunCalculator.cs`) was read in full to derive/confirm the expected values but was not modified.
- The second test (`Calculate_Throws_AroundDstSpringForwardGap`) documents current, uncaught `ArgumentException` behavior in the production code for the spring-forward gap. This is arguably a latent bug (the other error paths return `null` after logging a warning; this path throws instead), but per the task-context comment and the ticket's stated Out of Scope, fixing it is explicitly not part of this coverage-only task. Flagging it here as a legitimate follow-up candidate, not fixing it.
- No worktree or branch was created; work was committed directly on the already-checked-out branch `feature/4092-Coverage-Gap-Backgroundjobs-Recurringjobnextruncal`.

## PR Summary
Adds the two FR-5 DST-boundary unit tests for `RecurringJobNextRunCalculator.Calculate`: the Europe/Prague autumn ambiguous-hour case (asserts the exact UTC instant produced when `TimeZoneInfo.ConvertTimeToUtc` resolves an ambiguous local time using the zone's standard offset) and the Europe/Prague spring-forward gap case (asserts the currently-uncaught `ArgumentException` that propagates when the computed local run time falls inside the nonexistent hour). Both exact expected values were confirmed by running the tests against the actual implementation, not merely asserted from the task context. No production code changes; test class now has 7 tests, all passing.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs` — added `Calculate_ReturnsExpectedUtcInstant_AroundDstAutumnAmbiguousHour` and `Calculate_Throws_AroundDstSpringForwardGap`.

## Status
DONE
