# Implementation: update-existing-tests-to-three-argument-signature

## What was implemented
Updated all existing call sites of `ICronScheduler.UpdateCronSchedule` in the test suite to the new three-argument signature `(jobName, cronExpression, timeZoneId)`, matching the widened port introduced by the earlier `widen-icronscheduler-port-and-call-site` and `update-hangfire-adapter-to-use-passed-timezone` tasks.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/UpdateRecurringJobCronHandlerTests.cs` — updated the two "never called" `Verify` assertions (lines 61, 78) to the three-argument overload with `It.IsAny<string>()` for the time zone, and strengthened the happy-path assertion (line 125) to assert the concrete forwarded time zone `"Europe/Prague"` (matching the `CreateTestJob` helper's `timeZoneId` literal), so the test now proves the handler forwards the entity's `TimeZoneId`.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs` — updated three direct `UpdateCronSchedule` call sites (lines 42, 67, 107) to pass `"Europe/Prague"` as the third argument, matching `ParityTestRecurringJob.Metadata.TimeZoneId`. No assertions were deleted, renamed, or weakened.

## Tests
No new test files added — this task only updates existing tests to compile against and correctly exercise the new three-argument `ICronScheduler.UpdateCronSchedule` signature. As noted in the task context, `UpdateCronSchedule_AfterDiscoveryRegistration_UpdatesCronInStorage` and `UpdateCronSchedule_ProducesIdenticalRecordStructureToDiscoveryRegistration` now pass because the tests hand in `"Europe/Prague"` directly; they no longer independently prove runtime/startup time-zone parity (that guarantee is restored by the next two tasks in the plan).

## How to verify
- `cd backend && dotnet build` from the repo root (solution root, not `backend/`) — succeeded: `0 Error(s)`, only pre-existing warnings in unrelated files.
- `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~BackgroundJobs"` — `Passed! - Failed: 0, Passed: 114, Skipped: 0, Total: 114`.

## Notes
The task context's `cd backend && dotnet build` instruction assumes a solution file inside `backend/`; in this checkout `Anela.Heblo.sln` lives at the repo root, so the build was run as `dotnet build Anela.Heblo.sln` from the repo root instead. Same effect (full solution build), just invoked from the correct directory for this repo's actual layout.

## PR Summary
Updated the existing BackgroundJobs test suite to call `ICronScheduler.UpdateCronSchedule` with its new three-argument signature (job name, cron expression, time zone id), following the earlier widening of the interface and the Hangfire adapter update. The happy-path handler test was also strengthened to assert the concrete forwarded time zone rather than using `It.IsAny<string>()`, closing the gap that previously let a `cron`/`timeZoneId` argument transposition go undetected.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/UpdateRecurringJobCronHandlerTests.cs` — three-argument `Verify` calls; concrete time zone assertion on the happy path
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs` — three-argument `UpdateCronSchedule` calls at all three existing call sites

## Status
DONE
