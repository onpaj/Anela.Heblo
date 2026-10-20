namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Immutable snapshot of a proposal's actions. Never updated or deleted (Postgres trigger).</summary>
public class AdProposalVersion
{
    public long Id { get; set; }
    public int ProposalId { get; set; }
    public int Version { get; set; }
    /// <summary>JSON array of C1 AdAction records (AdActionJson in Application).</summary>
    public string ActionsJson { get; set; } = null!;
    public int ActionCount { get; set; }
    /// <summary>Free text from the proposer. Shown separately; never used to render the diff.</summary>
    public string Reasoning { get; set; } = null!;
    public string CreatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
