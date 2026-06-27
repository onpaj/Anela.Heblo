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
