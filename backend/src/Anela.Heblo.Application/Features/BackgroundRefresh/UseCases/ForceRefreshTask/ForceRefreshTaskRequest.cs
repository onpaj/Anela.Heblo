using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.ForceRefreshTask;

public class ForceRefreshTaskRequest : IRequest<ForceRefreshTaskResponse>
{
    public required string TaskId { get; init; }
}
