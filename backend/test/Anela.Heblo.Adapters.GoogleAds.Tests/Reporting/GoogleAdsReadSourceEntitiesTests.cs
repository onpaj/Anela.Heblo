using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Reporting;

public sealed class GoogleAdsReadSourceEntitiesTests
{
    [Fact]
    public async Task returns_the_configured_customer_as_the_only_account()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var accounts = await source.GetAccountsAsync(CancellationToken.None);

        accounts.Should().Equal(new AdAccountSnapshot("1234567890", "Anela (fixture)", "CZK", "Europe/Prague"));
    }

    [Fact]
    public async Task refuses_a_manager_account_with_an_explanation()
    {
        var api = new FixtureGoogleAdsApiClient().WithOverride("customer", """
            {"results":[{"customer":{"id":"1234567890","currencyCode":"CZK","timeZone":"Europe/Prague","manager":true}}]}
            """);
        var (source, _) = ReadSourceHarness.Create(api);

        var act = () => source.GetAccountsAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*manager account*LoginCustomerId*");
    }

    [Fact]
    public async Task maps_every_level_with_its_parent()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var entities = await source.GetEntitiesAsync(TestSettings.CustomerId, CancellationToken.None);

        entities.Select(e => (e.Level, e.ExternalId, e.ParentLevel, e.ParentExternalId, e.Status)).Should().BeEquivalentTo(new[]
        {
            (AdEntityLevel.Campaign, "111", (AdEntityLevel?)null, (string?)null, AdEntityStatus.Enabled),
            (AdEntityLevel.Campaign, "112", (AdEntityLevel?)null, (string?)null, AdEntityStatus.Paused),
            (AdEntityLevel.AdGroup, "221", (AdEntityLevel?)AdEntityLevel.Campaign, (string?)"111", AdEntityStatus.Enabled),
            (AdEntityLevel.AdGroup, "222", (AdEntityLevel?)AdEntityLevel.Campaign, (string?)"112", AdEntityStatus.Paused),
            (AdEntityLevel.Keyword, "221~331", (AdEntityLevel?)AdEntityLevel.AdGroup, (string?)"221", AdEntityStatus.Enabled),
            (AdEntityLevel.Keyword, "221~332", (AdEntityLevel?)AdEntityLevel.AdGroup, (string?)"221", AdEntityStatus.Paused),
            (AdEntityLevel.NegativeKeyword, "adGroupCriteria/221~341", (AdEntityLevel?)AdEntityLevel.AdGroup, (string?)"221", AdEntityStatus.Enabled),
            (AdEntityLevel.NegativeKeyword, "campaignCriteria/111~351", (AdEntityLevel?)AdEntityLevel.Campaign, (string?)"111", AdEntityStatus.Enabled),
            (AdEntityLevel.Ad, "221~441", (AdEntityLevel?)AdEntityLevel.AdGroup, (string?)"221", AdEntityStatus.Enabled),
            (AdEntityLevel.Ad, "222~442", (AdEntityLevel?)AdEntityLevel.AdGroup, (string?)"222", AdEntityStatus.Paused),
        });
    }

    [Fact]
    public async Task keeps_keyword_text_match_type_budget_and_ad_name_fallback()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var entities = await source.GetEntitiesAsync(TestSettings.CustomerId, CancellationToken.None);

        var negative = entities.Single(e => e.ExternalId == "campaignCriteria/111~351");
        negative.Name.Should().Be("návod");
        negative.Attributes[AdActionPayloadKeys.Text].Should().Be("návod");
        negative.Attributes[AdActionPayloadKeys.MatchType].Should().Be(nameof(KeywordMatchType.Phrase));

        var brand = entities.Single(e => e.Level == AdEntityLevel.Campaign && e.ExternalId == "111");
        decimal.Parse(brand.Attributes["budgetAmount"]!, System.Globalization.CultureInfo.InvariantCulture).Should().Be(500m);
        brand.Attributes["advertisingChannelType"].Should().Be("SEARCH");

        entities.Single(e => e.ExternalId == "221~441").Name.Should().Be("RESPONSIVE_SEARCH_AD 441");
        entities.Single(e => e.ExternalId == "222~442").Name.Should().Be("Podzimní akce");
    }

    [Fact]
    public async Task sends_every_query_to_the_configured_customer()
    {
        var api = new FixtureGoogleAdsApiClient();
        var (source, _) = ReadSourceHarness.Create(api);

        await source.GetEntitiesAsync("123-456-7890", CancellationToken.None);

        api.Calls.Should().OnlyContain(c => c.CustomerId == TestSettings.CustomerId);
        api.Calls.Select(c => c.Query.Name).Should().Equal(
            "campaigns", "ad_groups", "keywords", "ad_group_negative_keywords", "campaign_negative_keywords", "ads");
    }

    [Fact]
    public async Task refuses_an_account_that_is_not_configured()
    {
        var api = new FixtureGoogleAdsApiClient();
        var (source, _) = ReadSourceHarness.Create(api);

        var act = () => source.GetEntitiesAsync("9999999999", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        api.Calls.Should().BeEmpty();
    }
}
