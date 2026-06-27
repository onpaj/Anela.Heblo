using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetAllHistory;

public class GetAllHistoryResponse : BaseResponse
{
    public IReadOnlyList<RefreshTaskExecutionLogDto> History { get; set; } = [];
}
