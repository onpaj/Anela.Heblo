using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ReviseAdProposal;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.UseCases;

public class ReviseAdProposalHandlerTests
{
    private static ReviseAdProposalHandler Handler(ProposalTestHarness h, IAdProposalRepository? proposals = null,
        IOptions<AdProposalOptions>? options = null) => new(
        proposals ?? h.Proposals, h.Audit, h.ContextBuilder, h.Evaluator, h.Autonomy, h.Enqueuer.Object,
        h.CurrentUser.Object, h.Time, options ?? h.Options, NullLogger<ReviseAdProposalHandler>.Instance);

    private static ReviseAdProposalRequest Revision(int id, int expectedVersion, params string[] adIds) => new()
    {
        Id = id,
        ExpectedVersion = expectedVersion,
        Actions = adIds.Select(a => new AdActionInputDto { TargetLevel = AdEntityLevel.Ad, TargetExternalId = a }).ToList(),
        Reasoning = "narrowed to one ad",
    };

    [Fact]
    public async Task ReasoningWithNul_IsInvalidAndStoresNoVersion()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync(creator: "agent-1");
        h.ActAs("agent-1");
        var request = Revision(id, 1, "ad-1");
        request.Reasoning = "narrowed\0";

        var response = await Handler(h).Handle(request, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsInvalidProposal);
        await using var verify = h.NewContext();
        (await verify.AdProposalVersions.CountAsync(v => v.ProposalId == id)).Should().Be(1);
    }

    [Fact]
    public async Task Creator_RevisesCurrentVersion_InsertsVersionTwoAndVoidsApprovals()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync(actionCount: 2, creator: "agent-1");
        await using (var ctx = h.NewContext())
        {
            var p = await ctx.AdProposals.Include(x => x.Versions).Include(x => x.Approvals).SingleAsync(x => x.Id == id);
            p.RecordApproval("chief", ApprovalChannel.Web, h.Now, isFinal: false);
            await ctx.SaveChangesAsync();
        }
        h.ActAs("agent-1");
        h.Time.Advance(TimeSpan.FromHours(10));

        var response = await Handler(h).Handle(Revision(id, 1, "ad-1"), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Version.Should().Be(2);
        response.Status.Should().Be(AdProposalStatus.Pending);
        response.AutoApproved.Should().BeFalse();
        response.DiffLines.Should().ContainSingle();
        await using var verify = h.NewContext();
        var stored = await verify.AdProposals.Include(p => p.Versions).Include(p => p.Approvals).SingleAsync(p => p.Id == id);
        stored.Versions.Select(v => v.Version).Should().BeEquivalentTo(new[] { 1, 2 });
        stored.Versions.Single(v => v.Version == 1).ActionCount.Should().Be(2, "version 1 is immutable");
        stored.GetCurrentApprovers().Should().BeEmpty();
        stored.ExpiresAt.Should().Be(h.Now.AddHours(72));
        var audit = await verify.AdAuditEvents.SingleAsync(e => e.ProposalId == id);
        audit.EventType.Should().Be(AdAuditEventType.Revised);
        audit.Version.Should().Be(2);
        audit.Channel.Should().Be(ApprovalChannel.Mcp);
        h.Enqueuer.Verify(e => e.EnqueueExecution(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task AutoWithinLimits_RevisionIsAutoApprovedAndEnqueued()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync(creator: "agent-1");
        await h.SetAutonomyAsync("GoogleAds", "PauseAd", AdAutonomyMode.AutoWithinLimits);
        h.ActAs("agent-1");

        var response = await Handler(h).Handle(Revision(id, 1, "ad-1"), CancellationToken.None);

        response.AutoApproved.Should().BeTrue();
        response.Status.Should().Be(AdProposalStatus.Approved);
        h.Enqueuer.Verify(e => e.EnqueueExecution(id), Times.Once);
        await using var verify = h.NewContext();
        (await verify.AdAuditEvents.Where(e => e.ProposalId == id).Select(e => e.EventType).ToListAsync())
            .Should().BeEquivalentTo(new[] { AdAuditEventType.Revised, AdAuditEventType.Approved });
    }

    [Fact]
    public async Task StaleExpectedVersion_Returns409()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync(creator: "agent-1");
        h.ActAs("agent-1");

        var response = await Handler(h).Handle(Revision(id, 2, "ad-1"), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsProposalVersionMismatch);
    }

    [Fact]
    public async Task UnknownProposal_ReturnsNotFound()
    {
        using var h = new ProposalTestHarness();

        var response = await Handler(h).Handle(Revision(999, 1, "ad-1"), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsProposalNotFound);
    }

    [Fact]
    public async Task NonCreator_IsForbidden()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync(creator: "agent-1");
        h.ActAs("agent-2");

        var response = await Handler(h).Handle(Revision(id, 1, "ad-1"), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsNotProposalOwner);
    }

    [Fact]
    public async Task ExpiredProposal_CannotBeRevisedAndKeepsItsExpiry()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync(creator: "agent-1");
        var originalExpiry = h.Now.AddHours(72);
        h.ActAs("agent-1");
        h.Time.Advance(TimeSpan.FromHours(73));

        var response = await Handler(h).Handle(Revision(id, 1, "ad-1"), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsProposalExpired);
        await using var verify = h.NewContext();
        var stored = await verify.AdProposals.Include(p => p.Versions).SingleAsync(p => p.Id == id);
        stored.Status.Should().Be(AdProposalStatus.Pending, "the expiry job, not revise, moves it to Expired");
        stored.ExpiresAt.Should().Be(originalExpiry);
        stored.Versions.Should().ContainSingle();
        (await verify.AdAuditEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task InvalidShape_ReturnsErrorsAndKeepsVersionOne()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync(creator: "agent-1");
        h.ActAs("agent-1");

        var response = await Handler(h).Handle(Revision(id, 1), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsInvalidProposal);
        response.Params!["Errors"].Should().Contain("At least one action");
        await using var verify = h.NewContext();
        (await verify.AdProposalVersions.CountAsync(v => v.ProposalId == id)).Should().Be(1);
    }

    [Fact]
    public async Task HardLimitDenied_IsRejectedAndNothingIsSaved()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync(creator: "agent-1");
        h.ActAs("agent-1");
        h.ReadModel.Setup(r => r.IsAccountManagedAsync(AdPlatform.GoogleAds, "acc-1", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var response = await Handler(h).Handle(Revision(id, 1, "ad-1"), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsLimitsDenied);
        response.Params!["Reason"].Should().Be(AdLimitReasons.AccountNotManaged);
        h.Context.ChangeTracker.Entries().Should().BeEmpty("the revision is discarded from the shared context");
        await using var verify = h.NewContext();
        (await verify.AdProposalVersions.CountAsync(v => v.ProposalId == id)).Should().Be(1);
        (await verify.AdAuditEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentChange_Returns409AndLeavesTheStoredProposalUntouched()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync(creator: "agent-1");
        h.ActAs("agent-1");
        var racingRepository = new Mock<IAdProposalRepository>();
        racingRepository.Setup(r => r.GetForUpdateAsync(id, It.IsAny<CancellationToken>()))
            .Returns((int i, CancellationToken ct) => h.Proposals.GetForUpdateAsync(i, ct));
        racingRepository.Setup(r => r.TrySaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                await TouchInAnotherContextAsync(h, id);
                return await h.Proposals.TrySaveChangesAsync(ct);
            });

        var response = await Handler(h, racingRepository.Object).Handle(Revision(id, 1, "ad-1"), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsConcurrentChange);
        h.Context.ChangeTracker.Entries().Should().BeEmpty();
        h.Enqueuer.Verify(e => e.EnqueueExecution(It.IsAny<int>()), Times.Never);
        // InMemory is not transactional (child rows may land before the conflict); Postgres rolls the whole save back.
        await using var verify = h.NewContext();
        (await verify.AdProposals.SingleAsync(p => p.Id == id)).CurrentVersion.Should().Be(1);
    }

    [Fact]
    public async Task InvalidOptions_FailTheRequestBeforeAnyChange()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync(creator: "agent-1");
        h.ActAs("agent-1");
        var badOptions = Options.Create(new AdProposalOptions { ProposalTtlHours = 0 });

        var act = () => Handler(h, options: badOptions).Handle(Revision(id, 1, "ad-1"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ProposalTtlHours*");
        await using var verify = h.NewContext();
        (await verify.AdProposalVersions.CountAsync(v => v.ProposalId == id)).Should().Be(1);
    }

    private static async Task TouchInAnotherContextAsync(ProposalTestHarness h, int id)
    {
        await using var other = h.NewContext();
        var proposal = await other.AdProposals.SingleAsync(p => p.Id == id);
        proposal.Touch(h.Now);
        await other.SaveChangesAsync();
    }
}
