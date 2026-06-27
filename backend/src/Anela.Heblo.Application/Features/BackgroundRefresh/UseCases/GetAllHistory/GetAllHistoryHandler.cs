using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetAllHistory;

public class GetAllHistoryHandler : IRequestHandler<GetAllHistoryRequest, GetAllHistoryResponse>
{
    private readonly IBackgroundRefreshTaskRegistry _taskRegistry;

    public GetAllHistoryHandler(IBackgroundRefreshTaskRegistry taskRegistry)
    {
        _taskRegistry = taskRegistry;
    }

    public Task<GetAllHistoryResponse> Handle(GetAllHistoryRequest request, CancellationToken cancellationToken)
    {
        var history = _taskRegistry.GetExecutionHistory(null, request.MaxRecords)
            .Select(MapToDto)
            .ToList();

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
