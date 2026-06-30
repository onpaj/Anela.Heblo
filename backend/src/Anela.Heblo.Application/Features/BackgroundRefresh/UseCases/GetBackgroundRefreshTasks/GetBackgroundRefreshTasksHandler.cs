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
