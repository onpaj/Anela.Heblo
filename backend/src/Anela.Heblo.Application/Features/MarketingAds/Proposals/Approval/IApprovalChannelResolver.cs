namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;

/// <summary>
/// Spec 7.4: decides from the caller's token whether it may approve/reject and through which channel.
/// Synchronous: it only reads the current token's claims. Used by every approve/reject/revert/settings
/// handler. The real implementation ships in PR C4.
/// </summary>
public interface IApprovalChannelResolver
{
    ApprovalChannelResolution Resolve();
}
