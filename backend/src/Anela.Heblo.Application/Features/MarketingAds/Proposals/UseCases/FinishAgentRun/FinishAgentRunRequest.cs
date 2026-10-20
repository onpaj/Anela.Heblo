using Anela.Heblo.Domain.Features.MarketingAds;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.FinishAgentRun;

public class FinishAgentRunRequest : IRequest<FinishAgentRunResponse>
{
    public const int MaxSummaryLength = 4000;

    public Guid AgentRunId { get; set; }
    public AgentRunStatus Status { get; set; }
    public string? Summary { get; set; }
    public List<string> DataScopesRead { get; set; } = new();
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public decimal? CostCzk { get; set; }
}
