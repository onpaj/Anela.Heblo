namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>One approve/reject decision on a specific proposal version.</summary>
public class AdProposalApproval
{
    public long Id { get; set; }
    public int ProposalId { get; set; }
    public int Version { get; set; }
    public string Principal { get; set; } = null!;
    public ApprovalChannel Channel { get; set; }
    public ApprovalDecision Decision { get; set; }
    public string? Reason { get; set; }
    public DateTime At { get; set; }
}
