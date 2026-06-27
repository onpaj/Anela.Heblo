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
