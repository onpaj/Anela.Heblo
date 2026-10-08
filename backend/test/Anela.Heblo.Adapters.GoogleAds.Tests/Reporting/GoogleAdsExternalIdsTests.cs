using Anela.Heblo.Adapters.GoogleAds.Reporting;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Reporting;

public sealed class GoogleAdsExternalIdsTests
{
    [Fact]
    public void keyword_and_ad_ids_are_ad_group_tilde_child()
    {
        GoogleAdsExternalIds.Keyword("221", "331").Should().Be("221~331");
        GoogleAdsExternalIds.Ad("221", "441").Should().Be("221~441");
    }

    [Fact]
    public void negative_keyword_ids_are_prefixed_with_their_criteria_collection()
    {
        GoogleAdsExternalIds.AdGroupNegativeKeyword("221", "341").Should().Be("adGroupCriteria/221~341");
        GoogleAdsExternalIds.CampaignNegativeKeyword("111", "351").Should().Be("campaignCriteria/111~351");
    }
}
