using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class MarketingAdsFeaturesTests
{
    [Theory]
    [InlineData(Feature.Marketing_Ads, "Reklamní kampaně", true, false)]
    [InlineData(Feature.Marketing_AdApprovals, "Schvalování kampaní", true, true)]
    [InlineData(Feature.Marketing_AdApprovalsViaMcp, "Schvalování přes Claude", false, false)]
    public void Feature_IsDeclaredWithTheLevelsFromSpecSection72(Feature feature, string label, bool hasWrite, bool hasAdmin)
    {
        var definition = AccessMatrix.Features.Single(f => f.Key == feature);

        definition.Label.Should().Be(label);
        definition.HasWrite.Should().Be(hasWrite);
        definition.HasAdmin.Should().Be(hasAdmin);
    }

    [Theory]
    [InlineData(Feature.Marketing_Ads, AccessLevel.Read, "marketing.ads.read")]
    [InlineData(Feature.Marketing_Ads, AccessLevel.Write, "marketing.ads.write")]
    [InlineData(Feature.Marketing_AdApprovals, AccessLevel.Write, "marketing.ad_approvals.write")]
    [InlineData(Feature.Marketing_AdApprovals, AccessLevel.Admin, "marketing.ad_approvals.admin")]
    [InlineData(Feature.Marketing_AdApprovalsViaMcp, AccessLevel.Read, "marketing.ad_approvals_via_mcp.read")]
    public void Feature_MapsToAStableRoleString(Feature feature, AccessLevel level, string expected)
    {
        AccessRoles.For(feature, level).Should().Be(expected);
    }
}
