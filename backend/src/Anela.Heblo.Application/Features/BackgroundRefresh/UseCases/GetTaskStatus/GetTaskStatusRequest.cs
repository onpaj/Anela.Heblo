using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskStatus;

public class GetTaskStatusRequest : IRequest<GetTaskStatusResponse>
{
    public required string TaskId { get; init; }
}
