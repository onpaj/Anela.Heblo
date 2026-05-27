I have what I need. Important finding: the spec is incomplete — `Anela.Heblo.Application` has **six other Hangfire dependencies** beyond the two adapters, so FR-5 (remove `Hangfire.Core` PackageReference) as written is not achievable in this scope. The review must flag this.

```markdown
# Architecture Review: Relocate Hangfire Adapters from Application to API Infrastructure

## Skip Design: true

Pure backend refactor. No UI, no DTO, no API surface, no design system implications.

## Architectural Fit Assessment

The proposal aligns perfectly with the existing structure. `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/` is already the canonical home for Hangfire infrastructure: it contains `HangfireBackgroundWorker` (the existing `IBackgroundWorker` adapter), `HangfireSchemaInitializer`, `RecurringJobDiscoveryService`, and the three dashboard authorization filters — all in namespace `Anela.Heblo.API.Infrastructure.Hangfire`. Dropping `HangfireJobEnqueuer` and `HangfireRecurringJobScheduler` into the same folder is the smallest possible change that satisfies the Clean Architecture dependency rule for these two types.

**However, the brief's premise — that the two adapters are "the only reason the Application project needs Hangfire" — is wrong.** A grep across `backend/src/Anela.Heblo.Application/` shows six additional files that import `Hangfire`:

| File | Hangfire usage |
|------|----------------|
| `Features/Article/UseCases/GenerateArticle/GenerateArticleHandler.cs` | injects `IBackgroundJobClient`, calls `.Enqueue<GenerateArticleJob>(...)` |
| `Features/Article/UseCases/Generate/GenerateArticleJob.cs` | `[AutomaticRetry(Attempts = 0)]` |
| `Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs` | `[AutomaticRetry]` |
| `Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs` | `[AutomaticRetry]` |
| `Features/Dashboard/DashboardModule.cs` | `services.AddSingleton(_ => JobStorage.Current)` |
| `Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs` | injects `JobStorage`, calls `.GetMonitoringApi().FailedCount()` |

Consequence: **FR-5 cannot be satisfied by this feature alone**. Removing the `<PackageReference Include="Hangfire.Core" />` line would break compilation across at least these six files. The acceptance criterion "A grep for `using Hangfire` across `backend/src/Anela.Heblo.Application/` returns zero matches" is therefore not achievable in this scope and must be dropped or rescoped. See Specification Amendments.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Application (depends only on Domain, MediatR, framework primitives)
└─ Features/BackgroundJobs/
   ├─ Services/
   │  ├─ IHangfireJobEnqueuer.cs          (interface, unchanged, KEPT)
   │  └─ IHangfireRecurringJobScheduler.cs (interface, unchanged, KEPT)
   ├─ UseCases/...                         (handlers inject the interfaces, unchanged)
   ├─ BackgroundJobsModule.cs              (DI registration MODIFIED — adapter
   │                                        registrations removed)
   └─ ...

Anela.Heblo.API (composition root, owns Hangfire wiring)
└─ Infrastructure/Hangfire/
   ├─ HangfireBackgroundWorker.cs           (existing — unchanged)
   ├─ HangfireSchemaInitializer.cs          (existing — unchanged)
   ├─ RecurringJobDiscoveryService.cs       (existing — unchanged)
   ├─ HangfireDashboard*AuthorizationFilter (existing — unchanged)
   ├─ HangfireJobEnqueuer.cs                (NEW, moved from Application)
   └─ HangfireRecurringJobScheduler.cs      (NEW, moved from Application)

Extensions/ServiceCollectionExtensions.cs
└─ AddHangfireServices(...)                 (MODIFIED — registers the two
                                              moved adapters alongside existing
                                              Hangfire wiring)
```

### Key Design Decisions

#### Decision 1: Where to register the moved adapters

**Options considered:**
- A. Register inside `BackgroundJobsModule.AddBackgroundJobsModule` in Application.
- B. Register inside `AddHangfireServices(...)` in `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`.
- C. Register inside `AddRecurringJobs(...)` in the same file.
- D. Create a new `AddBackgroundJobAdapters` extension in API.

**Chosen approach:** **B — register inside `AddHangfireServices`.**

**Rationale:** Once the implementations live in `Anela.Heblo.API.Infrastructure.Hangfire`, option A is impossible without an Application→API reference (would violate the dependency rule we are trying to restore). Option B places the registrations next to `IBackgroundWorker → HangfireBackgroundWorker` (currently at line 339 of `ServiceCollectionExtensions.cs`), where every other Hangfire DI binding already lives, including the `HangfireDashboardTokenAuthorizationFilter` and the `JobStorage` plumbing implied by the `AddHangfire(...)` calls. Option C is wrong scope: `AddRecurringJobs` is for `IRecurringJob` implementations, not for scheduler adapters. Option D adds a new seam for no benefit when an existing one fits.

#### Decision 2: What to do about `BackgroundJobsModule.cs` in Application

**Options considered:**
- A. Leave the file in place; remove only the two adapter registrations from it.
- B. Delete the file entirely if no registrations remain.

**Chosen approach:** **A — keep the file, remove only the two adapter lines.**

**Rationale:** `BackgroundJobsModule` still owns `IRecurringJobStatusChecker → RecurringJobStatusChecker`, which is a pure Application concern (queries the configuration repository, no Hangfire dependency). Removing the file would require finding a new home for that registration. Surgical change is preferred.

#### Decision 3: Preserve existing service lifetimes verbatim

**Chosen approach:** Use exactly the lifetimes from `BackgroundJobsModule.cs:18-19`:
- `services.AddScoped<IHangfireJobEnqueuer, HangfireJobEnqueuer>()`
- `services.AddSingleton<IHangfireRecurringJobScheduler, HangfireRecurringJobScheduler>()`

**Rationale:** Changing lifetimes is out of scope and would mask refactor risk inside behavioral risk. The singleton scheduler already depends on `IServiceProvider` (root) and creates its own scope inside `UpdateCronSchedule`, which is a valid pattern for a singleton consuming scoped services — no captive-dependency issue introduced.

#### Decision 4: Do not rename, consolidate, or reuse `IBackgroundWorker`

**Chosen approach:** Treat `IBackgroundWorker` (in `Anela.Heblo.Xcc`) as a separate abstraction that already exists and serves a different caller pattern. Do not merge `IHangfireJobEnqueuer` into it as part of this work.

**Rationale:** `IBackgroundWorker.Enqueue<T>(Expression<Func<T, Task>>)` already exists and is conceptually what `HangfireJobEnqueuer` does (modulo the reflection-based call from `IRecurringJob`). Consolidation is tempting but is explicitly out of scope per the spec and would touch unrelated callers. Note for follow-up: `IBackgroundWorker` is the natural target abstraction when `GenerateArticleHandler` is decoupled from Hangfire later.

## Implementation Guidance

### Directory / Module Structure

**Files to move (git mv to preserve history):**

```
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobEnqueuer.cs
  → backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobEnqueuer.cs

backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs
  → backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs
```

**Files to keep untouched:**

```
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IHangfireJobEnqueuer.cs
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IHangfireRecurringJobScheduler.cs
```

**Files to edit:**

1. **Both moved files** — change namespace `Anela.Heblo.Application.Features.BackgroundJobs.Services` → `Anela.Heblo.API.Infrastructure.Hangfire`. Keep the `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` directive so they still implement the Application-level interfaces. Add `using Anela.Heblo.Domain.Features.BackgroundJobs;` (already present in both).

2. **`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs`** — delete lines 17-19 (the two adapter registrations). Also delete the now-unused `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` if no other reference remains in the file.

3. **`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, method `AddHangfireServices`** — immediately after the existing `IBackgroundWorker` registration (currently line 339), add:
   ```csharp
   services.AddScoped<IHangfireJobEnqueuer, HangfireJobEnqueuer>();
   services.AddSingleton<IHangfireRecurringJobScheduler, HangfireRecurringJobScheduler>();
   ```
   Add `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` to the file header. (The `Anela.Heblo.API.Infrastructure.Hangfire` using is already present at line 15.)

4. **`backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobEnqueuerTests.cs`** — change `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` to `using Anela.Heblo.API.Infrastructure.Hangfire;` (and keep the interface using if needed). Type references (`HangfireJobEnqueuer`, `ILogger<HangfireJobEnqueuer>`) work automatically once the new namespace is imported.

5. **`backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerIntegrationTests.cs`** — same namespace-using update.

### Interfaces and Contracts

- **`IHangfireJobEnqueuer`** (`Anela.Heblo.Application.Features.BackgroundJobs.Services`): unchanged. Members: `string? EnqueueJob(IRecurringJob job, CancellationToken cancellationToken)`.
- **`IHangfireRecurringJobScheduler`** (same namespace): unchanged. Members: `void UpdateCronSchedule(string jobName, string cronExpression)`.
- No new interfaces. No interface members added or removed.

### Data Flow

For both adapters the runtime flow is identical to today; only the assembly hosting the concrete type changes.

**Trigger-now flow (`/api/recurring-jobs/{name}/trigger`):**
```
Controller → MediatR → TriggerRecurringJobHandler (Application)
    → IHangfireJobEnqueuer (interface — Application)
        → HangfireJobEnqueuer (NEW location: API.Infrastructure.Hangfire)
            → IBackgroundJobClient.Enqueue (Hangfire — referenced via API project)
```

**Live CRON update flow (`/api/recurring-jobs/{name}/cron`):**
```
Controller → MediatR → UpdateRecurringJobCronHandler (Application)
    → IHangfireRecurringJobScheduler (interface — Application)
        → HangfireRecurringJobScheduler (NEW location: API.Infrastructure.Hangfire)
            → RecurringJob.AddOrUpdate<TJob>(...) (Hangfire — referenced via API project)
```

DI resolution still happens through the root container; consumers continue to receive the same instances with the same lifetimes.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Forgetting to register the moved types in API DI → `InvalidOperationException` at first request to `TriggerRecurringJobHandler` or `UpdateRecurringJobCronHandler`. | High | Add the two `services.Add*` calls in `AddHangfireServices` in the same commit as the file move. The two existing handler integration tests (`TriggerRecurringJobHandlerIntegrationTests`) exercise this path end-to-end with in-memory Hangfire — they will catch a missing/misregistered binding. |
| Test references to old namespace fail to compile. | Medium | Update `using` directives in the two affected test files in the same commit. `dotnet build` of the test project catches this trivially. |
| Stale duplicate registration in `BackgroundJobsModule` left behind, producing a last-wins binding pointing at a non-existent type after later edits. | Low | Remove `BackgroundJobsModule.cs:18-19` in the same commit. Also delete the obsolete using if it becomes unused. |
| Future contributor re-introduces `Hangfire.Core` into `Anela.Heblo.Application.csproj` to "fix" a build error caused by one of the six remaining Hangfire leaks. | Low (but real) | Leave a one-line comment in the csproj near the existing references, or better — file the follow-up tickets listed under "Specification Amendments". |
| `git mv` not used → file history lost. | Low | Use `git mv` explicitly for the two files. Verify with `git log --follow` after move. |

## Specification Amendments

The following changes to `spec.r1.md` are **required**:

### Amendment 1 — FR-5 is not satisfiable in this feature; reduce or split it

The spec's FR-5 asserts the two adapters are "the only reason `Hangfire.Core` appears in `Anela.Heblo.Application.csproj`". This is factually wrong. Six other files in the Application project import `Hangfire` (listed in Architectural Fit Assessment above). Removing the `Hangfire.Core` PackageReference now will break the build.

**Required change:** Replace FR-5 with:

> **FR-5 (revised): Reduce Application's Hangfire coupling**
> Remove the adapter-related Hangfire imports from the Application project (those previously in `HangfireJobEnqueuer.cs` and `HangfireRecurringJobScheduler.cs`). **Do not** remove the `<PackageReference Include="Hangfire.Core" />` line from `Anela.Heblo.Application.csproj` in this feature — it is still required by `GenerateArticleHandler`, `GenerateArticleJob`, `PlaudPollingJob`, `ProductExportDownloadJob`, `DashboardModule`, and `FailedJobsTile`. File a follow-up to address those six call sites; once they are clean, the PackageReference can be removed.
>
> Acceptance:
> - `grep -rn "using Hangfire" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/` returns zero matches.
> - The `Hangfire.Core` PackageReference may remain in `Anela.Heblo.Application.csproj`. Removing it is explicitly out of scope and will be tracked as a follow-up.

### Amendment 2 — Add follow-up scope to Out of Scope section

Add to the "Out of Scope" section:

> - Removing `Hangfire.Core` from `Anela.Heblo.Application.csproj`. Six additional Application files import Hangfire (`Article.GenerateArticleHandler`, `Article.GenerateArticleJob`, `MeetingTasks.PlaudPollingJob`, `FileStorage.ProductExportDownloadJob`, `Dashboard.DashboardModule`, `BackgroundJobs.DashboardTiles.FailedJobsTile`). Address them in follow-up tickets (see Open Questions).

### Amendment 3 — Add new Open Question / follow-up backlog

Add to "Open Questions":

> **OQ-1: Follow-up cleanup tickets (must be filed before this PR merges).** The following Hangfire leaks remain in the Application project and should each be tracked as separate work items:
> 1. `GenerateArticleHandler` injects `IBackgroundJobClient` and calls `Enqueue<GenerateArticleJob>(...)`. Replace with the existing `Anela.Heblo.Xcc.IBackgroundWorker` abstraction (already implemented by `HangfireBackgroundWorker` in API).
> 2. `[AutomaticRetry(Attempts = 0)]` attributes on `GenerateArticleJob`, `PlaudPollingJob`, `ProductExportDownloadJob`. Replace with a domain-level marker (e.g., a property on `RecurringJobMetadata`) interpreted by `RecurringJobDiscoveryService` when registering with Hangfire.
> 3. `DashboardModule` registers `JobStorage.Current` as a singleton, and `FailedJobsTile` consumes `JobStorage` directly. Move both to API/Infrastructure (or behind an Application-level abstraction such as `IJobMonitoringQuery`).
> 4. Once 1–3 are done, remove `<PackageReference Include="Hangfire.Core" />` from `Anela.Heblo.Application.csproj`.

### Amendment 4 — Tighten FR-6 acceptance

FR-6 acceptance criterion "No test is deleted or skipped" is good. Add: "The Hangfire `[Collection]` fixture `HangfireTestFixture` is not modified — both moved-adapter test files continue to use it."

## Prerequisites

None. No migrations, infrastructure, configuration, or feature-flag work is required before implementation. The change is a pure compile-time refactor:

- No DB schema change (NFR data model is N/A).
- No `appsettings*.json` change (Hangfire config keys are unchanged).
- No CI/CD pipeline change (`Hangfire.Core` is removed from one project file and the type definitions move to another within the same solution; `dotnet build` and `dotnet test` continue to drive validation).
- No Docker / deployment change.

Implementation can begin immediately. Recommended commit shape: a single commit containing the two `git mv`s, the namespace edits, the DI registration move, the `BackgroundJobsModule` edit, and the two test-namespace updates — all of which must land atomically to keep the build green.
```