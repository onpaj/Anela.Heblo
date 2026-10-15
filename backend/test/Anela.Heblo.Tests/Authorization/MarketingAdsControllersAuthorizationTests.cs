using System.Reflection;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class MarketingAdsControllersAuthorizationTests
{
    [Theory]
    [InlineData(typeof(MarketingAdsProposalsController))]
    [InlineData(typeof(MarketingAdsSettingsController))]
    [InlineData(typeof(MarketingAdsActivityController))]
    public void Controller_IsGatedByAdsRead(Type controller)
    {
        var attribute = controller.GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Feature.Should().Be(Feature.Marketing_Ads);
        attribute.Level.Should().Be(AccessLevel.Read);
    }

    [Theory]
    [InlineData(typeof(MarketingAdsProposalsController), nameof(MarketingAdsProposalsController.SubmitProposal), Feature.Marketing_Ads, AccessLevel.Write)]
    [InlineData(typeof(MarketingAdsProposalsController), nameof(MarketingAdsProposalsController.ReviseProposal), Feature.Marketing_Ads, AccessLevel.Write)]
    [InlineData(typeof(MarketingAdsProposalsController), nameof(MarketingAdsProposalsController.ApproveProposal), Feature.Marketing_AdApprovals, AccessLevel.Write)]
    [InlineData(typeof(MarketingAdsProposalsController), nameof(MarketingAdsProposalsController.RejectProposal), Feature.Marketing_AdApprovals, AccessLevel.Write)]
    [InlineData(typeof(MarketingAdsProposalsController), nameof(MarketingAdsProposalsController.RevertProposal), Feature.Marketing_AdApprovals, AccessLevel.Admin)]
    [InlineData(typeof(MarketingAdsSettingsController), nameof(MarketingAdsSettingsController.UpdateAutonomy), Feature.Marketing_AdApprovals, AccessLevel.Admin)]
    [InlineData(typeof(MarketingAdsSettingsController), nameof(MarketingAdsSettingsController.UpdateLimit), Feature.Marketing_AdApprovals, AccessLevel.Admin)]
    [InlineData(typeof(MarketingAdsSettingsController), nameof(MarketingAdsSettingsController.SetKillSwitch), Feature.Marketing_AdApprovals, AccessLevel.Admin)]
    public void Mutation_RequiresItsOwnGate(Type controller, string method, Feature feature, AccessLevel level)
    {
        var attribute = controller.GetMethod(method)!.GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull($"{method} mutates state and must not inherit only the class-level Read gate");
        attribute!.Feature.Should().Be(feature);
        attribute.Level.Should().Be(level);
    }

    [Theory]
    [InlineData(typeof(MarketingAdsProposalsController), nameof(MarketingAdsProposalsController.ListProposals))]
    [InlineData(typeof(MarketingAdsProposalsController), nameof(MarketingAdsProposalsController.GetProposal))]
    [InlineData(typeof(MarketingAdsSettingsController), nameof(MarketingAdsSettingsController.GetSettings))]
    [InlineData(typeof(MarketingAdsActivityController), nameof(MarketingAdsActivityController.ListAuditEvents))]
    [InlineData(typeof(MarketingAdsActivityController), nameof(MarketingAdsActivityController.ListAgentRuns))]
    [InlineData(typeof(MarketingAdsActivityController), nameof(MarketingAdsActivityController.ListChangeEvents))]
    public void Reads_StayAtTheClassLevelGate(Type controller, string method)
    {
        controller.GetMethod(method)!.GetCustomAttributes<FeatureAuthorizeAttribute>(inherit: false).Should().BeEmpty();
    }
}
