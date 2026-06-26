# Specification: Relocate Hangfire Adapters from Application to API Infrastructure Layer

## Summary
Two Hangfire adapter implementations currently live in the `Anela.Heblo.Application` project, forcing it to take a direct `PackageReference` on `Hangfire.Core` and violating Clean Architecture's dependency rule. This work moves the concrete adapters to the API project's infrastructure folder, leaving the abstractions in Application, and removes the `Hangfire.Core` dependency from `Anela.Heblo.Application.csproj`.

## Background
The codebase follows Clean Architecture: Application depends only on Domain; infrastructure concerns (Hangfire, EF Core, HTTP clients) are confined to API/Infrastructure projects. The interfaces `IHangfireJobEnqueuer` and `IHangfireRecurringJobScheduler` are correctly defined in Application, but their concrete implementations import `Hangfire` directly:

- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobEnqueuer.cs` — depends on `IBackgroundJobClient`
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs` — calls `RecurringJob.AddOrUpdate<TJob>(...)`

This is the only reason `Hangfire.Core` appears in `Anela.Heblo.Application.csproj`. Removing it restores the layer's purity, makes Application handlers easier to unit-test, and allows the scheduler to be swapped without touching business code. The daily architecture-review routine flagged this on 2026-05-27.

## Functional Requirements

### FR-1: Move `HangfireJobEnqueuer` to API Infrastructure
The concrete `HangfireJobEnqueuer` class must be physically relocated to `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobEnqueuer.cs`. The class continues to implement `IHangfireJobEnqueuer` (defined in Application) and continues to depend on `Hangfire.IBackgroundJobClient`. Namespace is updated to `Anela.Heblo.API.Infrastructure.Hangfire`.

**Acceptance criteria:**
- File no longer exists under `Anela.Heblo.Application/Features/BackgroundJobs/Services/`.
- File exists at `Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobEnqueuer.cs`.
- Class is in namespace `Anela.Heblo.API.Infrastructure.Hangfire`.
- Class still implements `Anela.Heblo.Application.Features.BackgroundJobs.Services.IHangfireJobEnqueuer` (or wherever the interface lives in Application).
- Public behavior (method signatures, observable behavior) is unchanged.

### FR-2: Move `HangfireRecurringJobScheduler` to API Infrastructure
Same treatment as FR-1: relocate `HangfireRecurringJobScheduler` to `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs`, update namespace, keep it implementing the Application-level interface.

**Acceptance criteria:**
- File no longer exists under `Anela.Heblo.Application/Features/BackgroundJobs/Services/`.
- File exists at `Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs`.
- Class is in namespace `Anela.Heblo.API.Infrastructure.Hangfire`.
- Class still implements `IHangfireRecurringJobScheduler`.
- All existing call sites (`RecurringJob.AddOrUpdate<TJob>(...)` etc.) work identically.

### FR-3: Keep Interfaces in Application
`IHangfireJobEnqueuer` and `IHangfireRecurringJobScheduler` remain in the Application project at their current locations. The interface contracts must not change.

**Acceptance criteria:**
- Both interface files remain untouched (location, namespace, members).
- All Application-layer consumers (MediatR handlers etc.) continue to inject the interfaces without modification.
- Interface names retain the `Hangfire` prefix per existing convention (renaming is out of scope — see Open Questions).

### FR-4: Register Implementations in API Composition Root
Dependency-injection registration must move with the implementations. The two concrete types are registered in the API project — either inside `BackgroundJobsModule.AddBackgroundJobsModule` (if that module already lives in API/Infrastructure and can access these types) or in `Program.cs` / a dedicated API extension method. Prefer co-locating registration with the rest of the Hangfire wiring already present in the API project.

**Acceptance criteria:**
- `IHangfireJobEnqueuer` resolves to `Anela.Heblo.API.Infrastructure.Hangfire.HangfireJobEnqueuer` at runtime.
- `IHangfireRecurringJobScheduler` resolves to `Anela.Heblo.API.Infrastructure.Hangfire.HangfireRecurringJobScheduler` at runtime.
- Service lifetimes are preserved (whatever they were before — likely scoped/singleton; do not change without justification).
- No duplicate or orphaned registrations remain in the Application project.

### FR-5: Remove Hangfire Dependency from Application Project
Once FR-1 through FR-4 are complete and the Application project no longer references any Hangfire types, the `<PackageReference Include="Hangfire.Core" .../>` entry must be removed from `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`.

**Acceptance criteria:**
- `Anela.Heblo.Application.csproj` contains no `PackageReference` to any `Hangfire.*` package.
- `dotnet build` of the Application project (and the full solution) succeeds.
- A grep for `using Hangfire` across `backend/src/Anela.Heblo.Application/` returns zero matches.
- A grep for `Hangfire.` (qualified type usage) across the same directory returns zero matches.

### FR-6: Preserve Existing Tests and Behavior
All existing unit and integration tests must continue to pass without functional modification. Test files that reference the moved classes by namespace must be updated to the new namespace; tests that consume only the interfaces should require no changes.

**Acceptance criteria:**
- `dotnet test` passes for the entire backend solution.
- No test is deleted or skipped to make this work.
- Any test that previously instantiated the concrete adapter directly is either updated to the new namespace or refactored to use the interface (mocked).

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance change is expected or permitted. This is a pure refactor; same code paths execute at runtime.

### NFR-2: Security
No change to security posture. Hangfire dashboard auth, job authorization, and existing access controls are untouched.

### NFR-3: Maintainability / Architecture
The Application project must satisfy the dependency rule after this change: depends only on Domain (and framework primitives), with no infrastructure package references introduced by background-jobs code. This is the primary success measure.

### NFR-4: Build & Tooling
- `dotnet build` from solution root must succeed with no new warnings attributable to this change.
- `dotnet format` must report a clean working tree afterward.
- No changes to CI pipelines, Dockerfiles, or deployment scripts should be required.

## Data Model
Not applicable. No database schema, migration, or persisted-data changes.

## API / Interface Design
No public HTTP API surface changes. The `IHangfireJobEnqueuer` and `IHangfireRecurringJobScheduler` C# interfaces are the only "API" involved and remain unchanged. Internal DI wiring is the only behavioral seam touched.

## Dependencies
- **Hangfire / Hangfire.Core** — already present in the API project; no version change.
- **`BackgroundJobsModule`** (current location to be confirmed during implementation) — registration entrypoint for the moved types.
- **Existing consumers** of `IHangfireJobEnqueuer` and `IHangfireRecurringJobScheduler` in MediatR handlers — must continue to work unchanged.

## Out of Scope
- Renaming the interfaces to drop the `Hangfire` prefix (e.g., `IJobEnqueuer`, `IRecurringJobScheduler`). The current names leak the implementation choice but renaming is a separate concern.
- Introducing a new abstraction layer or strategy for swappable schedulers beyond what the existing interfaces already provide.
- Migrating from Hangfire to a different background-job framework.
- Refactoring the `BackgroundJobsModule` registration shape beyond what's required to host the moved types.
- Adding new unit tests for the adapters (their behavior is unchanged; existing coverage stands).
- Removing `Hangfire` references from any project other than `Anela.Heblo.Application`.

## Open Questions
None.

## Status: COMPLETE