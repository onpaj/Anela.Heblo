namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;

/// <summary>Registered by C3 so nothing can be approved before C4's claims-based resolver is deployed.</summary>
public sealed class DenyAllApprovalChannelResolver : IApprovalChannelResolver
{
    public const string Reason = "Approval channels are not configured yet (PR C4).";

    public ApprovalChannelResolution Resolve() => ApprovalChannelResolution.Deny(Reason);
}
