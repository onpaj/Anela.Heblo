using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskStatus;

public class GetTaskStatusHandler : IRequestHandler<GetTaskStatusRequest, GetTaskStatusResponse>
{
    private readonly IBackgroundRefreshTaskRegistry _taskRegistry;

    public GetTaskStatusHandler(IBackgroundRefreshTaskRegistry taskRegistry)
    {
        _taskRegistry = taskRegistry;
    }

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
