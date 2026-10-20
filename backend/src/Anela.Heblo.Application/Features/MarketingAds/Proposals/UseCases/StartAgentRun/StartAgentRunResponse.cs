using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.StartAgentRun;

public class StartAgentRunResponse : BaseResponse
{
    public Guid AgentRunId { get; set; }

    public StartAgentRunResponse() { }
    public StartAgentRunResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
