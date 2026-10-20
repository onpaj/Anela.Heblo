namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Where an approval decision came from. Resolved server-side by IApprovalChannelResolver, never by the client.</summary>
public enum ApprovalChannel
{
    Web = 1,
    Mcp = 2,
    System = 3,
}
