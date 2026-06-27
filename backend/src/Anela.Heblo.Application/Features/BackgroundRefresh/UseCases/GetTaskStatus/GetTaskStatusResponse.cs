using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskStatus;

public class GetTaskStatusResponse
{
    public bool Found { get; init; }
    public RefreshTaskStatusDto? Status { get; init; }
}
