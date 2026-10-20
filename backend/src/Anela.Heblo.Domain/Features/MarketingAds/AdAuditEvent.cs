namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Append-only audit row (spec 6.5). A Postgres trigger rejects UPDATE, DELETE and TRUNCATE.</summary>
public class AdAuditEvent
{
    public long Id { get; set; }
    public int? ProposalId { get; set; }
    /// <summary>Set for proposal events so EF fills ProposalId when the proposal is inserted in the same SaveChanges.</summary>
    public AdProposal? Proposal { get; set; }
    public int? Version { get; set; }
    public AdAuditEventType EventType { get; set; }
    public string ActorPrincipal { get; set; } = null!;
    public AdActorKind ActorKind { get; set; }
    public ApprovalChannel Channel { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime OccurredAt { get; set; }
}
