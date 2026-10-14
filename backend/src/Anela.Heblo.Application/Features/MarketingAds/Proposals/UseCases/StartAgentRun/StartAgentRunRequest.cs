using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.StartAgentRun;

public class StartAgentRunRequest : IRequest<StartAgentRunResponse>
{
    public const int MaxAgentNameLength = 100;
    public const int MaxScopes = 50;
    public const int MaxScopeLength = 200;

    public string AgentName { get; set; } = string.Empty;
    public List<string> DataScopesRead { get; set; } = new();
}
