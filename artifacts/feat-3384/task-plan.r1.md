# BackgroundRefreshController MediatR Refactor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor BackgroundRefreshController from a fat controller into a thin MediatR dispatcher by creating a dedicated BackgroundRefresh application module.

**Architecture:** New `Application/Features/BackgroundRefresh/` module with 6 MediatR use cases. Three DTOs move from `BackgroundJobs.Contracts` to `BackgroundRefresh.Contracts`. Controller slimmed to dispatch only.

**Tech Stack:** .NET 8, MediatR, ASP.NET Core MVC

---

### task: create-module
[Create BackgroundRefreshModule.cs + 3 DTOs with new namespace + delete old DTO files]

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/BackgroundRefreshModule.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.BackgroundRefresh;

public static class BackgroundRefreshModule
{
    public static IServiceCollection AddBackgroundRefreshModule(this IServiceCollection services)
    {
        // MediatR handlers are auto-registered by assembly scanning
        return services;
    }
}
```

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskDto.cs`:

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

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskExecutionLogDto.cs`:

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

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskStatusDto.cs`:

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

- [ ] Delete `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs`
- [ ] Delete `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs`
- [ ] Delete `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs`
- [ ] Verify: `dotnet build backend/src/Anela.Heblo.Application/` — fix any namespace references that still point to the old `BackgroundJobs.Contracts` namespace for these three DTOs

---

### task: get-tasks-handler
[Create GetBackgroundRefreshTasks use case - 3 files]

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/GetBackgroundRefreshTasks/GetBackgroundRefreshTasksRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetBackgroundRefreshTasks;

public class GetBackgroundRefreshTasksRequest : IRequest<GetBackgroundRefreshTasksResponse> { }
```

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/GetBackgroundRefreshTasks/GetBackgroundRefreshTasksResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetBackgroundRefreshTasks;

public class GetBackgroundRefreshTasksResponse
{
    public required IReadOnlyList<RefreshTaskDto> Tasks { get; init; }
}
```

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/GetBackgroundRefreshTasks/GetBackgroundRefreshTasksHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetBackgroundRefreshTasks;

public class GetBackgroundRefreshTasksHandler : IRequestHandler<GetBackgroundRefreshTasksRequest, GetBackgroundRefreshTasksResponse>
{
    private readonly IBackgroundRefreshTaskRegistry _taskRegistry;
    private readonly ILogger<GetBackgroundRefreshTasksHandler> _logger;

    public GetBackgroundRefreshTasksHandler(IBackgroundRefreshTaskRegistry taskRegistry, ILogger<GetBackgroundRefreshTasksHandler> logger)
    {
        _taskRegistry = taskRegistry;
        _logger = logger;
    }

    public Task<GetBackgroundRefreshTasksResponse> Handle(GetBackgroundRefreshTasksRequest request, CancellationToken cancellationToken)
    {
        var tasks = _taskRegistry.GetRegisteredTasks()
            .Select(task =>
            {
                var lastExecution = _taskRegistry.GetLastExecution(task.TaskId);
                return MapToDto(task, lastExecution);
            })
            .ToList();

        return Task.FromResult(new GetBackgroundRefreshTasksResponse { Tasks = tasks });
    }

    private static RefreshTaskDto MapToDto(RefreshTaskConfiguration task, RefreshTaskExecutionLog? lastExecution)
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
            LastExecution = lastExecution != null ? MapToExecutionLogDto(lastExecution) : null
        };
    }

    private static RefreshTaskExecutionLogDto MapToExecutionLogDto(RefreshTaskExecutionLog log) =>
        new()
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
```

- [ ] Verify: `dotnet build backend/src/Anela.Heblo.Application/` — confirm handler compiles, types resolve correctly

---

### task: get-history-handlers
[Create GetTaskHistory + GetAllHistory use cases - 6 files]

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/GetTaskHistory/GetTaskHistoryRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskHistory;

public class GetTaskHistoryRequest : IRequest<GetTaskHistoryResponse>
{
    public required string TaskId { get; init; }
    public int MaxRecords { get; init; } = 50;
}
```

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/GetTaskHistory/GetTaskHistoryResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskHistory;

public class GetTaskHistoryResponse
{
    public required IReadOnlyList<RefreshTaskExecutionLogDto> History { get; init; }
}
```

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/GetTaskHistory/GetTaskHistoryHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskHistory;

public class GetTaskHistoryHandler : IRequestHandler<GetTaskHistoryRequest, GetTaskHistoryResponse>
{
    private readonly IBackgroundRefreshTaskRegistry _taskRegistry;

    public GetTaskHistoryHandler(IBackgroundRefreshTaskRegistry taskRegistry) => _taskRegistry = taskRegistry;

    public Task<GetTaskHistoryResponse> Handle(GetTaskHistoryRequest request, CancellationToken cancellationToken)
    {
        var history = _taskRegistry.GetExecutionHistory(request.TaskId, request.MaxRecords)
            .Select(MapToDto).ToList();

        return Task.FromResult(new GetTaskHistoryResponse { History = history });
    }

    private static RefreshTaskExecutionLogDto MapToDto(RefreshTaskExecutionLog log) =>
        new()
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
```

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/GetAllHistory/GetAllHistoryRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetAllHistory;

public class GetAllHistoryRequest : IRequest<GetAllHistoryResponse>
{
    public int MaxRecords { get; init; } = 100;
}
```

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/GetAllHistory/GetAllHistoryResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetAllHistory;

public class GetAllHistoryResponse
{
    public required IReadOnlyList<RefreshTaskExecutionLogDto> History { get; init; }
}
```

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/GetAllHistory/GetAllHistoryHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetAllHistory;

public class GetAllHistoryHandler : IRequestHandler<GetAllHistoryRequest, GetAllHistoryResponse>
{
    private readonly IBackgroundRefreshTaskRegistry _taskRegistry;

    public GetAllHistoryHandler(IBackgroundRefreshTaskRegistry taskRegistry) => _taskRegistry = taskRegistry;

    public Task<GetAllHistoryResponse> Handle(GetAllHistoryRequest request, CancellationToken cancellationToken)
    {
        var history = _taskRegistry.GetExecutionHistory(null, request.MaxRecords)
            .Select(MapToDto).ToList();

        return Task.FromResult(new GetAllHistoryResponse { History = history });
    }

    private static RefreshTaskExecutionLogDto MapToDto(RefreshTaskExecutionLog log) =>
        new()
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
```

- [ ] Verify: `dotnet build backend/src/Anela.Heblo.Application/` — confirm both history handlers compile

---

### task: get-status-handler
[Create GetTaskStatus use case - 3 files]

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/GetTaskStatus/GetTaskStatusRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskStatus;

public class GetTaskStatusRequest : IRequest<GetTaskStatusResponse>
{
    public required string TaskId { get; init; }
}
```

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/GetTaskStatus/GetTaskStatusResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskStatus;

public class GetTaskStatusResponse
{
    public bool Found { get; init; }
    public RefreshTaskStatusDto? Status { get; init; }
}
```

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/GetTaskStatus/GetTaskStatusHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskStatus;

public class GetTaskStatusHandler : IRequestHandler<GetTaskStatusRequest, GetTaskStatusResponse>
{
    private readonly IBackgroundRefreshTaskRegistry _taskRegistry;

    public GetTaskStatusHandler(IBackgroundRefreshTaskRegistry taskRegistry) => _taskRegistry = taskRegistry;

    public Task<GetTaskStatusResponse> Handle(GetTaskStatusRequest request, CancellationToken cancellationToken)
    {
        var task = _taskRegistry.GetRegisteredTasks().FirstOrDefault(t => t.TaskId == request.TaskId);
        if (task == null)
            return Task.FromResult(new GetTaskStatusResponse { Found = false });

        var lastExecution = _taskRegistry.GetLastExecution(request.TaskId);
        var status = new RefreshTaskStatusDto
        {
            TaskId = request.TaskId,
            Enabled = task.Enabled,
            RefreshInterval = task.RefreshInterval,
            LastExecution = lastExecution != null ? MapToDto(lastExecution) : null
        };

        return Task.FromResult(new GetTaskStatusResponse { Found = true, Status = status });
    }

    private static RefreshTaskExecutionLogDto MapToDto(RefreshTaskExecutionLog log) =>
        new()
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
```

- [ ] Verify: `dotnet build backend/src/Anela.Heblo.Application/` — confirm status handler compiles

---

### task: commands-handlers
[Create ForceRefreshTask + RunHydrationTier use cases - 6 files]

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/ForceRefreshTask/ForceRefreshTaskRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.ForceRefreshTask;

public class ForceRefreshTaskRequest : IRequest<ForceRefreshTaskResponse>
{
    public required string TaskId { get; init; }
}
```

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/ForceRefreshTask/ForceRefreshTaskResponse.cs`:

```csharp
namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.ForceRefreshTask;

public class ForceRefreshTaskResponse
{
    public bool Success { get; init; }
    public bool NotFound { get; init; }
    public string? ErrorMessage { get; init; }
}
```

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/ForceRefreshTask/ForceRefreshTaskHandler.cs`:

```csharp
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.ForceRefreshTask;

public class ForceRefreshTaskHandler : IRequestHandler<ForceRefreshTaskRequest, ForceRefreshTaskResponse>
{
    private readonly IBackgroundRefreshTaskRegistry _taskRegistry;
    private readonly ILogger<ForceRefreshTaskHandler> _logger;

    public ForceRefreshTaskHandler(IBackgroundRefreshTaskRegistry taskRegistry, ILogger<ForceRefreshTaskHandler> logger)
    {
        _taskRegistry = taskRegistry;
        _logger = logger;
    }

    public async Task<ForceRefreshTaskResponse> Handle(ForceRefreshTaskRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Force refresh requested for task '{TaskId}' by user", request.TaskId);
            await _taskRegistry.ForceRefreshAsync(request.TaskId, cancellationToken);
            return new ForceRefreshTaskResponse { Success = true };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Force refresh failed for task '{TaskId}': {Error}", request.TaskId, ex.Message);
            return new ForceRefreshTaskResponse { NotFound = true, ErrorMessage = ex.Message };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during force refresh of task '{TaskId}'", request.TaskId);
            return new ForceRefreshTaskResponse { Success = false, ErrorMessage = "An unexpected error occurred during force refresh" };
        }
    }
}
```

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/RunHydrationTier/RunHydrationTierRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.RunHydrationTier;

public class RunHydrationTierRequest : IRequest<RunHydrationTierResponse>
{
    public int Tier { get; init; }
}
```

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/RunHydrationTier/RunHydrationTierResponse.cs`:

```csharp
namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.RunHydrationTier;

public class RunHydrationTierResponse
{
    public bool Success { get; init; }
    public bool NotFound { get; init; }
    public bool Cancelled { get; init; }
    public string? ErrorMessage { get; init; }
    public int TaskCount { get; init; }
}
```

- [ ] Create `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/RunHydrationTier/RunHydrationTierHandler.cs`:

```csharp
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.RunHydrationTier;

public class RunHydrationTierHandler : IRequestHandler<RunHydrationTierRequest, RunHydrationTierResponse>
{
    private readonly IBackgroundRefreshTaskRegistry _taskRegistry;
    private readonly ILogger<RunHydrationTierHandler> _logger;

    public RunHydrationTierHandler(IBackgroundRefreshTaskRegistry taskRegistry, ILogger<RunHydrationTierHandler> logger)
    {
        _taskRegistry = taskRegistry;
        _logger = logger;
    }

    public async Task<RunHydrationTierResponse> Handle(RunHydrationTierRequest request, CancellationToken cancellationToken)
    {
        var tasksInTier = _taskRegistry.GetRegisteredTasks()
            .Where(t => t.HydrationTier == request.Tier && t.Enabled)
            .OrderBy(t => t.TaskId)
            .ToList();

        if (tasksInTier.Count == 0)
            return new RunHydrationTierResponse { NotFound = true, ErrorMessage = $"No enabled tasks found for tier {request.Tier}" };

        _logger.LogInformation("Manual hydration of tier {Tier} requested ({TaskCount} tasks)", request.Tier, tasksInTier.Count);

        try
        {
            foreach (var task in tasksInTier)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _taskRegistry.ForceRefreshAsync(task.TaskId, cancellationToken);
            }

            return new RunHydrationTierResponse { Success = true, TaskCount = tasksInTier.Count };
        }
        catch (OperationCanceledException)
        {
            return new RunHydrationTierResponse { Cancelled = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual hydration of tier {Tier} failed", request.Tier);
            return new RunHydrationTierResponse { Success = false, ErrorMessage = "An unexpected error occurred during tier hydration" };
        }
    }
}
```

- [ ] Verify: `dotnet build backend/src/Anela.Heblo.Application/` — confirm both command handlers compile

---

### task: refactor-controller
[Slim BackgroundRefreshController + add AddBackgroundRefreshModule() to Program.cs + build verify]

- [ ] Replace `backend/src/Anela.Heblo.API/Controllers/BackgroundRefreshController.cs` with the thin dispatcher:

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
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
        _mediator = mediator;
    }

    [HttpGet("tasks")]
    public async Task<ActionResult<IEnumerable<RefreshTaskDto>>> GetRegisteredTasks(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBackgroundRefreshTasksRequest(), cancellationToken);
        return Ok(result.Tasks);
    }

    [HttpGet("tasks/{taskId}/history")]
    public async Task<ActionResult<IEnumerable<RefreshTaskExecutionLogDto>>> GetTaskHistory(
        string taskId, [FromQuery] int maxRecords = 50, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetTaskHistoryRequest { TaskId = taskId, MaxRecords = maxRecords }, cancellationToken);
        return Ok(result.History);
    }

    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<RefreshTaskExecutionLogDto>>> GetAllHistory(
        [FromQuery] int maxRecords = 100, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetAllHistoryRequest { MaxRecords = maxRecords }, cancellationToken);
        return Ok(result.History);
    }

    [HttpPost("tasks/{taskId}/force-refresh")]
    [FeatureAuthorize(Feature.Admin_Administration, AccessLevel.Write)]
    public async Task<ActionResult> ForceRefresh(string taskId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ForceRefreshTaskRequest { TaskId = taskId }, cancellationToken);
        if (result.NotFound) return NotFound(new { Error = result.ErrorMessage });
        if (!result.Success) return StatusCode(500, new { Error = result.ErrorMessage });
        return Ok(new { Message = $"Task '{taskId}' refresh initiated successfully" });
    }

    [HttpPost("tiers/{tier}/run")]
    [FeatureAuthorize(Feature.Admin_Administration, AccessLevel.Write)]
    public async Task<ActionResult> RunHydrationTier(int tier, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RunHydrationTierRequest { Tier = tier }, cancellationToken);
        if (result.NotFound) return NotFound(new { Error = result.ErrorMessage });
        if (result.Cancelled) return StatusCode(499, new { Error = "Hydration was cancelled" });
        if (!result.Success) return StatusCode(500, new { Error = result.ErrorMessage });
        return Ok(new { Message = $"Tier {tier} hydration completed ({result.TaskCount} tasks)" });
    }

    [HttpGet("tasks/{taskId}/status")]
    public async Task<ActionResult<RefreshTaskStatusDto>> GetTaskStatus(string taskId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTaskStatusRequest { TaskId = taskId }, cancellationToken);
        if (!result.Found) return NotFound(new { Error = $"Task '{taskId}' not found" });
        return Ok(result.Status);
    }
}
```

- [ ] In `backend/src/Anela.Heblo.API/Program.cs` (or the equivalent startup file), locate the block where other application modules are registered (e.g. `services.AddSomeOtherModule()`) and add:

```csharp
services.AddBackgroundRefreshModule();
```

  Also add the corresponding using directive at the top of the file if not already present:

```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh;
```

- [ ] Verify the full solution builds: `dotnet build backend/` — zero errors expected
- [ ] Verify formatting: `dotnet format backend/` — no diff
- [ ] Confirm old DTO files are gone: ensure no remaining file in `backend/` references `Anela.Heblo.Application.Features.BackgroundJobs.Contracts.RefreshTaskDto`, `RefreshTaskStatusDto`, or `RefreshTaskExecutionLogDto` (use grep to check)
