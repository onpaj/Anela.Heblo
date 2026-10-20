using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ApproveAdProposal;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.UseCases;

public class ApproveAdProposalHandlerTests
{
    private static ApproveAdProposalHandler Handler(ProposalTestHarness h) => new(
        h.Proposals, h.Audit, h.ContextBuilder, h.Evaluator, h.ChannelResolver.Object, h.Enqueuer.Object,
        h.CurrentUser.Object, h.Time, NullLogger<ApproveAdProposalHandler>.Instance);

    [Fact]
    public async Task WebApproval_OfCurrentVersion_ApprovesAuditsAndEnqueues()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();

        var response = await Handler(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Status.Should().Be(AdProposalStatus.Approved);
        response.ExecutionJobId.Should().Be("job-1");
        await using var verify = h.NewContext();
        var approval = await verify.AdProposalApprovals.SingleAsync(a => a.ProposalId == id);
        approval.Principal.Should().Be("chief");
        approval.Channel.Should().Be(ApprovalChannel.Web);
        var audit = await verify.AdAuditEvents.SingleAsync(e => e.ProposalId == id);
        audit.EventType.Should().Be(AdAuditEventType.Approved);
        audit.Channel.Should().Be(ApprovalChannel.Web);
        h.Enqueuer.Verify(e => e.EnqueueExecution(id), Times.Once);
    }

    [Fact]
    public async Task DeniedChannel_BlocksApproval_EvenForAnAdmin()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();
        h.DenyChannel();

        var response = await Handler(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsApprovalChannelDenied);
        await using var verify = h.NewContext();
        (await verify.AdProposals.SingleAsync(p => p.Id == id)).Status.Should().Be(AdProposalStatus.Pending);
        h.Enqueuer.Verify(e => e.EnqueueExecution(It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData(ApprovalChannel.System)]
    [InlineData((ApprovalChannel)0)]
    public async Task ResolverAllowingANonHumanChannel_BlocksApproval(ApprovalChannel channel)
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();
        h.UseChannel(channel);

        var response = await Handler(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsApprovalChannelDenied);
        await using var verify = h.NewContext();
        (await verify.AdProposals.SingleAsync(p => p.Id == id)).Status.Should().Be(AdProposalStatus.Pending);
        h.Enqueuer.Verify(e => e.EnqueueExecution(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task OldVersion_Returns409()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();

        var response = await Handler(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 2 }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsProposalVersionMismatch);
    }

    [Fact]
    public async Task VersionSupersededByRevision_Returns409AndRecordsNothing()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();
        await using (var other = h.NewContext())
        {
            var p = await other.AdProposals.Include(x => x.Versions).Include(x => x.Approvals).SingleAsync(x => x.Id == id);
            p.Revise(p.GetCurrentVersion().ActionsJson, 1, "revised", "agent-1", h.Now, TimeSpan.FromHours(72));
            await other.SaveChangesAsync();
        }

        var response = await Handler(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsProposalVersionMismatch);
        await using var verify = h.NewContext();
        (await verify.AdProposalApprovals.CountAsync(a => a.ProposalId == id)).Should().Be(0);
    }

    [Fact]
    public async Task UnknownProposal_Returns404()
    {
        using var h = new ProposalTestHarness();

        var response = await Handler(h).Handle(new ApproveAdProposalRequest { Id = 999, Version = 1 }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsProposalNotFound);
    }

    [Fact]
    public async Task ApprovedButNotYetExecutedPauses_CountAgainstTheWeeklyCap()
    {
        using var h = new ProposalTestHarness();
        await h.SeedApprovedAsync(actionCount: 10);
        await h.SeedApprovedAsync(actionCount: 10);
        var third = await h.SeedPendingAsync(actionCount: 10);

        var response = await Handler(h).Handle(new ApproveAdProposalRequest { Id = third, Version = 1 }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsLimitsDenied);
        h.Enqueuer.Verify(e => e.EnqueueExecution(third), Times.Never);
    }

    [Fact]
    public async Task McpApproval_OverMcpThreshold_NeedsWebApproval()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync(actionCount: 4);
        h.UseChannel(ApprovalChannel.Mcp);

        var response = await Handler(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsNeedsWebApproval);
    }

    [Fact]
    public async Task SecondApprover_FirstStaysPending_SamePrincipalRejected_DifferentPrincipalApproves()
    {
        using var h = new ProposalTestHarness();
        await h.SetLimitAsync("GoogleAds", "PauseAd", "SecondApproverAbove", 1);
        var id = await h.SeedPendingAsync(actionCount: 2);

        var first = await Handler(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);
        h.Context.ChangeTracker.Clear();
        var again = await Handler(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);
        h.Context.ChangeTracker.Clear();
        h.ActAs("specialist");
        var second = await Handler(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);

        first.Status.Should().Be(AdProposalStatus.Pending);
        first.AwaitingSecondApprover.Should().BeTrue();
        again.ErrorCode.Should().Be(ErrorCodes.MarketingAdsAlreadyApprovedByPrincipal);
        second.Status.Should().Be(AdProposalStatus.Approved);
        h.Enqueuer.Verify(e => e.EnqueueExecution(id), Times.Once);
    }

    [Fact]
    public async Task ExpiredProposal_CannotBeApproved()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();
        h.Time.Advance(TimeSpan.FromHours(72));

        var response = await Handler(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsProposalExpired);
        h.Enqueuer.Verify(e => e.EnqueueExecution(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ConcurrentRevision_Returns409AndLeavesNothingTracked()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();
        var handler = Handler(h);
        // Load the proposal into the handler's context, then let "someone else" bump its Revision.
        _ = await h.Proposals.GetForUpdateAsync(id, CancellationToken.None);
        await using (var other = h.NewContext())
        {
            var p = await other.AdProposals.Include(x => x.Versions).Include(x => x.Approvals).SingleAsync(x => x.Id == id);
            p.Touch(h.Now);
            await other.SaveChangesAsync();
        }

        var response = await handler.Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsConcurrentChange);
        h.Context.ChangeTracker.Entries().Should().BeEmpty();
        h.Enqueuer.Verify(e => e.EnqueueExecution(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task TwoApproversRacing_OnlyTheFirstWriteWins()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();
        // "chief" has loaded the proposal; meanwhile "specialist" approves it in another request.
        _ = await h.Proposals.GetForUpdateAsync(id, CancellationToken.None);
        await using (var other = h.NewContext())
        {
            var p = await other.AdProposals.Include(x => x.Versions).Include(x => x.Approvals).SingleAsync(x => x.Id == id);
            p.RecordApproval("specialist", ApprovalChannel.Web, h.Now, isFinal: true);
            await other.SaveChangesAsync();
        }

        var response = await Handler(h).Handle(new ApproveAdProposalRequest { Id = id, Version = 1 }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsConcurrentChange);
        h.Context.ChangeTracker.Entries().Should().BeEmpty();
        h.Enqueuer.Verify(e => e.EnqueueExecution(It.IsAny<int>()), Times.Never);
        await using var verify = h.NewContext();
        var stored = await verify.AdProposals.SingleAsync(p => p.Id == id);
        stored.Status.Should().Be(AdProposalStatus.Approved);
        // InMemory is not transactional, so only the parent's state is asserted; Postgres rolls the losing save back whole.
    }
}
