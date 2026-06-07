# Implementation: Consolidate Hangfire RecurringJob Registration

## What was implemented

Extracted shared `HangfireJobRegistrationHelper` that centralises the reflection-based generic dispatch and the call to `RecurringJob.AddOrUpdate<TJob>(..., RecurringJobOptions)`. Both startup registration (`RecurringJobDiscoveryService`) and runtime CRON updates (`HangfireRecurringJobScheduler`) now delegate to this single helper, eliminating duplicated reflection plumbing and standardising on the newer `RecurringJobOptions` overload across both code paths.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs` — new public static helper; validates inputs, resolves private `RegisterOrUpdateGeneric<TJob>` via reflection, invokes `RecurringJob.AddOrUpdate<TJob>(..., RecurringJobOptions)`, unwraps `TargetInvocationException`
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs` — replaced reflection block (lines 79–96) + deleted `RegisterRecurringJobInternal<TJob>` private method; now calls helper; updated usings
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs` — replaced reflection block (lines 41–64) + deleted `UpdateJobInternal<TJob>` private method; now calls helper; updated XML doc and usings
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobRegistrationHelperTests.cs` — new unit tests (TDD red→green): 8 test methods covering successful registration, idempotent updates, all argument validation cases, type-contract enforcement, and `TargetInvocationException` unwrapping
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs` — new behaviour-parity tests: 3 tests covering unknown-job no-op, CRON update after discovery registration, and structural parity between discovery and scheduler registration paths

## Tests

| File | Coverage |
|------|----------|
| `HangfireJobRegistrationHelperTests.cs` | Valid registration; idempotent update; null `jobType`; null/empty/whitespace `jobName`, `cronExpression`, `timeZoneId`; non-`IRecurringJob` type; `TimeZoneNotFoundException` unwrapping |
| `HangfireRecurringJobSchedulerTests.cs` | Unknown job name (no-op); CRON update after startup registration; structural parity (return type, timezone, job type, method name identical after both paths) |
| `RecurringJobDiscoveryServiceTests.cs` (existing) | All 3 pre-existing tests continue to pass |

Total BackgroundJobs test count: **88 passed, 0 failed**.

## How to verify

```bash
# Run all BackgroundJobs tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Features.BackgroundJobs" --nologo

# Confirm exactly one RecurringJob.AddOrUpdate call site
grep -rn "RecurringJob.AddOrUpdate" backend/src
# Expected: 1 match in HangfireJobRegistrationHelper.cs

# Confirm no orphaned old methods
grep -rn "RegisterRecurringJobInternal\|UpdateJobInternal" backend/src backend/test
# Expected: no matches

# Full build
dotnet build backend/Anela.Heblo.sln --nologo
```

## Notes

- **Helper location corrected from spec FR-6**: Spec proposed `Anela.Heblo.API/Infrastructure/Hangfire/`; architecture review (Amendment 1) corrected to `Anela.Heblo.Application/Features/BackgroundJobs/Services/` because `Application` cannot reference `API` (circular). Application already references `Hangfire.Core`.
- **`public static` visibility** (Amendment 2): `internal` would block API-project consumption across assembly boundaries.
- **`TargetInvocationException` unwrapping** (Amendment 3): Helper rethrows `ex.InnerException` so callers see `TimeZoneNotFoundException` directly; both caller try/catch blocks log structured context as before.
- **Legacy `TimeZoneInfo` overload** is fully removed from the codebase; the single call site in `RegisterOrUpdateGeneric<TJob>` uses `RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId) }` only.
- No `csproj` changes, no DI changes, no database migrations, no public API surface changes.

## PR Summary

Consolidated duplicated Hangfire recurring-job registration into a single `HangfireJobRegistrationHelper`, eliminating two independent copies of the reflection plumbing (`GetMethod → MakeGenericMethod → Invoke`) and standardising both startup and runtime-update paths on the `RecurringJobOptions` overload.

Before this change, `RecurringJobDiscoveryService` (startup) used the `RecurringJobOptions` overload while `HangfireRecurringJobScheduler` (runtime updates) used the legacy `TimeZoneInfo` overload, meaning the two code paths could produce subtly different Hangfire recurring-job records and any future option (queue name, misfire policy, retry count) would require editing both files.

After this change there is exactly one `RecurringJob.AddOrUpdate<TJob>` call site in the entire solution; all future changes to recurring-job registration are made in one place.

### Changes
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs` — new shared helper: input validation, reflection dispatch, `RecurringJobOptions` overload, `TargetInvocationException` unwrapping
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs` — delegates to helper; reflection block + private generic method removed
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs` — delegates to helper; reflection block + private generic method removed
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobRegistrationHelperTests.cs` — unit tests for the new helper (8 cases)
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs` — behaviour-parity tests (3 cases)

## Status
DONE
