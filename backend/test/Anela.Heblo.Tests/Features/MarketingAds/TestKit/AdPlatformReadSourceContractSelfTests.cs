using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestKit;

public class AdPlatformReadSourceContractSelfTests
{
    private const string Account = "123";
    private static readonly DateOnly Date = new(2026, 10, 6);

    private static FakeAdPlatformReadSource Sample() =>
        FakeAdPlatformReadSource.CreateSample(AdPlatform.Sklik, Account, Date);

    [Fact]
    public async Task Contract_fails_a_source_reporting_negative_cost()
    {
        var suite = new Suite(Sample().WithDailyFacts(Account, new AdDailyFactRow(
            AdEntityLevel.Campaign, FakeAdPlatformReadSource.SampleCampaignExternalId, Date, 1, 1, -5m, 0m, 0m, "CZK")));

        var act = () => suite.GetDailyFactsAsync_returns_rows_for_the_requested_date_with_non_negative_metrics_and_a_currency();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Contract_fails_a_source_reporting_a_lowercase_currency()
    {
        var suite = new Suite(new FakeAdPlatformReadSource(AdPlatform.Sklik)
            .WithAccount(new AdAccountSnapshot(Account, "Anela", "czk", "Europe/Prague")));

        var act = () => suite.GetAccountsAsync_returns_the_fixture_account_with_every_identity_field_set();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Contract_fails_a_source_whose_ad_group_points_at_a_missing_campaign()
    {
        var suite = new Suite(Sample().WithEntities(Account, new AdEntitySnapshot(
            AdEntityLevel.AdGroup, "orphan", AdEntityLevel.Campaign, "campaign-missing", "Orphan",
            AdEntityStatus.Enabled, new Dictionary<string, string?>())));

        var act = () => suite.GetEntitiesAsync_campaigns_have_no_parent_and_every_other_entity_has_a_parent_in_the_same_snapshot();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Contract_fails_a_source_whose_facts_reference_an_unknown_entity()
    {
        var suite = new Suite(Sample().WithDailyFacts(Account, new AdDailyFactRow(
            AdEntityLevel.Ad, "ad-not-in-snapshot", Date, 1, 1, 1m, 0m, 0m, "CZK")));

        var act = () => suite.GetDailyFactsAsync_rows_reference_entities_returned_by_GetEntitiesAsync();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Contract_fails_a_source_returning_two_fact_rows_for_one_entity()
    {
        var suite = new Suite(Sample().WithDailyFacts(Account, new AdDailyFactRow(
            AdEntityLevel.Campaign, FakeAdPlatformReadSource.SampleCampaignExternalId, Date, 1, 1, 1m, 0m, 0m, "CZK")));

        var act = () => suite.GetDailyFactsAsync_returns_at_most_one_row_per_entity_for_the_date();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Contract_fails_a_source_returning_a_duplicate_search_term_row()
    {
        var suite = new Suite(Sample().WithSearchTerms(Account, new AdSearchTermRow(
            FakeAdPlatformReadSource.SampleAdGroupExternalId, Date, "krém na obličej", KeywordMatchType.Phrase,
            1, 1, 1m, 0m, 0m, "CZK")));

        var act = () => suite.GetSearchTermsAsync_returns_at_most_one_row_per_ad_group_term_and_match_type();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Contract_fails_a_source_returning_duplicate_change_event_ids()
    {
        var occurredAt = new DateTimeOffset(Date.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);
        var suite = new Suite(Sample().WithChangeEvents(Account, new AdChangeEventRow(
            "change-1", occurredAt, null, AdChangeActorKind.Unknown, null, null, "StatusChanged", null, null)));

        var act = () => suite.GetChangeEventsAsync_rows_are_unique_well_formed_and_not_older_than_since();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Contract_fails_a_source_returning_change_values_that_are_not_json()
    {
        var occurredAt = new DateTimeOffset(Date.ToDateTime(new TimeOnly(11, 0)), TimeSpan.Zero);
        var suite = new Suite(Sample().WithChangeEvents(Account, new AdChangeEventRow(
            "change-2", occurredAt, null, AdChangeActorKind.Unknown, null, null, "StatusChanged", "Enabled", null)));

        var act = () => suite.GetChangeEventsAsync_rows_are_unique_well_formed_and_not_older_than_since();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Contract_fails_a_source_that_ignores_the_requested_date_for_daily_facts()
    {
        var suite = new Suite(new IgnoresFilters(Sample()));

        var act = () => suite.GetDailyFactsAsync_returns_rows_for_the_requested_date_with_non_negative_metrics_and_a_currency();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Contract_fails_a_source_that_ignores_the_requested_date_for_search_terms()
    {
        var suite = new Suite(new IgnoresFilters(Sample()));

        var act = () => suite.GetSearchTermsAsync_rows_belong_to_known_ad_groups_and_carry_non_negative_metrics();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Contract_fails_a_source_that_ignores_since_for_change_events()
    {
        var suite = new Suite(new IgnoresFilters(Sample()));

        var act = () => suite.GetChangeEventsAsync_rows_are_unique_well_formed_and_not_older_than_since();

        await act.Should().ThrowAsync<Exception>();
    }

    private sealed class Suite(IAdPlatformReadSource source) : AdPlatformReadSourceContractTests
    {
        protected override IAdPlatformReadSource CreateSource() => source;
        protected override string AccountExternalId => Account;
        protected override DateOnly FixtureDate => Date;
    }

    /// <summary>Models an adapter that drops the date / since filter: it returns every row the fake holds.</summary>
    private sealed class IgnoresFilters(IAdPlatformReadSource inner) : IAdPlatformReadSource
    {
        private static readonly DateOnly[] AllSampleDates = [Date, Date.AddDays(1)];

        public AdPlatform Platform => inner.Platform;
        public AdSourceCapabilities Capabilities => inner.Capabilities;

        public Task<IReadOnlyList<AdAccountSnapshot>> GetAccountsAsync(CancellationToken ct) => inner.GetAccountsAsync(ct);

        public Task<IReadOnlyList<AdEntitySnapshot>> GetEntitiesAsync(string accountExternalId, CancellationToken ct) =>
            inner.GetEntitiesAsync(accountExternalId, ct);

        public async Task<IReadOnlyList<AdDailyFactRow>> GetDailyFactsAsync(string accountExternalId, DateOnly date, CancellationToken ct)
        {
            var rows = new List<AdDailyFactRow>();
            foreach (var sampleDate in AllSampleDates)
                rows.AddRange(await inner.GetDailyFactsAsync(accountExternalId, sampleDate, ct));
            return rows;
        }

        public async Task<IReadOnlyList<AdSearchTermRow>> GetSearchTermsAsync(string accountExternalId, DateOnly date, CancellationToken ct)
        {
            var rows = new List<AdSearchTermRow>();
            foreach (var sampleDate in AllSampleDates)
                rows.AddRange(await inner.GetSearchTermsAsync(accountExternalId, sampleDate, ct));
            return rows;
        }

        public Task<IReadOnlyList<AdChangeEventRow>> GetChangeEventsAsync(string accountExternalId, DateTimeOffset since, CancellationToken ct) =>
            inner.GetChangeEventsAsync(accountExternalId, DateTimeOffset.MinValue, ct);
    }
}
