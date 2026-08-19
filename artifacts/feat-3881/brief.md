# [arch-review] Dashboard: DashboardModule registers the JobStorage singleton that unrelated Hangfire infrastructure depends on

## Module
Dashboard & Tiles (#32)

## Finding
`DashboardModule.AddDashboardModule()` registers the only DI binding for Hangfire's `JobStorage` in the entire backend, even though nothing under Dashboard's own owned paths uses it:

- **File**: `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs:19-20`
  ```csharp
  // Hangfire storage singleton — resolved lazily after Hangfire is configured
  services.AddSingleton(_ => JobStorage.Current);
  ```
- Confirmed via `grep -rln "JobStorage" --include="*.cs" backend/src`: this is the *only* registration site. A further grep restricted to `Features/Dashboard`, `Xcc/Services/Dashboard`, `Persistence/Dashboard`, and `DashboardController.cs` finds no local consumer of `JobStorage` at all.
- The actual consumers live entirely outside Dashboard's scope, in `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/`:
  - `HangfireFailedJobCounter.cs:14` — `public HangfireFailedJobCounter(JobStorage jobStorage)`
  - `HangfireBackgroundWorker.cs:18` — `public HangfireBackgroundWorker(IOptions<HangfireOptions> options, JobStorage jobStorage)`
- Both are wired up together in `ServiceCollectionExtensions.AddHangfireServices` (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:352-360`), directly under a comment documenting the project's own convention: *"Hangfire adapter implementations (interfaces live in Application, concrete types live in API/Infrastructure/Hangfire) ... registered [here]"*. `BackgroundJobsModule.cs:19-21` restates the same rule: Hangfire adapter implementations belong in `AddHangfireServices` because they live in the API project (Clean Architecture dependency rule). The `JobStorage` singleton those adapters need is the one piece that doesn't follow that rule.

## Why it matters
`AddDashboardModule()` and `AddHangfireServices()` are independently-wired module registrations with no declared relationship — Dashboard's own `Depends on:` in the module map is `#36, #34`, not BackgroundJobs/Hangfire. Someone reading `AddHangfireServices`, where every sibling Hangfire adapter is registered, has no way to discover that a required singleton for two of those adapters is actually supplied by the unrelated Dashboard feature. If `AddDashboardModule()` is ever removed, feature-flagged off, or simply not called (e.g. in a slimmed-down host), `HangfireBackgroundWorker` and `HangfireFailedJobCounter` fail DI resolution at startup — a hard crash in background-job infrastructure whose root cause is invisible from any file that actually depends on the broken registration.

## Suggested fix
Move `services.AddSingleton(_ => JobStorage.Current);` out of `DashboardModule.AddDashboardModule()` into `AddHangfireServices` (or `BackgroundJobsModule`), next to the other Hangfire adapter registrations it already documents as belonging there.

---
_Filed by arch-review skill, part #32 (Dashboard & Tiles), 2026-08-06._
