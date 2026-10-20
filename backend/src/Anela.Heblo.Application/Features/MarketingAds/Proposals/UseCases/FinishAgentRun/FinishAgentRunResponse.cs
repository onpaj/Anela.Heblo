using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.FinishAgentRun;

public class FinishAgentRunResponse : BaseResponse
{
    public AgentRunDto Run { get; set; } = new();

    public FinishAgentRunResponse() { }
    public FinishAgentRunResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
