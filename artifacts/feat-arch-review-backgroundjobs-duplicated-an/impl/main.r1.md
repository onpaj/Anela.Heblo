Now writing the final implementation artifact:

---

# Implementation: Consolidate Hangfire RecurringJob Registration

## What was implemented

Extracted a single shared `HangfireJobRegistrationHelper` to replace two duplicated reflection-based Hangfire registration paths. Both `RecurringJobDiscoveryService` (startup) and `HangfireRecurringJobScheduler` (runtime CRON updates) now delegate to the helper, ensuring both paths use the same `RecurringJobOptions` overload and produce identical Hangfire `RecurringJob` records.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs` — Public static helper: validates inputs, resolves private generic dispatcher via reflection, invokes `RecurringJob.AddOrUpdate<TJob>(..., RecurringJobOptions)`, unwraps `TargetInvocationException`
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs` — Removed `RegisterRecurringJobInternal<TJob>` + reflection dispatch; now calls `HangfireJobRegistrationHelper.RegisterOrUpdate(...)` 
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs` — Removed `UpdateJobInternal<TJob>` (legacy `TimeZoneInfo` overload) + reflection dispatch; now calls helper; removed `using System.Reflection` and `using Hangfire`
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobRegistrationHelperTests.cs` — 11 test cases covering: successful registration, update-on-second-call, ArgumentNullException, ArgumentException for all 3 string args (Theory ×3 each), ArgumentException for non-IRecurringJob type (verifies paramName), TimeZoneNotFoundException unwrapping
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs` — 3 parity tests: unknown-job-name returns cleanly, update-after-discovery changes CRON, structural parity (job type/method/timezone match between discovery and scheduler paths)

## Tests

- `HangfireJobRegistrationHelperTests` — 11 cases; all pass
- `HangfireRecurringJobSchedulerTests` — 3 cases; all pass
- Existing `RecurringJobDiscoveryServiceTests` — 3 cases; continue to pass
- Full BackgroundJobs suite: **88 tests pass, 0 failures**
- Full solution build: **0 errors, 12 pre-existing warnings**

## How to verify

```bash
cd backend
dotnet test Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Features.BackgroundJobs" --nologo
# Expected: 88 passed, 0 failed

grep -rn "RecurringJob.AddOrUpdate" src/
# Expected: 1 match in HangfireJobRegistrationHelper.cs:77

grep -rn "RegisterRecurringJobInternal\|UpdateJobInternal" src/ test/
# Expected: no matches
```

## Notes

- **Architecture correction (Amendment 1):** The spec proposed the helper in `Anela.Heblo.API/Infrastructure/Hangfire/` — this would create a circular project reference since `Application` cannot reference `API`. The helper lives in `Application` instead, reachable via the existing `API → Application` reference direction.
- **Visibility (Amendment 2):** Helper is `public static` (not `internal`) because it's consumed cross-assembly from the `API` project.
- One post-review fix applied: added `_ =` discard on `closed.Invoke(...)` return value for defensive clarity.
- Minor issues noted by reviewer (redundant DI registration in test setup, shared `TestWebHostEnvironment` utility) are non-blocking and left as-is per YAGNI.

## PR Summary

Extracted `HangfireJobRegistrationHelper` — a single public static helper — to consolidate two independently-drifted Hangfire recurring-job registration paths. Before this change, `RecurringJobDiscoveryService` used the `RecurringJobOptions` overload while `HangfireRecurringJobScheduler` used the legacy `TimeZoneInfo` overload, meaning startup and runtime CRON updates could produce subtly different Hangfire storage records and any future option (queue name, misfire policy, etc.) would require editing both files.

Both consumers now call `HangfireJobRegistrationHelper.RegisterOrUpdate(jobType, jobName, cron, tzId)`, which validates inputs, dispatches to a private generic method via reflection, unwraps `TargetInvocationException`, and calls `RecurringJob.AddOrUpdate<TJob>(..., RecurringJobOptions)`. New parity tests assert that a job registered via discovery and then updated via the scheduler ends up with the same method signature, time zone, and job type in Hangfire storage.

### Changes
- `HangfireJobRegistrationHelper.cs` — new; single reflection-based registration entry point
- `RecurringJobDiscoveryService.cs` — removed `RegisterRecurringJobInternal<TJob>` + reflection dispatch (~14 lines)
- `HangfireRecurringJobScheduler.cs` — removed `UpdateJobInternal<TJob>` + reflection dispatch (~22 lines); class doc updated
- `HangfireJobRegistrationHelperTests.cs` — new; 11 unit tests (validation, happy path, exception unwrapping)
- `HangfireRecurringJobSchedulerTests.cs` — new; 3 parity/integration tests

## Status

DONE