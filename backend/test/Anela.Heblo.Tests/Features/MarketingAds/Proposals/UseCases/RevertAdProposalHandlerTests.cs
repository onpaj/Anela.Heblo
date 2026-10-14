using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RevertAdProposal;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.UseCases;

public class RevertAdProposalHandlerTests
{
    private const string CriterionId = "customers/1/campaignCriteria/111~7";
    private readonly Mock<IAdActionExecutor> _executor = new();

    public RevertAdProposalHandlerTests()
    {
        _executor.SetupGet(e => e.Platform).Returns(AdPlatform.GoogleAds);
        _executor.SetupGet(e => e.SupportedActions).Returns(new HashSet<AdActionType> { AdActionType.PauseAd, AdActionType.AddNegativeKeyword });
        _executor.Setup(e => e.ReadCurrentAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdAction a, CancellationToken _) => new AdTargetState(true, a.OldValue, "{}"));
        _executor.Setup(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdAction a, CancellationToken _) => new AdExecutionResult(AdExecutionOutcome.Succeeded, a.OldValue, a.NewValue, $"res-{a.TargetExternalId}", "{}", null));
        _executor.Setup(e => e.RevertAsync(It.IsAny<AdAction>(), It.IsAny<AdExecutionResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Paused, AdActionValues.Enabled, null, """{"reverted":true}""", null));
    }

    private RevertAdProposalHandler Handler(ProposalTestHarness h) => new(
        h.Proposals, h.Audit, new[] { _executor.Object }, h.ChannelResolver.Object, h.CurrentUser.Object, h.Time,
        NullLogger<RevertAdProposalHandler>.Instance);

    private Task<RevertAdProposalResponse> RevertAsync(ProposalTestHarness h, int id, CancellationToken ct = default) =>
        Handler(h).Handle(new RevertAdProposalRequest { Id = id }, ct);

    private async Task<int> SeedExecutedAsync(ProposalTestHarness h, int actionCount)
    {
        var id = await h.SeedApprovedAsync(actionCount);
        await RunJobAsync(h, id);
        return id;
    }

    /// <summary>An executed AddNegativeKeyword proposal; the executor's ExecuteAsync answers with <paramref name="executeResult"/>.</summary>
    private async Task<int> SeedExecutedNegativeAsync(ProposalTestHarness h, AdExecutionResult executeResult)
    {
        var action = new AdAction(AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, "acc-1", AdEntityLevel.Campaign, "111",
            AdActionValues.Absent, AdActionValues.Present,
            new Dictionary<string, string> { [AdActionPayloadKeys.Text] = "zdarma", [AdActionPayloadKeys.MatchType] = nameof(KeywordMatchType.Exact) });
        var proposal = AdProposal.Create(new AdProposalDraft("GoogleAds", "acc-1", nameof(AdActionType.AddNegativeKeyword), "agent-1",
            AdActorKind.Agent, null, AdActionJson.Serialize(new[] { action }), 1, "reason"), h.Now, h.Options.Value.ProposalTtl);
        proposal.RecordApproval("chief", ApprovalChannel.Web, h.Now, isFinal: true);
        h.Proposals.Add(proposal);
        await h.Proposals.SaveChangesAsync(CancellationToken.None);
        h.Context.ChangeTracker.Clear();
        _executor.Setup(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>())).ReturnsAsync(executeResult);
        await RunJobAsync(h, proposal.Id);
        return proposal.Id;
    }

    private async Task RunJobAsync(ProposalTestHarness h, int id)
    {
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, true);
        await new AdProposalExecutionJob(h.Proposals, h.Governance, h.Audit, new[] { _executor.Object }, h.Time,
            NullLogger<AdProposalExecutionJob>.Instance).RunAsync(id, CancellationToken.None);
        h.Context.ChangeTracker.Clear();
    }

    private static async Task<(AdProposal Proposal, List<AdProposalActionExecution> Rows, List<AdAuditEvent> RevertEvents)> LoadAsync(ProposalTestHarness h, int id)
    {
        await using var verify = h.NewContext();
        var proposal = await verify.AdProposals.SingleAsync(p => p.Id == id);
        var rows = await verify.AdProposalActionExecutions.Where(e => e.ProposalId == id).OrderBy(e => e.ActionIndex).ToListAsync();
        var events = (await verify.AdAuditEvents.Where(e => e.ProposalId == id).OrderBy(e => e.Id).ToListAsync())
            .Where(e => e.PayloadJson.Contains("\"phase\":\"revert\"")).ToList();
        return (proposal, rows, events);
    }

    private static JsonElement Payload(AdAuditEvent e) => JsonDocument.Parse(e.PayloadJson).RootElement;

    private void VerifyRevertCalls(Times times) =>
        _executor.Verify(e => e.RevertAsync(It.IsAny<AdAction>(), It.IsAny<AdExecutionResult>(), It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task Revert_UsesStoredResultsInReverseOrder_AndMarksReverted()
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedAsync(h, 2);
        var order = new List<string>();
        var originals = new List<AdExecutionResult>();
        _executor.Setup(e => e.RevertAsync(It.IsAny<AdAction>(), It.IsAny<AdExecutionResult>(), It.IsAny<CancellationToken>()))
            .Callback((AdAction a, AdExecutionResult original, CancellationToken _) =>
            {
                order.Add(a.TargetExternalId);
                originals.Add(original);
            })
            .ReturnsAsync(new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Paused, AdActionValues.Enabled, null, "{}", null));

        var response = await RevertAsync(h, id);

        response.Success.Should().BeTrue();
        response.Status.Should().Be(AdProposalStatus.Reverted);
        response.RevertedActions.Should().Be(2);
        response.FailedActions.Should().Be(0);
        order.Should().Equal("ad-2", "ad-1");
        originals.Select(o => o.PlatformResourceId).Should().Equal("res-ad-2", "res-ad-1");
        originals.Should().OnlyContain(o => o.Outcome == AdExecutionOutcome.Succeeded && o.BeforeValue == AdActionValues.Enabled && o.AfterValue == AdActionValues.Paused);
        var (proposal, rows, events) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Reverted);
        proposal.RevertedAt.Should().NotBeNull();
        rows.Should().OnlyContain(r => r.RevertState == AdActionRevertState.Reverted);
        events.Should().HaveCount(2).And.OnlyContain(e => e.EventType == AdAuditEventType.Reverted
            && e.ActorPrincipal == "chief" && e.ActorKind == AdActorKind.Human && e.Channel == ApprovalChannel.Web);
    }

    [Fact]
    public async Task PartialRevertFailure_KeepsStatus_AndRetryRevertsOnlyTheRest()
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedAsync(h, 2);
        _executor.Setup(e => e.RevertAsync(It.Is<AdAction>(a => a.TargetExternalId == "ad-1"), It.IsAny<AdExecutionResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdExecutionResult(AdExecutionOutcome.Failed, null, null, null, "{}", "RATE_LIMIT"));
        // ad-1 is still paused when the retry re-reads it, so the retry calls the platform again
        _executor.Setup(e => e.ReadCurrentAsync(It.Is<AdAction>(a => a.TargetExternalId == "ad-1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdTargetState(true, AdActionValues.Paused, "{}"));

        var first = await RevertAsync(h, id);
        h.Context.ChangeTracker.Clear();
        var (afterFirst, firstRows, firstEvents) = await LoadAsync(h, id);
        _executor.Setup(e => e.RevertAsync(It.Is<AdAction>(a => a.TargetExternalId == "ad-1"), It.IsAny<AdExecutionResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Paused, AdActionValues.Enabled, null, "{}", null));
        var second = await RevertAsync(h, id);

        first.ErrorCode.Should().Be(ErrorCodes.MarketingAdsRevertFailed);
        first.RevertedActions.Should().Be(1);
        first.FailedActions.Should().Be(1);
        first.ValidateOnlyActions.Should().Be(0);
        first.Status.Should().Be(AdProposalStatus.Executed);
        afterFirst.Status.Should().Be(AdProposalStatus.Executed);
        firstRows.Select(r => r.RevertState).Should().Equal(AdActionRevertState.RevertFailed, AdActionRevertState.Reverted);
        firstRows[0].RevertError.Should().Be("RATE_LIMIT");
        firstEvents.Select(e => e.EventType).Should().Equal(AdAuditEventType.Reverted, AdAuditEventType.ActionFailed);
        second.Status.Should().Be(AdProposalStatus.Reverted);
        second.RevertedActions.Should().Be(1);
        _executor.Verify(e => e.RevertAsync(It.Is<AdAction>(a => a.TargetExternalId == "ad-2"), It.IsAny<AdExecutionResult>(), It.IsAny<CancellationToken>()), Times.Once);
        _executor.Verify(e => e.RevertAsync(It.Is<AdAction>(a => a.TargetExternalId == "ad-1"), It.IsAny<AdExecutionResult>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RetryOfAFailedRevert_WhoseChangeAlreadyLanded_IsMarkedRevertedWithoutCallingThePlatform()
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedAsync(h, 1);
        _executor.Setup(e => e.RevertAsync(It.IsAny<AdAction>(), It.IsAny<AdExecutionResult>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("HttpClient.Timeout")); // the mutate landed, the answer did not

        var first = await RevertAsync(h, id);
        h.Context.ChangeTracker.Clear();
        var (_, firstRows, _) = await LoadAsync(h, id);
        var second = await RevertAsync(h, id); // the re-read shows Enabled, the original value

        first.ErrorCode.Should().Be(ErrorCodes.MarketingAdsRevertFailed);
        firstRows.Single().RevertState.Should().Be(AdActionRevertState.RevertFailed);
        firstRows.Single().RevertError.Should().Contain("HttpClient.Timeout").And.Contain(RevertAdProposalHandler.MayHaveBeenApplied);
        second.Success.Should().BeTrue();
        second.Status.Should().Be(AdProposalStatus.Reverted);
        VerifyRevertCalls(Times.Once());
        var (_, rows, events) = await LoadAsync(h, id);
        rows.Single().RevertState.Should().Be(AdActionRevertState.Reverted);
        Payload(events.Last()).GetProperty("confirmedByReread").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task FirstRevertAttempt_DoesNotReadTheCurrentState()
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedAsync(h, 1);
        _executor.Invocations.Clear();

        await RevertAsync(h, id);

        _executor.Verify(e => e.ReadCurrentAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyRevertCalls(Times.Once());
    }

    [Fact]
    public async Task RetryOfAFailedRevert_WhenTheReadFails_StillCallsThePlatform()
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedAsync(h, 1);
        _executor.SetupSequence(e => e.RevertAsync(It.IsAny<AdAction>(), It.IsAny<AdExecutionResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdExecutionResult(AdExecutionOutcome.Failed, null, null, null, "{}", "RATE_LIMIT"))
            .ReturnsAsync(new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Paused, AdActionValues.Enabled, null, "{}", null));
        await RevertAsync(h, id);
        h.Context.ChangeTracker.Clear();
        _executor.Setup(e => e.ReadCurrentAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("503"));

        var second = await RevertAsync(h, id);

        second.Status.Should().Be(AdProposalStatus.Reverted);
        VerifyRevertCalls(Times.Exactly(2));
    }

    [Fact]
    public async Task ThrownRevert_IsRecordedAsFailedWithAVerifyNote_AndAudited()
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedAsync(h, 1);
        _executor.Setup(e => e.RevertAsync(It.IsAny<AdAction>(), It.IsAny<AdExecutionResult>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("502 Bad Gateway"));

        var response = await RevertAsync(h, id);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsRevertFailed);
        response.Params.Should().Contain("Failed", "1").And.Contain("Reverted", "0");
        var (proposal, rows, events) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Executed);
        rows.Single().RevertError.Should().Contain("502 Bad Gateway").And.Contain(RevertAdProposalHandler.MayHaveBeenApplied);
        var failed = events.Single();
        failed.EventType.Should().Be(AdAuditEventType.ActionFailed);
        Payload(failed).GetProperty("validateOnly").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ValidateOnlyRevert_StaysRevertFailed_ButIsFlaggedAsValidateOnly()
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedAsync(h, 1);
        _executor.Setup(e => e.RevertAsync(It.IsAny<AdAction>(), It.IsAny<AdExecutionResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdExecutionResult(AdExecutionOutcome.Failed, null, null, null, "{}",
                $"{AdExecutionErrors.ValidateOnlyPrefix} Google accepted the request; nothing was changed."));

        var response = await RevertAsync(h, id);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsRevertFailed);
        response.FailedActions.Should().Be(1);
        response.ValidateOnlyActions.Should().Be(1);
        response.Params.Should().Contain("ValidateOnly", "1");
        var (proposal, rows, events) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Executed);
        rows.Single().RevertState.Should().Be(AdActionRevertState.RevertFailed);
        rows.Single().RevertError.Should().StartWith(AdExecutionErrors.ValidateOnlyPrefix);
        Payload(events.Single()).GetProperty("validateOnly").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task NegativeKeywordWithoutAnyRecoverableResourceId_IsNotSentToThePlatform()
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedNegativeAsync(h,
            new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Absent, AdActionValues.Present, null, """{"status":"ok"}""", null));
        _executor.Setup(e => e.ReadCurrentAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdTargetState(true, AdActionValues.Present, "{}"));

        var response = await RevertAsync(h, id);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsRevertFailed);
        VerifyRevertCalls(Times.Never());
        var (_, rows, events) = await LoadAsync(h, id);
        rows.Single().State.Should().Be(AdActionExecutionState.Succeeded);
        rows.Single().RevertState.Should().Be(AdActionRevertState.RevertFailed);
        rows.Single().RevertError.Should().Be(RevertAdProposalHandler.ResourceIdUnknown);
        events.Single().EventType.Should().Be(AdAuditEventType.ActionFailed);
    }

    [Fact]
    public async Task NegativeKeywordWithAnOversizedResourceId_RecoversItFromTheStoredEnvelope()
    {
        using var h = new ProposalTestHarness();
        var longId = CriterionId + new string('9', 250);
        var id = await SeedExecutedNegativeAsync(h,
            new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Absent, AdActionValues.Present, longId, "{}", null));
        AdExecutionResult? original = null;
        _executor.Setup(e => e.RevertAsync(It.IsAny<AdAction>(), It.IsAny<AdExecutionResult>(), It.IsAny<CancellationToken>()))
            .Callback((AdAction _, AdExecutionResult o, CancellationToken _) => original = o)
            .ReturnsAsync(new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Present, AdActionValues.Absent, null, "{}", null));

        var response = await RevertAsync(h, id);

        response.Status.Should().Be(AdProposalStatus.Reverted);
        original!.PlatformResourceId.Should().Be(longId);
    }

    [Fact]
    public async Task NegativeKeywordConfirmedByReread_RecoversTheResourceNameFromTheStoredRow()
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedNegativeAsync(h,
            new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Absent, AdActionValues.Present, null,
                "{\"campaignCriterion\":{\"resourceName\":\"" + CriterionId + "\",\"keyword\":{\"text\":\"zdarma\",\"matchType\":\"EXACT\"}}}", null));
        AdExecutionResult? original = null;
        _executor.Setup(e => e.RevertAsync(It.IsAny<AdAction>(), It.IsAny<AdExecutionResult>(), It.IsAny<CancellationToken>()))
            .Callback((AdAction _, AdExecutionResult o, CancellationToken _) => original = o)
            .ReturnsAsync(new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Present, AdActionValues.Absent, null, "{}", null));

        var response = await RevertAsync(h, id);

        response.Status.Should().Be(AdProposalStatus.Reverted);
        original!.PlatformResourceId.Should().Be(CriterionId);
    }

    [Fact]
    public async Task NegativeKeywordWithUnknownId_RemovedByHand_ConvergesOnRetry()
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedNegativeAsync(h,
            new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Absent, AdActionValues.Present, null, "{}", null));
        await RevertAsync(h, id);
        h.Context.ChangeTracker.Clear();
        // a human removed the keyword in the account; the re-read now shows it Absent

        var second = await RevertAsync(h, id);

        second.Status.Should().Be(AdProposalStatus.Reverted);
        VerifyRevertCalls(Times.Never());
    }

    [Fact]
    public async Task CancelledRequest_RecordsTheRowAsFailedThenRethrows()
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedAsync(h, 2);
        using var cts = new CancellationTokenSource();
        _executor.Setup(e => e.RevertAsync(It.IsAny<AdAction>(), It.IsAny<AdExecutionResult>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new TaskCanceledException("cancelled"));

        var act = () => RevertAsync(h, id, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        VerifyRevertCalls(Times.Once());
        var (proposal, rows, _) = await LoadAsync(h, id);
        proposal.Status.Should().Be(AdProposalStatus.Executed);
        rows.Select(r => r.RevertState).Should().Equal(AdActionRevertState.NotReverted, AdActionRevertState.RevertFailed);
        rows[1].RevertError.Should().Contain(RevertAdProposalHandler.MayHaveBeenApplied);
    }

    [Fact]
    public async Task PendingProposal_IsNotRevertible()
    {
        using var h = new ProposalTestHarness();
        var id = await h.SeedPendingAsync();

        var response = await RevertAsync(h, id);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsNotRevertible);
    }

    [Fact]
    public async Task RevertedProposal_IsNotRevertibleAgain()
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedAsync(h, 1);
        await RevertAsync(h, id);
        h.Context.ChangeTracker.Clear();

        var response = await RevertAsync(h, id);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsNotRevertible);
        VerifyRevertCalls(Times.Once());
    }

    [Fact]
    public async Task UnknownProposal_IsNotFound()
    {
        using var h = new ProposalTestHarness();

        var response = await RevertAsync(h, 999);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsProposalNotFound);
    }

    [Fact]
    public async Task NoExecutorForThePlatform_IsAConflict()
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedAsync(h, 1);
        var handler = new RevertAdProposalHandler(h.Proposals, h.Audit, Array.Empty<IAdActionExecutor>(), h.ChannelResolver.Object,
            h.CurrentUser.Object, h.Time, NullLogger<RevertAdProposalHandler>.Instance);

        var response = await handler.Handle(new RevertAdProposalRequest { Id = id }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsNoExecutor);
    }

    [Fact]
    public async Task RevertIsNotGatedByTheKillSwitch()
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedAsync(h, 1);
        await h.SetKillSwitchAsync(AdKillSwitch.GlobalScope, false);

        var response = await RevertAsync(h, id);

        response.Status.Should().Be(AdProposalStatus.Reverted);
    }

    [Theory]
    [InlineData(ApprovalChannel.Mcp)]
    [InlineData(ApprovalChannel.System)]
    public async Task NonWebChannel_CannotRevert(ApprovalChannel channel)
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedAsync(h, 1);
        h.UseChannel(channel);

        var response = await RevertAsync(h, id);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsApprovalChannelDenied);
        VerifyRevertCalls(Times.Never());
    }

    [Fact]
    public async Task DenyAllResolver_CannotRevert()
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedAsync(h, 1);
        h.DenyChannel();

        var response = await RevertAsync(h, id);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsApprovalChannelDenied);
    }

    [Fact]
    public async Task NonAdmin_CannotRevert()
    {
        using var h = new ProposalTestHarness();
        var id = await SeedExecutedAsync(h, 1);
        h.CurrentUser.Setup(c => c.IsInRole(It.IsAny<string>())).Returns(false);

        var response = await RevertAsync(h, id);

        response.ErrorCode.Should().Be(ErrorCodes.InsufficientPermissions);
        VerifyRevertCalls(Times.Never());
    }
}
