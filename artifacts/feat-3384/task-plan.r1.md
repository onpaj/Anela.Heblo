# Implementation Plan: BackgroundRefreshController — Fat Controller → MediatR Dispatcher

## Goal

Refactor `BackgroundRefreshController` from a fat controller that injects `IBackgroundRefreshTaskRegistry` directly and embeds all mapping/business logic, into a thin MediatR dispatcher. Move all logic into a new `BackgroundRefresh` Application module with one handler per use case. The controller's public API surface (HTTP routes, status codes, response shapes) must not change.

## Architecture

Clean Architecture vertical slice:

```
Application layer  →  BackgroundRefresh/
                          BackgroundRefreshModule.cs
                          Contracts/          (DTOs — classes, not records)
                          UseCases/           (one sub-folder per use case)

API layer          →  Controllers/BackgroundRefreshController.cs  (thin dispatcher)
```

MediatR is already registered via `services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ApplicationModule).Assembly))` in `ApplicationModule.cs`. No new registrations are needed for handlers — they are discovered automatically.

`IBackgroundRefreshTaskRegistry` is registered as a singleton by `XccModule.AddXccServices()` → `AddBackgroundRefresh(configuration)`. Do **not** re-register it.

## Tech Stack

- .NET 8 / C#
- MediatR (already referenced)
- No AutoMapper — all mapping done with `private static` helper methods on each handler class
- No new NuGet packages
- No database changes

---

## New file layout

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

## Error codes to add (ErrorCodes.cs)

New block `// BackgroundRefresh module errors (33XX)` after the Authorization block (32XX):

```csharp
// BackgroundRefresh module errors (33XX)
[HttpStatusCode(HttpStatusCode.NotFound)]
BackgroundRefreshTaskNotFound = 3301,
[HttpStatusCode(HttpStatusCode.NotFound)]
BackgroundRefreshTierNotFound = 3302,
[HttpStatusCode(HttpStatusCode.InternalServerError)]
BackgroundRefreshForceFailed = 3303,
[HttpStatusCode(HttpStatusCode.BadRequest)]
BackgroundRefreshCancelled = 3304,
```

## Files to delete after all tasks are done

- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs`
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs`
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs`

Before deleting, verify no other file in the solution references `BackgroundJobs.Contracts.RefreshTask*`:

```bash
grep -r "BackgroundJobs.Contracts.RefreshTask" backend/src --include="*.cs"
```

Only the old controller and the old DTO files themselves should appear. After the controller is repointed to the new namespace those files can be deleted.

---

### task: create-module

Create the module registration file and the three DTO files in the new location.

#### `BackgroundRefreshModule.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.BackgroundRefresh;

public static class BackgroundRefreshModule
{
    public static IServiceCollection AddBackgroundRefreshModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by the assembly scan in ApplicationModule.
        // IBackgroundRefreshTaskRegistry is registered as a singleton by XccModule — do not re-register.
        return services;
    }
}
```

#### `Contracts/RefreshTaskDto.cs`

```csharp
namespace Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;

public class RefreshTaskDto
{
    public required string TaskId { get; init; }
    public required TimeSpan InitialDelay { get; init; }
    public required TimeSpan RefreshInterval { get; init; }
    public required bool Enabled { get; init; }
    public int HydrationTier { get; init; }
    public DateTime? NextScheduledRun { get; init; }
    public RefreshTaskExecutionLogDto? LastExecution { get; init; }
}
```

#### `Contracts/RefreshTaskExecutionLogDto.cs`

```csharp
namespace Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;

public class RefreshTaskExecutionLogDto
{
    public required string TaskId { get; init; }
    public required DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public required string Status { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan? Duration { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}
```

#### `Contracts/RefreshTaskStatusDto.cs`

```csharp
namespace Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;

public class RefreshTaskStatusDto
{
    public required string TaskId { get; init; }
    public required bool Enabled { get; init; }
    public string? Description { get; init; }
    public required TimeSpan RefreshInterval { get; init; }
    public RefreshTaskExecutionLogDto? LastExecution { get; init; }
}
```

#### Register in `ApplicationModule.cs`

Add the following using and call **after** the existing `services.AddBackgroundJobsModule()` line:

```csharp
// new using at the top of ApplicationModule.cs:
using Anela.Heblo.Application.Features.BackgroundRefresh;

// in AddApplicationServices, after services.AddBackgroundJobsModule():
services.AddBackgroundRefreshModule();
```

#### Verification

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expect: build succeeds, no errors about the new files.

---

### task: get-tasks-handler

Implement the `GetBackgroundRefreshTasks` use case — maps `IBackgroundRefreshTaskRegistry.GetRegisteredTasks()` to a list of `RefreshTaskDto`.

#### `UseCases/GetBackgroundRefreshTasks/GetBackgroundRefreshTasksRequest.cs`

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetBackgroundRefreshTasks;

public class GetBackgroundRefreshTasksRequest : IRequest<GetBackgroundRefreshTasksResponse>
{
}
```

#### `UseCases/GetBackgroundRefreshTasks/GetBackgroundRefreshTasksResponse.cs`

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetBackgroundRefreshTasks;

public class GetBackgroundRefreshTasksResponse : BaseResponse
{
    public List<RefreshTaskDto> Tasks { get; set; } = new();

    public GetBackgroundRefreshTasksResponse() { }

    public GetBackgroundRefreshTasksResponse(ErrorCodes errorCode)
        : base(errorCode) { }
}
```

#### `UseCases/GetBackgroundRefreshTasks/GetBackgroundRefreshTasksHandler.cs`

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetBackgroundRefreshTasks;

public class GetBackgroundRefreshTasksHandler
    : IRequestHandler<GetBackgroundRefreshTasksRequest, GetBackgroundRefreshTasksResponse>
{
    private readonly IBackgroundRefreshTaskRegistry _taskRegistry;
    private readonly ILogger<GetBackgroundRefreshTasksHandler> _logger;

    public GetBackgroundRefreshTasksHandler(
        IBackgroundRefreshTaskRegistry taskRegistry,
        ILogger<GetBackgroundRefreshTasksHandler> logger)
    {
        _taskRegistry = taskRegistry ?? throw new ArgumentNullException(nameof(taskRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<GetBackgroundRefreshTasksResponse> Handle(
        GetBackgroundRefreshTasksRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting registered background refresh tasks");

        var tasks = _taskRegistry.GetRegisteredTasks()
            .Select(task =>
            {
                var lastExecution = _taskRegistry.GetLastExecution(task.TaskId);
                return MapToTaskDto(task, lastExecution);
            })
            .ToList();

        _logger.LogInformation("Retrieved {Count} background refresh tasks", tasks.Count);

        return Task.FromResult(new GetBackgroundRefreshTasksResponse { Tasks = tasks });
    }

    private static RefreshTaskDto MapToTaskDto(
        RefreshTaskConfiguration task,
        RefreshTaskExecutionLog? lastExecution)
    {
        DateTime? nextScheduledRun = null;
        if (task.Enabled && lastExecution?.CompletedAt != null)
            nextScheduledRun = lastExecution.CompletedAt.Value.Add(task.RefreshInterval);

        return new RefreshTaskDto
        {
            TaskId = task.TaskId,
            InitialDelay = task.InitialDelay,
            RefreshInterval = task.RefreshInterval,
            Enabled = task.Enabled,
            HydrationTier = task.HydrationTier,
            NextScheduledRun = nextScheduledRun,
            LastExecution = lastExecution != null ? MapToLogDto(lastExecution) : null
        };
    }

    private static RefreshTaskExecutionLogDto MapToLogDto(RefreshTaskExecutionLog log)
    {
        return new RefreshTaskExecutionLogDto
        {
            TaskId = log.TaskId,
            StartedAt = log.StartedAt,
            CompletedAt = log.CompletedAt,
            Status = log.Status.ToString(),
            ErrorMessage = log.ErrorMessage,
            Duration = log.Duration,
            Metadata = log.Metadata
        };
    }
}
```

#### Verification

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

---

### task: get-history-handlers

Implement the two history use cases: `GetTaskHistory` (single task, filtered) and `GetAllHistory` (all tasks).

#### `UseCases/GetTaskHistory/GetTaskHistoryRequest.cs`

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskHistory;

public class GetTaskHistoryRequest : IRequest<GetTaskHistoryResponse>
{
    public required string TaskId { get; init; }
    public int MaxRecords { get; init; } = 50;
}
```

#### `UseCases/GetTaskHistory/GetTaskHistoryResponse.cs`

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskHistory;

public class GetTaskHistoryResponse : BaseResponse
{
    public List<RefreshTaskExecutionLogDto> History { get; set; } = new();

    public GetTaskHistoryResponse() { }

    public GetTaskHistoryResponse(ErrorCodes errorCode)
        : base(errorCode) { }
}
```

#### `UseCases/GetTaskHistory/GetTaskHistoryHandler.cs`

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskHistory;

public class GetTaskHistoryHandler : IRequestHandler<GetTaskHistoryRequest, GetTaskHistoryResponse>
{
    private readonly IBackgroundRefreshTaskRegistry _taskRegistry;
    private readonly ILogger<GetTaskHistoryHandler> _logger;

    public GetTaskHistoryHandler(
        IBackgroundRefreshTaskRegistry taskRegistry,
        ILogger<GetTaskHistoryHandler> logger)
    {
        _taskRegistry = taskRegistry ?? throw new ArgumentNullException(nameof(taskRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<GetTaskHistoryResponse> Handle(
        GetTaskHistoryRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Getting execution history for task '{TaskId}' (max {MaxRecords})",
            request.TaskId, request.MaxRecords);

        var history = _taskRegistry
            .GetExecutionHistory(request.TaskId, request.MaxRecords)
            .Select(MapToDto)
            .ToList();

        return Task.FromResult(new GetTaskHistoryResponse { History = history });
    }

    private static RefreshTaskExecutionLogDto MapToDto(RefreshTaskExecutionLog log)
    {
        return new RefreshTaskExecutionLogDto
        {
            TaskId = log.TaskId,
            StartedAt = log.StartedAt,
            CompletedAt = log.CompletedAt,
            Status = log.Status.ToString(),
            ErrorMessage = log.ErrorMessage,
            Duration = log.Duration,
            Metadata = log.Metadata
        };
    }
}
```

#### `UseCases/GetAllHistory/GetAllHistoryRequest.cs`

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetAllHistory;

public class GetAllHistoryRequest : IRequest<GetAllHistoryResponse>
{
    public int MaxRecords { get; init; } = 100;
}
```

#### `UseCases/GetAllHistory/GetAllHistoryResponse.cs`

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetAllHistory;

public class GetAllHistoryResponse : BaseResponse
{
    public List<RefreshTaskExecutionLogDto> History { get; set; } = new();

    public GetAllHistoryResponse() { }

    public GetAllHistoryResponse(ErrorCodes errorCode)
        : base(errorCode) { }
}
```

#### `UseCases/GetAllHistory/GetAllHistoryHandler.cs`

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetAllHistory;

public class GetAllHistoryHandler : IRequestHandler<GetAllHistoryRequest, GetAllHistoryResponse>
{
    private readonly IBackgroundRefreshTaskRegistry _taskRegistry;
    private readonly ILogger<GetAllHistoryHandler> _logger;

    public GetAllHistoryHandler(
        IBackgroundRefreshTaskRegistry taskRegistry,
        ILogger<GetAllHistoryHandler> logger)
    {
        _taskRegistry = taskRegistry ?? throw new ArgumentNullException(nameof(taskRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<GetAllHistoryResponse> Handle(
        GetAllHistoryRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Getting full execution history (max {MaxRecords})", request.MaxRecords);

        var history = _taskRegistry
            .GetExecutionHistory(null, request.MaxRecords)
            .Select(MapToDto)
            .ToList();

        return Task.FromResult(new GetAllHistoryResponse { History = history });
    }

    private static RefreshTaskExecutionLogDto MapToDto(RefreshTaskExecutionLog log)
    {
        return new RefreshTaskExecutionLogDto
        {
            TaskId = log.TaskId,
            StartedAt = log.StartedAt,
            CompletedAt = log.CompletedAt,
            Status = log.Status.ToString(),
            ErrorMessage = log.ErrorMessage,
            Duration = log.Duration,
            Metadata = log.Metadata
        };
    }
}
```

#### Verification

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

---

### task: get-status-handler

Implement `GetTaskStatus` — looks up a single task's configuration and last execution.

#### `UseCases/GetTaskStatus/GetTaskStatusRequest.cs`

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskStatus;

public class GetTaskStatusRequest : IRequest<GetTaskStatusResponse>
{
    public required string TaskId { get; init; }
}
```

#### `UseCases/GetTaskStatus/GetTaskStatusResponse.cs`

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskStatus;

public class GetTaskStatusResponse : BaseResponse
{
    public RefreshTaskStatusDto? Status { get; set; }

    public GetTaskStatusResponse() { }

    public GetTaskStatusResponse(ErrorCodes errorCode)
        : base(errorCode) { }
}
```

#### `UseCases/GetTaskStatus/GetTaskStatusHandler.cs`

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskStatus;

public class GetTaskStatusHandler : IRequestHandler<GetTaskStatusRequest, GetTaskStatusResponse>
{
    private readonly IBackgroundRefreshTaskRegistry _taskRegistry;
    private readonly ILogger<GetTaskStatusHandler> _logger;

    public GetTaskStatusHandler(
        IBackgroundRefreshTaskRegistry taskRegistry,
        ILogger<GetTaskStatusHandler> logger)
    {
        _taskRegistry = taskRegistry ?? throw new ArgumentNullException(nameof(taskRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<GetTaskStatusResponse> Handle(
        GetTaskStatusRequest request,
        CancellationToken cancellationToken)
    {
        var task = _taskRegistry.GetRegisteredTasks()
            .FirstOrDefault(t => t.TaskId == request.TaskId);

        if (task == null)
        {
            _logger.LogWarning("Task '{TaskId}' not found", request.TaskId);
            return Task.FromResult(
                new GetTaskStatusResponse(ErrorCodes.BackgroundRefreshTaskNotFound));
        }

        var lastExecution = _taskRegistry.GetLastExecution(request.TaskId);

        var status = new RefreshTaskStatusDto
        {
            TaskId = request.TaskId,
            Enabled = task.Enabled,
            RefreshInterval = task.RefreshInterval,
            LastExecution = lastExecution != null ? MapToLogDto(lastExecution) : null
        };

        return Task.FromResult(new GetTaskStatusResponse { Status = status });
    }

    private static RefreshTaskExecutionLogDto MapToLogDto(RefreshTaskExecutionLog log)
    {
        return new RefreshTaskExecutionLogDto
        {
            TaskId = log.TaskId,
            StartedAt = log.StartedAt,
            CompletedAt = log.CompletedAt,
            Status = log.Status.ToString(),
            ErrorMessage = log.ErrorMessage,
            Duration = log.Duration,
            Metadata = log.Metadata
        };
    }
}
```

**Note:** `ErrorCodes.BackgroundRefreshTaskNotFound` must be added to `ErrorCodes.cs` (see the error codes block at the top of this plan) before this task can build.

#### Verification

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

---

### task: commands-handlers

Implement the two command use cases: `ForceRefreshTask` and `RunHydrationTier`.

#### `UseCases/ForceRefreshTask/ForceRefreshTaskRequest.cs`

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.ForceRefreshTask;

public class ForceRefreshTaskRequest : IRequest<ForceRefreshTaskResponse>
{
    public required string TaskId { get; init; }
}
```

#### `UseCases/ForceRefreshTask/ForceRefreshTaskResponse.cs`

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.ForceRefreshTask;

public class ForceRefreshTaskResponse : BaseResponse
{
    public string? Message { get; set; }

    public ForceRefreshTaskResponse() { }

    public ForceRefreshTaskResponse(ErrorCodes errorCode)
        : base(errorCode) { }
}
```

#### `UseCases/ForceRefreshTask/ForceRefreshTaskHandler.cs`

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.ForceRefreshTask;

public class ForceRefreshTaskHandler : IRequestHandler<ForceRefreshTaskRequest, ForceRefreshTaskResponse>
{
    private readonly IBackgroundRefreshTaskRegistry _taskRegistry;
    private readonly ILogger<ForceRefreshTaskHandler> _logger;

    public ForceRefreshTaskHandler(
        IBackgroundRefreshTaskRegistry taskRegistry,
        ILogger<ForceRefreshTaskHandler> logger)
    {
        _taskRegistry = taskRegistry ?? throw new ArgumentNullException(nameof(taskRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ForceRefreshTaskResponse> Handle(
        ForceRefreshTaskRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Force refresh requested for task '{TaskId}'", request.TaskId);

            await _taskRegistry.ForceRefreshAsync(request.TaskId, cancellationToken);

            return new ForceRefreshTaskResponse
            {
                Message = $"Task '{request.TaskId}' refresh initiated successfully"
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                "Force refresh failed for task '{TaskId}': {Error}", request.TaskId, ex.Message);
            return new ForceRefreshTaskResponse(ErrorCodes.BackgroundRefreshTaskNotFound);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Unexpected error during force refresh of task '{TaskId}'", request.TaskId);
            return new ForceRefreshTaskResponse(ErrorCodes.BackgroundRefreshForceFailed);
        }
    }
}
```

#### `UseCases/RunHydrationTier/RunHydrationTierRequest.cs`

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.RunHydrationTier;

public class RunHydrationTierRequest : IRequest<RunHydrationTierResponse>
{
    public required int Tier { get; init; }
}
```

#### `UseCases/RunHydrationTier/RunHydrationTierResponse.cs`

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.RunHydrationTier;

public class RunHydrationTierResponse : BaseResponse
{
    public string? Message { get; set; }

    public RunHydrationTierResponse() { }

    public RunHydrationTierResponse(ErrorCodes errorCode)
        : base(errorCode) { }
}
```

#### `UseCases/RunHydrationTier/RunHydrationTierHandler.cs`

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.RunHydrationTier;

public class RunHydrationTierHandler : IRequestHandler<RunHydrationTierRequest, RunHydrationTierResponse>
{
    private readonly IBackgroundRefreshTaskRegistry _taskRegistry;
    private readonly ILogger<RunHydrationTierHandler> _logger;

    public RunHydrationTierHandler(
        IBackgroundRefreshTaskRegistry taskRegistry,
        ILogger<RunHydrationTierHandler> logger)
    {
        _taskRegistry = taskRegistry ?? throw new ArgumentNullException(nameof(taskRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RunHydrationTierResponse> Handle(
        RunHydrationTierRequest request,
        CancellationToken cancellationToken)
    {
        var tasksInTier = _taskRegistry.GetRegisteredTasks()
            .Where(t => t.HydrationTier == request.Tier && t.Enabled)
            .OrderBy(t => t.TaskId)
            .ToList();

        if (tasksInTier.Count == 0)
        {
            _logger.LogWarning(
                "No enabled tasks found for hydration tier {Tier}", request.Tier);
            return new RunHydrationTierResponse(ErrorCodes.BackgroundRefreshTierNotFound);
        }

        _logger.LogInformation(
            "Manual hydration of tier {Tier} requested ({TaskCount} tasks)",
            request.Tier, tasksInTier.Count);

        try
        {
            foreach (var task in tasksInTier)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _taskRegistry.ForceRefreshAsync(task.TaskId, cancellationToken);
            }

            return new RunHydrationTierResponse
            {
                Message = $"Tier {request.Tier} hydration completed ({tasksInTier.Count} tasks)"
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Hydration of tier {Tier} was cancelled", request.Tier);
            return new RunHydrationTierResponse(ErrorCodes.BackgroundRefreshCancelled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual hydration of tier {Tier} failed", request.Tier);
            return new RunHydrationTierResponse(ErrorCodes.BackgroundRefreshForceFailed);
        }
    }
}
```

**Note on cancelled status code:** `BackgroundRefreshCancelled` is mapped to `HttpStatusCode.BadRequest` in the error codes block above. The original controller returned HTTP 499. If the team prefers to keep 499, add `[HttpStatusCode((HttpStatusCode)499)]` to that error code entry instead — HTTP 499 is not a standard `HttpStatusCode` enum value so it requires an explicit cast.

#### Verification

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

---

### task: refactor-controller

Replace the fat controller body with a thin MediatR dispatcher. The controller stays on `ControllerBase` (not `BaseApiController`) because it returns raw `ActionResult` shapes that embed custom error objects — this matches the existing client contract and avoids a breaking change.

The `ILogger` field is kept because the controller still participates in per-request logging context that is currently used (the logger is used in the old controller; in the new version it is not needed — it can be removed, but is kept here to match the minimal-change mandate).

#### Updated `BackgroundRefreshController.cs`

Replace the full content of `backend/src/Anela.Heblo.API/Controllers/BackgroundRefreshController.cs` with:

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.ForceRefreshTask;
using Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetAllHistory;
using Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetBackgroundRefreshTasks;
using Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskHistory;
using Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskStatus;
using Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.RunHydrationTier;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Admin_Administration)]
[ApiController]
[Route("api/[controller]")]
public class BackgroundRefreshController : ControllerBase
{
    private readonly IMediator _mediator;

    public BackgroundRefreshController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [HttpGet("tasks")]
    public async Task<ActionResult> GetRegisteredTasks(CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetBackgroundRefreshTasksRequest(), cancellationToken);
        return Ok(response.Tasks);
    }

    [HttpGet("tasks/{taskId}/history")]
    public async Task<ActionResult> GetTaskHistory(
        string taskId, [FromQuery] int maxRecords = 50, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new GetTaskHistoryRequest { TaskId = taskId, MaxRecords = maxRecords },
            cancellationToken);
        return Ok(response.History);
    }

    [HttpGet("history")]
    public async Task<ActionResult> GetAllHistory(
        [FromQuery] int maxRecords = 100, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new GetAllHistoryRequest { MaxRecords = maxRecords },
            cancellationToken);
        return Ok(response.History);
    }

    [HttpPost("tasks/{taskId}/force-refresh")]
    [FeatureAuthorize(Feature.Admin_Administration, AccessLevel.Write)]
    public async Task<ActionResult> ForceRefresh(string taskId, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new ForceRefreshTaskRequest { TaskId = taskId },
            cancellationToken);

        if (!response.Success)
        {
            return response.ErrorCode == Application.Shared.ErrorCodes.BackgroundRefreshTaskNotFound
                ? NotFound(new { Error = $"Task '{taskId}' not found or could not be refreshed" })
                : StatusCode(500, new { Error = "An unexpected error occurred during force refresh" });
        }

        return Ok(new { Message = response.Message });
    }

    [HttpPost("tiers/{tier}/run")]
    [FeatureAuthorize(Feature.Admin_Administration, AccessLevel.Write)]
    public async Task<ActionResult> RunHydrationTier(int tier, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new RunHydrationTierRequest { Tier = tier },
            cancellationToken);

        if (!response.Success)
        {
            return response.ErrorCode switch
            {
                Application.Shared.ErrorCodes.BackgroundRefreshTierNotFound =>
                    NotFound(new { Error = $"No enabled tasks found for tier {tier}" }),
                Application.Shared.ErrorCodes.BackgroundRefreshCancelled =>
                    StatusCode(499, new { Error = "Hydration was cancelled" }),
                _ => StatusCode(500, new { Error = "An unexpected error occurred during tier hydration" })
            };
        }

        return Ok(new { Message = response.Message });
    }

    [HttpGet("tasks/{taskId}/status")]
    public async Task<ActionResult> GetTaskStatus(
        string taskId, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new GetTaskStatusRequest { TaskId = taskId },
            cancellationToken);

        if (!response.Success)
            return NotFound(new { Error = $"Task '{taskId}' not found" });

        return Ok(response.Status);
    }
}
```

**Important:** The controller deliberately maps handler `ErrorCodes` back to the original raw `{ Error = "..." }` anonymous objects. This preserves the existing client contract exactly — the frontend and any MCP tooling already consume these shapes.

#### Add error codes to `ErrorCodes.cs`

In `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`, add after the Authorization block (line ~413, after `AuthorizationDuplicateGroupName`):

```csharp
// BackgroundRefresh module errors (33XX)
[HttpStatusCode(HttpStatusCode.NotFound)]
BackgroundRefreshTaskNotFound = 3301,
[HttpStatusCode(HttpStatusCode.NotFound)]
BackgroundRefreshTierNotFound = 3302,
[HttpStatusCode(HttpStatusCode.InternalServerError)]
BackgroundRefreshForceFailed = 3303,
[HttpStatusCode(HttpStatusCode.BadRequest)]
BackgroundRefreshCancelled = 3304,
```

#### Register the module in `ApplicationModule.cs`

In `backend/src/Anela.Heblo.Application/ApplicationModule.cs`:

1. Add using (top of file):
   ```csharp
   using Anela.Heblo.Application.Features.BackgroundRefresh;
   ```
2. In `AddApplicationServices`, after `services.AddBackgroundJobsModule();`:
   ```csharp
   services.AddBackgroundRefreshModule();
   ```

#### Delete old BackgroundJobs DTOs

After confirming no remaining references (run the grep below), delete:

```
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs
```

Confirm no references remain first:

```bash
grep -rn "BackgroundJobs\.Contracts\.RefreshTask\|BackgroundJobs/Contracts/RefreshTask" \
  backend/src --include="*.cs"
```

Expected output: no matches (or only the files being deleted themselves).

#### Build and format verification

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --verify-no-changes
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --verify-no-changes
```

All three commands must exit 0.
