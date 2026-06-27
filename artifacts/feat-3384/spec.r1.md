# Specification: Refactor BackgroundRefreshController to MediatR Pattern

## Summary

`BackgroundRefreshController` bypasses the project's mandatory `controller → IMediator → handler → service` pattern by injecting `IBackgroundRefreshTaskRegistry` directly, embedding filtering/orchestration logic in the controller body, and performing object mapping in two private `MapToDto` methods. This refactor extracts all business logic into dedicated MediatR use cases under a new `BackgroundRefresh` application module, making the controller a thin dispatcher consistent with every other controller in the codebase.

## Background

Every other controller in the project dispatches to MediatR: the controller body is limited to extracting HTTP inputs, calling `_mediator.Send(...)`, and returning an HTTP result. `BackgroundRefreshController` was written without MediatR, violating the rule in `docs/architecture/development_guidelines.md`: "Business logic must be in MediatR handlers, NOT in controllers."

The violations are:
1. Two private `MapToDto` methods (lines 145–178) that convert Xcc domain types to DTOs — object mapping is application logic and belongs in the handler.
2. `RunHydrationTier` (lines 84–118) contains a filter (`Where(t => t.HydrationTier == tier && t.Enabled)`), an ordering step, a not-found guard, and a sequential execution loop — all orchestration logic that belongs in a handler.
3. The three GET endpoints also contain inline LINQ projections and lookups that belong in handlers.
4. No unit tests exist for any of this logic because controllers are not testable in isolation the way handlers are.

The DTOs (`RefreshTaskDto`, `RefreshTaskStatusDto`, `RefreshTaskExecutionLogDto`) currently live in `BackgroundJobs.Contracts`. Per the module-ownership rule in development guidelines, they must move to `BackgroundRefresh.Contracts` as part of this work.

## Functional Requirements

### FR-1: New BackgroundRefresh application module

Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/` as a new vertical slice module, mirroring the structure of `BackgroundJobs` and all other feature modules.

The module must include:
- `BackgroundRefreshModule.cs` — `IServiceCollection` extension (`AddBackgroundRefreshModule()`) registered in `Program.cs`
- `Contracts/` — DTOs moved from `BackgroundJobs.Contracts` (see FR-6)
- `UseCases/` — one sub-folder per use case (see FR-2 through FR-5)

**Acceptance criteria:**
- `BackgroundRefreshModule.AddBackgroundRefreshModule()` exists and is called in `Program.cs` / the application's service registration entry point
- MediatR scans the assembly and auto-registers all handlers in this module (no explicit handler registration required if MediatR assembly scanning already covers `Anela.Heblo.Application`)
- `dotnet build` succeeds with no errors or warnings introduced by this change

### FR-2: GetBackgroundRefreshTasksQuery use case

Create `UseCases/GetBackgroundRefreshTasks/` containing:
- `GetBackgroundRefreshTasksRequest : IRequest<GetBackgroundRefreshTasksResponse>` — no input parameters
- `GetBackgroundRefreshTasksResponse` — wraps `IReadOnlyList<RefreshTaskDto>`
- `GetBackgroundRefreshTasksHandler` — injects `IBackgroundRefreshTaskRegistry`; calls `GetRegisteredTasks()` and `GetLastExecution(task.TaskId)` for each task; maps results to `RefreshTaskDto` including the `NextScheduledRun` calculation (when `task.Enabled && lastExecution?.CompletedAt != null`, compute `lastExecution.CompletedAt.Value.Add(task.RefreshInterval)`)

The controller action `GetRegisteredTasks()` (GET `/api/BackgroundRefresh/tasks`) becomes:

```csharp
var result = await _mediator.Send(new GetBackgroundRefreshTasksRequest(), cancellationToken);
return Ok(result.Tasks);
```

**Acceptance criteria:**
- Handler contains all mapping and `NextScheduledRun` calculation logic; no mapping code remains in the controller
- Handler is independently unit-testable by providing a mock `IBackgroundRefreshTaskRegistry`
- GET `/api/BackgroundRefresh/tasks` returns identical JSON shape as before

### FR-3: GetTaskHistoryQuery use case

Create `UseCases/GetTaskHistory/` containing:
- `GetTaskHistoryRequest : IRequest<GetTaskHistoryResponse>` — properties: `string TaskId`, `int MaxRecords = 50`
- `GetTaskHistoryResponse` — wraps `IReadOnlyList<RefreshTaskExecutionLogDto>`
- `GetTaskHistoryHandler` — injects `IBackgroundRefreshTaskRegistry`; calls `GetExecutionHistory(request.TaskId, request.MaxRecords)`; maps each `RefreshTaskExecutionLog` to `RefreshTaskExecutionLogDto` (Status mapped via `.ToString()`)

Covers the controller action `GetTaskHistory(string taskId, int maxRecords = 50)` (GET `/api/BackgroundRefresh/tasks/{taskId}/history`).

Also create `UseCases/GetAllHistory/` containing:
- `GetAllHistoryRequest : IRequest<GetAllHistoryResponse>` — property: `int MaxRecords = 100`
- `GetAllHistoryResponse` — wraps `IReadOnlyList<RefreshTaskExecutionLogDto>`
- `GetAllHistoryHandler` — injects `IBackgroundRefreshTaskRegistry`; calls `GetExecutionHistory(null, request.MaxRecords)`; maps results

Covers the controller action `GetAllHistory(int maxRecords = 100)` (GET `/api/BackgroundRefresh/history`).

**Acceptance criteria:**
- Both handlers independently unit-testable with mocked registry
- GET `/api/BackgroundRefresh/tasks/{taskId}/history` and GET `/api/BackgroundRefresh/history` return identical JSON shapes as before
- `MapToDto` for `RefreshTaskExecutionLog` exists once in the handler (or a private static helper on the handler), not in the controller

### FR-4: GetTaskStatusQuery use case

Create `UseCases/GetTaskStatus/` containing:
- `GetTaskStatusRequest : IRequest<GetTaskStatusResponse>` — property: `string TaskId`
- `GetTaskStatusResponse` — wraps `RefreshTaskStatusDto?` and a boolean `Found`
- `GetTaskStatusHandler` — injects `IBackgroundRefreshTaskRegistry`; calls `GetRegisteredTasks().FirstOrDefault(t => t.TaskId == request.TaskId)`; if not found, sets `Found = false`; otherwise maps to `RefreshTaskStatusDto` (mapping `LastExecution` via `GetLastExecution`)

The controller action `GetTaskStatus(string taskId)` (GET `/api/BackgroundRefresh/tasks/{taskId}/status`) becomes:

```csharp
var result = await _mediator.Send(new GetTaskStatusRequest { TaskId = taskId }, cancellationToken);
if (!result.Found) return NotFound(new { Error = $"Task '{taskId}' not found" });
return Ok(result.Status);
```

**Acceptance criteria:**
- Not-found guard logic lives in the handler (via `Found` flag) or the handler returns a nullable, with the controller doing only the HTTP shape decision
- Handler is independently unit-testable
- GET `/api/BackgroundRefresh/tasks/{taskId}/status` returns identical JSON shape as before

### FR-5: ForceRefreshTaskCommand and RunHydrationTierCommand use cases

**ForceRefreshTask:**

Create `UseCases/ForceRefreshTask/` containing:
- `ForceRefreshTaskRequest : IRequest<ForceRefreshTaskResponse>` — property: `string TaskId`
- `ForceRefreshTaskResponse` — properties: `bool Success`, `string? ErrorMessage`, `bool NotFound`
- `ForceRefreshTaskHandler` — injects `IBackgroundRefreshTaskRegistry` and `ILogger<ForceRefreshTaskHandler>`; calls `_taskRegistry.ForceRefreshAsync(request.TaskId, cancellationToken)`; catches `InvalidOperationException` (sets `NotFound = true`) and general exceptions (sets `Success = false` with error message); logs as the existing controller does

The controller action `ForceRefresh(string taskId)` (POST `/api/BackgroundRefresh/tasks/{taskId}/force-refresh`) becomes:

```csharp
var result = await _mediator.Send(new ForceRefreshTaskRequest { TaskId = taskId }, cancellationToken);
if (result.NotFound) return NotFound(new { Error = result.ErrorMessage });
if (!result.Success) return StatusCode(500, new { Error = result.ErrorMessage });
return Ok(new { Message = $"Task '{taskId}' refresh initiated successfully" });
```

**RunHydrationTier:**

Create `UseCases/RunHydrationTier/` containing:
- `RunHydrationTierRequest : IRequest<RunHydrationTierResponse>` — property: `int Tier`
- `RunHydrationTierResponse` — properties: `bool Success`, `bool NotFound`, `bool Cancelled`, `string? ErrorMessage`, `int TaskCount`
- `RunHydrationTierHandler` — injects `IBackgroundRefreshTaskRegistry` and `ILogger<RunHydrationTierHandler>`; contains the full filter (`Where(t => t.HydrationTier == tier && t.Enabled)`), `OrderBy(t => t.TaskId)`, not-found guard, sequential `ForceRefreshAsync` loop with `cancellationToken.ThrowIfCancellationRequested()`, and all logging; catches `OperationCanceledException` (sets `Cancelled = true`) and general exceptions

The controller action `RunHydrationTier(int tier)` (POST `/api/BackgroundRefresh/tiers/{tier}/run`) becomes:

```csharp
var result = await _mediator.Send(new RunHydrationTierRequest { Tier = tier }, cancellationToken);
if (result.NotFound) return NotFound(new { Error = result.ErrorMessage });
if (result.Cancelled) return StatusCode(499, new { Error = "Hydration was cancelled" });
if (!result.Success) return StatusCode(500, new { Error = result.ErrorMessage });
return Ok(new { Message = $"Tier {tier} hydration completed ({result.TaskCount} tasks)" });
```

**Acceptance criteria:**
- `RunHydrationTierHandler` is independently unit-testable: inject a mock registry and verify the filter, ordering, sequential execution, and cancellation behaviour
- POST `/api/BackgroundRefresh/tasks/{taskId}/force-refresh` and POST `/api/BackgroundRefresh/tiers/{tier}/run` return identical HTTP status codes and JSON shapes as before
- The sequential loop and `cancellationToken.ThrowIfCancellationRequested()` check are present in the handler

### FR-6: Relocate DTOs from BackgroundJobs.Contracts to BackgroundRefresh.Contracts

Move `RefreshTaskDto`, `RefreshTaskStatusDto`, and `RefreshTaskExecutionLogDto` from `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/` to `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/`.

Update namespaces from `Anela.Heblo.Application.Features.BackgroundJobs.Contracts` to `Anela.Heblo.Application.Features.BackgroundRefresh.Contracts` in:
- The moved DTO files themselves
- `BackgroundRefreshController.cs` using directive
- Any other files that import these DTOs (search for `using Anela.Heblo.Application.Features.BackgroundJobs.Contracts` referencing these three types)

**Acceptance criteria:**
- No file references the old `BackgroundJobs.Contracts` namespace for `RefreshTaskDto`, `RefreshTaskStatusDto`, or `RefreshTaskExecutionLogDto`
- `BackgroundJobs.Contracts` folder still exists and retains its own DTO types (`RecurringJobDto`, `UpdateJobCronRequestBody`, `UpdateJobStatusRequestBody`) — only the three Refresh-specific DTOs move
- `dotnet build` succeeds with no missing-reference errors

### FR-7: Controller becomes a thin dispatcher

After extracting all use cases, `BackgroundRefreshController` must:
- Replace `IBackgroundRefreshTaskRegistry _taskRegistry` injection with `IMediator _mediator`
- Remove both `MapToDto` private methods
- Each action body dispatches to exactly one `_mediator.Send(...)` call, then converts the response to an HTTP result
- Retain all `[HttpGet]` / `[HttpPost]` route attributes, all `[FeatureAuthorize]` attributes, and the `CancellationToken` parameters unchanged
- Retain the `_logger` field only if any logging remains in the controller (after the refactor, logging should live in handlers; remove `_logger` if the controller no longer calls it directly)

**Acceptance criteria:**
- `BackgroundRefreshController` injects `IMediator` and does not inject `IBackgroundRefreshTaskRegistry`
- No LINQ (`Where`, `OrderBy`, `Select`, `FirstOrDefault`) in the controller body
- No `MapToDto` methods in the controller
- All six endpoints remain on the same routes with the same HTTP verbs and authorization levels

## Non-Functional Requirements

### NFR-1: No Breaking API Changes

All six existing endpoints must preserve their HTTP method, route, authorization requirement, query parameters, request body shape, and response JSON shape. This is a pure internal refactor with no external interface changes.

**Target response times:** unchanged — this refactor adds no I/O or significant CPU work; handler dispatch overhead via MediatR is sub-millisecond.

### NFR-2: Security

Authorization attributes (`[FeatureAuthorize(Feature.Admin_Administration)]` at controller level, `[FeatureAuthorize(Feature.Admin_Administration, AccessLevel.Write)]` on the two POST actions) must be preserved verbatim on the controller — MediatR handlers do not enforce authorization.

### NFR-3: Testability

Each handler must be unit-testable in isolation. The constructor of every handler must accept only interfaces (no concrete classes), allowing mock substitution. Handlers injecting `IBackgroundRefreshTaskRegistry` must be testable without an HTTP context.

### NFR-4: Logging Consistency

Logging currently in the controller (`LogInformation`, `LogWarning`, `LogError`) moves to the corresponding handler. Log message templates and structured property names (e.g., `{TaskId}`, `{Tier}`, `{TaskCount}`) must be preserved. The controller may retain a logger only if there is a genuine controller-level concern to log (there is none after the refactor — remove `_logger` from the controller's constructor and field).

## Data Model

No new persistence entities. All data comes from `IBackgroundRefreshTaskRegistry` (in-memory registry, part of `Anela.Heblo.Xcc`):

| Xcc Type | Role | Maps to DTO |
|---|---|---|
| `RefreshTaskConfiguration` | Per-task config (TaskId, InitialDelay, RefreshInterval, Enabled, HydrationTier) | `RefreshTaskDto` |
| `RefreshTaskExecutionLog` | Single execution record (TaskId, StartedAt, CompletedAt, Status, ErrorMessage, Duration, Metadata) | `RefreshTaskExecutionLogDto` |
| Derived: `NextScheduledRun` | `lastExecution.CompletedAt + task.RefreshInterval` when enabled and completed | Field on `RefreshTaskDto` |
| Derived: `RefreshTaskStatusDto` | Combines config fields + last execution log | `RefreshTaskStatusDto` |

`RefreshTaskExecutionStatus` (enum: Running, Completed, Failed, Cancelled) is serialized as `.ToString()` in `RefreshTaskExecutionLogDto.Status`.

## API / Interface Design

All routes unchanged. For reference:

| Verb | Route | Authorization | Handler |
|---|---|---|---|
| GET | `/api/BackgroundRefresh/tasks` | Admin_Administration (Read) | `GetBackgroundRefreshTasksHandler` |
| GET | `/api/BackgroundRefresh/tasks/{taskId}/history?maxRecords=50` | Admin_Administration (Read) | `GetTaskHistoryHandler` |
| GET | `/api/BackgroundRefresh/history?maxRecords=100` | Admin_Administration (Read) | `GetAllHistoryHandler` |
| GET | `/api/BackgroundRefresh/tasks/{taskId}/status` | Admin_Administration (Read) | `GetTaskStatusHandler` |
| POST | `/api/BackgroundRefresh/tasks/{taskId}/force-refresh` | Admin_Administration (Write) | `ForceRefreshTaskHandler` |
| POST | `/api/BackgroundRefresh/tiers/{tier}/run` | Admin_Administration (Write) | `RunHydrationTierHandler` |

### Request / Response classes (class, not record — per project DTO rule)

All `*Request` and `*Response` classes use `class`, not C# `record`, to comply with the project rule that DTOs are classes (OpenAPI client generator compatibility).

### Module file layout

```
backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/
├── BackgroundRefreshModule.cs
├── Contracts/
│   ├── RefreshTaskDto.cs
│   ├── RefreshTaskExecutionLogDto.cs
│   └── RefreshTaskStatusDto.cs
└── UseCases/
    ├── GetBackgroundRefreshTasks/
    │   ├── GetBackgroundRefreshTasksRequest.cs
    │   ├── GetBackgroundRefreshTasksResponse.cs
    │   └── GetBackgroundRefreshTasksHandler.cs
    ├── GetTaskHistory/
    │   ├── GetTaskHistoryRequest.cs
    │   ├── GetTaskHistoryResponse.cs
    │   └── GetTaskHistoryHandler.cs
    ├── GetAllHistory/
    │   ├── GetAllHistoryRequest.cs
    │   ├── GetAllHistoryResponse.cs
    │   └── GetAllHistoryHandler.cs
    ├── GetTaskStatus/
    │   ├── GetTaskStatusRequest.cs
    │   ├── GetTaskStatusResponse.cs
    │   └── GetTaskStatusHandler.cs
    ├── ForceRefreshTask/
    │   ├── ForceRefreshTaskRequest.cs
    │   ├── ForceRefreshTaskResponse.cs
    │   └── ForceRefreshTaskHandler.cs
    └── RunHydrationTier/
        ├── RunHydrationTierRequest.cs
        ├── RunHydrationTierResponse.cs
        └── RunHydrationTierHandler.cs
```

## Dependencies

- **`IBackgroundRefreshTaskRegistry`** (`Anela.Heblo.Xcc.Services.BackgroundRefresh`) — all handlers depend on this interface. No new service interfaces need to be created; the handlers call the same registry methods the controller currently calls.
- **MediatR** — already a project dependency; `BackgroundRefreshController` gains the same `IMediator` injection pattern as all other controllers.
- **`Microsoft.Extensions.Logging`** — handlers inject typed `ILogger<THandler>`.
- **`Anela.Heblo.Domain.Features.Authorization`** — `Feature` and `AccessLevel` enums remain on the controller's `[FeatureAuthorize]` attributes; no handler dependency required.
- **No new packages** — zero new NuGet packages are needed.

## Out of Scope

- Changing the behaviour of any endpoint (no new business rules, no changes to filtering logic, no new response fields).
- Adding or changing E2E tests — the nightly E2E suite covers these endpoints through the UI; no new E2E test files are created.
- Writing new unit tests for the handlers (the brief calls out testability as the motivation; writing the tests themselves is follow-on work and not part of this refactor).
- Modifying `IBackgroundRefreshTaskRegistry` or any Xcc types.
- Any frontend changes — the API shape is preserved, so the TypeScript client regenerates identically.
- Removing or modifying the `BackgroundJobs` module — only the three Refresh-specific DTOs move; everything else in `BackgroundJobs` is unchanged.
- Changing the `[Route("api/[controller]")]` convention or controller name.

## Open Questions

None.

## Status: COMPLETE
