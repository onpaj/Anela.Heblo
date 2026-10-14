using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.MarketingAds;
using Anela.Heblo.Tests.Common;
using Anela.Heblo.Tests.Features.MarketingAds.Proposals.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Execution;

/// <summary>Double delivery on Postgres: the Revision token lets one claim win and the loser's SaveChanges rolls back whole.</summary>
[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class AdProposalExecutionJobPostgresTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;

    public AdProposalExecutionJobPostgresTests(PostgresSharedContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _connectionString = await MarketingAdsProposalsPostgres.CreateMigratedAsync(_fixture, "marketing_ads_c3_exec");
        await MarketingAdsProposalsPostgres.ExecuteAsync(_connectionString,
            """UPDATE public."AdKillSwitches" SET "ExecutionEnabled" = true WHERE "Scope" = 'Global';""");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ApplicationDbContext NewContext() => MarketingAdsProposalsPostgres.NewContext(_connectionString);

    private async Task<int> SeedApprovedAsync()
    {
        var actions = new[] { ProposalTestHarness.PauseAction("ad-1") };
        var proposal = AdProposal.Create(new AdProposalDraft("GoogleAds", "acc-1", nameof(AdActionType.PauseAd), "agent-1",
            AdActorKind.Agent, null, AdActionJson.Serialize(actions), actions.Length, "reason"), Now, TimeSpan.FromHours(72));
        proposal.RecordApproval("chief", ApprovalChannel.Web, Now, isFinal: true);
        await using var ctx = NewContext();
        ctx.AdProposals.Add(proposal);
        await ctx.SaveChangesAsync();
        return proposal.Id;
    }

    private static AdProposalExecutionJob Job(ApplicationDbContext context, LoadBarrier barrier, IAdActionExecutor executor)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(Now.AddMinutes(5)));
        var readModel = new Mock<IAdsReadModel>();
        readModel.Setup(r => r.IsAccountManagedAsync(It.IsAny<AdPlatform>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        readModel.Setup(r => r.GetAdGroupOccupancyAsync(It.IsAny<AdPlatform>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, AdGroupOccupancy>());
        var governance = new AdGovernanceRepository(context);
        var proposals = new AdProposalRepository(context);
        return new AdProposalExecutionJob(new FaultyProposalRepository(proposals, barrier), governance, new AdAuditLog(context),
            new[] { executor }, new AdLimitsContextBuilder(governance, proposals, readModel.Object, time), new AdLimitsEvaluator(), time,
            NullLogger<AdProposalExecutionJob>.Instance);
    }

    [Fact]
    public async Task RunAsync_TwoDeliveriesClaimConcurrently_OneExecutesAndTheLoserLeavesNoTrace()
    {
        var id = await SeedApprovedAsync();
        var executor = new Mock<IAdActionExecutor>();
        executor.SetupGet(e => e.Platform).Returns(AdPlatform.GoogleAds);
        executor.SetupGet(e => e.SupportedActions).Returns(new HashSet<AdActionType> { AdActionType.PauseAd });
        executor.Setup(e => e.ReadCurrentAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdTargetState(true, AdActionValues.Enabled, "{}"));
        executor.Setup(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Enabled, AdActionValues.Paused, "res-1", "{}", null));
        var bothLoaded = new LoadBarrier(participants: 2);
        await using var firstContext = NewContext();
        await using var secondContext = NewContext();

        await Task.WhenAll(
            Task.Run(() => Job(firstContext, bothLoaded, executor.Object).RunAsync(id, CancellationToken.None)),
            Task.Run(() => Job(secondContext, bothLoaded, executor.Object).RunAsync(id, CancellationToken.None)));

        bothLoaded.Loads.Should().Be(2, "both deliveries must have seen the proposal as Approved");
        executor.Verify(e => e.ExecuteAsync(It.IsAny<AdAction>(), It.IsAny<CancellationToken>()), Times.Once);
        await using var verify = NewContext();
        var proposal = await verify.AdProposals.Include(p => p.Executions).SingleAsync(p => p.Id == id);
        proposal.Status.Should().Be(AdProposalStatus.Executed);
        proposal.Executions.Should().ContainSingle();
        (await verify.AdAuditEvents.CountAsync(e => e.ProposalId == id && e.EventType == AdAuditEventType.ExecutionStarted))
            .Should().Be(1, "the losing claim's audit row rolls back with its proposal update");
    }
}
