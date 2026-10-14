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
}
