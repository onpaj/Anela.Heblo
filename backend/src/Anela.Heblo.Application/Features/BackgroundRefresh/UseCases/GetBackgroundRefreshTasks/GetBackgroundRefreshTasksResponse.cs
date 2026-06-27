using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetBackgroundRefreshTasks;

public class GetBackgroundRefreshTasksResponse
{
    public required IReadOnlyList<RefreshTaskDto> Tasks { get; init; }
}
