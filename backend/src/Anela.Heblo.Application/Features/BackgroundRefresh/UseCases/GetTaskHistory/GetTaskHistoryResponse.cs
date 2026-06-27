using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskHistory;

public class GetTaskHistoryResponse
{
    public required IReadOnlyList<RefreshTaskExecutionLogDto> History { get; init; }
}
