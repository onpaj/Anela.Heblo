using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Execution;

/// <summary>
/// Spec 6.3 hard limits are re-checked by the execution job right after its claim: a proposal approved while the
/// limits allowed it is not executed once they deny it. Only Denied blocks; the approval-only outcomes were
/// satisfied by the people who approved it.
/// </summary>
public class AdProposalExecutionLimitsTests
{
    private static AdProposalExecutionJob Job(ProposalTestHarness h, IAdActionExecutor executor) => new(
        h.Proposals, h.Governance, h.Audit, new[] { executor }, h.ContextBuilder, h.Evaluator, h.Time, NullLogger<AdProposalExecutionJob>.Instance);

    private static Mock<IAdActionExecutor> Executor()
    {
        var mock = new Mock<IAdActionExecutor>();
        mock.SetupGet(e => e.Platform).Returns(AdPlatform.GoogleAds);
        mock.SetupGet(e => e.SupportedActions).Returns(new HashSet<AdActionType> { AdActionType.PauseAd });
        mock.Setup(e => e.ReadCurrentAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdTargetState(true, AdActionValues.Enabled, "{}"));
        mock.Setup(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdAction a, CancellationToken _) => new AdExecutionResult(AdExecutionOutcome.Succeeded,
                AdActionValues.Enabled, AdActionValues.Paused, $"res-{a.TargetExternalId}", "{}", null));
        return mock;
    }

    private static async Task<(AdProposal Proposal, List<AdProposalActionExecution> Rows, List<AdAuditEvent> Audit)> LoadAsync(ProposalTestHarness h, int id)
    {
        await using var verify = h.NewContext();
        var proposal = await verify.AdProposals.Include(p => p.Executions).SingleAsync(p => p.Id == id);
        var audit = await verify.AdAuditEvents.Where(e => e.ProposalId == id).OrderBy(e => e.Id).ToListAsync();
        return (proposal, proposal.Executions.OrderBy(e => e.ActionIndex).ToList(), audit);
    }

    private static async Task RunAsync(ProposalTestHarness h, IAdActionExecutor executor, int id)
    {
        h.Context.ChangeTracker.Clear();
        await Job(h, executor).RunAsync(id, CancellationToken.None);
    }

    private static void ShouldBeDeniedWithoutExecuting((AdProposal Proposal, List<AdProposalActionExecution> Rows, List<AdAuditEvent> Audit) loaded,
        int actionCount, string reason)
    {
        loaded.Proposal.Status.Should().Be(AdProposalStatus.Failed);
        loaded.Proposal.ExecutionFinishedAt.Should().NotBeNull();
        loaded.Rows.Should().HaveCount(actionCount).And.OnlyContain(r => r.State == AdActionExecutionState.Skipped
            && r.Error == AdProposalExecutionJob.DeniedByLimits + reason);
        loaded.Audit.Where(e => e.EventType == AdAuditEventType.ActionFailed).Should().HaveCount(actionCount)
            .And.OnlyContain(e => e.PayloadJson.Contains(reason));
        loaded.Audit.Should().NotContain(e => e.EventType == AdAuditEventType.ActionExecuted);
    }

    [Fact]
    public async Task RunAsync_ThirdApprovedPauseProposalWouldExceedTheWeeklyCap_IsDeniedAndNothingIsExecutedForIt()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var first = await h.SeedApprovedAsync(actionCount: 10);
        var second = await h.SeedApprovedAsync(actionCount: 10);
        var third = await h.SeedApprovedAsync(actionCount: 10); // all three were approved before any ran (cap 20)
        var executor = Executor();

        await RunAsync(h, executor.Object, first);
        await RunAsync(h, executor.Object, second);
        await RunAsync(h, executor.Object, third);

        (await LoadAsync(h, first)).Proposal.Status.Should().Be(AdProposalStatus.Executed);
        (await LoadAsync(h, second)).Proposal.Status.Should().Be(AdProposalStatus.Executed);
        ShouldBeDeniedWithoutExecuting(await LoadAsync(h, third), 10, AdLimitReasons.WeeklyPauseLimit);
        executor.Verify(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Exactly(20));
    }

    [Fact]
    public async Task RunAsync_ActionTypeDisabledAfterApproval_SkipsTheExecution()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync(actionCount: 2);
        await h.SetLimitAsync("GoogleAds", "PauseAd", AdLimitKeys.Enabled, 0);
        var executor = Executor();

        await RunAsync(h, executor.Object, id);

        ShouldBeDeniedWithoutExecuting(await LoadAsync(h, id), 2, AdLimitReasons.ActionTypeNotEnabled);
        executor.Verify(e => e.ReadCurrentAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Never);
        executor.Verify(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_AccountNoLongerManagedAfterApproval_SkipsTheExecution()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync(actionCount: 2);
        h.ReadModel.Setup(r => r.IsAccountManagedAsync(It.IsAny<AdPlatform>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var executor = Executor();

        await RunAsync(h, executor.Object, id);

        ShouldBeDeniedWithoutExecuting(await LoadAsync(h, id), 2, AdLimitReasons.AccountNotManaged);
        executor.Verify(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_ApprovalOnlyOutcomes_DoNotBlockAnApprovedProposal()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        // Pausing the only enabled ad of its group would need web approval, and 2 actions exceed a second-approver
        // threshold of 1; both were settled by the approvals that made it Approved.
        h.ReadModel.Setup(r => r.GetAdGroupOccupancyAsync(It.IsAny<AdPlatform>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdPlatform _, string _, IReadOnlyCollection<string> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, _ => new AdGroupOccupancy("grp-1", 1)));
        await h.SetLimitAsync("GoogleAds", "PauseAd", AdLimitKeys.SecondApproverAbove, 1);
        var id = await h.SeedApprovedAsync(actionCount: 2);
        var executor = Executor();

        await RunAsync(h, executor.Object, id);

        (await LoadAsync(h, id)).Proposal.Status.Should().Be(AdProposalStatus.Executed);
        executor.Verify(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
