using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Reporting;

public sealed class GoogleAdsReadSourceFactsTests
{
    private static readonly DateOnly Date = ReadSourceHarness.FixtureDate;

    [Fact]
    public async Task returns_facts_for_all_four_levels_in_account_currency()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var facts = await source.GetDailyFactsAsync(TestSettings.CustomerId, Date, CancellationToken.None);

        facts.Select(f => (f.Level, f.EntityExternalId)).Should().BeEquivalentTo(new[]
        {
            (AdEntityLevel.Campaign, "111"), (AdEntityLevel.Campaign, "112"),
            (AdEntityLevel.AdGroup, "221"), (AdEntityLevel.Keyword, "221~331"), (AdEntityLevel.Ad, "221~441"),
        });
        facts.Should().OnlyContain(f => f.Currency == "CZK" && f.Date == Date);
    }

    [Fact]
    public async Task converts_micros_and_keeps_fractional_conversions()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var facts = await source.GetDailyFactsAsync(TestSettings.CustomerId, Date, CancellationToken.None);

        facts.Single(f => f.Level == AdEntityLevel.Campaign && f.EntityExternalId == "111").Should().Be(
            new AdDailyFactRow(AdEntityLevel.Campaign, "111", Date, 1200, 85, 432.1m, 4.0m, 3150.5m, "CZK"));
    }

    [Fact]
    public async Task treats_metrics_google_omits_as_zero()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var facts = await source.GetDailyFactsAsync(TestSettings.CustomerId, Date, CancellationToken.None);

        facts.Single(f => f.EntityExternalId == "112").Should().Be(
            new AdDailyFactRow(AdEntityLevel.Campaign, "112", Date, 10, 0, 0m, 0m, 0m, "CZK"));
    }

    [Fact]
    public async Task filters_every_fact_query_on_the_requested_date()
    {
        var api = new FixtureGoogleAdsApiClient();
        var (source, _) = ReadSourceHarness.Create(api);

        await source.GetDailyFactsAsync(TestSettings.CustomerId, Date, CancellationToken.None);

        api.Calls.Where(c => c.Query.Name.EndsWith("_facts", StringComparison.Ordinal))
            .Should().HaveCount(4)
            .And.OnlyContain(c => c.Query.Gaql.Contains("segments.date = '2026-10-06'"));
    }

    [Fact]
    public async Task a_day_without_traffic_returns_no_facts()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var facts = await source.GetDailyFactsAsync(TestSettings.CustomerId, Date.AddDays(-1), CancellationToken.None);

        facts.Should().BeEmpty();
    }

    [Fact]
    public async Task merges_near_exact_into_exact()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var terms = await source.GetSearchTermsAsync(TestSettings.CustomerId, Date, CancellationToken.None);

        terms.Should().HaveCount(3);
        terms.Single(t => t.SearchTerm == "anela krém").Should().Be(new AdSearchTermRow(
            "221", Date, "anela krém", KeywordMatchType.Exact, 50, 6, 15.0m, 1.0m, 450.0m, "CZK"));
    }

    [Fact]
    public async Task keeps_terms_without_a_keyword_match_type_with_a_null_match_type()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var terms = await source.GetSearchTermsAsync(TestSettings.CustomerId, Date, CancellationToken.None);

        terms.Single(t => t.SearchTerm == "přírodní kosmetika").MatchType.Should().BeNull();
        terms.Single(t => t.SearchTerm == "kosmetika zdarma").MatchType.Should().Be(KeywordMatchType.Broad);
    }
}
