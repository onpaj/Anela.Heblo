using Anela.Heblo.Domain.Features.MarketingAds;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAgentRuns;

public class ListAgentRunsHandler : IRequestHandler<ListAgentRunsRequest, ListAgentRunsResponse>
{
    private readonly IAgentRunRepository _agentRuns;

    public ListAgentRunsHandler(IAgentRunRepository agentRuns) => _agentRuns = agentRuns;

    public async Task<ListAgentRunsResponse> Handle(ListAgentRunsRequest request, CancellationToken cancellationToken)
    {
        var (items, total) = await _agentRuns.ListAsync(request.Page, request.PageSize, cancellationToken);
        return new ListAgentRunsResponse { Items = items.Select(AdProposalDtoMapper.ToDto).ToList(), TotalCount = total };
    }
}
