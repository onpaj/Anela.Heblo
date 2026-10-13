# Implementation: update-hangfire-adapter-to-use-passed-timezone

## What was implemented

`HangfireRecurringJobScheduler.UpdateCronSchedule` now takes a third `timeZoneId`
parameter (matching the widened `ICronScheduler` interface from the prior task)
and uses it directly when calling `HangfireJobRegistrationHelper.RegisterOrUpdate`,
instead of re-deriving the time zone from `IRecurringJob.Metadata.TimeZoneId`.
The class-level XML doc was extended to record that the caller (the DB
`RecurringJobConfiguration` row, via `RecurringJobSeeder`'s startup re-sync) now
owns the time zone, and why that is equivalent to the old metadata-derived value
today.

The DI-scope lookup was simplified to resolve just the job's `Type` (`jobType`)
instead of holding onto the whole `IRecurringJob` instance, since the method no
longer needs anything else off it (metadata is deliberately not read for the
time zone anymore). The not-found log message and warning text were also
updated to describe the effect more precisely: the schedule update is skipped,
the DB row is still saved, and the new schedule applies on next app start if the
job type is restored.

All three arguments (`jobName`, `cronExpression`, `timeZoneId`) are validated
with `ArgumentException.ThrowIfNullOrWhiteSpace` in parameter order, all three
guards run before `_serviceProvider.CreateScope()`, and the method remains
fire-and-forget (`catch` logs and returns, never rethrows), per the task's
load-bearing constraints. Exactly one `CreateScope()`, one
`GetServices<IRecurringJob>()` enumeration, and one `RegisterOrUpdate` call
per invocation, unchanged from before.

## Files created/modified
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs` — `UpdateCronSchedule` widened to 3 args, uses caller-supplied `timeZoneId`, class doc updated, log messages updated.

## Tests

None written in this task — the task context does not require new tests here
(existing tests calling the old 2-arg overload are addressed by the next task,
`update-existing-tests-to-three-argument-signature`).

## How to verify

1. `grep -n "Metadata.TimeZoneId" backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs` → no matches (confirmed, exit code 1).
2. `grep -n "Metadata" backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs` → 2 matches: the XML-doc line referencing `IRecurringJob.Metadata` and the `.FirstOrDefault(j => j.Metadata.JobName == jobName)?.GetType();` line. (Note: the task context's step 3 describes this as "three lines total, one of them the lowercase-metadata comment," but that comment uses lowercase `metadata`, which a case-sensitive `grep "Metadata"` does not match — so it correctly does not appear in this grep's output. The verbatim comment text from the task's own Step 2 code block was used as-is.)
3. `cd backend && dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj` → `Build succeeded.`, 0 errors, 0 new warnings (142 pre-existing warnings elsewhere in the solution, none in this file).
4. Solution-wide `dotnet build` still fails at this point as expected — the two test files calling the old 2-argument `UpdateCronSchedule` overload are fixed by the next task.

## Notes

- No deviations from the task context's exact code blocks (Step 1 and Step 2 were applied verbatim).
- Committed as its own commit: `refactor(background-jobs): use the caller-supplied timeZoneId in HangfireRecurringJobScheduler`.

## PR Summary
Widened `HangfireRecurringJobScheduler.UpdateCronSchedule` to accept a `timeZoneId` parameter and use it directly instead of re-reading `IRecurringJob.Metadata.TimeZoneId`, so the live Hangfire schedule update now honors the caller-supplied (DB-row) time zone rather than silently ignoring it.

### Changes
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs` — `UpdateCronSchedule(string, string, string)`, updated XML doc and log messages

## Status
DONE
