using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.MarketingAds.TestKit;
using Anela.Heblo.Persistence.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Execution;

public class AdProposalExecutionJobTests
{
    private static AdProposalExecutionJob Job(ProposalTestHarness h, params IAdActionExecutor[] executors) => new(
        h.Proposals, h.Governance, h.Audit, executors, h.Time, NullLogger<AdProposalExecutionJob>.Instance);

    private static Mock<IAdActionExecutor> Executor(AdPlatform platform = AdPlatform.GoogleAds)
    {
        var mock = new Mock<IAdActionExecutor>();
        mock.SetupGet(e => e.Platform).Returns(platform);
        mock.SetupGet(e => e.SupportedActions).Returns(new HashSet<AdActionType> { AdActionType.PauseAd, AdActionType.AddNegativeKeyword });
        mock.Setup(e => e.ReadCurrentAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdTargetState(true, AdActionValues.Enabled, """{"status":"ENABLED"}"""));
        mock.Setup(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdAction a, CancellationToken _) => new AdExecutionResult(AdExecutionOutcome.Succeeded,
                AdActionValues.Enabled, AdActionValues.Paused, $"res-{a.TargetExternalId}", """{"ok":true}""", null));
        return mock;
    }

    private static async Task<(AdProposal Proposal, List<AdProposalActionExecution> Rows, List<AdAuditEvent> Audit)> LoadAsync(ProposalTestHarness h, int id)
    {
        await using var verify = h.NewContext();
        var proposal = await verify.AdProposals.Include(p => p.Executions).SingleAsync(p => p.Id == id);
        var audit = await verify.AdAuditEvents.Where(e => e.ProposalId == id).OrderBy(e => e.Id).ToListAsync();
        return (proposal, proposal.Executions.OrderBy(e => e.ActionIndex).ToList(), audit);
    }

    [Fact]
    public async Task RunAsync_AllActionsSucceed_StoresBeforeStateResponseAndExecutes()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync(actionCount: 2);
        var executor = Executor();

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        var (proposal, rows, audit) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Executed);
        rows.Should().HaveCount(2).And.OnlyContain(r => r.State == AdActionExecutionState.Succeeded
            && r.BeforeValue == AdActionValues.Enabled && r.BeforeRawJson == """{"status":"ENABLED"}"""
            && r.AfterValue == AdActionValues.Paused && r.PlatformResponseJson == """{"ok":true}""");
        rows[0].PlatformResourceId.Should().Be("res-ad-1");
        audit.Select(e => e.EventType).Should().Equal(AdAuditEventType.ExecutionStarted, AdAuditEventType.ActionExecuted, AdAuditEventType.ActionExecuted);
        audit.Should().OnlyContain(e => e.Channel == ApprovalChannel.System && e.ActorPrincipal == "system:executor");
    }

    [Fact]
    public async Task RunAsync_DefaultKillSwitchOff_SkipsEverythingAndNeverCallsThePlatform()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedApprovedAsync(actionCount: 2);
        var executor = Executor();

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Failed);
        rows.Should().OnlyContain(r => r.State == AdActionExecutionState.Skipped);
        rows[0].Error.Should().Be(AdProposalExecutionJob.KillSwitchOff);
        executor.Verify(e => e.ReadCurrentAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_PlatformOverrideOff_BlocksThatPlatform()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        await h.SetKillSwitchAsync("GoogleAds", false);
        var id = await h.SeedApprovedAsync();
        var executor = Executor();

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        (await LoadAsync(h, id)).Proposal.Status.Should().Be(AdProposalStatus.Failed);
        executor.Verify(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_KillSwitchTurnedOffMidRun_SkipsRemainingActions()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync(actionCount: 3);
        var executor = Executor();
        executor.Setup(e => e.ExecuteAsync(It.Is<AdAction>(a => a.TargetExternalId == "ad-1"), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, false); // an admin flips the switch
                return new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Enabled, AdActionValues.Paused, "res-ad-1", "{}", null);
            });

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.PartiallyExecuted);
        rows.Select(r => r.State).Should().Equal(AdActionExecutionState.Succeeded, AdActionExecutionState.Skipped, AdActionExecutionState.Skipped);
        rows[1].Error.Should().Be(AdProposalExecutionJob.KillSwitchOff);
        executor.Verify(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_CurrentValueDiffers_IsStaleAndStops()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync(actionCount: 2);
        var executor = Executor();
        executor.Setup(e => e.ReadCurrentAsync(It.Is<AdAction>(a => a.TargetExternalId == "ad-1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdTargetState(true, AdActionValues.Paused, "{}"));

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        var (proposal, rows, audit) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Failed);
        rows[0].State.Should().Be(AdActionExecutionState.StaleState);
        rows[0].BeforeValue.Should().Be(AdActionValues.Paused);
        rows[1].State.Should().Be(AdActionExecutionState.Skipped);
        audit.Should().Contain(e => e.EventType == AdAuditEventType.ActionFailed);
        executor.Verify(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_PlatformRejectsOneAction_ContinuesWithTheNext()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync(actionCount: 2);
        var executor = Executor();
        executor.Setup(e => e.ExecuteAsync(It.Is<AdAction>(a => a.TargetExternalId == "ad-1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdExecutionResult(AdExecutionOutcome.Failed, AdActionValues.Enabled, null, null, """{"error":"POLICY"}""", "POLICY"));

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.PartiallyExecuted);
        rows.Select(r => r.State).Should().Equal(AdActionExecutionState.Failed, AdActionExecutionState.Succeeded);
        rows[0].Error.Should().Be("POLICY");
    }

    [Fact]
    public async Task RunAsync_ValidateOnlyResult_IsFailedFlaggedAndNeverCountedAsApplied()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync();
        var executor = Executor();
        executor.Setup(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdExecutionResult(AdExecutionOutcome.Failed, null, null, null, "{}",
                $"{AdExecutionErrors.ValidateOnlyPrefix} Google accepted the request; nothing was changed."));

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        var (proposal, rows, audit) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Failed);
        rows.Single().State.Should().Be(AdActionExecutionState.Failed);
        rows.Single().AfterValue.Should().BeNull();
        audit.Single(e => e.EventType == AdAuditEventType.ActionFailed).PayloadJson.Should().Contain("\"validateOnly\":true");
        audit.Should().NotContain(e => e.EventType == AdAuditEventType.ActionExecuted);
        await using var verify = h.NewContext();
        var repo = new AdProposalRepository(verify);
        (await repo.GetExecutionsInWindowAsync("GoogleAds", "acc-1", h.Now.AddDays(-1), h.Now.AddDays(1), CancellationToken.None))
            .Should().BeEmpty("a validate-only run changed nothing in the account");
        executor.Verify(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_PlatformRejection_IsNotFlaggedValidateOnly()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync();
        var executor = Executor();
        executor.Setup(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdExecutionResult(AdExecutionOutcome.Failed, null, null, null, "{}", "POLICY"));

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        (await LoadAsync(h, id)).Audit.Single(e => e.EventType == AdAuditEventType.ActionFailed)
            .PayloadJson.Should().Contain("\"validateOnly\":false");
    }

    [Fact]
    public async Task RunAsync_TransportException_FailsAndSkipsTheRest()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync(actionCount: 2);
        var executor = Executor();
        executor.Setup(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("401 Unauthorized"));

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Failed);
        rows[0].State.Should().Be(AdActionExecutionState.Failed);
        rows[0].Error.Should().Contain("401 Unauthorized");
        rows[1].State.Should().Be(AdActionExecutionState.Skipped);
        executor.Verify(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_ExecuteThrows_RereadsOnceAndStoresTheObservedValueWithoutRetrying()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync();
        var executor = Executor();
        executor.SetupSequence(e => e.ReadCurrentAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdTargetState(true, AdActionValues.Enabled, "{}"))
            .ReturnsAsync(new AdTargetState(true, AdActionValues.Paused, "{}")); // the mutate landed after all
        executor.Setup(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("HttpClient.Timeout"));

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Failed);
        var row = rows.Single();
        row.State.Should().Be(AdActionExecutionState.Failed);
        row.BeforeValue.Should().Be(AdActionValues.Enabled);
        row.AfterValue.Should().Be(AdActionValues.Paused);
        row.Error.Should().Contain("HttpClient.Timeout").And.Contain(AdProposalExecutionJob.MayHaveBeenApplied);
        executor.Verify(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Once);
        executor.Verify(e => e.ReadCurrentAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_ExecuteThrowsAndRereadThrows_StillRecordsTheFailure()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync();
        var executor = Executor();
        executor.SetupSequence(e => e.ReadCurrentAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdTargetState(true, AdActionValues.Enabled, "{}"))
            .ThrowsAsync(new HttpRequestException("503"));
        executor.Setup(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("502 Bad Gateway"));

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Failed);
        rows.Single().State.Should().Be(AdActionExecutionState.Failed);
        rows.Single().AfterValue.Should().BeNull();
        rows.Single().Error.Should().Contain("502 Bad Gateway").And.Contain(AdProposalExecutionJob.MayHaveBeenApplied);
    }

    [Fact]
    public async Task RunAsync_HttpTimeout_IsRecordedAsFailureNotCancellation()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync();
        var executor = Executor();
        executor.Setup(e => e.ReadCurrentAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TaskCanceledException("HttpClient.Timeout"));

        var act = () => Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        await act.Should().NotThrowAsync();
        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Failed);
        rows.Single().State.Should().Be(AdActionExecutionState.Failed);
        rows.Single().Error.Should().NotBe(AdProposalExecutionJob.Cancelled);
    }

    [Fact]
    public async Task RunAsync_JobCancelled_FinalisesStatusThenRethrows()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync(actionCount: 2);
        using var cts = new CancellationTokenSource();
        var executor = Executor();
        executor.Setup(e => e.ReadCurrentAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .Returns(() => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });

        var act = () => Job(h, executor.Object).RunAsync(id, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Failed);
        rows.Should().HaveCount(2).And.OnlyContain(r => r.State == AdActionExecutionState.Skipped && r.Error == AdProposalExecutionJob.Cancelled);
    }

    [Fact]
    public async Task RunAsync_KillSwitchReadThrows_FinalisesInsteadOfStrandingExecuting()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedApprovedAsync(actionCount: 2);
        var governance = new Mock<IAdGovernanceRepository>();
        governance.Setup(g => g.GetKillSwitchesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
        var executor = Executor();
        var job = new AdProposalExecutionJob(h.Proposals, governance.Object, h.Audit, new[] { executor.Object }, h.Time,
            NullLogger<AdProposalExecutionJob>.Instance);

        var act = () => job.RunAsync(id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>("Hangfire must show the run as failed");
        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Failed);
        proposal.ExecutionFinishedAt.Should().NotBeNull();
        rows.Should().HaveCount(2).And.OnlyContain(r => r.State == AdActionExecutionState.Skipped && r.Error == AdProposalExecutionJob.StoppedUnexpectedly);
        executor.Verify(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_SaveAfterAnActionThrows_KeepsTheRecordedActionAndFinalises()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync(actionCount: 2);
        var repo = new FaultyProposalRepository(h.Proposals, failOnSaves: 2); // 1 = claim, 2 = after action 0
        var executor = Executor();
        var job = new AdProposalExecutionJob(repo, h.Governance, h.Audit, new[] { executor.Object }, h.Time,
            NullLogger<AdProposalExecutionJob>.Instance);

        var act = () => job.RunAsync(id, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.PartiallyExecuted);
        rows.Select(r => r.State).Should().Equal(AdActionExecutionState.Succeeded, AdActionExecutionState.Skipped);
        rows[1].Error.Should().Be(AdProposalExecutionJob.StoppedUnexpectedly);
        executor.Verify(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_FinalSaveAlsoFails_ReloadsAndFinalisesWithAVerifyNote()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync(actionCount: 2);
        var repo = new FaultyProposalRepository(h.Proposals, failOnSaves: new[] { 2, 3 });
        var executor = Executor();
        var job = new AdProposalExecutionJob(repo, h.Governance, h.Audit, new[] { executor.Object }, h.Time,
            NullLogger<AdProposalExecutionJob>.Instance);

        var act = () => job.RunAsync(id, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Failed, "the success of action 0 was never stored");
        rows.Should().HaveCount(2).And.OnlyContain(r => r.State == AdActionExecutionState.Skipped && r.Error == AdProposalExecutionJob.NotRecorded);
    }

    [Fact]
    public async Task RunAsync_NoExecutorForPlatform_Fails()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync(platform: AdPlatform.Sklik);

        await Job(h, Executor(AdPlatform.GoogleAds).Object).RunAsync(id, CancellationToken.None);

        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Failed);
        rows.Single().Error.Should().Be(AdProposalExecutionJob.NoExecutor);
    }

    [Fact]
    public async Task RunAsync_RunTwice_ExecutesOnce()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync();
        var executor = Executor();

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);
        h.Context.ChangeTracker.Clear();
        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        executor.Verify(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_TwoDeliveriesClaimConcurrently_OnlyOneExecutes()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync();
        var executor = Executor();
        var bothLoaded = new LoadBarrier(participants: 2);
        await using var otherContext = h.NewContext();
        var first = new AdProposalExecutionJob(new FaultyProposalRepository(h.Proposals, bothLoaded), h.Governance, h.Audit,
            new[] { executor.Object }, h.Time, NullLogger<AdProposalExecutionJob>.Instance);
        var second = new AdProposalExecutionJob(new FaultyProposalRepository(new AdProposalRepository(otherContext), bothLoaded),
            new AdGovernanceRepository(otherContext), new AdAuditLog(otherContext), new[] { executor.Object }, h.Time,
            NullLogger<AdProposalExecutionJob>.Instance);

        await Task.WhenAll(
            Task.Run(() => first.RunAsync(id, CancellationToken.None)),
            Task.Run(() => second.RunAsync(id, CancellationToken.None)));

        bothLoaded.Loads.Should().Be(2, "both deliveries must have seen the proposal as Approved");
        executor.Verify(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Once);
        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Executed);
        rows.Should().ContainSingle();
        // InMemory has no transactions, so the loser's ExecutionStarted row is not rolled back here;
        // AdProposalExecutionJobPostgresTests pins the single audit row on Postgres.
    }

    [Fact]
    public async Task RunAsync_PendingProposal_IsIgnored()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedPendingAsync();
        var executor = Executor();

        await Job(h, executor.Object).RunAsync(id, CancellationToken.None);

        (await LoadAsync(h, id)).Proposal.Status.Should().Be(AdProposalStatus.Pending);
        executor.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_WithTheTestKitFakeExecutor_PausesTheAd()
    {
        using var h = new ProposalTestHarness();
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        var id = await h.SeedApprovedAsync();
        // C1's in-memory executor: ctor (platform, params types), SeedAd(account, adId, status), ExecutedActions.
        var fake = new FakeAdActionExecutor(AdPlatform.GoogleAds).SeedAd("acc-1", "ad-1", AdActionValues.Enabled);

        await Job(h, fake).RunAsync(id, CancellationToken.None);

        (await LoadAsync(h, id)).Proposal.Status.Should().Be(AdProposalStatus.Executed);
        fake.ExecutedActions.Should().ContainSingle(a => a.TargetExternalId == "ad-1" && a.NewValue == AdActionValues.Paused);
    }
}
