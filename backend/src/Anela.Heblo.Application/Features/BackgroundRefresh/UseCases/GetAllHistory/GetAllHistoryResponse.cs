using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetAllHistory;

public class GetAllHistoryResponse
{
    public required IReadOnlyList<RefreshTaskExecutionLogDto> History { get; init; }
}
