# Implementation: widen-icronscheduler-port-and-call-site

## What was implemented
Widened the `ICronScheduler.UpdateCronSchedule` port to accept a third `timeZoneId`
parameter, and updated its interface doc comment to describe the fire-and-forget
contract (implementations log/swallow scheduling failures except for invalid
arguments, which throw before any side effect; the caller owns the time zone and
implementations must not re-derive it elsewhere). Updated the single call site in
`UpdateRecurringJobCronHandler` to forward the already-loaded job's `TimeZoneId`
to the widened method, with no other change to that file's logic, ordering, or
error handling.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/ICronScheduler.cs` — widened `UpdateCronSchedule` signature to `(string jobName, string cronExpression, string timeZoneId)` with updated XML doc comments; no `using` directives added (verified zero remain).
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs` — line 74 call site now passes `job.TimeZoneId` as the third argument; no other line in the file changed.

## Tests
None written by this task — the task context explicitly scopes this to the
interface + call-site widening only; the adapter implementation and its tests
are handled by the next two tasks in the plan.

## How to verify
- `grep -c "^using" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/ICronScheduler.cs` → `0` (confirmed).
- `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` → `Build succeeded.`, 0 errors (confirmed; 123 pre-existing warnings unrelated to these two files, no new warnings introduced).
- `cd backend && dotnet build` (whole solution) is expected to fail at this point with `CS0535` on `HangfireRecurringJobScheduler` (does not yet implement the widened interface member) plus `CS1501` in two test files — this is the documented, intentional state until the next two tasks land. Not run here since it is explicitly out of scope for this task.

## Notes
No deviations from the task context. The code changes match the exact snippets
given in Steps 1 and 3 verbatim. Did not touch the Hangfire adapter or any test
file, per the task's explicit scope boundary.

## Status
DONE
