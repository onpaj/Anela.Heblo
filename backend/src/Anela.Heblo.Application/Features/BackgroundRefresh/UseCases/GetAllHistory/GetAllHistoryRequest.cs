using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetAllHistory;

public class GetAllHistoryRequest : IRequest<GetAllHistoryResponse>
{
    public int MaxRecords { get; init; } = 100;
}
