# Architecture Review: Move HangfireJobRegistrationHelper to API Layer

## Skip Design: true

This is a backend-only structural refactor — no UI, no new visual components, no design system impact.

## Architectural Fit Assessment

The proposal aligns with the codebase's documented Clean Architecture rule, but it does **not fully restore the boundary** as FR-2's strictest acceptance criterion implies. Active exploration of the `Anela.Heblo.Application` project surfaced **six other files** with `using Hangfire;` directives that the spec does not address:

- `Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs` (uses `Hangfire.JobStorage`)
- `Features/Dashboard/DashboardModule.cs` (registers `JobStorage.Current` in DI)
- `Features/Article/UseCases/Generate/GenerateArticleJob.cs` (`[AutomaticRetry]` attribute)
- `Features/Article/UseCases/GenerateArticle/GenerateArticleHandler.cs`
- `Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs`
- `Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs`

Consequently the `Hangfire.Core` `PackageReference` in `Anela.Heblo.Application.csproj` **must remain** after this refactor. FR-2's hard rule "`grep -r "using Hangfire" backend/src/Anela.Heblo.Application/` returns no results" conflicts with its softer escape clause "unless another legitimate Application-layer use remains". This conflict must be resolved in the spec before implementation begins (see *Specification Amendments*).

Integration points are localised and well-understood: the only callers are `RecurringJobDiscoveryService` (startup) and `HangfireRecurringJobScheduler` (runtime CRON updates), both already in `Anela.Heblo.API/Infrastructure/Hangfire/`. Test infrastructure (`HangfireTestFixture`, `[Collection("Hangfire")]`) is already in `Anela.Heblo.Tests` and needs only namespace/folder updates.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Domain
  └─ Features/BackgroundJobs/IRecurringJob (abstraction)

Anela.Heblo.Application                       (no Hangfire-static use after refactor)
  └─ Features/BackgroundJobs/
        ├─ Services/IHangfireJobEnqueuer            (abstraction)
        └─ Services/IHangfireRecurringJobScheduler  (abstraction)
  └─ Application.csproj
        └─ PackageReference Hangfire.Core           (REMAINS — used by other tiles/jobs)

Anela.Heblo.API
  └─ Infrastructure/Hangfire/
        ├─ HangfireJobEnqueuer.cs
        ├─ HangfireRecurringJobScheduler.cs ─────────► calls ─┐
        ├─ RecurringJobDiscoveryService.cs ──────────► calls ─┤
        ├─ HangfireJobRegistrationHelper.cs  (NEW LOCATION) ◄─┘
        └─ ... (dashboard auth filters, schema init, worker)
  └─ Extensions/ServiceCollectionExtensions.cs (AddHangfireServices — unchanged)

Anela.Heblo.Tests
  └─ Features/BackgroundJobs/Infrastructure/HangfireTestFixture.cs (unchanged)
  └─ Infrastructure/Hangfire/HangfireJobRegistrationHelperTests.cs (RELOCATED)
```

### Key Design Decisions

#### Decision 1: Target namespace and folder

**Options considered:**
- (a) `Anela.Heblo.API.Infrastructure.Hangfire` — matches sibling files (`HangfireRecurringJobScheduler`, `RecurringJobDiscoveryService`, `HangfireJobEnqueuer`).
- (b) `Anela.Heblo.API.Hangfire` — shorter but inconsistent with current sibling files.

**Chosen approach:** (a) — file at `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs`, namespace `Anela.Heblo.API.Infrastructure.Hangfire`.

**Rationale:** Mirrors the existing convention used by every Hangfire adapter file in the API project today. Zero new precedent.

#### Decision 2: Keep `public static`, do not introduce a wrapper interface

**Options considered:**
- (a) Preserve `public static class HangfireJobRegistrationHelper` exactly.
- (b) Convert to `internal static` since callers are now in the same assembly.
- (c) Refactor to an injected `IHangfireJobRegistrar` for testability.

**Chosen approach:** (a). The spec is explicit: "The class type, public surface … must be preserved exactly" and "Renaming the helper or splitting its responsibilities … is a separate change".

**Rationale:** This is a pure structural move. Accessibility change is technically safe (both callers are in the API assembly, no `InternalsVisibleTo` gymnastics needed because `Anela.Heblo.API` already exposes internals to `Anela.Heblo.Tests`) but it is **out of scope** per FR-1's "accessibility … are unchanged" clause and Out of Scope section. A follow-up task can tighten visibility and introduce a wrapper.

#### Decision 3: Test project placement

**Options considered:**
- (a) Keep tests in the single `Anela.Heblo.Tests` project but relocate folder to mirror the new source path: `backend/test/Anela.Heblo.Tests/Infrastructure/Hangfire/HangfireJobRegistrationHelperTests.cs`.
- (b) Leave tests at the current path and only update the `using` directive.

**Chosen approach:** (a) — move the test file to a folder that mirrors the production location.

**Rationale:** The repo has a single shared test project (`Anela.Heblo.Tests`) covering both Application and API code. FR-4's "move them to the appropriate API test project" is satisfied at the *folder* level, not the *project* level. Mirroring source paths under `tests/` is the codebase's existing convention (see `Adapters.Flexi.Tests` etc.) and aligns with `csharp-testing` guidance.

## Implementation Guidance

### Directory / Module Structure

**Create / move:**
- Move `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs` → `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs` using `git mv` (preserves history per NFR-4).
- Move `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobRegistrationHelperTests.cs` → `backend/test/Anela.Heblo.Tests/Infrastructure/Hangfire/HangfireJobRegistrationHelperTests.cs` using `git mv`.

**Edit:**
- In the moved helper file: change namespace from `Anela.Heblo.Application.Features.BackgroundJobs.Services` to `Anela.Heblo.API.Infrastructure.Hangfire`. Body unchanged.
- In `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs` (line 1): remove `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` — no replacement needed (helper is now in the same namespace as the file).
- In `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs` (line 1): same removal.
- In the moved test file: update `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` → `using Anela.Heblo.API.Infrastructure.Hangfire;`. Also update the test class namespace from `Anela.Heblo.Tests.Features.BackgroundJobs` → `Anela.Heblo.Tests.Infrastructure.Hangfire` and adjust the `using Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure;` reference (the `HangfireTestFixture` stays put).

**Do NOT edit:**
- `Anela.Heblo.Application.csproj` — `Hangfire.Core` PackageReference must remain (see Architectural Fit).
- Any of the other six Application files that use `Hangfire` — explicitly out of scope.
- `BackgroundJobsModule.cs` — its comment already correctly describes the architecture and references only `IHangfireJobEnqueuer`/`IHangfireRecurringJobScheduler`, which are unaffected.

### Interfaces and Contracts

No interface changes. The helper's public surface is preserved:

```csharp
public static class HangfireJobRegistrationHelper
{
    public static void RegisterOrUpdate(
        Type jobType,
        string jobName,
        string cronExpression,
        string timeZoneId);
}
```

The two `IHangfire*` abstractions in `Anela.Heblo.Application.Features.BackgroundJobs.Services` remain untouched and continue to be the cross-layer contracts.

### Data Flow

Unchanged. Both call paths produce identical Hangfire records:

```
Startup:   RecurringJobDiscoveryService (API/Hosted)
           ─► reads DB CRON via IRecurringJobConfigurationRepository
           ─► HangfireJobRegistrationHelper.RegisterOrUpdate(...)
           ─► RecurringJob.AddOrUpdate<TJob>(...)

Runtime:   UpdateRecurringJobCronHandler (Application/MediatR)
           ─► IHangfireRecurringJobScheduler.UpdateCronSchedule (API impl)
           ─► HangfireJobRegistrationHelper.RegisterOrUpdate(...)
           ─► RecurringJob.AddOrUpdate<TJob>(...)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| FR-2's strict `grep` acceptance criterion is unsatisfiable while six other files in Application still use `Hangfire`; blind enforcement would scope-creep this refactor. | High | Apply the *Specification Amendments* below before implementation. Narrow FR-2 to the helper's own dependency. |
| Reviewers expect `Hangfire.Core` removed from `Application.csproj` and reject the PR. | Medium | Add a one-line note to the PR description: "Application.csproj retains Hangfire.Core because FailedJobsTile, AutomaticRetry attributes, GenerateArticleJob, ProductExportDownloadJob, PlaudPollingJob, and GenerateArticleHandler still depend on it. Out of scope per spec." Link to a follow-up tracking issue. |
| `git mv` not used → file history orphaned, violating NFR-4. | Medium | Mandate `git mv` in the PR checklist. Verify with `git log --follow backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs`. |
| Test file relocated but `HangfireTestFixture` not — broken `using` reference. | Low | The fixture lives at `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/Infrastructure/HangfireTestFixture.cs` and stays put. The moved test must keep `using Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure;`. Verify the `[Collection("Hangfire")]` attribute still resolves. |
| Reflection inside `HangfireJobRegistrationHelper.RegisterOrUpdate` looks up `RegisterOrUpdateGeneric` by name with `BindingFlags.NonPublic | BindingFlags.Static`. Type identity is preserved by the namespace change, but a typo in the move would surface only at runtime. | Medium | Existing tests `RegisterOrUpdate_WithValidInputs_RegistersJobInHangfireStorage` and `RegisterOrUpdate_WithInvalidTimeZoneId_ThrowsUnwrappedTimeZoneNotFoundException` cover the reflection path. Confirm both pass after move (NFR-2). |
| Hidden assembly-internal consumer (e.g. a Razor/MVC view, reflective scan) referencing the old namespace string. | Low | Run `grep -r "Anela.Heblo.Application.Features.BackgroundJobs.Services.HangfireJobRegistrationHelper"` across the whole repo (including non-`.cs` files) before declaring done. |

## Specification Amendments

The following changes to `spec.r1.md` are **required** before implementation can start cleanly:

**Amendment A — FR-2 acceptance criteria (rewrite):**

Replace the current FR-2 acceptance bullets with:

> - `HangfireJobRegistrationHelper.cs` no longer exists under `Anela.Heblo.Application/`.
> - No new `using Hangfire;` directive is introduced in `Anela.Heblo.Application/` by this change. (Pre-existing usages in `FailedJobsTile`, `DashboardModule`, `GenerateArticleJob`, `GenerateArticleHandler`, `ProductExportDownloadJob`, `PlaudPollingJob` remain and are explicitly out of scope.)
> - `Anela.Heblo.Application.csproj`'s `Hangfire.Core` `PackageReference` is **retained**, justified by the six pre-existing consumers listed above. A follow-up issue is filed to evaluate moving those consumers (or their Hangfire-coupled bits) to API/Infrastructure in a future change.
> - `dotnet build` succeeds for the full solution.

**Amendment B — Out of Scope (add bullet):**

> - Removing the residual `Application` → `Hangfire.Core` package reference. Six other files in `Application` still use Hangfire types (`JobStorage`, `[AutomaticRetry]`, etc.). A separate, broader Clean Architecture cleanup is needed and is explicitly **not** addressed here.

**Amendment C — FR-4 clarification:**

The repo has a single shared `Anela.Heblo.Tests` project — there is no separate "API test project". FR-4's intent is satisfied by:
- Moving `HangfireJobRegistrationHelperTests.cs` to a folder that mirrors the new source location: `backend/test/Anela.Heblo.Tests/Infrastructure/Hangfire/`.
- Updating its namespace to `Anela.Heblo.Tests.Infrastructure.Hangfire`.
- Updating the `using` directive to `Anela.Heblo.API.Infrastructure.Hangfire`.

## Prerequisites

None.

- No migrations.
- No infrastructure changes (`AddHangfireServices` registration is unchanged).
- No new packages — `Hangfire.AspNetCore` and `Hangfire.Core` are already referenced by `Anela.Heblo.API.csproj` (transitively giving the helper access to `Hangfire.RecurringJob` in its new home).
- No configuration changes.
- No deployment ordering constraints — single Docker image, single deploy.