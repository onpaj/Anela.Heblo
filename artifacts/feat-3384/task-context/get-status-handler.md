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
