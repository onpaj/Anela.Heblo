using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Autonomy;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Autonomy;

public class AdAutonomyServiceTests
{
    [Theory]
    [InlineData(AdAutonomyMode.ProposeOnly, AdLimitsDecision.Allowed, false)]
    [InlineData(AdAutonomyMode.AutoWithinLimits, AdLimitsDecision.Allowed, true)]
    [InlineData(AdAutonomyMode.AutoWithinLimits, AdLimitsDecision.NeedsWebApproval, false)]
    [InlineData(AdAutonomyMode.AutoWithinLimits, AdLimitsDecision.NeedsSecondApprover, false)]
    [InlineData(AdAutonomyMode.AutoWithinLimits, AdLimitsDecision.Denied, false)]
    [InlineData(AdAutonomyMode.Auto, AdLimitsDecision.Allowed, true)]
    [InlineData(AdAutonomyMode.Auto, AdLimitsDecision.NeedsWebApproval, false)]
    [InlineData(AdAutonomyMode.Auto, AdLimitsDecision.NeedsSecondApprover, false)]
    [InlineData(AdAutonomyMode.Auto, AdLimitsDecision.Denied, false)]
    public void IsAutoApprovable_FollowsSpec64(AdAutonomyMode mode, AdLimitsDecision decision, bool expected) =>
        AdAutonomyService.IsAutoApprovable(mode, decision).Should().Be(expected);

    [Fact]
    public async Task SeededProposeOnly_NeverAutoApproves()
    {
        using var h = new ProposalTestHarness();
        var (proposal, actions) = await LoadPendingAsync(h);

        var approved = await h.Autonomy.TryAutoApproveAsync(proposal, actions, CancellationToken.None);

        approved.Should().BeFalse();
        proposal.Status.Should().Be(AdProposalStatus.Pending);
        h.Context.ChangeTracker.Entries<AdAuditEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task AutoWithinLimits_AllowedProposal_IsApprovedBySystemAndAudited()
    {
        using var h = new ProposalTestHarness();
        await h.SetAutonomyAsync("GoogleAds", "PauseAd", AdAutonomyMode.AutoWithinLimits);
        var (proposal, actions) = await LoadPendingAsync(h);

        var approved = await h.Autonomy.TryAutoApproveAsync(proposal, actions, CancellationToken.None);
        await h.Proposals.SaveChangesAsync(CancellationToken.None);

        approved.Should().BeTrue();
        await using var verify = h.NewContext();
        (await verify.AdProposals.SingleAsync(p => p.Id == proposal.Id)).Status.Should().Be(AdProposalStatus.Approved);
        var approval = await verify.AdProposalApprovals.SingleAsync(a => a.ProposalId == proposal.Id);
        approval.Principal.Should().Be(AdSystemPrincipals.Autonomy);
        approval.Channel.Should().Be(ApprovalChannel.System);
        var audit = await verify.AdAuditEvents.SingleAsync(e => e.ProposalId == proposal.Id);
        audit.EventType.Should().Be(AdAuditEventType.Approved);
        audit.ActorKind.Should().Be(AdActorKind.System);
        audit.ActorPrincipal.Should().Be(AdSystemPrincipals.Autonomy);
        audit.Version.Should().Be(1);
        audit.PayloadJson.Should().Contain("\"autonomyMode\":\"AutoWithinLimits\"");
    }

    [Fact]
    public async Task AutoWithinLimits_PauseThatEmptiesAnAdGroup_StaysPending()
    {
        using var h = new ProposalTestHarness();
        await h.SetAutonomyAsync("GoogleAds", "PauseAd", AdAutonomyMode.AutoWithinLimits);
        h.ReadModel.Setup(r => r.GetAdGroupOccupancyAsync(It.IsAny<AdPlatform>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, AdGroupOccupancy> { ["ad-1"] = new("grp-1", 1) });
        var (proposal, actions) = await LoadPendingAsync(h);

        var approved = await h.Autonomy.TryAutoApproveAsync(proposal, actions, CancellationToken.None);

        approved.Should().BeFalse();
        proposal.Status.Should().Be(AdProposalStatus.Pending);
    }

    // Spec 6.3: the web-only and second-approver rules apply "independent of who approved"; Auto is reserved
    // and in C3 behaves exactly like AutoWithinLimits.
    [Theory]
    [InlineData(AdAutonomyMode.AutoWithinLimits, AdLimitsDecision.NeedsWebApproval)]
    [InlineData(AdAutonomyMode.AutoWithinLimits, AdLimitsDecision.NeedsSecondApprover)]
    [InlineData(AdAutonomyMode.Auto, AdLimitsDecision.NeedsWebApproval)]
    [InlineData(AdAutonomyMode.Auto, AdLimitsDecision.NeedsSecondApprover)]
    public void HumanGatedDecisions_AreNeverAutoApproved_InEitherAutoMode(AdAutonomyMode mode, AdLimitsDecision decision) =>
        AdAutonomyService.IsAutoApprovable(mode, decision).Should().BeFalse();

    [Fact]
    public async Task Auto_PauseThatEmptiesAnAdGroup_StaysPendingForAHuman()
    {
        using var h = new ProposalTestHarness();
        await h.SetAutonomyAsync("GoogleAds", "PauseAd", AdAutonomyMode.Auto);
        h.ReadModel.Setup(r => r.GetAdGroupOccupancyAsync(It.IsAny<AdPlatform>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, AdGroupOccupancy> { ["ad-1"] = new("grp-1", 1) });
        var (proposal, actions) = await LoadPendingAsync(h);

        var approved = await h.Autonomy.TryAutoApproveAsync(proposal, actions, CancellationToken.None);

        approved.Should().BeFalse();
        proposal.Status.Should().Be(AdProposalStatus.Pending);
        h.Context.ChangeTracker.Entries<AdAuditEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Auto_AllowedProposal_IsApprovedBySystem()
    {
        using var h = new ProposalTestHarness();
        await h.SetAutonomyAsync("GoogleAds", "PauseAd", AdAutonomyMode.Auto);
        var (proposal, actions) = await LoadPendingAsync(h);

        var approved = await h.Autonomy.TryAutoApproveAsync(proposal, actions, CancellationToken.None);

        approved.Should().BeTrue();
        proposal.Status.Should().Be(AdProposalStatus.Approved);
    }

    [Fact]
    public async Task Auto_OverTheHardPerProposalLimit_StaysPending()
    {
        using var h = new ProposalTestHarness();
        await h.SetAutonomyAsync("GoogleAds", "PauseAd", AdAutonomyMode.Auto);
        var (proposal, actions) = await LoadPendingAsync(h, actionCount: 11);

        var approved = await h.Autonomy.TryAutoApproveAsync(proposal, actions, CancellationToken.None);

        approved.Should().BeFalse();
        proposal.Status.Should().Be(AdProposalStatus.Pending);
    }

    private static async Task<(AdProposal Proposal, IReadOnlyList<AdAction> Actions)> LoadPendingAsync(ProposalTestHarness h, int actionCount = 1)
    {
        var id = await h.SeedPendingAsync(actionCount);
        var proposal = await h.Proposals.GetForUpdateAsync(id, CancellationToken.None);
        return (proposal!, AdActionJson.Deserialize(proposal!.GetCurrentVersion().ActionsJson));
    }
}
