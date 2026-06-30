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
