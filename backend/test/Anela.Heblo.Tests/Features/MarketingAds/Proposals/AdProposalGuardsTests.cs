using Anela.Heblo.Application.Features.MarketingAds.Proposals;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals;

public class AdProposalGuardsTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);

    private static AdProposal Pending() =>
        AdProposal.Create(new AdProposalDraft("GoogleAds", "acc-1", "PauseAd", "agent-1", AdActorKind.Agent, null, "[]", 1, "r"),
            Now, TimeSpan.FromHours(72));

    [Fact]
    public void CheckPendingVersion_PendingAtCurrentVersion_Passes() =>
        AdProposalGuards.CheckPendingVersion(Pending(), 1, Now).Should().BeNull();

    [Fact]
    public void CheckPendingVersion_Missing_IsNotFound() =>
        AdProposalGuards.CheckPendingVersion(null, 1, Now).Should().Be(ErrorCodes.MarketingAdsProposalNotFound);

    [Fact]
    public void CheckPendingVersion_PastExpiry_IsExpired() =>
        AdProposalGuards.CheckPendingVersion(Pending(), 1, Now.AddHours(72)).Should().Be(ErrorCodes.MarketingAdsProposalExpired);

    [Fact]
    public void CheckPendingVersion_NotPending_IsNotPending()
    {
        var proposal = Pending();
        proposal.Reject("chief", ApprovalChannel.Web, "no", Now);

        AdProposalGuards.CheckPendingVersion(proposal, 1, Now).Should().Be(ErrorCodes.MarketingAdsProposalNotPending);
    }

    [Fact]
    public void CheckPendingVersion_StaleVersion_IsVersionMismatch()
    {
        var proposal = Pending();
        proposal.Revise("[]", 1, "r2", "agent-1", Now, TimeSpan.FromHours(72));

        AdProposalGuards.CheckPendingVersion(proposal, 1, Now).Should().Be(ErrorCodes.MarketingAdsProposalVersionMismatch);
    }

    [Fact]
    public void ErrorParams_CarryTheJoinedErrorsAndTheReason()
    {
        AdProposalErrors.Params(new[] { "a.", "b." }).Should().Contain("Errors", "a. b.");
        AdProposalErrors.Reason(null).Should().Contain("Reason", string.Empty);
    }
}
