All 6 tasks complete. Here is the output artifact:

---

# Implementation: Move HangfireJobRegistrationHelper to API Layer

## What was implemented

Pure structural refactor relocating `HangfireJobRegistrationHelper` from `Anela.Heblo.Application/Features/BackgroundJobs/Services/` to `Anela.Heblo.API/Infrastructure/Hangfire/`. The Application layer no longer takes a compile-time dependency on the Hangfire static API through this helper. Class body, method signatures, reflection dispatch, and runtime behavior are byte-for-byte unchanged.

## Files created/modified

- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs` — helper moved here; namespace updated to `Anela.Heblo.API.Infrastructure.Hangfire`
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs` — deleted (moved via `git mv`)
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs` — removed now-unused `using Anela.Heblo.Application.Features.BackgroundJobs.Services;`
- `backend/test/Anela.Heblo.Tests/Infrastructure/Hangfire/HangfireJobRegistrationHelperTests.cs` — test file moved here; namespace updated to `Anela.Heblo.Tests.Infrastructure.Hangfire`; using directive updated to `Anela.Heblo.API.Infrastructure.Hangfire`
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobRegistrationHelperTests.cs` — deleted (moved via `git mv`)

## Tests

`backend/test/Anela.Heblo.Tests/Infrastructure/Hangfire/HangfireJobRegistrationHelperTests.cs` — 14 tests covering: valid inputs, double-registration (update), null job type, missing job name (3 variants), missing cron (3 variants), missing time zone (3 variants), non-`IRecurringJob` type, invalid time zone ID. Both reflection-path tests (`RegistersJobInHangfireStorage`, `ThrowsUnwrappedTimeZoneNotFoundException`) pass.

## How to verify

```bash
dotnet build backend/Anela.Heblo.sln -nologo -v minimal
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HangfireJobRegistrationHelperTests" --no-build --nologo
git log --follow --oneline -3 -- backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs
```

## Notes

The plan estimated 7 pre-refactor files with `using Hangfire` in Application; the actual count was 8 (`IHangfireJobEnqueuer.cs` also has one). Post-refactor count is 7 (not 6 as the plan estimated). This does not affect correctness — the amended spec requires only that `HangfireJobRegistrationHelper.cs` be removed from Application, which it is. `Hangfire.Core` PackageReference on `Anela.Heblo.Application.csproj` was intentionally retained (FailedJobsTile, DashboardModule, GenerateArticleJob, GenerateArticleHandler, ProductExportDownloadJob, PlaudPollingJob, and IHangfireJobEnqueuer still depend on it). Removing those references is out of scope per arch-review Amendment B.

## PR Summary

Moves `HangfireJobRegistrationHelper` from the Application layer to `Anela.Heblo.API/Infrastructure/Hangfire/` to restore the Clean Architecture boundary for this helper. The Application project was taking a direct compile-time dependency on the Hangfire static API (`RecurringJob.AddOrUpdate`) through this class, which belongs alongside the other Hangfire adapter code in the API project. Runtime behavior, public surface, and `git --follow` history are fully preserved.

### Changes
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs` — moved here from Application; namespace updated to match sibling files
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs` — removed (moved via `git mv`)
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs` — removed now-redundant using directive (helper now in same namespace)
- `backend/test/Anela.Heblo.Tests/Infrastructure/Hangfire/HangfireJobRegistrationHelperTests.cs` — test file relocated to mirror production path; namespace and using updated
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobRegistrationHelperTests.cs` — removed (moved via `git mv`)

`Hangfire.Core` on `Application.csproj` is intentionally retained — seven other Application files still depend on it. A separate cleanup is needed to evaluate moving those consumers.

## Status

DONE