Now writing the output artifact:

---

# Implementation: Relocate Hangfire Adapters from Application to API Infrastructure

## What was implemented

Pure Clean Architecture refactor: the two concrete Hangfire adapter classes (`HangfireJobEnqueuer` and `HangfireRecurringJobScheduler`) were relocated from `Anela.Heblo.Application/Features/BackgroundJobs/Services/` to `Anela.Heblo.API/Infrastructure/Hangfire/`. Their interfaces remain in Application unchanged. DI registration moved from `BackgroundJobsModule` (Application) to `ServiceCollectionExtensions.AddHangfireServices` (API). Two test files had their using directives updated. All changes were committed atomically.

## Files created/modified

- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobEnqueuer.cs` — moved from Application; namespace updated to `Anela.Heblo.API.Infrastructure.Hangfire`; added `using` for Application interface namespace
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs` — moved from Application; same namespace/using updates
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs` — removed adapter DI registrations + unused `using`; added explanatory comment pointing to new registration site
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — added `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` and both adapter registrations (`AddScoped` / `AddSingleton`) in `AddHangfireServices`, co-located with `IBackgroundWorker` registration
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobEnqueuerTests.cs` — added `using Anela.Heblo.API.Infrastructure.Hangfire;`
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerIntegrationTests.cs` — added `using Anela.Heblo.API.Infrastructure.Hangfire;`

Files intentionally not changed: `IHangfireJobEnqueuer.cs`, `IHangfireRecurringJobScheduler.cs`, `Anela.Heblo.Application.csproj`, `HangfireTestFixture.cs`.

## Tests

- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/` — 71 BackgroundJobs tests all pass
- Full solution: 4,410 tests pass (4,063 + 266 + 81 across three test projects)

## How to verify

```bash
cd backend
dotnet build Anela.Heblo.sln           # 0 errors, 0 warnings
dotnet test Anela.Heblo.sln --no-build # full suite green

# Architecture checks
ls backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/
# → only IHangfireJobEnqueuer.cs and IHangfireRecurringJobScheduler.cs

ls backend/src/Anela.Heblo.API/Infrastructure/Hangfire/
# → includes HangfireJobEnqueuer.cs and HangfireRecurringJobScheduler.cs

grep -rn "using Hangfire" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/
# → zero matches

grep -n "Hangfire" backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
# → Hangfire.Core PackageReference still present (intentional)
```

## Notes

- **FR-5 scoped down per arch-review Amendment 1:** `Hangfire.Core` PackageReference was intentionally kept in `Anela.Heblo.Application.csproj`. Six other Application files still import Hangfire (`GenerateArticleHandler`, `GenerateArticleJob`, `PlaudPollingJob`, `ProductExportDownloadJob`, `DashboardModule`, `FailedJobsTile`). Removing it is tracked as follow-up work (see arch-review OQ-1).
- `git mv` was used for both files to preserve full commit history.
- Service lifetimes preserved exactly: Scoped for `IHangfireJobEnqueuer`, Singleton for `IHangfireRecurringJobScheduler`.
- `dotnet format` was run; no formatting changes needed.

## PR Summary

Relocated the two concrete Hangfire adapter implementations (`HangfireJobEnqueuer` and `HangfireRecurringJobScheduler`) from `Anela.Heblo.Application` to `Anela.Heblo.API/Infrastructure/Hangfire/`, restoring the Clean Architecture dependency rule for the background-jobs adapters. The Application layer now contains only the interfaces for these types; the API layer owns the Hangfire-aware concrete implementations alongside the rest of the Hangfire wiring.

DI registration was moved from `BackgroundJobsModule.AddBackgroundJobsModule` (Application) to `ServiceCollectionExtensions.AddHangfireServices` (API), co-located with the existing `IBackgroundWorker → HangfireBackgroundWorker` binding. Service lifetimes are unchanged. Two test files had using directives updated to resolve the concrete types' new namespace; no test logic was modified.

`Hangfire.Core` is intentionally kept in `Anela.Heblo.Application.csproj` — six other Application-layer files (`GenerateArticleHandler`, `GenerateArticleJob`, `PlaudPollingJob`, `ProductExportDownloadJob`, `DashboardModule`, `FailedJobsTile`) still depend on it; removing the PackageReference is tracked as follow-up work.

All 4,410 tests pass. Build is clean.

### Changes
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobEnqueuer.cs` — moved from Application, namespace updated
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs` — moved from Application, namespace updated
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs` — removed adapter registrations, added explanatory comment
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — added adapter registrations in `AddHangfireServices`
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobEnqueuerTests.cs` — using directive updated
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerIntegrationTests.cs` — using directive updated

## Status
DONE