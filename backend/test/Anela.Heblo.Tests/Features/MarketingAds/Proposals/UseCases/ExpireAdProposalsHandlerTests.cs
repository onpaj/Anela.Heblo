using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ExpireAdProposals;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.UseCases;

public class ExpireAdProposalsHandlerTests
{
    private static ExpireAdProposalsHandler Handler(ProposalTestHarness h) => new(
        h.Proposals, h.Audit, h.Enqueuer.Object, h.Time, h.Options, NullLogger<ExpireAdProposalsHandler>.Instance);

    [Fact]
    public async Task PendingPastTtl_IsExpiredAndAuditedBySystem()
    {
        using var h = new ProposalTestHarness();
        var old = await h.SeedPendingAsync();
        h.Time.Advance(TimeSpan.FromHours(48));
        var fresh = await h.SeedPendingAsync();
        h.Time.Advance(TimeSpan.FromHours(25));

        var response = await Handler(h).Handle(new ExpireAdProposalsRequest(), CancellationToken.None);

        response.ExpiredCount.Should().Be(1);
        await using var verify = h.NewContext();
        (await verify.AdProposals.SingleAsync(p => p.Id == old)).Status.Should().Be(AdProposalStatus.Expired);
        (await verify.AdProposals.SingleAsync(p => p.Id == fresh)).Status.Should().Be(AdProposalStatus.Pending);
        var audit = await verify.AdAuditEvents.SingleAsync(e => e.ProposalId == old);
        audit.EventType.Should().Be(AdAuditEventType.Expired);
        audit.ActorPrincipal.Should().Be("system:expiry");
        audit.Channel.Should().Be(ApprovalChannel.System);
    }

    [Fact]
    public async Task ApprovedNotPickedUp_IsReEnqueued_FreshApprovedIsNot()
    {
        using var h = new ProposalTestHarness();
        h.Options.Value.StuckApprovedRequeueMinutes = 15;
        var stuck = await h.SeedApprovedAsync();
        h.Time.Advance(TimeSpan.FromMinutes(20));
        var fresh = await h.SeedApprovedAsync();

        var response = await Handler(h).Handle(new ExpireAdProposalsRequest(), CancellationToken.None);

        response.RequeuedCount.Should().Be(1);
        h.Enqueuer.Verify(e => e.EnqueueExecution(stuck), Times.Once);
        h.Enqueuer.Verify(e => e.EnqueueExecution(fresh), Times.Never);
    }

    [Fact]
    public async Task FailedEnqueue_IsNotCounted()
    {
        using var h = new ProposalTestHarness();
        h.Options.Value.StuckApprovedRequeueMinutes = 15;
        await h.SeedApprovedAsync();
        h.Time.Advance(TimeSpan.FromMinutes(20));
        h.Enqueuer.Setup(e => e.EnqueueExecution(It.IsAny<int>())).Returns((string?)null);

        var response = await Handler(h).Handle(new ExpireAdProposalsRequest(), CancellationToken.None);

        response.RequeuedCount.Should().Be(0);
    }

    [Fact]
    public async Task InvalidOptions_Throw_BeforeTouchingAnything()
    {
        using var h = new ProposalTestHarness();
        h.Options.Value.StuckApprovedRequeueMinutes = 0;

        var act = () => Handler(h).Handle(new ExpireAdProposalsRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*StuckApprovedRequeueMinutes*");
    }

    [Fact]
    public async Task ProposalApprovedAfterIdsWereRead_IsSkipped_AndOthersStillExpire()
    {
        using var h = new ProposalTestHarness();
        var raced = await h.SeedPendingAsync();
        var other = await h.SeedPendingAsync();
        h.Time.Advance(TimeSpan.FromHours(73));
        // The raced proposal was approved by someone else after the pending-expired id list was read:
        // by the time the handler loads it, it is no longer Pending.
        await using (var ctx = h.NewContext())
        {
            var p = await ctx.AdProposals.SingleAsync(x => x.Id == raced);
            p.RecordApproval("chief", ApprovalChannel.Web, h.Now.AddHours(-1), isFinal: true);
            p.ExpiresAt = h.Now.AddHours(-1); // still listed as expired-by-time
            await ctx.SaveChangesAsync();
        }

        var response = await Handler(h).Handle(new ExpireAdProposalsRequest(), CancellationToken.None);

        response.ExpiredCount.Should().Be(1);
        await using var verify = h.NewContext();
        (await verify.AdProposals.SingleAsync(p => p.Id == raced)).Status.Should().Be(AdProposalStatus.Approved);
        (await verify.AdProposals.SingleAsync(p => p.Id == other)).Status.Should().Be(AdProposalStatus.Expired);
    }
}
