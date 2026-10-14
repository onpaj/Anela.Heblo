using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SubmitAdProposal;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.UseCases;

public class SubmitAdProposalHandlerTests
{
    private static SubmitAdProposalHandler Handler(ProposalTestHarness h, IOptions<AdProposalOptions>? options = null) => new(
        h.Proposals, h.AgentRuns, h.Audit, h.ContextBuilder, h.Evaluator, h.Autonomy, h.Enqueuer.Object,
        h.CurrentUser.Object, h.Time, options ?? h.Options, NullLogger<SubmitAdProposalHandler>.Instance);

    private static SubmitAdProposalRequest PauseRequest(params string[] adIds) => new()
    {
        Platform = AdPlatform.GoogleAds,
        AccountExternalId = "123-456-7890",
        ActionType = AdActionType.PauseAd,
        Actions = adIds.Select(id => new AdActionInputDto { TargetLevel = AdEntityLevel.Ad, TargetExternalId = id }).ToList(),
        Reasoning = "CTR fell below 0.5 % for 30 days",
    };

    [Fact]
    public async Task ValidProposal_IsStoredPendingWithVersionAuditAndDiff()
    {
        using var h = new ProposalTestHarness();

        var response = await Handler(h).Handle(PauseRequest("ad-1", "ad-2"), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Version.Should().Be(1);
        response.Status.Should().Be(AdProposalStatus.Pending);
        response.AutoApproved.Should().BeFalse();
        response.Evaluation.Decision.Should().Be(AdLimitsDecision.Allowed);
        response.DiffLines.Should().HaveCount(2);

        await using var verify = h.NewContext();
        var stored = await verify.AdProposals.Include(p => p.Versions).SingleAsync(p => p.Id == response.ProposalId);
        stored.AccountExternalId.Should().Be("1234567890", "Google Ads ids are stored without dashes, like the actions");
        stored.CreatedByPrincipal.Should().Be("chief");
        stored.CreatedByKind.Should().Be(AdActorKind.Human);
        stored.ExpiresAt.Should().Be(h.Now.AddHours(72));
        stored.Versions.Single().ActionCount.Should().Be(2);
        var audit = await verify.AdAuditEvents.SingleAsync(e => e.ProposalId == response.ProposalId);
        audit.EventType.Should().Be(AdAuditEventType.Submitted);
        audit.Channel.Should().Be(ApprovalChannel.Web);
        h.Enqueuer.Verify(e => e.EnqueueExecution(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task InvalidShape_ReturnsErrorsAndStoresNothing()
    {
        using var h = new ProposalTestHarness();

        var response = await Handler(h).Handle(PauseRequest(), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsInvalidProposal);
        response.Params!["Errors"].Should().Contain("At least one action");
        await using var verify = h.NewContext();
        (await verify.AdProposals.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task HardLimitDenied_IsRejectedWithReasonAndNotStored()
    {
        using var h = new ProposalTestHarness();
        h.ReadModel.Setup(r => r.IsAccountManagedAsync(AdPlatform.GoogleAds, "1234567890", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var response = await Handler(h).Handle(PauseRequest("ad-1"), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsLimitsDenied);
        response.Params!["Reason"].Should().Be(AdLimitReasons.AccountNotManaged);
        await using var verify = h.NewContext();
        (await verify.AdProposals.CountAsync()).Should().Be(0);
        (await verify.AdAuditEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AgentRunOfTheCaller_MarksTheProposalAsAgentCreatedOverMcp()
    {
        using var h = new ProposalTestHarness();
        h.ActAs("agent-1");
        var runId = Guid.NewGuid();
        h.AgentRuns.Add(AgentRun.Start(runId, "paid-search", "agent-1", "[]", h.Now));
        await h.AgentRuns.SaveChangesAsync(CancellationToken.None);
        var request = PauseRequest("ad-1");
        request.AgentRunId = runId;

        var response = await Handler(h).Handle(request, CancellationToken.None);

        await using var verify = h.NewContext();
        var stored = await verify.AdProposals.SingleAsync(p => p.Id == response.ProposalId);
        stored.CreatedByKind.Should().Be(AdActorKind.Agent);
        stored.AgentRunId.Should().Be(runId);
        (await verify.AdAuditEvents.SingleAsync()).Channel.Should().Be(ApprovalChannel.Mcp);
    }

    [Fact]
    public async Task AgentRunOfAnotherPrincipal_IsRejected()
    {
        using var h = new ProposalTestHarness();
        var runId = Guid.NewGuid();
        h.AgentRuns.Add(AgentRun.Start(runId, "paid-search", "agent-1", "[]", h.Now));
        await h.AgentRuns.SaveChangesAsync(CancellationToken.None);
        var request = PauseRequest("ad-1");
        request.AgentRunId = runId;

        var response = await Handler(h).Handle(request, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsAgentRunNotFound);
    }

    [Fact]
    public async Task FinishedAgentRun_IsRejected()
    {
        using var h = new ProposalTestHarness();
        h.ActAs("agent-1");
        var runId = Guid.NewGuid();
        var run = AgentRun.Start(runId, "paid-search", "agent-1", "[]", h.Now);
        run.Finish(AgentRunStatus.Succeeded, null, "[]", 0, null, null, null, h.Now);
        h.AgentRuns.Add(run);
        await h.AgentRuns.SaveChangesAsync(CancellationToken.None);
        var request = PauseRequest("ad-1");
        request.AgentRunId = runId;

        var response = await Handler(h).Handle(request, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsAgentRunNotFound);
        await using var verify = h.NewContext();
        (await verify.AdProposals.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AutoWithinLimits_ApprovesAndEnqueuesAfterSaving()
    {
        using var h = new ProposalTestHarness();
        await h.SetAutonomyAsync("GoogleAds", "PauseAd", AdAutonomyMode.AutoWithinLimits);

        var response = await Handler(h).Handle(PauseRequest("ad-1"), CancellationToken.None);

        response.AutoApproved.Should().BeTrue();
        response.Status.Should().Be(AdProposalStatus.Approved);
        h.Enqueuer.Verify(e => e.EnqueueExecution(response.ProposalId), Times.Once);
        await using var verify = h.NewContext();
        (await verify.AdAuditEvents.Where(e => e.ProposalId == response.ProposalId).Select(e => e.EventType).ToListAsync())
            .Should().BeEquivalentTo(new[] { AdAuditEventType.Submitted, AdAuditEventType.Approved });
    }

    [Fact]
    public async Task AutoApprovedButEnqueueFails_StillSucceedsAndStaysApproved()
    {
        using var h = new ProposalTestHarness();
        await h.SetAutonomyAsync("GoogleAds", "PauseAd", AdAutonomyMode.AutoWithinLimits);
        h.Enqueuer.Setup(e => e.EnqueueExecution(It.IsAny<int>())).Returns((string?)null);

        var response = await Handler(h).Handle(PauseRequest("ad-1"), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.AutoApproved.Should().BeTrue();
        await using var verify = h.NewContext();
        (await verify.AdProposals.SingleAsync(p => p.Id == response.ProposalId)).Status.Should().Be(AdProposalStatus.Approved);
    }

    [Fact]
    public async Task AnonymousCaller_IsUnauthorized()
    {
        using var h = new ProposalTestHarness();
        h.CurrentUser.Setup(c => c.GetCurrentUser()).Returns(new Anela.Heblo.Domain.Features.Users.CurrentUser(null, "Anonymous", null, false));

        var response = await Handler(h).Handle(PauseRequest("ad-1"), CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task InvalidOptions_FailTheRequestAndStoreNothing()
    {
        using var h = new ProposalTestHarness();
        var badOptions = Options.Create(new AdProposalOptions { ProposalTtlHours = 0 });

        var act = () => Handler(h, badOptions).Handle(PauseRequest("ad-1"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ProposalTtlHours*");
        await using var verify = h.NewContext();
        (await verify.AdProposals.CountAsync()).Should().Be(0);
    }
}
