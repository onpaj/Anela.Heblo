# Architecture Review: Move BackgroundRefresh DTOs out of BackgroundJobs module

## Skip Design: true

## Architectural Fit Assessment

This is a pure structural refactoring. The three DTOs (`RefreshTaskDto`, `RefreshTaskStatusDto`, `RefreshTaskExecutionLogDto`) are confirmed to have exactly one consumer: `BackgroundRefreshController`. No BackgroundJobs MediatR handler references them. The BackgroundJobs mapping profile (`BackgroundJobsMappingProfile.cs`) maps only `RecurringJobConfiguration → RecurringJobDto` — the Refresh* DTOs are entirely absent from it. There is no cross-module reference to untangle; the move is a namespace rename plus a new module scaffold.

The proposed approach (Option A from the brief — a `BackgroundRefresh` Application module) is architecturally correct and consistent with all existing modules in `Application/Features/`. Option B (Xcc subfolder) is correctly ruled out: the development guidelines forbid DTOs in Xcc, which is reserved for cross-cutting technical concerns, not HTTP contracts.

The new module will be the thinnest module in the codebase — no handlers, no repository bindings, no infrastructure. Precedent exists: several modules (`GridLayouts`, `WeatherForecast`, `Authorization`) register minimal or zero services in their `{Feature}Module.cs`. The pattern holds.

One constraint to flag: `BackgroundRefreshController` does not go through MediatR. It calls `IBackgroundRefreshTaskRegistry` (an Xcc service) directly. The new module therefore owns only the DTO contracts; it has no handlers to register. The `BackgroundRefreshModule.cs` file will be a near-empty extension method, which is the correct shape for a module whose business logic lives in Xcc infrastructure rather than Application handlers.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Xcc
  └── Services/BackgroundRefresh/
        ├── IBackgroundRefreshTaskRegistry      (consumed by controller)
        ├── RefreshTaskConfiguration            (domain model)
        ├── RefreshTaskExecutionLog             (domain model)
        └── ...

Anela.Heblo.Application                        [NEW MODULE]
  └── Features/BackgroundRefresh/
        ├── BackgroundRefreshModule.cs          (AddBackgroundRefreshModule extension)
        └── Contracts/
              ├── RefreshTaskDto.cs             (moved, namespace updated)
              ├── RefreshTaskStatusDto.cs       (moved, namespace updated)
              └── RefreshTaskExecutionLogDto.cs (moved, namespace updated)

Anela.Heblo.Application
  └── Features/BackgroundJobs/
        ├── BackgroundJobsModule.cs             (unchanged)
        └── Contracts/
              ├── RecurringJobDto.cs            (unchanged)
              ├── UpdateJobCronRequestBody.cs   (unchanged)
              └── UpdateJobStatusRequestBody.cs (unchanged)
              [RefreshTask* files REMOVED]

Anela.Heblo.API
  └── Controllers/
        └── BackgroundRefreshController.cs     (using clause updated)

ApplicationModule.cs                           (AddBackgroundRefreshModule call added)
```

### Key Design Decisions

#### Decision 1: Module without handlers is intentional and correct
**Options considered:**
- Create a full module with placeholder handlers now
- Create a contracts-only module (`BackgroundRefreshModule` registers nothing)
- Defer module creation and park DTOs temporarily in `Application/Shared/`

**Chosen approach:** Create `BackgroundRefreshModule` with an empty `AddBackgroundRefreshModule` extension method that registers no services. Three DTOs live under `BackgroundRefresh/Contracts/`.

**Rationale:** Every module in this codebase follows the `{Feature}Module.cs` pattern regardless of service count. The absence of handlers is a current-state fact, not an error — `BackgroundRefreshController` is intentionally a direct-wired controller over an Xcc registry. A no-op module is cheaper than explaining why `BackgroundRefresh` is the only feature with contracts but no `Module.cs`. When MediatR handlers are added in the future, the module already exists.

#### Decision 2: No mapping profile in the new module
**Options considered:**
- Add a `BackgroundRefreshMappingProfile` using AutoMapper
- Keep manual mapping in the controller (status quo)

**Chosen approach:** Keep the existing manual `MapToDto` private methods in `BackgroundRefreshController` unchanged. Do not introduce AutoMapper.

**Rationale:** The mapping is already written and working. This change is scoped to namespace relocation only. Introducing AutoMapper for three trivial projections adds a dependency and a profile class with zero benefit. The mapping pattern can be revisited when MediatR handlers are introduced.

#### Decision 3: Registration in ApplicationModule.cs
**Options considered:**
- Register `AddBackgroundRefreshModule()` in `ApplicationModule.cs`
- Register it directly in `Program.cs`

**Chosen approach:** Add `services.AddBackgroundRefreshModule()` to `ApplicationModule.cs`, alongside the 30+ other module calls.

**Rationale:** `Program.cs` delegates all Application-layer module wiring to `AddApplicationServices()` in `ApplicationModule.cs`. Adding a direct call in `Program.cs` would break the established pattern and scatter registration across two files.

## Implementation Guidance

### Directory / Module Structure

Files to create:
```
backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/
  BackgroundRefreshModule.cs
  Contracts/
    RefreshTaskDto.cs
    RefreshTaskStatusDto.cs
    RefreshTaskExecutionLogDto.cs
```

Files to modify:
```
backend/src/Anela.Heblo.Application/ApplicationModule.cs
  — add: using Anela.Heblo.Application.Features.BackgroundRefresh;
  — add: services.AddBackgroundRefreshModule();  (alongside other module calls)

backend/src/Anela.Heblo.API/Controllers/BackgroundRefreshController.cs
  — replace: using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
  — with:    using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
```

Files to delete:
```
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs
```

### Interfaces and Contracts

`BackgroundRefreshModule.cs` — the module registration stub:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.BackgroundRefresh;

public static class BackgroundRefreshModule
{
    public static IServiceCollection AddBackgroundRefreshModule(this IServiceCollection services)
    {
        // No Application-layer services to register yet.
        // BackgroundRefreshController wires directly to IBackgroundRefreshTaskRegistry (Xcc).
        // MediatR handlers will be added here when the HTTP surface is migrated to CQRS.
        return services;
    }
}
```

Each moved DTO file requires one change — the namespace declaration:

```
// Before:
namespace Anela.Heblo.Application.Features.BackgroundJobs.Contracts;

// After:
namespace Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
```

DTO class bodies, property names, and access modifiers are unchanged.

### Data Flow

No data flow changes. The runtime behavior is identical before and after:

```
HTTP Request
  → BackgroundRefreshController
      → IBackgroundRefreshTaskRegistry (Xcc)
          → RefreshTaskConfiguration / RefreshTaskExecutionLog (Xcc domain models)
      ← MapToDto() private methods in controller
          → RefreshTaskDto / RefreshTaskStatusDto / RefreshTaskExecutionLogDto
               [now from BackgroundRefresh.Contracts namespace]
  ← JSON response (schema unchanged)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A future consumer imports old namespace and compiles against cached output | Low | `dotnet build` clean pass after move; delete the three source files, do not leave stubs |
| `ModuleBoundariesTests.cs` picks up a new violation | Low | The new module does not reference any other Application module — no cross-module dependency is introduced; no new rule needed |
| `BackgroundRefreshModule` grows stale comments | Low | The inline comment in `AddBackgroundRefreshModule` is self-explaining; no additional documentation required |
| TypeScript client regeneration exposes a schema diff | Low | JSON property names are unchanged (no `[JsonPropertyName]` attributes); OpenAPI output is identical; `npm run build` verifies |
| Merge conflict if another branch touches BackgroundJobs/Contracts | Low | This change removes files from that folder; conflicts are obvious and trivially resolved |

## Specification Amendments

The spec is accurate. Two clarifications for the implementer:

1. **`ApplicationModule.cs` must be updated.** The spec mentions `Program.cs` or the "central composition root" as the call site, but in this codebase the composition root for Application modules is `ApplicationModule.cs` (`AddApplicationServices`). `Program.cs` calls `AddApplicationServices` — it is not where individual module calls are added. The implementer should add `services.AddBackgroundRefreshModule()` in `ApplicationModule.cs`.

2. **No test files require namespace updates.** The Xcc BackgroundRefresh test files (`BackgroundRefreshSchedulerServiceTests.cs`, `TierBasedHydrationOrchestratorTests.cs`, `RefreshTaskConfigurationTests.cs`) reference Xcc types (`RefreshTaskConfiguration`, `RefreshTaskExecutionLog`, `RefreshTaskExecutionStatus`) not the three DTO classes being moved. The BackgroundJobs test files (`GetRecurringJobHandlerTests.cs`, `RecurringJobsControllerTests.cs`, `GetRecurringJobsListHandlerTests.cs`) do not reference the Refresh DTOs. There are no test files to update.

## Prerequisites

None. No migrations, no infrastructure changes, no configuration, no feature flags. The change is entirely within the Application layer and one API controller. It can be implemented and merged independently.
