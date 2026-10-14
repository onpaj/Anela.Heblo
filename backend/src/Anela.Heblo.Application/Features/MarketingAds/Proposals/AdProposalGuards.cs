using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals;

public static class AdProposalGuards
{
    /// <summary>Approve, reject and revise all require a Pending, unexpired proposal at the named version.</summary>
    public static ErrorCodes? CheckPendingVersion(AdProposal? proposal, int expectedVersion, DateTime now)
    {
        if (proposal is null) return ErrorCodes.MarketingAdsProposalNotFound;
        if (proposal.IsExpiredAt(now)) return ErrorCodes.MarketingAdsProposalExpired;
        if (proposal.Status != AdProposalStatus.Pending) return ErrorCodes.MarketingAdsProposalNotPending;
        if (proposal.CurrentVersion != expectedVersion) return ErrorCodes.MarketingAdsProposalVersionMismatch;
        return null;
    }

    /// <summary>
    /// A principal decides each version at most once (unique index on ProposalId, Version, Principal). Checked up
    /// front so a repeat approve, or a reject after one's own approval, is a clear conflict, not a failed save.
    /// </summary>
    public static ErrorCodes? CheckNotDecidedBy(AdProposal proposal, string principal) =>
        proposal.Approvals.Any(a => a.Version == proposal.CurrentVersion && string.Equals(a.Principal, principal, StringComparison.Ordinal))
            ? ErrorCodes.MarketingAdsAlreadyApprovedByPrincipal
            : null;
}
