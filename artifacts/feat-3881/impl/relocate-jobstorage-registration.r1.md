# Implementation: relocate-jobstorage-registration

## What was implemented
Moved the Hangfire `JobStorage` singleton DI registration out of
`DashboardModule.AddDashboardModule()` (Application layer, Dashboard feature) and into
`ServiceCollectionExtensions.AddHangfireServices()` (API layer), placing it right after the
`HangfireDashboardTokenAuthorizationFilter` registration and before `IBackgroundWorker`, next to
its actual consumers (`HangfireBackgroundWorker`, `HangfireFailedJobCounter`). This is a pure
relocation with no behavior change — `JobStorage.Current` is still resolved lazily via the same
factory lambda. Added two regression tests locking in the new ownership boundary.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs` — removed the
  `services.AddSingleton(_ => JobStorage.Current);` registration and its comment, and removed the
  now-unused `using Hangfire;`. The other three registrations
  (`IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `IUserDashboardSettingsMutator`)
  are unchanged.
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — added the
  `services.AddSingleton(_ => JobStorage.Current);` registration (with explanatory comment) inside
  `AddHangfireServices`, between the dashboard-authorization-filter registration and
  `IBackgroundWorker`. No new `using` needed (`using Hangfire;` was already present).
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/DashboardModuleTests.cs` — new regression
  test asserting `AddDashboardModule()` does not register `JobStorage`.
- `backend/test/Anela.Heblo.Tests/Infrastructure/HangfireServicesTests.cs` — new regression test
  asserting `AddHangfireServices()` registers `JobStorage`.

## Tests
- `DashboardModuleTests.AddDashboardModule_DoesNotRegisterJobStorage` — fails if `JobStorage` is
  ever re-added to `DashboardModule`.
- `HangfireServicesTests.AddHangfireServices_RegistersJobStorage` — fails if `JobStorage`
  registration is ever dropped from `AddHangfireServices` (using in-memory Hangfire storage config
  and a mocked `IWebHostEnvironment` so no real DB/Hangfire server is needed).

Both pass. Full Dashboard + Hangfire-adjacent suite (`FullyQualifiedName~Features.Dashboard|
FullyQualifiedName~Infrastructure.HangfireServicesTests|FullyQualifiedName~BackgroundJobs`) also
passes: 167/167, 0 failed — confirming no other test implicitly depended on `DashboardModule`
providing `JobStorage`.

## How to verify
```bash
cd backend
dotnet build ../Anela.Heblo.sln          # build from repo root (no .sln directly under backend/)
dotnet format ../Anela.Heblo.sln --verify-no-changes
dotnet test test/Anela.Heblo.Tests --filter "DashboardModuleTests|HangfireServicesTests" -v minimal
dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~Features.Dashboard|FullyQualifiedName~Infrastructure.HangfireServicesTests|FullyQualifiedName~BackgroundJobs" -v minimal
```

## Notes
- Deviation from the task context's exact test snippet: `IWebHostEnvironment` lives in
  `Microsoft.AspNetCore.Hosting`, not `Microsoft.Extensions.Hosting` as the task context's code
  block imported. Using the task context's exact `using` list produced `CS0246: The type or
  namespace name 'IWebHostEnvironment' could not be found`. Fixed by importing
  `Microsoft.AspNetCore.Hosting` instead, matching the convention already used elsewhere in this
  test project (e.g. `ApplicationStartupTests.cs`). No other part of the test was changed —
  configuration keys (`Hangfire:UseInMemoryStorage`, `WorkerCount`, `SchemaName`,
  `ConnectionLimit`) matched `HangfireOptions`'s real property names exactly as the task context
  predicted, so `AddHangfireServices_RegistersJobStorage` passed on the first run without needing
  the fallback investigation into `HangfireOptions.cs` the task context outlined.
- `dotnet build`/`dotnet format` must be run against `Anela.Heblo.sln` at the repo root (or with
  `cd backend && dotnet build ../Anela.Heblo.sln`) — there is no `.sln` or single unambiguous
  project directly under `backend/`, so a bare `cd backend && dotnet build` (as literally written
  in the task context's Step 8) fails with `MSB1003`/`FileNotFoundException`. Built from the repo
  root instead; this is purely a working-directory detail, not a change to any tracked file.
- `dotnet format --verify-no-changes` reports pre-existing whitespace drift in
  `backend/test/Anela.Heblo.Tests/Application/Overtime/GetMonthlyStatementsHandlerTests.cs`,
  a file untouched by this change (confirmed via `git status` showing no modification to it).
  None of the four files touched by this task have any formatting drift.
- The post-build `AccessMatrixGen` step throws an unhandled `JsonException` in this sandboxed
  environment during `dotnet build`/`dotnet test` (pre-existing, unrelated to this change — it
  reads a generated JSON file that doesn't parse in this environment) and surfaces only as an
  MSB3073 warning; it does not fail the build (0 build errors) or the test run (exit code 0).
- No `Program.cs` change was needed or made — the registration is a lazy factory lambda, so DI
  container registration order is irrelevant to resolution correctness, as the task context noted.

## Status
DONE
