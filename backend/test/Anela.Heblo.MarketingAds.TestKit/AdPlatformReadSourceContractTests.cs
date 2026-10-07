using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.MarketingAds.TestKit;

/// <summary>
/// Cross-platform semantics every IAdPlatformReadSource must satisfy (spec 4.2, 4.4, 12.3). A
/// platform test project derives from this class, wires CreateSource() to recorded JSON fixtures and
/// inherits every [Fact]. Never override or skip a fact: a platform that cannot meet one is a design
/// question for the core, not a test to silence. The fixture must contain at least one entity, one
/// daily fact on FixtureDate, and — when advertised — one search term on FixtureDate and one change
/// event on or after <see cref="ChangeEventsSince"/>.
/// </summary>
public abstract class AdPlatformReadSourceContractTests
{
    public const int ChangeEventsLookbackDays = 7;
    private const string CurrencyPattern = "^[A-Z]{3}$";

    protected abstract IAdPlatformReadSource CreateSource();
    protected abstract string AccountExternalId { get; }
    protected abstract DateOnly FixtureDate { get; }

    protected DateTimeOffset ChangeEventsSince =>
        new(FixtureDate.AddDays(-ChangeEventsLookbackDays).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    [Fact]
    public void Platform_is_a_defined_AdPlatform()
    {
        Enum.IsDefined(CreateSource().Platform).Should().BeTrue();
    }

    [Fact]
    public void Capabilities_report_a_change_log_max_age_only_with_a_change_log_and_only_a_positive_one()
    {
        var capabilities = CreateSource().Capabilities;

        capabilities.Should().NotBeNull();
        if (!capabilities.ChangeLog)
        {
            capabilities.ChangeLogMaxAge.Should().BeNull();
            return;
        }

        if (capabilities.ChangeLogMaxAge is { } maxAge)
            maxAge.Should().BePositive();
    }

    [Fact]
    public async Task GetAccountsAsync_returns_the_fixture_account_with_every_identity_field_set()
    {
        var accounts = await CreateSource().GetAccountsAsync(CancellationToken.None);

        accounts.Should().Contain(a => a.ExternalId == AccountExternalId);
        accounts.Should().AllSatisfy(a =>
        {
            a.ExternalId.Should().NotBeNullOrWhiteSpace();
            a.Name.Should().NotBeNullOrWhiteSpace();
            a.TimeZone.Should().NotBeNullOrWhiteSpace();
            a.Currency.Should().MatchRegex(CurrencyPattern);
        });
    }

    [Fact]
    public async Task GetEntitiesAsync_returns_entities_with_defined_levels_statuses_and_non_empty_ids()
    {
        var entities = await CreateSource().GetEntitiesAsync(AccountExternalId, CancellationToken.None);

        entities.Should().NotBeEmpty();
        entities.Should().AllSatisfy(e =>
        {
            Enum.IsDefined(e.Level).Should().BeTrue();
            Enum.IsDefined(e.Status).Should().BeTrue();
            e.ExternalId.Should().NotBeNullOrWhiteSpace();
            e.Name.Should().NotBeNull();
            e.Attributes.Should().NotBeNull();
        });
    }

    [Fact]
    public async Task GetEntitiesAsync_external_ids_are_unique_within_each_level()
    {
        // ads.ad_entities is unique on (account_id, level, external_id): platforms whose ids are only
        // unique within a parent (Google keyword criteria) must emit composite ids.
        var entities = await CreateSource().GetEntitiesAsync(AccountExternalId, CancellationToken.None);

        entities.Select(e => (e.Level, e.ExternalId)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task GetEntitiesAsync_campaigns_have_no_parent_and_every_other_entity_has_a_parent_in_the_same_snapshot()
    {
        var entities = await CreateSource().GetEntitiesAsync(AccountExternalId, CancellationToken.None);
        var keys = entities.Select(e => (e.Level, e.ExternalId)).ToHashSet();

        entities.Should().AllSatisfy(e =>
        {
            if (e.Level == AdEntityLevel.Campaign)
            {
                e.ParentLevel.Should().BeNull();
                e.ParentExternalId.Should().BeNull();
                return;
            }

            e.ParentLevel.Should().NotBeNull();
            e.ParentExternalId.Should().NotBeNullOrWhiteSpace();
            keys.Should().Contain((e.ParentLevel!.Value, e.ParentExternalId!));
        });
    }

    [Fact]
    public async Task GetDailyFactsAsync_returns_rows_for_the_requested_date_with_non_negative_metrics_and_a_currency()
    {
        var facts = await CreateSource().GetDailyFactsAsync(AccountExternalId, FixtureDate, CancellationToken.None);

        facts.Should().NotBeEmpty();
        facts.Should().AllSatisfy(f =>
        {
            Enum.IsDefined(f.Level).Should().BeTrue();
            f.EntityExternalId.Should().NotBeNullOrWhiteSpace();
            f.Date.Should().Be(FixtureDate);
            f.Impressions.Should().BeGreaterThanOrEqualTo(0);
            f.Clicks.Should().BeGreaterThanOrEqualTo(0);
            f.Cost.Should().BeGreaterThanOrEqualTo(0m);
            f.Conversions.Should().BeGreaterThanOrEqualTo(0m);
            f.ConversionValue.Should().BeGreaterThanOrEqualTo(0m);
            f.Currency.Should().MatchRegex(CurrencyPattern);
        });
    }

    [Fact]
    public async Task GetDailyFactsAsync_returns_at_most_one_row_per_entity_for_the_date()
    {
        // ad_daily_facts is keyed (entity_id, date); a second row for the same entity would violate it.
        var facts = await CreateSource().GetDailyFactsAsync(AccountExternalId, FixtureDate, CancellationToken.None);

        facts.Select(f => (f.Level, f.EntityExternalId)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task GetDailyFactsAsync_rows_reference_entities_returned_by_GetEntitiesAsync()
    {
        // The core maps (level, external id) to ad_entities.id; a fact with no entity cannot be stored.
        var source = CreateSource();
        var entities = await source.GetEntitiesAsync(AccountExternalId, CancellationToken.None);
        var keys = entities.Select(e => (e.Level, e.ExternalId)).ToHashSet();

        var facts = await source.GetDailyFactsAsync(AccountExternalId, FixtureDate, CancellationToken.None);

        facts.Should().AllSatisfy(f => keys.Should().Contain((f.Level, f.EntityExternalId)));
    }

    [Fact]
    public async Task GetSearchTermsAsync_honours_the_SearchTerms_capability()
    {
        var source = CreateSource();

        var rows = await source.GetSearchTermsAsync(AccountExternalId, FixtureDate, CancellationToken.None);

        if (source.Capabilities.SearchTerms)
            rows.Should().NotBeEmpty("a source advertising search terms must deliver them for the fixture date");
        else
            rows.Should().BeEmpty("an unsupported capability returns an empty list, never throws");
    }

    [Fact]
    public async Task GetSearchTermsAsync_rows_belong_to_known_ad_groups_and_carry_non_negative_metrics()
    {
        var source = CreateSource();
        var adGroups = (await source.GetEntitiesAsync(AccountExternalId, CancellationToken.None))
            .Where(e => e.Level == AdEntityLevel.AdGroup)
            .Select(e => e.ExternalId)
            .ToHashSet();

        var rows = await source.GetSearchTermsAsync(AccountExternalId, FixtureDate, CancellationToken.None);

        // FluentAssertions' AllSatisfy fails on an empty collection; emptiness is judged by the
        // capability fact above, so a source without the capability legitimately has nothing to check.
        if (rows.Count == 0)
            return;

        rows.Should().AllSatisfy(r =>
        {
            adGroups.Should().Contain(r.AdGroupExternalId);
            r.Date.Should().Be(FixtureDate);
            r.SearchTerm.Should().NotBeNullOrWhiteSpace();
            if (r.MatchType is { } matchType)
                Enum.IsDefined(matchType).Should().BeTrue();
            r.Impressions.Should().BeGreaterThanOrEqualTo(0);
            r.Clicks.Should().BeGreaterThanOrEqualTo(0);
            r.Cost.Should().BeGreaterThanOrEqualTo(0m);
            r.Conversions.Should().BeGreaterThanOrEqualTo(0m);
            r.ConversionValue.Should().BeGreaterThanOrEqualTo(0m);
            r.Currency.Should().MatchRegex(CurrencyPattern);
        });
    }

    [Fact]
    public async Task GetSearchTermsAsync_returns_at_most_one_row_per_ad_group_term_and_match_type()
    {
        // ad_search_terms is keyed (ad_group_entity_id, date, search_term, match_type); a null match
        // type is stored as "Unknown", distinct from every enum name, so the nullable compares as-is.
        var rows = await CreateSource().GetSearchTermsAsync(AccountExternalId, FixtureDate, CancellationToken.None);

        if (rows.Count == 0)
            return;

        rows.Select(r => (r.AdGroupExternalId, r.SearchTerm, r.MatchType)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task GetChangeEventsAsync_honours_the_ChangeLog_capability()
    {
        var source = CreateSource();

        var rows = await source.GetChangeEventsAsync(AccountExternalId, ChangeEventsSince, CancellationToken.None);

        if (source.Capabilities.ChangeLog)
            rows.Should().NotBeEmpty("a source advertising a change log must deliver the fixture's events");
        else
            rows.Should().BeEmpty("an unsupported capability returns an empty list, never throws");
    }

    [Fact]
    public async Task GetChangeEventsAsync_rows_are_unique_well_formed_and_not_older_than_since()
    {
        var rows = await CreateSource().GetChangeEventsAsync(AccountExternalId, ChangeEventsSince, CancellationToken.None);

        rows.Select(r => r.ExternalEventId).Should().OnlyHaveUniqueItems();

        // See the search-terms fact: AllSatisfy fails on an empty collection; the capability fact owns emptiness.
        if (rows.Count == 0)
            return;

        rows.Should().AllSatisfy(r =>
        {
            r.ExternalEventId.Should().NotBeNullOrWhiteSpace();
            r.ChangeType.Should().NotBeNullOrWhiteSpace();
            r.OccurredAt.Should().BeOnOrAfter(ChangeEventsSince);
            Enum.IsDefined(r.ActorKind).Should().BeTrue();
            (r.EntityLevel is null).Should().Be(r.EntityExternalId is null,
                "an entity reference needs both its level and its external id");
            ShouldBeJsonOrNull(r.OldValueJson);
            ShouldBeJsonOrNull(r.NewValueJson);
        });
    }

    private static void ShouldBeJsonOrNull(string? json)
    {
        if (json is null)
            return;

        var parse = () => JsonDocument.Parse(json).Dispose();
        parse.Should().NotThrow("old/new values are stored in jsonb columns");
    }
}
