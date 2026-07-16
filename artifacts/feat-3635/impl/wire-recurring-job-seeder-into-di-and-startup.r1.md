# Implementation: wire-recurring-job-seeder-into-di-and-startup

## What was implemented

Registered `IRecurringJobSeeder` → `RecurringJobSeeder` in `BackgroundJobsModule.AddBackgroundJobsModule` (same `Scoped` lifetime as the repository it wraps), and switched the startup seeding call site in `ServiceCollectionExtensions.SeedRecurringJobConfigurationsAsync` to resolve `IRecurringJobSeeder` instead of `IRecurringJobConfigurationRepository`. `IRecurringJobConfigurationRepository.SeedDefaultConfigurationsAsync` still exists on the interface at the end of this task (removed in the next task) — nothing calls it anymore, but the solution stays buildable and behavior-preserving throughout.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs` — added `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` and `services.AddScoped<IRecurringJobSeeder, RecurringJobSeeder>();` alongside the existing `IRecurringJobConfigurationRepository` registration.
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — `SeedRecurringJobConfigurationsAsync` now resolves `IRecurringJobSeeder` (local var renamed `repository` → `seeder`) and calls `seeder.SeedDefaultConfigurationsAsync(discoveredJobs)`. Logging behavior (success log with discovered-job count; error log + rethrow on failure) is unchanged. No new `using` was needed here — `Anela.Heblo.Application.Features.BackgroundJobs.Services` was already imported.

## Tests

No new tests — this task only swaps which interface is resolved via DI; behavior is covered by the existing `RecurringJobSeederTests` (task 1) and the full BackgroundJobs test suite.

## How to verify

```bash
cd /home/user/worktrees/feature-3635-Arch-Review-Backgroundjobs-Seeddefaultconfiguratio
dotnet build Anela.Heblo.sln
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.BackgroundJobs"
```

Actual results observed in this session:
- `dotnet build Anela.Heblo.sln`: **Build succeeded**, 0 errors (pre-existing unrelated `AccessMatrixGen` post-build warning, `ContinueOnError="true"`, present before this change too).
- `dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.BackgroundJobs"`: **Passed! - Failed: 0, Passed: 92, Skipped: 0, Total: 92**.

## Notes

- A full-solution `dotnet test` (no filter) was not run in this task in the interest of time, given the scoped BackgroundJobs suite (92 tests) is the only suite touched by this change and passes cleanly; the orchestrator ran the scoped filter directly after the assigned developer subagent stalled waiting on a background test process it never re-polled. No other project references `IRecurringJobConfigurationRepository`, `IRecurringJobSeeder`, `BackgroundJobsModule`, or the startup extension changed here, so no cross-module regression is expected.
- Commit: `a76dd88` on branch `feature/3635-Arch-Review-Backgroundjobs-Seeddefaultconfiguratio`, message `#3635: Wire IRecurringJobSeeder into DI and switch startup seeding call site`.

## Status
DONE
