# Implementation: add-timezone-forwarding-regression-tests

## What was implemented

Added three regression tests to `HangfireRecurringJobSchedulerTests` covering the
`timeZoneId` forwarding behavior of `HangfireRecurringJobScheduler.UpdateCronSchedule`:

1. `UpdateCronSchedule_UsesPassedTimeZone_NotJobMetadataTimeZone` — proves the
   *argument* time zone (not `IRecurringJob.Metadata`'s "Europe/Prague") drives the
   Hangfire registration, by registering via discovery (which uses the metadata
   zone) and then updating with a deliberately different zone ("UTC").
2. `UpdateCronSchedule_WithMissingTimeZoneId_ThrowsArgumentException` (theory over
   `null`, `""`, `"   "`) — proves the guard clause rejects a missing time zone
   before any side effect, and that nothing was written to storage.
3. `UpdateCronSchedule_WithUnresolvableTimeZone_LogsErrorAndLeavesScheduleUnchanged`
   — proves an unresolvable zone id ("Not/AZone") is caught, never rethrown
   (fire-and-forget), and leaves the previously stored cron/time zone untouched.

Inserted immediately after `UpdateCronSchedule_ProducesIdenticalRecordStructureToDiscoveryRegistration`
and before `Dispose()`, matching the file's existing arrange-block style (the
discovery-registration setup is repeated verbatim, not extracted into a shared
helper, per the task context's explicit instruction not to refactor).

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs` — added the three tests above (no new `using` directives needed; all were already present).

## Tests

- `HangfireRecurringJobSchedulerTests.cs` — 3 new test methods (1 fact + 1 theory with 3 inline data rows + 1 fact) = 5 new test cases, bringing the file's total to 8 test cases.

## How to verify

```bash
cd backend && dotnet build
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~HangfireRecurringJobSchedulerTests"
```

Expected: `Build succeeded.` with 0 errors, no new warnings; `Passed!` with 8/8 tests.

## Load-bearing verification performed

Per the task context's Step 4, temporarily reverted
`HangfireRecurringJobScheduler.RegisterOrUpdate` call to pass the hardcoded
`"Europe/Prague"` (simulating the pre-fix behavior that read from job metadata
instead of the caller-supplied `timeZoneId`), and re-ran
`UpdateCronSchedule_UsesPassedTimeZone_NotJobMetadataTimeZone` in isolation:

- **Before revert (current/fixed code):** 8/8 passed.
- **With temporary revert:** 1 failed — `Assert.Equal() Failure: Expected: UTC, Actual: Europe/Prague`, confirming the test is load-bearing and would have caught the original defect.
- **After restoring the fix** (`git checkout -- backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs`): re-ran the same single test — 1/1 passed.
- Confirmed via `git status --porcelain` that `HangfireRecurringJobScheduler.cs` is unmodified before committing — only the test file is part of this change.

## Notes

No deviations from the task context. The task context said `cd backend && dotnet build`,
but this repo's solution file (`Anela.Heblo.sln`) lives at the worktree root, not inside
`backend/` — ran `dotnet build` from the worktree root instead, which is also what
`docs/development/setup.md` documents as the canonical build command. Same applies to the
`dotnet test` invocations, which already used a path relative to the worktree root as written.

## PR Summary

Added three regression tests to `HangfireRecurringJobSchedulerTests` that lock in the
`UpdateCronSchedule(jobName, cronExpression, timeZoneId)` contract added earlier in this
feature: the caller-supplied time zone drives the live Hangfire schedule update (not
`IRecurringJob.Metadata`), a missing/blank time zone throws before any side effect, and an
unresolvable time zone is logged and swallowed without corrupting the existing schedule.
Verified the primary new test is load-bearing by temporarily reproducing the pre-fix
behavior and confirming the test fails with the expected message, then restored the fix.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs` — added `UpdateCronSchedule_UsesPassedTimeZone_NotJobMetadataTimeZone`, `UpdateCronSchedule_WithMissingTimeZoneId_ThrowsArgumentException` (theory), and `UpdateCronSchedule_WithUnresolvableTimeZone_LogsErrorAndLeavesScheduleUnchanged`

## Status
DONE
