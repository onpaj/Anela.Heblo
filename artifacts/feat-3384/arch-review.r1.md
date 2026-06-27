# Architecture Review: Refactor BackgroundRefreshController to MediatR Pattern

## Skip Design: true

## Architectural Fit Assessment

This refactor aligns cleanly with the project's mandatory `controller → IMediator → handler → service/registry` pattern. Every other controller in the codebase (`RecurringJobsController`, `PackagingController`, and others) already follows this pattern. `BackgroundRefreshController` is the only controller that injects an infrastructure service (`IBackgroundRefreshTaskRegistry`) directly and houses business logic in private methods and action bodies.

The integration points are narrow and well-understood:
- The new handlers depend solely on `IBackgroundRefreshTaskRegistry` (already a registered singleton in the Xcc layer) and `ILogger<T>`. No new service interfaces, no new repositories, no database involvement.
- MediatR assembly scanning is already configured over `Anela.Heblo.Application` in `ApplicationModule.cs` (`cfg.RegisterServicesFromAssembly(typeof(ApplicationModule).Assembly)`), so no explicit handler registration is required.
- The three DTOs (`RefreshTaskDto`, `RefreshTaskStatusDto`, `RefreshTaskExecutionLogDto`) currently sit in `BackgroundJobs.Contracts`. The only consumer of those three types outside of their own definition files is `BackgroundRefreshController.cs`. Moving them is a safe, clean cut.

The spec is complete and leaves no ambiguity. No functional behaviour changes.

---

## Proposed Architecture

### Component Overview

```
Anela.Heblo.API
└── Controllers
    └── BackgroundRefreshController          [thin dispatcher]
        injects: IMediator
        removes: IBackgroundRefreshTaskRegistry, MapToDto methods, LINQ logic

        ──────── IMediator.Send() ────────►

Anela.Heblo.Application
└── Features
    └── BackgroundRefresh/                   [NEW module]
        ├── BackgroundRefreshModule.cs        AddBackgroundRefreshModule() — no-op registration (MediatR handles handlers)
        ├── Contracts/
        │   ├── RefreshTaskDto.cs             MOVED from BackgroundJobs.Contracts
        │   ├── RefreshTaskExecutionLogDto.cs  MOVED from BackgroundJobs.Contracts
        │   └── RefreshTaskStatusDto.cs        MOVED from BackgroundJobs.Contracts
        └── UseCases/
            ├── GetBackgroundRefreshTasks/
            │   ├── GetBackgroundRefreshTasksRequest.cs
            │   ├── GetBackgroundRefreshTasksResponse.cs
            │   └── GetBackgroundRefreshTasksHandler.cs   ──► IBackgroundRefreshTaskRegistry
            ├── GetTaskHistory/
            │   ├── GetTaskHistoryRequest.cs
            │   ├── GetTaskHistoryResponse.cs
            │   └── GetTaskHistoryHandler.cs               ──► IBackgroundRefreshTaskRegistry
            ├── GetAllHistory/
            │   ├── GetAllHistoryRequest.cs
            │   ├── GetAllHistoryResponse.cs
            │   └── GetAllHistoryHandler.cs                ──► IBackgroundRefreshTaskRegistry
            ├── GetTaskStatus/
            │   ├── GetTaskStatusRequest.cs
            │   ├── GetTaskStatusResponse.cs
            │   └── GetTaskStatusHandler.cs                ──► IBackgroundRefreshTaskRegistry
            ├── ForceRefreshTask/
            │   ├── ForceRefreshTaskRequest.cs
            │   ├── ForceRefreshTaskResponse.cs
            │   └── ForceRefreshTaskHandler.cs             ──► IBackgroundRefreshTaskRegistry, ILogger
            └── RunHydrationTier/
                ├── RunHydrationTierRequest.cs
                ├── RunHydrationTierResponse.cs
                └── RunHydrationTierHandler.cs             ──► IBackgroundRefreshTaskRegistry, ILogger

Anela.Heblo.Xcc
└── Services/BackgroundRefresh/
    └── IBackgroundRefreshTaskRegistry       [unchanged — handlers depend on this interface]
```

---

### Key Design Decisions

#### Decision 1: `BackgroundRefreshModule` registration content
**Options considered:**
- (A) Empty module body — just return `services` with a comment that MediatR scanning covers handlers.
- (B) Register `IBackgroundRefreshTaskRegistry` here.

**Chosen approach:** Option A — empty module body.

**Rationale:** `IBackgroundRefreshTaskRegistry` is a singleton registered by `XccModule` (Xcc layer). Registering it again in the Application module would duplicate or conflict with the existing registration. The module exists for structural consistency and as the future extension point if the feature ever grows its own services or validators. Modelling it after `BackgroundJobsModule`, which is similarly lean, is correct.

#### Decision 2: Controller base class — keep `ControllerBase`, do not migrate to `BaseApiController`
**Options considered:**
- (A) Leave `BackgroundRefreshController : ControllerBase` as-is.
- (B) Migrate to `BaseApiController` to get the `HandleResponse<T>()` helper.

**Chosen approach:** Option A — leave the inheritance unchanged.

**Rationale:** The spec explicitly notes the difference and flags it as a known discrepancy to preserve. Migrating to `BaseApiController` is an independent refactor. Doing it here would entangle two concerns and require the handlers to return `BaseResponse`-derived types, expanding scope. The slim controller bodies produced by this refactor do not need `HandleResponse<T>()` — the HTTP decisions are straightforward `if/else` branches that are legible without the helper.

#### Decision 3: Where does `MapToDto` logic live after the move?
**Options considered:**
- (A) Private static helper method on each handler class.
- (B) Standalone static mapper class in the `BackgroundRefresh` namespace.
- (C) AutoMapper profile (pattern used elsewhere for `RecurringJobDto`).

**Chosen approach:** Option A — private static helper on each handler.

**Rationale:** There are two distinct mapping operations (`RefreshTaskConfiguration + RefreshTaskExecutionLog → RefreshTaskDto` and `RefreshTaskExecutionLog → RefreshTaskExecutionLogDto`). Both are needed by multiple handlers. Private static helpers on each handler keep them fully self-contained and unit-testable without any AutoMapper dependency. The existing `BackgroundJobs` module does use AutoMapper for `RecurringJobDto`, but that DTO has a one-to-one entity mapping profile; here the `RefreshTaskDto` mapping is non-trivial (it derives `NextScheduledRun` from two separate registry calls and conditional logic), making AutoMapper a poor fit. Replicating the private static helpers across the two handlers that need `MapExecutionLogToDto` is a small duplication; a shared internal static class (`RefreshTaskMappingHelpers`) inside the module is acceptable if both handlers need the same logic, but is not mandated.

#### Decision 4: `GetTaskStatusHandler` not-found signal — `Found` flag vs. nullable response
**Options considered:**
- (A) `GetTaskStatusResponse` contains `bool Found` and nullable `RefreshTaskStatusDto? Status`.
- (B) Handler returns a nullable `RefreshTaskStatusDto?` directly (flat response, no wrapper).

**Chosen approach:** Option A — `Found` flag on the response, matching the spec.

**Rationale:** The project does not use `IRequest<T?>` (nullable response types) — every response is a concrete class. The `Found` flag keeps the controller action readable: one conditional guard on `result.Found`, then `return Ok(result.Status)`. This also mirrors the `ForceRefreshTaskResponse.NotFound` pattern on the same controller, creating internal consistency.

#### Decision 5: `ForceRefreshTaskResponse` and `RunHydrationTierResponse` success/error signalling — custom flags vs. `BaseResponse` error codes
**Options considered:**
- (A) Custom boolean flags (`Success`, `NotFound`, `Cancelled`) on the response, as the spec prescribes.
- (B) Inherit from `BaseResponse` and use `ErrorCodes`.

**Chosen approach:** Option A — custom flags.

**Rationale:** `BaseResponse` with `ErrorCodes` is the pattern used by controllers that already extend `BaseApiController`. Since `BackgroundRefreshController` stays on `ControllerBase`, the `HandleResponse<T>()` helper is unavailable. Adding an `ErrorCodes` entry for these transient operational states (task not found, tier cancelled) just to remain consistent with a helper this controller does not use adds noise. The custom flag approach mirrors how the existing `GetTaskStatusResponse` works and keeps the controller HTTP mapping explicit and readable.

---

## Implementation Guidance

### Directory / Module Structure

All new files live under:
```
backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/
```

Three existing files **move** (namespace updated, content unchanged):
```
FROM: backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs
TO:   backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskDto.cs

FROM: backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs
TO:   backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskExecutionLogDto.cs

FROM: backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs
TO:   backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskStatusDto.cs
```

One existing file is modified:
- `BackgroundRefreshController.cs` — updated `using` directive (old `BackgroundJobs.Contracts` → new `BackgroundRefresh.Contracts`), constructor changed, `MapToDto` methods removed, action bodies replaced.

`ApplicationModule.cs` gets one new line: `services.AddBackgroundRefreshModule();`

### Interfaces and Contracts

**All Request/Response classes must be `class`, not `record`.** (Project rule — OpenAPI generator compatibility.)

```csharp
// GetBackgroundRefreshTasks
public class GetBackgroundRefreshTasksRequest : IRequest<GetBackgroundRefreshTasksResponse> { }
public class GetBackgroundRefreshTasksResponse
{
    public IReadOnlyList<RefreshTaskDto> Tasks { get; init; } = [];
}

// GetTaskHistory
public class GetTaskHistoryRequest : IRequest<GetTaskHistoryResponse>
{
    public required string TaskId { get; init; }
    public int MaxRecords { get; init; } = 50;
}
public class GetTaskHistoryResponse
{
    public IReadOnlyList<RefreshTaskExecutionLogDto> History { get; init; } = [];
}

// GetAllHistory
public class GetAllHistoryRequest : IRequest<GetAllHistoryResponse>
{
    public int MaxRecords { get; init; } = 100;
}
public class GetAllHistoryResponse
{
    public IReadOnlyList<RefreshTaskExecutionLogDto> History { get; init; } = [];
}

// GetTaskStatus
public class GetTaskStatusRequest : IRequest<GetTaskStatusResponse>
{
    public required string TaskId { get; init; }
}
public class GetTaskStatusResponse
{
    public bool Found { get; init; }
    public RefreshTaskStatusDto? Status { get; init; }
}

// ForceRefreshTask
public class ForceRefreshTaskRequest : IRequest<ForceRefreshTaskResponse>
{
    public required string TaskId { get; init; }
}
public class ForceRefreshTaskResponse
{
    public bool Success { get; init; } = true;
    public bool NotFound { get; init; }
    public string? ErrorMessage { get; init; }
}

// RunHydrationTier
public class RunHydrationTierRequest : IRequest<RunHydrationTierResponse>
{
    public int Tier { get; init; }
}
public class RunHydrationTierResponse
{
    public bool Success { get; init; } = true;
    public bool NotFound { get; init; }
    public bool Cancelled { get; init; }
    public string? ErrorMessage { get; init; }
    public int TaskCount { get; init; }
}
```

**Updated DTO namespaces:**
```csharp
namespace Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
```

**BackgroundRefreshModule:**
```csharp
namespace Anela.Heblo.Application.Features.BackgroundRefresh;

public static class BackgroundRefreshModule
{
    public static IServiceCollection AddBackgroundRefreshModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by MediatR assembly scan.
        // No additional DI registrations required: IBackgroundRefreshTaskRegistry
        // is registered as a singleton by XccModule.
        return services;
    }
}
```

**Updated controller constructor:**
```csharp
public BackgroundRefreshController(IMediator mediator)
{
    _mediator = mediator;
}
```
Remove `_logger` (no controller-level logging remains after the refactor).

### Data Flow

**Read path (GetBackgroundRefreshTasks as representative example):**
```
HTTP GET /api/BackgroundRefresh/tasks
  → BackgroundRefreshController.GetRegisteredTasks()
      → _mediator.Send(new GetBackgroundRefreshTasksRequest(), cancellationToken)
          → GetBackgroundRefreshTasksHandler.Handle(...)
              → _taskRegistry.GetRegisteredTasks()          // IReadOnlyList<RefreshTaskConfiguration>
              → foreach task: _taskRegistry.GetLastExecution(task.TaskId)
              → MapToDto(task, lastExecution)               // computes NextScheduledRun inline
              → return GetBackgroundRefreshTasksResponse { Tasks = [...] }
      → return Ok(result.Tasks)
```

**Write path with error handling (RunHydrationTier):**
```
HTTP POST /api/BackgroundRefresh/tiers/{tier}/run
  → BackgroundRefreshController.RunHydrationTier(int tier, CancellationToken)
      → _mediator.Send(new RunHydrationTierRequest { Tier = tier }, cancellationToken)
          → RunHydrationTierHandler.Handle(...)
              → _taskRegistry.GetRegisteredTasks()
                  .Where(t => t.HydrationTier == tier && t.Enabled)
                  .OrderBy(t => t.TaskId)
              → if empty: return RunHydrationTierResponse { NotFound = true }
              → foreach task:
                  cancellationToken.ThrowIfCancellationRequested()
                  await _taskRegistry.ForceRefreshAsync(task.TaskId, cancellationToken)
              → catch OperationCanceledException: return { Cancelled = true }
              → catch Exception: return { Success = false, ErrorMessage = ex.Message }
              → return { Success = true, TaskCount = tasksInTier.Count }
      → controller maps RunHydrationTierResponse to HTTP result
```

---

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| `RefreshTaskExecutionLogDto.Status` field — the existing controller serializes `log.Status.ToString()`. If a handler emits the enum member name but a consumer relies on an exact casing, the output could change. | Low | The `RefreshTaskExecutionStatus` enum members match PascalCase (Running, Completed, Failed, Cancelled). The existing controller uses `.ToString()` which produces the same result. Copy the pattern exactly: `Status = log.Status.ToString()`. No regression possible. |
| Duplicate `MapToDto(RefreshTaskExecutionLog)` logic across handlers (`GetTaskHistoryHandler`, `GetAllHistoryHandler`, `GetBackgroundRefreshTasksHandler`, `GetTaskStatusHandler`). | Low | Extract a private static helper `MapExecutionLogToDto(RefreshTaskExecutionLog log)` inside a shared internal static class `RefreshTaskMappingHelpers` in the `BackgroundRefresh` namespace, or repeat it as a private static on each handler. Either is acceptable; the former avoids drift. |
| `ApplicationModule.cs` omission — forgetting to call `AddBackgroundRefreshModule()` causes the `dotnet build` to succeed but the module to never be registered (no runtime error since there is nothing to register, but the new `using` import in the controller changes may not get called). | Low | Since `BackgroundRefreshModule` is currently empty (MediatR auto-scans), this risk is cosmetic rather than functional. The handlers are auto-discovered regardless. Still, add the call to maintain structural parity and future-proof the module. |
| DTO namespace change breaks the OpenAPI-generated TypeScript client shape. | None | The JSON property names are unchanged; only the C# namespace changes. The TypeScript client is generated from the OpenAPI schema, which reflects the JSON shape, not C# namespaces. No client impact. |
| `RefreshTaskStatusDto` has a `Description` property (`string? Description { get; init; }`) that the existing controller never populates (it constructs the DTO directly without setting `Description`). | Low | The current response silently omits or nulls this field. The handler must replicate this: do not attempt to populate `Description`. The field exists on the DTO but is not set; this is an existing quirk, not something to fix in this refactor. |

---

## Specification Amendments

**One gap found:** The spec (FR-4) shows the updated controller body for `GetTaskStatus` as:
```csharp
var result = await _mediator.Send(new GetTaskStatusRequest { TaskId = taskId }, cancellationToken);
```
but the current controller action signature is `GetTaskStatus(string taskId)` with **no** `CancellationToken` parameter. The spec's snippet introduces a `cancellationToken` variable that does not exist in the current action signature. The action should either be updated to accept a `CancellationToken` (consistent with `ForceRefresh` and `RunHydrationTier`) or the `Send` call should not pass a cancellationToken. Recommendation: add `CancellationToken cancellationToken` to the `GetTaskStatus` signature for consistency with the write actions. This is a minor improvement, not a breaking change.

**Documentation note:** `RefreshTaskStatusDto` contains a `Description` property that is never populated by the current controller, and the spec does not mention it. Implementors should leave it null in the handler — do not attempt to derive or populate it, as there is no source for this value in `RefreshTaskConfiguration`.

---

## Prerequisites

1. No prerequisites beyond the current codebase state. All dependencies (`IBackgroundRefreshTaskRegistry`, MediatR, `ILogger<T>`) are already present and registered.
2. The three DTO files must be moved (namespace updated) before any handler file can compile, since handlers in `BackgroundRefresh.UseCases.*` will reference `BackgroundRefresh.Contracts.*`.
3. `BackgroundRefreshController.cs`'s `using` directive update must happen in the same change as the DTO move, otherwise the build breaks between steps.

**Suggested implementation order:**
1. Create `Features/BackgroundRefresh/Contracts/` and move the three DTO files with updated namespaces.
2. Update `BackgroundRefreshController.cs` `using` to `BackgroundRefresh.Contracts` — run `dotnet build` to confirm clean.
3. Create `BackgroundRefreshModule.cs`.
4. Add `services.AddBackgroundRefreshModule()` to `ApplicationModule.cs`.
5. Create all six use case folders with Request/Response/Handler files.
6. Update `BackgroundRefreshController.cs` constructor and action bodies.
7. Run `dotnet build` and `dotnet format` to confirm clean.
