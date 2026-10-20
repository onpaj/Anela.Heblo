using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAgentRuns;

public class ListAgentRunsResponse : BaseResponse
{
    public List<AgentRunDto> Items { get; set; } = new();
    public int TotalCount { get; set; }

    public ListAgentRunsResponse() { }
    public ListAgentRunsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
