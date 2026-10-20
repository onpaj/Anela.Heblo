using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAgentRuns;

public class ListAgentRunsRequest : IRequest<ListAgentRunsResponse>
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
