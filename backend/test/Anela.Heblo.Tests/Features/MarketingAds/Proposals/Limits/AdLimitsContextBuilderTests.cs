using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.MarketingAds;
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
        request.SucceededPausesLast7Days.Should().Be(0);
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
}
