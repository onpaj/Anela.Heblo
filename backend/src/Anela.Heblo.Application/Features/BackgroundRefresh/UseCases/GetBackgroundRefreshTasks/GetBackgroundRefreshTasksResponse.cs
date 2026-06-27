using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetBackgroundRefreshTasks;

public class GetBackgroundRefreshTasksResponse : BaseResponse
{
    public IReadOnlyList<RefreshTaskDto> Tasks { get; set; } = [];
}
