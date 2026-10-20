using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.MarketingAds;
using Anela.Heblo.Tests.Features.MarketingAds.Proposals.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Limits;

public class AdLimitsContextBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

    private static ApplicationDbContext NewContext()
    {
        var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.Database.EnsureCreated();
        return context;
    }

    private static AdProposal NewProposal(string platform, string account) =>
        AdProposal.Create(new AdProposalDraft(platform, account, "PauseAd", "agent-1", AdActorKind.Agent, null, "[]", 1, "r"), Now.UtcDateTime, TimeSpan.FromHours(72));

    private static AdAction PauseAction(AdPlatform platform, string account) =>
        new(AdActionType.PauseAd, platform, account, AdEntityLevel.Ad, "ad-1", AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>());

    private static AdLimitsContextBuilder NewBuilder(ApplicationDbContext context, IAdsReadModel readModel) =>
        new(new AdGovernanceRepository(context), new AdProposalRepository(context), readModel, new FakeTimeProvider(Now));

    [Fact]
    public async Task BuildAsync_GathersSeededLimitsReadModelAndWeeklyPauses()
    {
        var context = NewContext();
        var readModel = new Mock<IAdsReadModel>();
        readModel.Setup(r => r.IsAccountManagedAsync(AdPlatform.GoogleAds, "acc-1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        readModel.Setup(r => r.GetAdGroupOccupancyAsync(AdPlatform.GoogleAds, "acc-1", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, AdGroupOccupancy> { ["ad-1"] = new("grp-1", 2) });
        var builder = NewBuilder(context, readModel.Object);

        var request = await builder.BuildAsync(NewProposal("GoogleAds", "acc-1"), new[] { PauseAction(AdPlatform.GoogleAds, "acc-1") }, ApprovalChannel.Mcp, "chief", CancellationToken.None);

        request.Limits[AdLimitKeys.MaxActionsPerProposal].Should().Be(10);
        request.IsAccountManaged.Should().BeTrue();
        request.AdGroupOccupancy["ad-1"].EnabledAdCount.Should().Be(2);
        request.CommittedPausesLast7Days.Should().Be(0);
        request.Channel.Should().Be(ApprovalChannel.Mcp);
        request.ApproverPrincipal.Should().Be("chief");
    }

    [Fact]
    public async Task UnavailableReadModel_MakesTheEngineDeny()
    {
        var unavailable = new UnavailableAdsReadModel();
        (await unavailable.IsAccountManagedAsync(AdPlatform.GoogleAds, "acc-1", CancellationToken.None)).Should().BeNull();
        (await unavailable.GetAdGroupOccupancyAsync(AdPlatform.GoogleAds, "acc-1", new[] { "ad-1" }, CancellationToken.None)).Should().BeEmpty();
        var builder = NewBuilder(NewContext(), unavailable);

        var request = await builder.BuildAsync(NewProposal("GoogleAds", "acc-1"), new[] { PauseAction(AdPlatform.GoogleAds, "acc-1") }, ApprovalChannel.Web, "chief", CancellationToken.None);
        var evaluation = new AdLimitsEvaluator().Evaluate(request);

        evaluation.Decision.Should().Be(AdLimitsDecision.Denied);
        evaluation.Reason.Should().Be(AdLimitReasons.AccountNotManaged);
    }

    [Fact]
    public async Task BuildAsync_PriorApproversAreOnlyHumansOfTheCurrentVersion()
    {
        var proposal = NewProposal("GoogleAds", "acc-1");
        proposal.RecordApproval("old-web", ApprovalChannel.Web, Now.UtcDateTime, isFinal: false);
        proposal.Revise("[]", 1, "r2", "agent-1", Now.UtcDateTime, TimeSpan.FromHours(72));
        proposal.RecordApproval("system", ApprovalChannel.System, Now.UtcDateTime, isFinal: false);
        proposal.RecordApproval("web-human", ApprovalChannel.Web, Now.UtcDateTime, isFinal: false);
        var readModel = new Mock<IAdsReadModel>();
        readModel.Setup(r => r.IsAccountManagedAsync(It.IsAny<AdPlatform>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        readModel.Setup(r => r.GetAdGroupOccupancyAsync(It.IsAny<AdPlatform>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, AdGroupOccupancy>());

        var request = await NewBuilder(NewContext(), readModel.Object)
            .BuildAsync(proposal, new[] { PauseAction(AdPlatform.GoogleAds, "acc-1") }, ApprovalChannel.Web, "chief", CancellationToken.None);

        request.PriorApproverPrincipals.Should().BeEquivalentTo(new[] { "web-human" });
    }

    [Fact]
    public async Task UnknownOccupancy_IsNeverAutoApprovedOnAnAgentChannel()
    {
        var readModel = new Mock<IAdsReadModel>();
        readModel.Setup(r => r.IsAccountManagedAsync(It.IsAny<AdPlatform>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        readModel.Setup(r => r.GetAdGroupOccupancyAsync(It.IsAny<AdPlatform>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, AdGroupOccupancy>());
        var request = await NewBuilder(NewContext(), readModel.Object)
            .BuildAsync(NewProposal("GoogleAds", "acc-1"), new[] { PauseAction(AdPlatform.GoogleAds, "acc-1") }, ApprovalChannel.Mcp, "agent", CancellationToken.None);

        var evaluation = new AdLimitsEvaluator().Evaluate(request);

        evaluation.Decision.Should().Be(AdLimitsDecision.NeedsWebApproval);
        evaluation.Reason.Should().Be(AdLimitReasons.AdGroupWouldBeEmpty);
    }

    private static Mock<IAdsReadModel> ManagedReadModel()
    {
        var readModel = new Mock<IAdsReadModel>();
        readModel.Setup(r => r.IsAccountManagedAsync(It.IsAny<AdPlatform>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        readModel.Setup(r => r.GetAdGroupOccupancyAsync(It.IsAny<AdPlatform>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, AdGroupOccupancy>());
        return readModel;
    }

    [Fact]
    public async Task BuildAsync_CountsSucceededPlusOtherApprovedAndExecutingPauses_NotTheEvaluatedProposal()
    {
        var context = NewContext();
        var selfId = await AdProposalRepositoryTests.SeedInFlightPausesAsync(new AdProposalRepository(context));
        var self = await new AdProposalRepository(context).GetReadOnlyAsync(selfId, CancellationToken.None);

        var request = await NewBuilder(context, ManagedReadModel().Object)
            .BuildAsync(self!, new[] { PauseAction(AdPlatform.GoogleAds, "acc-1") }, ApprovalChannel.Web, "chief", CancellationToken.None);

        // 1 succeeded + Approved 3 + Executing 4 with 1 recorded; Pending, another account and itself are not counted.
        request.CommittedPausesLast7Days.Should().Be(7);
    }

    [Fact]
    public async Task BuildForExecutionAsync_CountsSucceededPlusOtherExecutingOnly_AsTheSystem()
    {
        var context = NewContext();
        var selfId = await AdProposalRepositoryTests.SeedInFlightPausesAsync(new AdProposalRepository(context));
        var self = await new AdProposalRepository(context).GetReadOnlyAsync(selfId, CancellationToken.None);

        var request = await NewBuilder(context, ManagedReadModel().Object)
            .BuildForExecutionAsync(self!, new[] { PauseAction(AdPlatform.GoogleAds, "acc-1") }, CancellationToken.None);

        // Approved proposals queue behind this one and are checked when they run themselves.
        request.CommittedPausesLast7Days.Should().Be(4);
        request.Channel.Should().Be(ApprovalChannel.System);
    }
}
