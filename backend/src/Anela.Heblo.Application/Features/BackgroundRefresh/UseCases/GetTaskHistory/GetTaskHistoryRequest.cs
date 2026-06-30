using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskHistory;

public class GetTaskHistoryRequest : IRequest<GetTaskHistoryResponse>
{
    public required string TaskId { get; init; }
    public int MaxRecords { get; init; } = 50;
}
