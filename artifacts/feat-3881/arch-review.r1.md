# Architecture Review: Move the `JobStorage` DI registration out of `DashboardModule`

## Skip Design: true

This is a backend-only DI-registration relocation with zero UI, contract, or endpoint surface. No screens, components, or visual decisions are involved.

## Architectural Fit Assessment

This fix directly restores an already-documented convention rather than introducing a new one. Two independent sources in this codebase state the same rule the current code violates:

- `docs/architecture/development_guidelines.md` ("Repository bindings belong to the slice, never to `PersistenceModule`" / ADR-004): a binding must live in the module that actually needs/owns it, not in a central or unrelated module, because splitting ownership creates a "multi-module coupling/merge-conflict hotspot" and has already caused a duplicate-registration bug (`IDqtRunRepository`). This is the same failure shape as the current issue — a shared binding registered somewhere its consumers can't discover it.
- `BackgroundJobsModule.cs:19-21` and `ServiceCollectionExtensions.cs:355-357` both state, in comments, that "Hangfire adapter implementations (interfaces live in Application, concrete types live in API/Infrastructure/Hangfire) ... are registered in `AddHangfireServices`." `IJobEnqueuer`, `IFailedJobCounter`, `ICronScheduler`, and `IBackgroundWorker` all follow this rule today. `JobStorage` — a dependency of two of those same adapters (`HangfireFailedJobCounter`, `HangfireBackgroundWorker`) — is the one piece that doesn't.

Verified directly in the repo (this review re-confirmed the brief's findings by reading the files, not just trusting the issue text):
- `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs:19-20` — the only `JobStorage` registration in the backend.
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:274-373` (`AddHangfireServices`) — configures Hangfire storage (`AddHangfire(...)` with `UseMemoryStorage` or `UsePostgreSqlStorage`, lines 283-341) *before* registering the adapters that need `JobStorage` (lines 352-360). This ordering is exactly what the relocated registration needs: `JobStorage.Current` must reflect a fully-configured storage backend when first resolved.
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs:85,89` calls `AddBackgroundJobsModule()` then `AddDashboardModule()`; `Program.cs:104` calls `AddApplicationServices()` (which includes both) *before* `Program.cs:144` calls `AddHangfireServices()`. Registration order across these calls is irrelevant here because `AddSingleton(_ => JobStorage.Current)` is a lazy factory — it only executes `JobStorage.Current` at first *resolution*, not at registration. No consumer resolves `JobStorage` before the DI container is fully built and the app starts serving/running jobs, so moving the registration to `AddHangfireServices` (which itself runs after `AddApplicationServices` in `Program.cs`) is safe and changes nothing observable.

No conflicting pattern exists that would justify keeping the registration where it is. This is a straight convention violation with an unambiguous fix.

## Proposed Architecture

### Component Overview

```
Before:
  Application/Features/Dashboard/DashboardModule.cs
    AddDashboardModule()
      +-- IUserDashboardSettingsRepository
      +-- JobStorage (singleton)  <-- orphaned; no local consumer
      +-- IUserDashboardSettingsLock
      +-- IUserDashboardSettingsMutator

  API/Extensions/ServiceCollectionExtensions.cs
    AddHangfireServices()
      +-- Hangfire storage config (AddHangfire/UseMemoryStorage/UsePostgreSqlStorage)
      +-- IBackgroundWorker -> HangfireBackgroundWorker(JobStorage)   <-- depends on a singleton
      +-- IJobEnqueuer      -> HangfireJobEnqueuer                        registered elsewhere,
      +-- IFailedJobCounter -> HangfireFailedJobCounter(JobStorage)  <--  invisible from here
      +-- ICronScheduler    -> HangfireRecurringJobScheduler

After:
  Application/Features/Dashboard/DashboardModule.cs
    AddDashboardModule()
      +-- IUserDashboardSettingsRepository
      +-- IUserDashboardSettingsLock
      +-- IUserDashboardSettingsMutator

  API/Extensions/ServiceCollectionExtensions.cs
    AddHangfireServices()
      +-- Hangfire storage config (AddHangfire/UseMemoryStorage/UsePostgreSqlStorage)
      +-- JobStorage (singleton)                                    <-- now co-located with
      +-- IBackgroundWorker -> HangfireBackgroundWorker(JobStorage)       its consumers
      +-- IJobEnqueuer      -> HangfireJobEnqueuer
      +-- IFailedJobCounter -> HangfireFailedJobCounter(JobStorage)
      +-- ICronScheduler    -> HangfireRecurringJobScheduler
```

### Key Design Decisions

#### Decision 1: Destination is `AddHangfireServices`, not `BackgroundJobsModule`
**Options considered:**
- (a) `ServiceCollectionExtensions.AddHangfireServices` (API project, where `HangfireBackgroundWorker` and `HangfireFailedJobCounter` — the two actual consumers — are already registered).
- (b) `BackgroundJobsModule.AddBackgroundJobsModule` (Application project, mentioned as an alternative in the issue's suggested fix).

**Chosen approach:** (a) `AddHangfireServices`.

**Rationale:** The consumers of `JobStorage` are `HangfireBackgroundWorker` and `HangfireFailedJobCounter`, both registered inside `AddHangfireServices`, both living in `API/Infrastructure/Hangfire/`. Placing the singleton next to its consumers in the same method is the strongest possible discoverability win — a reader never has to leave the method to see the full dependency graph. `BackgroundJobsModule` (Application project) registers `IRecurringJobConfigurationRepository`, `IRecurringJobSeeder`, `IRecurringJobStatusChecker`, and the `FailedJobsTile` — none of which take a `JobStorage` dependency — so placing it there would just relocate the same discoverability problem one module over, not fix it. It would also require adding a `using Hangfire;` to an Application-layer file for a concrete Hangfire type, which is the exact layering concern `BackgroundJobsModule.cs`'s own comment (lines 19-21) says Hangfire adapters are kept out of the Application project to avoid.

#### Decision 2: Placement within `AddHangfireServices` — after storage configuration, before/alongside the adapter registrations
**Options considered:**
- Register `JobStorage` immediately after the `AddHangfire(...)`/`UseMemoryStorage`/`UsePostgreSqlStorage` block (~line 341/342).
- Register it inline with the other adapter registrations (~line 352-360), e.g. directly above `services.AddScoped<IFailedJobCounter, HangfireFailedJobCounter>();`.

**Chosen approach:** Register it as the first line of the adapter-registration block, immediately above the existing comment at line 355 ("Register Hangfire adapter implementations..."), so it reads as part of that group.

**Rationale:** `JobStorage` is not itself an "adapter implementation" like the others (it's a raw Hangfire singleton, not an interface/adapter pair), so it should be visually distinguishable but immediately adjacent — a one-line addition with its own short comment carried over from `DashboardModule.cs` ("Hangfire storage singleton — resolved lazily after Hangfire is configured"), placed right before the adapters that consume it. This keeps the causal chain (storage configured → storage singleton registered → adapters that need it registered) readable top-to-bottom in one method.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Two existing files change:
- `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs` — remove the `JobStorage` registration (lines 19-20) and its comment; remove `using Hangfire;` (line 5) if nothing else in the file references the `Hangfire` namespace after the removal — verify with a build, don't assume.
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — add `services.AddSingleton(_ => JobStorage.Current);` (with its comment) inside `AddHangfireServices`, positioned per Decision 2 above. `ServiceCollectionExtensions.cs` already has whatever `using Hangfire;`/`using Hangfire.Storage...` directives it needs since `JobStorage`-adjacent types (`GlobalJobFilters`, `CompatibilityLevel`, `PostgreSqlStorageOptions`) are already used in this file — no new usings expected, but verify at build time.

### Interfaces and Contracts
None. No interface is added, removed, or changed. `JobStorage` itself remains registered as a bare singleton (not behind an abstraction) — this matches the existing pattern for `HangfireBackgroundWorker`/`HangfireFailedJobCounter`, which take `JobStorage` as a concrete constructor dependency, not an interface.

### Data Flow
Unchanged. At container-build time, `services.AddSingleton(_ => JobStorage.Current)` registers a factory; nothing runs yet. At first resolution of `JobStorage` (i.e., the first resolution of `HangfireBackgroundWorker` or `HangfireFailedJobCounter`, both scoped/transient consumers created well after app startup completes and Hangfire's static `JobStorage.Current` has been set by `AddHangfire(...)`), the factory executes and returns the already-configured storage instance. Relocating the *registration* changes nothing about *when* it resolves.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Leftover unused `using Hangfire;` in `DashboardModule.cs` after removal, or a missing using in `ServiceCollectionExtensions.cs` | Low | Run `dotnet build` after the change; the compiler will flag either an unused-using warning (if treated as error) or a missing-type error. Fix accordingly. |
| Someone assumes registration *order* in `Program.cs` matters (Dashboard's `AddApplicationServices` at line ~104 currently runs before `AddHangfireServices` at line ~144) and worries the move reverses a dependency | Low | Not applicable — `AddSingleton(factory)` defers execution to first resolution, not registration. No consumer resolves `JobStorage` during either `AddApplicationServices` or `AddHangfireServices` itself (both are pure registration-time calls), so registration order between the two methods is irrelevant. State this explicitly in the PR description to preempt review pushback. |
| Regression: a future PR reintroduces a `JobStorage`-shaped binding in an unrelated module, repeating this exact drift | Low-Medium | Optional (not required for this fix, but cheap and consistent with `PersistenceModuleTests.AddPersistenceServices_RegistersNoRepositoryBindings`, which guards an analogous convention): add a small xUnit regression test asserting `AddDashboardModule()`'s registered `ServiceDescriptor`s contain no `Hangfire.JobStorage` service type. See "Specification Amendments" below — left as a suggestion, not a hard requirement, since the issue doesn't ask for a regression test and the codebase doesn't uniformly enforce this pattern with a test for every module. |

## Specification Amendments

- **FR-1 acceptance criteria, add:** confirm via `dotnet build` (not just visual inspection) that no unused-`using` or missing-`using` issue results from moving the `Hangfire` reference between the two files — the spec already implies this but the architect flags it as a concrete build-time check, not just a code-review check.
- **Optional addition (not required, developer's discretion):** a regression test in `backend/test/Anela.Heblo.Tests/` mirroring `PersistenceModuleTests.AddPersistenceServices_RegistersNoRepositoryBindings` — e.g. `DashboardModuleTests.AddDashboardModule_DoesNotRegisterJobStorage` — asserting `services.Where(d => d.ServiceType == typeof(Hangfire.JobStorage))` is empty after calling `AddDashboardModule()` in isolation. This is genuinely optional: unlike the repository-binding convention (which has ~15 historical violations and an enforced test), this is a single one-off drift with no evidence of a recurring pattern, so a test is a nice-to-have, not a blocker.
- No other amendments — the spec's FR-1/FR-2 acceptance criteria already correctly capture the required end state.

## Prerequisites
None. No migrations, config, or infrastructure changes are needed. The change is a same-commit, same-PR code move that can be implemented, built, and tested standalone.
