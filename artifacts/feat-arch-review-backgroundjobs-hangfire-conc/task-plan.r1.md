# Relocate Hangfire Adapters from Application to API Infrastructure — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the two concrete Hangfire adapter implementations (`HangfireJobEnqueuer`, `HangfireRecurringJobScheduler`) from `Anela.Heblo.Application` to `Anela.Heblo.API/Infrastructure/Hangfire/`, leaving the interfaces in Application, and rewire DI so the application still resolves them at runtime.

**Architecture:** A pure refactor governed by Clean Architecture's dependency rule. Interfaces `IHangfireJobEnqueuer` and `IHangfireRecurringJobScheduler` stay in Application. Concrete adapters move to the API composition root alongside the existing Hangfire wiring (`HangfireBackgroundWorker`, `RecurringJobDiscoveryService`, etc.). DI registration moves from `BackgroundJobsModule.AddBackgroundJobsModule` to `AddHangfireServices` in `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`. Two test files update their `using` directives. **Important per architecture review:** the `Hangfire.Core` PackageReference in `Anela.Heblo.Application.csproj` is NOT removed — six other Application files still import Hangfire (tracked as follow-ups in the spec); removing it is out of scope here.

**Tech Stack:** .NET 8, C#, Hangfire 1.8.21, xUnit, Moq, FluentAssertions.

---

## File Structure

**Files moved (use `git mv` to preserve history):**

| From | To |
|------|----|
| `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobEnqueuer.cs` | `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobEnqueuer.cs` |
| `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs` | `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs` |

**Files modified:**

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobEnqueuer.cs` (moved) | Namespace → `Anela.Heblo.API.Infrastructure.Hangfire`; add `using` for the Application interface namespace. |
| `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs` (moved) | Namespace → `Anela.Heblo.API.Infrastructure.Hangfire`; add `using` for the Application interface namespace. |
| `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs` | Remove the two adapter `services.Add*` lines (currently lines 17–19). Keep the `IRecurringJobStatusChecker` registration. Remove the `Services` using if it becomes unused. |
| `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` | Inside `AddHangfireServices`, after the existing `IBackgroundWorker → HangfireBackgroundWorker` registration (line 339), add the two adapter registrations. Add `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` to the file header. |
| `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobEnqueuerTests.cs` | Add `using Anela.Heblo.API.Infrastructure.Hangfire;` so `HangfireJobEnqueuer` and `ILogger<HangfireJobEnqueuer>` still resolve. Keep the Application `Services` using (the test still references `IHangfireJobEnqueuer`-adjacent types via that import — verify and remove only if unused). |
| `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerIntegrationTests.cs` | Same `using` adjustment. |

**Files explicitly NOT modified:**

| File | Reason |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IHangfireJobEnqueuer.cs` | Interface stays in Application per FR-3. |
| `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IHangfireRecurringJobScheduler.cs` | Interface stays in Application per FR-3. |
| `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` | `Hangfire.Core` PackageReference must remain — six other Application files still import Hangfire (see arch-review Amendment 1). Removal is explicitly out of scope. |
| `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/Infrastructure/HangfireTestFixture.cs` | Per arch-review Amendment 4 — fixture is not modified. |
| All MediatR handlers (`TriggerRecurringJobHandler`, `UpdateRecurringJobCronHandler`, etc.) | They consume only the interfaces and require no changes. |

---

## Task 1: Pre-flight — confirm starting state

**Files:**
- Inspect: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobEnqueuer.cs`
- Inspect: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs`
- Inspect: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs`
- Inspect: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Establish a green baseline before any changes**

Run from the repo root:

```bash
cd backend && dotnet build Anela.Heblo.sln
```

Expected: `Build succeeded.` with 0 errors. (Warnings unchanged from main are acceptable.)

- [ ] **Step 2: Run the affected tests to confirm baseline green**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.BackgroundJobs" --no-build
```

Expected: All BackgroundJobs tests pass. Note the test count; we will compare against it at the end.

- [ ] **Step 3: Snapshot the current Hangfire usings in Application/BackgroundJobs/Services**

```bash
grep -rn "using Hangfire" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/
```

Expected: two matches — both inside `HangfireJobEnqueuer.cs` (line 4) and `HangfireRecurringJobScheduler.cs` (line 3). This is the set of imports we are removing from Application.

- [ ] **Step 4: Confirm DI registration lines we will edit**

```bash
grep -n "IHangfireJobEnqueuer\|IHangfireRecurringJobScheduler" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs
```

Expected output (line numbers exact):

```
18:        services.AddScoped<IHangfireJobEnqueuer, HangfireJobEnqueuer>();
19:        services.AddSingleton<IHangfireRecurringJobScheduler, HangfireRecurringJobScheduler>();
```

```bash
grep -n "IBackgroundWorker, HangfireBackgroundWorker" backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
```

Expected:

```
339:        services.AddTransient<IBackgroundWorker, HangfireBackgroundWorker>();
```

These line numbers anchor later edits. If the numbers differ in your branch, locate the same statements by content and adjust.

---

## Task 2: Move `HangfireJobEnqueuer.cs` (preserve git history)

**Files:**
- Move: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobEnqueuer.cs` → `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobEnqueuer.cs`

- [ ] **Step 1: `git mv` the file**

```bash
git mv backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobEnqueuer.cs \
       backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobEnqueuer.cs
```

Expected: command succeeds silently. `git status` shows a rename.

- [ ] **Step 2: Verify rename detected**

```bash
git status --short
```

Expected: a line like `R  ...Application/...HangfireJobEnqueuer.cs -> ...API/Infrastructure/Hangfire/HangfireJobEnqueuer.cs` (the leading `R` confirms rename detection).

- [ ] **Step 3: Update the namespace inside the moved file**

Open `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobEnqueuer.cs`. Replace the header block:

```csharp
using System.Linq.Expressions;
using System.Reflection;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;
```

with:

```csharp
using System.Linq.Expressions;
using System.Reflection;
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.API.Infrastructure.Hangfire;
```

Rationale: the class implements `IHangfireJobEnqueuer`, which now lives in a different namespace, so we add a `using` for it. The namespace is updated to match the new physical location.

- [ ] **Step 4: Confirm the rest of the file is unchanged**

```bash
git diff backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobEnqueuer.cs
```

Expected: the only changes vs. the pre-move content are the two header lines (added `using` and updated namespace). Class body, members, and behavior are byte-identical.

- [ ] **Step 5: Build the API project alone (Application still won't compile yet; that's fine — we have not removed its DI registrations)**

```bash
cd backend && dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: build fails — Application project still references the now-non-existent `Anela.Heblo.Application.Features.BackgroundJobs.Services.HangfireJobEnqueuer` from `BackgroundJobsModule.cs`. This is the next task. Do **not** commit yet.

---

## Task 3: Move `HangfireRecurringJobScheduler.cs`

**Files:**
- Move: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs` → `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs`

- [ ] **Step 1: `git mv` the file**

```bash
git mv backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs \
       backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs
```

- [ ] **Step 2: Verify rename detected**

```bash
git status --short
```

Expected: two `R` lines now (one per moved file).

- [ ] **Step 3: Update the namespace inside the moved file**

Open `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs`. Replace the header block:

```csharp
using System.Reflection;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;
```

with:

```csharp
using System.Reflection;
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.API.Infrastructure.Hangfire;
```

- [ ] **Step 4: Verify only header changed**

```bash
git diff backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs
```

Expected: only the added `using` and updated namespace; class body untouched.

---

## Task 4: Remove adapter registrations from `BackgroundJobsModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs`

- [ ] **Step 1: Edit the module to drop the two adapter registrations**

Open `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs`. Current contents:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.BackgroundJobs;

public static class BackgroundJobsModule
{
    public static IServiceCollection AddBackgroundJobsModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by MediatR scan
        // Repository is registered in PersistenceModule

        // Register recurring job status checker
        services.AddScoped<IRecurringJobStatusChecker, RecurringJobStatusChecker>();

        // Register Hangfire job enqueuer
        services.AddScoped<IHangfireJobEnqueuer, HangfireJobEnqueuer>();
        services.AddSingleton<IHangfireRecurringJobScheduler, HangfireRecurringJobScheduler>();

        return services;
    }
}
```

Replace with:

```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.BackgroundJobs;

public static class BackgroundJobsModule
{
    public static IServiceCollection AddBackgroundJobsModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by MediatR scan
        // Repository is registered in PersistenceModule
        // Hangfire adapter implementations (IHangfireJobEnqueuer, IHangfireRecurringJobScheduler)
        // are registered in Anela.Heblo.API.Extensions.ServiceCollectionExtensions.AddHangfireServices
        // because their implementations live in the API project (Clean Architecture dependency rule).

        // Register recurring job status checker
        services.AddScoped<IRecurringJobStatusChecker, RecurringJobStatusChecker>();

        return services;
    }
}
```

Changes:
1. Removed `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` (no longer referenced — `RecurringJobStatusChecker` is in the `BackgroundJobs` namespace, not `BackgroundJobs.Services`).
2. Removed both `services.AddScoped<IHangfireJobEnqueuer, ...>` and `services.AddSingleton<IHangfireRecurringJobScheduler, ...>` lines.
3. Added a one-line comment pointing future readers to the new registration site (this is one of the rare cases where the WHY is non-obvious — Clean Architecture forces the split — and the comment prevents someone from "fixing" the apparent missing registration here).

- [ ] **Step 2: Confirm `RecurringJobStatusChecker` still resolves**

```bash
grep -rn "class RecurringJobStatusChecker" backend/src/Anela.Heblo.Application/
```

Expected: one file at `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobStatusChecker.cs` in namespace `Anela.Heblo.Application.Features.BackgroundJobs` (same namespace the module is in, so no `using` is required for it).

If `RecurringJobStatusChecker` actually lives under `Services/` and the `using` is still needed, add the `using` back. The build in the next task will catch this either way.

---

## Task 5: Register the moved adapters in `AddHangfireServices`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Add the Application Services using directive at the top of the file**

The file currently has these `using` lines in its header (around lines 1–27). Add one more import — alphabetically just after `using Anela.Heblo.Application.Features.Manufacture.UseCases.GetSemiproductRecipePdf;`:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
```

(The exact placement among the existing usings doesn't affect correctness; place it alphabetically with the other `Anela.Heblo.Application.*` usings to match local convention.)

- [ ] **Step 2: Add the two registrations inside `AddHangfireServices`**

Locate the existing block in `AddHangfireServices` (around line 339):

```csharp
        // Register IBackgroundWorker implementation
        services.AddTransient<IBackgroundWorker, HangfireBackgroundWorker>();

        // Note: IRecurringJobStatusChecker is now registered in Application layer (BackgroundJobsModule)
```

Replace with:

```csharp
        // Register IBackgroundWorker implementation
        services.AddTransient<IBackgroundWorker, HangfireBackgroundWorker>();

        // Register Hangfire adapter implementations (interfaces live in Application,
        // concrete types live in API/Infrastructure/Hangfire — relocated to keep the
        // Application project free of Hangfire imports for these specific adapters).
        services.AddScoped<IHangfireJobEnqueuer, HangfireJobEnqueuer>();
        services.AddSingleton<IHangfireRecurringJobScheduler, HangfireRecurringJobScheduler>();

        // Note: IRecurringJobStatusChecker is now registered in Application layer (BackgroundJobsModule)
```

Lifetimes are **identical to what `BackgroundJobsModule` used previously** (`Scoped` for the enqueuer, `Singleton` for the scheduler). Do not change them — per arch-review Decision 3, lifetime changes are out of scope.

- [ ] **Step 3: Build the full solution**

```bash
cd backend && dotnet build Anela.Heblo.sln
```

Expected: `Build succeeded.` with 0 errors. Warning count must not increase versus the Task 1 baseline.

If a `CS0246` ("type or namespace not found") error appears for `HangfireJobEnqueuer` or `HangfireRecurringJobScheduler`, double-check that (a) the namespace inside each moved file is `Anela.Heblo.API.Infrastructure.Hangfire` and (b) the `using Anela.Heblo.API.Infrastructure.Hangfire;` line already present at line 15 of `ServiceCollectionExtensions.cs` is still in place.

If a `CS0246` appears for `IHangfireJobEnqueuer` / `IHangfireRecurringJobScheduler`, the using added in Step 1 is missing.

---

## Task 6: Update test usings

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobEnqueuerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerIntegrationTests.cs`

- [ ] **Step 1: Update `HangfireJobEnqueuerTests.cs` usings**

Open `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobEnqueuerTests.cs`. Current header (lines 1–9):

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure;
using FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
```

Replace with:

```csharp
using Anela.Heblo.API.Infrastructure.Hangfire;
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure;
using FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
```

Rationale: `HangfireJobEnqueuer` and `ILogger<HangfireJobEnqueuer>` are now in `Anela.Heblo.API.Infrastructure.Hangfire`. We keep the `Anela.Heblo.Application.Features.BackgroundJobs.Services` using because the test type also references `IHangfireJobEnqueuer` (the interface, which remains in that namespace).

- [ ] **Step 2: Update `TriggerRecurringJobHandlerIntegrationTests.cs` usings**

Open `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerIntegrationTests.cs`. Current header (lines 1–9):

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
```

Replace with:

```csharp
using Anela.Heblo.API.Infrastructure.Hangfire;
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
```

- [ ] **Step 3: Build the tests project**

```bash
cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: build succeeds. If a `CS0246` complains about `HangfireJobEnqueuer` or `ILogger<HangfireJobEnqueuer>`, the new using is missing or misspelled in the file that errored.

---

## Task 7: Verify behavior end-to-end with the existing test suite

**Files:**
- Run: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/*`

- [ ] **Step 1: Run all BackgroundJobs unit + integration tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.BackgroundJobs" --no-build
```

Expected: same number of tests as in Task 1 Step 2; **all passing**. No `Skipped` increase.

If `HangfireJobEnqueuerTests` or `TriggerRecurringJobHandlerIntegrationTests` fail with `Hangfire.JobStorage` errors, confirm `HangfireTestFixture` was not touched (`git status` should not list it).

If any test fails with `InvalidOperationException: Unable to resolve service for type IHangfireJobEnqueuer` (or `IHangfireRecurringJobScheduler`), the DI registration in Task 5 is missing or in the wrong extension method.

- [ ] **Step 2: Run the full test suite to confirm no collateral damage**

```bash
cd backend && dotnet test Anela.Heblo.sln --no-build
```

Expected: full suite green. Compare the totals to a recent main-branch run if you have it; the only difference should be test-runtime noise.

---

## Task 8: Architectural acceptance checks

**Files:**
- Inspect: `backend/src/Anela.Heblo.Application/` (whole tree)
- Inspect: `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`

- [ ] **Step 1: Verify the adapter directory in Application is gone**

```bash
ls backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/
```

Expected output (exactly these — only the interfaces remain):

```
IHangfireJobEnqueuer.cs
IHangfireRecurringJobScheduler.cs
```

If `HangfireJobEnqueuer.cs` or `HangfireRecurringJobScheduler.cs` still appears, the `git mv` in Tasks 2 or 3 was not applied; re-run.

- [ ] **Step 2: Verify the adapters landed in the API project**

```bash
ls backend/src/Anela.Heblo.API/Infrastructure/Hangfire/
```

Expected to include (alongside the pre-existing entries):

```
HangfireBackgroundWorker.cs
HangfireDashboardAuthorizationFilter.cs
HangfireDashboardNoAuthFilter.cs
HangfireDashboardTokenAuthorizationFilter.cs
HangfireJobEnqueuer.cs
HangfireRecurringJobScheduler.cs
HangfireSchemaInitializer.cs
RecurringJobDiscoveryService.cs
```

- [ ] **Step 3: Verify no Hangfire imports remain in `Application/Features/BackgroundJobs/Services/`**

This is the **arch-review-amended FR-5** acceptance:

```bash
grep -rn "using Hangfire" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/
```

Expected: **zero matches**.

```bash
grep -rn "Hangfire\." backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/
```

Expected: zero matches (no qualified Hangfire type references).

- [ ] **Step 4: Confirm `Hangfire.Core` PackageReference is still present (intentionally)**

```bash
grep -n "Hangfire" backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: one line — `<PackageReference Include="Hangfire.Core" Version="1.8.21" />` — still present.

Per arch-review Amendment 1: six other Application files (`GenerateArticleHandler`, `GenerateArticleJob`, `PlaudPollingJob`, `ProductExportDownloadJob`, `DashboardModule`, `FailedJobsTile`) still depend on `Hangfire.Core`. Removing the PackageReference here would break the build and is explicitly out of scope.

- [ ] **Step 5: Confirm git rename detection survived**

```bash
git log --follow --oneline backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobEnqueuer.cs | head -3
git log --follow --oneline backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs | head -3
```

Expected: the historical commits from the file's previous location are visible (verifies `git mv` preserved history). Note: until we commit the rename in Task 9, the new location only shows pre-rename commits; that's still the right signal.

---

## Task 9: Format, final build, single commit

**Files:**
- All edited files from Tasks 2–6.

- [ ] **Step 1: Run `dotnet format`**

```bash
cd backend && dotnet format Anela.Heblo.sln
```

Expected: completes without changes that the developer didn't make. If it reformats whitespace in the moved files, accept those changes (per CLAUDE.md validation requirements).

- [ ] **Step 2: Final solution build**

```bash
cd backend && dotnet build Anela.Heblo.sln
```

Expected: `Build succeeded.` 0 errors. Warning count not increased vs. baseline (Task 1).

- [ ] **Step 3: Final full test run**

```bash
cd backend && dotnet test Anela.Heblo.sln --no-build
```

Expected: full suite green, identical test count to baseline.

- [ ] **Step 4: Review the diff one more time**

```bash
git status
git diff --stat
```

Expected `git status` (only the moves and edits listed below — no other files touched):

```
renamed:    backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobEnqueuer.cs -> backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobEnqueuer.cs
renamed:    backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs -> backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs
modified:   backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs
modified:   backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
modified:   backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobEnqueuerTests.cs
modified:   backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerIntegrationTests.cs
```

If anything else is modified (especially `Anela.Heblo.Application.csproj`), revert it before committing — that file must stay untouched in this scope.

- [ ] **Step 5: Commit as a single atomic change**

```bash
git add -A
git commit -m "refactor(backgroundjobs): relocate Hangfire adapters to API/Infrastructure

Move HangfireJobEnqueuer and HangfireRecurringJobScheduler from
Anela.Heblo.Application/Features/BackgroundJobs/Services/ to
Anela.Heblo.API/Infrastructure/Hangfire/. The interfaces (IHangfireJobEnqueuer,
IHangfireRecurringJobScheduler) remain in Application so handlers continue to
depend only on Application-layer abstractions.

DI registration moves from BackgroundJobsModule.AddBackgroundJobsModule to
ServiceCollectionExtensions.AddHangfireServices, co-located with the rest of
the Hangfire wiring. Lifetimes are preserved (Scoped enqueuer, Singleton scheduler).

Note: Hangfire.Core PackageReference is intentionally kept in
Anela.Heblo.Application.csproj — six other files in Application still import
Hangfire (GenerateArticleHandler, GenerateArticleJob, PlaudPollingJob,
ProductExportDownloadJob, DashboardModule, FailedJobsTile). Removing the
PackageReference is tracked as follow-up work."
```

Expected: commit succeeds; pre-commit hooks (if any) pass; `git log -1 --stat` shows the six expected file entries.

---

## Acceptance Mapping (spec → tasks)

| Spec requirement | Task(s) | Verification step |
|------------------|---------|-------------------|
| FR-1 — Move `HangfireJobEnqueuer` | Task 2 | Task 8 Step 1 + Step 2 |
| FR-2 — Move `HangfireRecurringJobScheduler` | Task 3 | Task 8 Step 1 + Step 2 |
| FR-3 — Interfaces stay in Application | (no edit needed) | Task 8 Step 1 shows only the two `I*.cs` files in the old folder |
| FR-4 — Register implementations in API composition root | Task 5 | Task 7 (integration tests resolve via DI) |
| FR-5 (amended) — Remove adapter Hangfire imports from Application; **keep** `Hangfire.Core` PackageReference | Tasks 2–4 + verification in Task 8 Step 3, 4 | Grep returns 0 matches in `Services/`; csproj retains `Hangfire.Core` |
| FR-6 — Preserve existing tests | Task 6 (usings) + Task 7 (run) | All tests pass, none skipped, fixture untouched |
| NFR-3 — Application layer purity for adapters | Task 8 Step 3 | Zero `using Hangfire` in `Application/Features/BackgroundJobs/Services/` |
| NFR-4 — Build & tooling clean | Task 9 Steps 1, 2 | `dotnet build` + `dotnet format` succeed |
| Arch-review Amendment 4 — Fixture not modified | Task 9 Step 4 | `git status` does not list `HangfireTestFixture.cs` |

---

## Out of Scope (do not do in this PR)

Per spec + arch-review:

1. **Do not** remove `<PackageReference Include="Hangfire.Core" ... />` from `Anela.Heblo.Application.csproj`.
2. **Do not** rename `IHangfireJobEnqueuer` / `IHangfireRecurringJobScheduler` to drop the `Hangfire` prefix.
3. **Do not** consolidate `IHangfireJobEnqueuer` into the existing `Anela.Heblo.Xcc.IBackgroundWorker` abstraction — that is a follow-up.
4. **Do not** touch `GenerateArticleHandler`, `GenerateArticleJob`, `PlaudPollingJob`, `ProductExportDownloadJob`, `DashboardModule`, `FailedJobsTile` — they still legitimately import Hangfire and are tracked as separate follow-ups.
5. **Do not** modify `HangfireTestFixture`.
6. **Do not** change service lifetimes.
7. **Do not** improve adjacent code style/whitespace beyond what `dotnet format` does automatically.
