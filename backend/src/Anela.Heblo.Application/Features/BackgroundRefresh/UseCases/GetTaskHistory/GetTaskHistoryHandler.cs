using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskHistory;

public class GetTaskHistoryHandler : IRequestHandler<GetTaskHistoryRequest, GetTaskHistoryResponse>
{
    private readonly IBackgroundRefreshTaskRegistry _taskRegistry;

    public GetTaskHistoryHandler(IBackgroundRefreshTaskRegistry taskRegistry)
    {
        _taskRegistry = taskRegistry;
    }

    public Task<GetTaskHistoryResponse> Handle(GetTaskHistoryRequest request, CancellationToken cancellationToken)
    {
        var taskExists = _taskRegistry.GetRegisteredTasks().Any(t => t.TaskId == request.TaskId);
        if (!taskExists)
            return Task.FromResult(new GetTaskHistoryResponse { Success = false });

        var history = _taskRegistry.GetExecutionHistory(request.TaskId, request.MaxRecords)
            .Select(MapToDto)
            .ToList();

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
