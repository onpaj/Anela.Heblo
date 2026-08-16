# Specification: Move the `JobStorage` DI registration out of `DashboardModule`

## Summary
`DashboardModule.AddDashboardModule()` currently registers the backend's only `JobStorage` singleton (`services.AddSingleton(_ => JobStorage.Current)`), even though nothing in Dashboard's own owned code consumes it. The real consumers — `HangfireBackgroundWorker` and `HangfireFailedJobCounter` — live in `Anela.Heblo.API/Infrastructure/Hangfire/` and are registered in `ServiceCollectionExtensions.AddHangfireServices`. This specification covers relocating that one registration line to sit alongside its actual consumers, with no behavior change.

## Background
The project follows a Clean Architecture / vertical-slice convention where each feature module (`XyzModule.AddXyzModule()`) registers only the DI bindings its own feature owns, and Hangfire adapter implementations are registered centrally in `AddHangfireServices` (API project) because their concrete types live there. `BackgroundJobsModule.cs` already documents this convention in a comment. `DashboardModule` breaks the convention by owning the `JobStorage` singleton registration that two unrelated Hangfire adapters depend on, with no declared relationship between `AddDashboardModule()` and `AddHangfireServices()` in the module map (Dashboard's declared dependencies are `#36, #34`, not BackgroundJobs/Hangfire). This is a latent-crash risk: if `AddDashboardModule()` is ever skipped, feature-flagged off, or removed, `HangfireBackgroundWorker` and `HangfireFailedJobCounter` fail DI resolution at startup with no discoverable root cause from either failing type's own registration site.

## Functional Requirements

### FR-1: Relocate the `JobStorage` singleton registration
Remove `services.AddSingleton(_ => JobStorage.Current);` (and its preceding comment) from `DashboardModule.AddDashboardModule()` in `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs`, and add the equivalent registration to `ServiceCollectionExtensions.AddHangfireServices` in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, placed near the other Hangfire adapter registrations (`IJobEnqueuer`, `IFailedJobCounter`, `ICronScheduler`, `IBackgroundWorker`) that depend on it.

**Acceptance criteria:**
- `DashboardModule.cs` no longer references `Hangfire.JobStorage` and no longer needs a `using Hangfire;` directive if that was its only use.
- `AddHangfireServices` registers `services.AddSingleton(_ => JobStorage.Current);` (or equivalent) after Hangfire itself is configured (`AddHangfire(...)` / `UsePostgreSqlStorage` / `UseMemoryStorage`) within the same method, so `JobStorage.Current` resolves to a valid, fully-configured storage instance when the singleton factory runs.
- No other module or file changes are required to keep `HangfireBackgroundWorker` and `HangfireFailedJobCounter` resolvable.

### FR-2: Preserve existing behavior exactly
The change is a pure relocation — no new abstractions, no interface changes, no change to *what* is registered (still a singleton factory returning `JobStorage.Current`), only *where*.

**Acceptance criteria:**
- `HangfireBackgroundWorker` and `HangfireFailedJobCounter` continue to resolve `JobStorage` successfully at runtime in all environments (in-memory storage in Test, PostgreSQL storage otherwise).
- `DashboardModule.AddDashboardModule()` still registers everything it registered before, minus the `JobStorage` line: `IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `IUserDashboardSettingsMutator`.
- Registration/resolution order in `Program.cs` is unaffected: `AddApplicationServices` (which calls `AddDashboardModule`) still runs before `AddHangfireServices` at line ~104 vs ~144; because both bindings are lazy factories, resolution order (not registration order) is what matters, and `JobStorage.Current` is only read when a consumer is actually resolved (after Hangfire startup configuration completes).

## Non-Functional Requirements

### NFR-1: No runtime behavior change
This is a structural/architectural fix only. The application must build, start, and behave identically before and after the change — Hangfire dashboard, background jobs, and failed-job counting must all continue to work exactly as before.

### NFR-2: Discoverability
After the fix, a developer reading `AddHangfireServices` (where `HangfireBackgroundWorker` and `HangfireFailedJobCounter` are registered) must be able to see the full set of dependencies those adapters need, including `JobStorage`, in the same method — no more implicit cross-module dependency on `AddDashboardModule()` having run first.

## Data Model
Not applicable — this is a DI-registration-only change; no entities, tables, or persistence are affected.

## API / Interface Design
Not applicable — no public API, controller, or contract surface changes. This is an internal `IServiceCollection` extension-method change only.

## Dependencies
- `Hangfire` NuGet package (already referenced by both `Anela.Heblo.Application` for the old registration and `Anela.Heblo.API` for `AddHangfireServices`; after the fix, confirm whether `Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs` still needs its `using Hangfire;` import — remove if unused).
- Depends on `AddHangfireServices` already calling `AddHangfire(...)` (or `UseMemoryStorage`/`UsePostgreSqlStorage`) before the new `JobStorage` registration line, which it does today (Hangfire storage is configured at lines ~283–341, before the adapter registrations at lines ~352–360 in `ServiceCollectionExtensions.cs`).

## Out of Scope
- Any change to `BackgroundJobsModule.cs` — it is explicitly not the destination for this registration per the issue's suggested fix (which offers `AddHangfireServices` or `BackgroundJobsModule` as options); this spec chooses `AddHangfireServices` because that is where the two consuming adapter types (`HangfireBackgroundWorker`, `HangfireFailedJobCounter`) are already registered, keeping the singleton and its two consumers together.
- Any broader audit of other possible module-boundary violations mentioned or implied elsewhere in the arch-review module map.
- Any change to how `JobStorage.Current` itself is populated/configured (Hangfire's own static initialization) — only the DI registration site moves.
- Any change to the Hangfire dashboard, job scheduling behavior, or job storage backend selection logic.

## Open Questions

## Status: COMPLETE
