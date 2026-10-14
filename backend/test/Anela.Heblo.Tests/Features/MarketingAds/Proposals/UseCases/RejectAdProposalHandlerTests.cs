using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ApproveAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RejectAdProposal;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.UseCases;

public class RejectAdProposalHandlerTests
{
    private static RejectAdProposalHandler Handler(ProposalTestHarness h) => new(
        h.Proposals, h.Audit, h.ChannelResolver.Object, h.CurrentUser.Object, h.Time, NullLogger<RejectAdProposalHandler>.Instance);

    private static ApproveAdProposalHandler Approver(ProposalTestHarness h) => new(
        h.Proposals, h.Audit, h.ContextBuilder, h.Evaluator, h.ChannelResolver.Object, h.Enqueuer.Object,
        h.CurrentUser.Object, h.Time, NullLogger<ApproveAdProposalHandler>.Instance);

    private static RejectAdProposalRequest Rejection(int id, int version = 1, string reason = "no") =>
        new() { Id = id, Version = version, Reason = reason };

    [Fact]
    public async Task McpReject_StoresReasonChannelAndAudit()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();
        h.UseChannel(ApprovalChannel.Mcp);

        var response = await Handler(h).Handle(Rejection(id, reason: "brand term"), CancellationToken.None);

        response.Status.Should().Be(AdProposalStatus.Rejected);
        await using var verify = h.NewContext();
        var decision = await verify.AdProposalApprovals.SingleAsync(a => a.ProposalId == id);
        decision.Decision.Should().Be(ApprovalDecision.Rejected);
        decision.Reason.Should().Be("brand term");
        decision.Channel.Should().Be(ApprovalChannel.Mcp);
        (await verify.AdAuditEvents.SingleAsync(e => e.ProposalId == id)).EventType.Should().Be(AdAuditEventType.Rejected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingReason_IsInvalid(string reason)
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();

        var response = await Handler(h).Handle(Rejection(id, reason: reason), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsInvalidProposal);
    }

    [Fact]
    public async Task TooLongReason_IsInvalid()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();

        var response = await Handler(h).Handle(
            Rejection(id, reason: new string('x', RejectAdProposalRequest.MaxReasonLength + 1)), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsInvalidProposal);
    }

    [Fact]
    public async Task DeniedChannel_CannotReject()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();
        h.DenyChannel();

        var response = await Handler(h).Handle(Rejection(id), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsApprovalChannelDenied);
        await using var verify = h.NewContext();
        (await verify.AdProposals.SingleAsync(p => p.Id == id)).Status.Should().Be(AdProposalStatus.Pending);
    }

    [Fact]
    public async Task StaleVersion_Returns409()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();

        var response = await Handler(h).Handle(Rejection(id, version: 3), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsProposalVersionMismatch);
    }

    [Fact]
    public async Task UnknownProposal_Returns404()
    {
        using var h = new ProposalTestHarness();

        var response = await Handler(h).Handle(Rejection(999), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsProposalNotFound);
    }

    [Fact]
    public async Task ExpiredProposal_CannotBeRejected()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();
        h.Time.Advance(TimeSpan.FromHours(72));

        var response = await Handler(h).Handle(Rejection(id), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsProposalExpired);
        await using var verify = h.NewContext();
        (await verify.AdProposalApprovals.CountAsync(a => a.ProposalId == id)).Should().Be(0);
    }

    [Fact]
    public async Task PrincipalWhoAlreadyApprovedTheVersion_CannotRejectIt()
    {
        using var h = new ProposalTestHarness();
        await h.SetLimitAsync("GoogleAds", "PauseAd", "SecondApproverAbove", 1);
        var id = await h.SeedPendingAsync(actionCount: 2);
        var approved = await Approver(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);
        h.Context.ChangeTracker.Clear();

        var response = await Handler(h).Handle(Rejection(id), CancellationToken.None);

        approved.AwaitingSecondApprover.Should().BeTrue();
        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsAlreadyApprovedByPrincipal);
        h.Context.ChangeTracker.HasChanges().Should().BeFalse();
        await using var verify = h.NewContext();
        (await verify.AdProposals.SingleAsync(p => p.Id == id)).Status.Should().Be(AdProposalStatus.Pending);
        (await verify.AdProposalApprovals.CountAsync(a => a.ProposalId == id)).Should().Be(1);
    }

    [Fact]
    public async Task DifferentPrincipal_CanRejectAfterAFirstApproval()
    {
        using var h = new ProposalTestHarness();
        await h.SetLimitAsync("GoogleAds", "PauseAd", "SecondApproverAbove", 1);
        var id = await h.SeedPendingAsync(actionCount: 2);
        await Approver(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);
        h.Context.ChangeTracker.Clear();
        h.ActAs("specialist");

        var response = await Handler(h).Handle(Rejection(id), CancellationToken.None);

        response.Status.Should().Be(AdProposalStatus.Rejected);
    }

    [Fact]
    public async Task ConcurrentChange_Returns409AndLeavesNothingTracked()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();
        _ = await h.Proposals.GetForUpdateAsync(id, CancellationToken.None);
        await using (var other = h.NewContext())
        {
            var p = await other.AdProposals.Include(x => x.Versions).Include(x => x.Approvals).SingleAsync(x => x.Id == id);
            p.Touch(h.Now);
            await other.SaveChangesAsync();
        }

        var response = await Handler(h).Handle(Rejection(id), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsConcurrentChange);
        h.Context.ChangeTracker.Entries().Should().BeEmpty();
        await using var verify = h.NewContext();
        (await verify.AdProposals.SingleAsync(p => p.Id == id)).Status.Should().Be(AdProposalStatus.Pending);
    }
}
