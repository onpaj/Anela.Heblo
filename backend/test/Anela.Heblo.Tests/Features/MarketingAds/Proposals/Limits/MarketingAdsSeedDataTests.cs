using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Persistence.MarketingAds;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Limits;

public class MarketingAdsSeedDataTests
{
    [Fact]
    public void SeedStrings_MatchTheContractEnumsAndLimitKeys()
    {
        MarketingAdsSeedData.Platforms.Should().BeEquivalentTo(Enum.GetNames<AdPlatform>());
        MarketingAdsSeedData.ActionTypes.Should().BeEquivalentTo(Enum.GetNames<AdActionType>());
        MarketingAdsSeedData.Limits().Select(l => l.Key).Should().OnlyContain(k => AdLimitKeys.All.Contains(k));
    }
}
