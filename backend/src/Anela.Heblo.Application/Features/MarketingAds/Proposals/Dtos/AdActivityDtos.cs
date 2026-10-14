using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;

public class AdAuditEventDto
{
    public long Id { get; set; }
    public int? ProposalId { get; set; }
    public int? Version { get; set; }
    public AdAuditEventType EventType { get; set; }
    public string ActorPrincipal { get; set; } = null!;
    public AdActorKind ActorKind { get; set; }
    public ApprovalChannel Channel { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime OccurredAt { get; set; }
}

public class AgentRunDto
{
    public Guid Id { get; set; }
    public string AgentName { get; set; } = null!;
    public string Principal { get; set; } = null!;
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public AgentRunStatus Status { get; set; }
    /// <summary>The agent's own free text; untrusted, display as plain text.</summary>
    public string? Summary { get; set; }
    public List<string> DataScopesRead { get; set; } = new();
    public int ProposalsCreated { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public decimal? CostCzk { get; set; }
}
