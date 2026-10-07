using System.Text.Json;
using System.Text.RegularExpressions;
using Anela.Heblo.Adapters.GoogleAds.Reporting;
using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Reporting;

public sealed class GoogleAdsReadSourceChangeEventsTests
{
    private static readonly Regex LowerBound = new(@"change_date_time >= '([^']+)'");
    private static readonly Regex UpperBound = new(@"change_date_time <= '([^']+)'");
    private static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    [Fact]
    public async Task maps_actor_entity_and_change_of_each_event()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var events = await source.GetChangeEventsAsync(
            TestSettings.CustomerId, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        events.Should().HaveCount(3);
        var recommendation = events[0];
        recommendation.ExternalEventId.Should().Be("1759648500123456~0~0");
        recommendation.ActorKind.Should().Be(AdChangeActorKind.PlatformAutomation);
        recommendation.Actor.Should().BeNull();
        recommendation.EntityLevel.Should().Be(AdEntityLevel.Campaign);
        recommendation.EntityExternalId.Should().Be("111");
        recommendation.ChangeType.Should().Be("UPDATE:CAMPAIGN");
        JsonDocument.Parse(recommendation.NewValueJson!).RootElement
            .GetProperty("campaign").GetProperty("biddingStrategyType").GetString().Should().Be("MAXIMIZE_CONVERSIONS");

        var pause = events[1];
        (pause.ActorKind, pause.Actor, pause.EntityLevel, pause.EntityExternalId)
            .Should().Be((AdChangeActorKind.User, "specialist@agency.example", (AdEntityLevel?)AdEntityLevel.Ad, "221~441"));

        var heblo = events[2];
        (heblo.ActorKind, heblo.EntityLevel, heblo.EntityExternalId, heblo.ChangeType)
            .Should().Be((AdChangeActorKind.Heblo, (AdEntityLevel?)AdEntityLevel.NegativeKeyword, "adGroupCriteria/221~341", "CREATE:AD_GROUP_CRITERION"));
        heblo.OldValueJson.Should().BeNull();
    }

    [Fact]
    public async Task converts_account_local_times_to_utc()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var events = await source.GetChangeEventsAsync(
            TestSettings.CustomerId, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        events[0].OccurredAt.Should().Be(new DateTimeOffset(2026, 10, 5, 7, 15, 0, TimeSpan.Zero).AddTicks(1_234_560));
        events[1].OccurredAt.Should().Be(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        events[2].OccurredAt.Should().Be(new DateTimeOffset(2026, 10, 6, 6, 30, 0, 500, TimeSpan.Zero));
    }

    [Fact]
    public async Task queries_from_since_to_now_in_account_time_and_drops_older_events()
    {
        var api = new FixtureGoogleAdsApiClient();
        var (source, _) = ReadSourceHarness.Create(api);
        var since = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

        var events = await source.GetChangeEventsAsync(TestSettings.CustomerId, since, CancellationToken.None);

        events.Select(e => e.ExternalEventId).Should().Equal("1759665600000000~0~0", "1759732200000000~1~0");
        var gaql = api.Calls.Single(c => c.Query.Name == "change_events").Query.Gaql;
        LowerBound.Match(gaql).Groups[1].Value.Should().Be("2026-10-05 12:00:00");
        UpperBound.Match(gaql).Groups[1].Value.Should().Be("2026-10-07 08:00:00");
        gaql.Should().MatchRegex(@"LIMIT 10000$");
    }

    [Fact]
    public async Task clamps_a_watermark_older_than_the_30_day_window()
    {
        var api = new FixtureGoogleAdsApiClient();
        var (source, _) = ReadSourceHarness.Create(api);

        var events = await source.GetChangeEventsAsync(
            TestSettings.CustomerId, new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        events.Should().HaveCount(4); // 3 recent events + the 2026-09-25 one the 30-day floor still reaches
        var gaql = api.Calls.Single(c => c.Query.Name == "change_events").Query.Gaql;
        // now − 30 days + 1 hour = 2026-09-07T07:00Z = 09:00 in Prague (CEST)
        LowerBound.Match(gaql).Groups[1].Value.Should().Be("2026-09-07 09:00:00");
    }

    [Fact]
    public async Task a_watermark_in_the_future_returns_nothing_without_querying_change_events()
    {
        var api = new FixtureGoogleAdsApiClient();
        var (source, _) = ReadSourceHarness.Create(api);

        var events = await source.GetChangeEventsAsync(
            TestSettings.CustomerId, TestSettings.Now.AddMinutes(5), CancellationToken.None);

        events.Should().BeEmpty();
        api.Calls.Should().NotContain(c => c.Query.Name == "change_events");
    }

    [Fact]
    public void converts_winter_time_too()
    {
        GoogleAdsChangeEventMapper.ToUtc("2026-12-01 10:00:00", Prague)
            .Should().Be(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero));
        GoogleAdsChangeEventMapper.ToAccountLocal(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero), Prague)
            .Should().Be("2026-12-01 10:00:00");
    }

    [Fact]
    public void a_targeting_criterion_change_keeps_its_row_but_has_no_keyword_entity()
    {
        using var campaignLocation = JsonDocument.Parse("""
            { "changeEvent": {
                "resourceName": "customers/1234567890/changeEvents/1759700000000000~0~0",
                "changeDateTime": "2026-10-05 20:00:00",
                "changeResourceType": "CAMPAIGN_CRITERION",
                "changeResourceName": "customers/1234567890/campaignCriteria/111~2203",
                "clientType": "GOOGLE_ADS_WEB_CLIENT",
                "userEmail": "specialist@agency.example",
                "resourceChangeOperation": "CREATE",
                "newResource": { "campaignCriterion": { "type": "LOCATION", "location": { "geoTargetConstant": "geoTargetConstants/2203" } } }
            } }
            """);

        var row = GoogleAdsChangeEventMapper.Map(campaignLocation.RootElement, Prague, null);

        (row.EntityLevel, row.EntityExternalId).Should().Be(((AdEntityLevel?)null, (string?)null));
        row.ChangeType.Should().Be("CREATE:CAMPAIGN_CRITERION");
        row.ActorKind.Should().Be(AdChangeActorKind.User);
        row.NewValueJson.Should().Contain("geoTargetConstants/2203");
    }

    [Theory]
    [InlineData("adGroupCriterion", "adGroupCriteria/221~9", "{ \"type\": \"AGE_RANGE\", \"ageRange\": { \"type\": \"AGE_RANGE_25_34\" } }")]
    [InlineData("adGroupCriterion", "adGroupCriteria/221~9", "{ \"userList\": { \"userList\": \"customers/1/userLists/5\" } }")]
    [InlineData("campaignCriterion", "campaignCriteria/111~9", "{ \"device\": { \"type\": \"MOBILE\" } }")]
    [InlineData("campaignCriterion", "campaignCriteria/111~9", "{ \"language\": { \"languageConstant\": \"languageConstants/1021\" } }")]
    public void other_non_keyword_criteria_have_no_entity(string objectName, string collectionAndId, string criterion)
    {
        using var document = JsonDocument.Parse(CriterionEvent(objectName, collectionAndId, $"{{ \"{objectName}\": {criterion} }}"));

        var row = GoogleAdsChangeEventMapper.Map(document.RootElement, Prague, null);

        (row.EntityLevel, row.EntityExternalId).Should().Be(((AdEntityLevel?)null, (string?)null));
    }

    [Fact]
    public void a_keyword_typed_criterion_and_an_untyped_update_keep_their_entity()
    {
        using var typedKeyword = JsonDocument.Parse(CriterionEvent("adGroupCriterion", "adGroupCriteria/221~341",
            "{ \"adGroupCriterion\": { \"type\": \"KEYWORD\", \"keyword\": { \"text\": \"krém\" } } }"));
        using var untypedUpdate = JsonDocument.Parse(CriterionEvent("adGroupCriterion", "adGroupCriteria/221~341",
            "{ \"adGroupCriterion\": { \"status\": \"PAUSED\" } }"));

        GoogleAdsChangeEventMapper.Map(typedKeyword.RootElement, Prague, null)
            .Should().Match<AdChangeEventRow>(r => r.EntityLevel == AdEntityLevel.Keyword && r.EntityExternalId == "221~341");
        GoogleAdsChangeEventMapper.Map(untypedUpdate.RootElement, Prague, null)
            .Should().Match<AdChangeEventRow>(r => r.EntityLevel == AdEntityLevel.Keyword && r.EntityExternalId == "221~341");
    }

    private static string CriterionEvent(string objectName, string collectionAndId, string newResource) => $$"""
        { "changeEvent": {
            "resourceName": "customers/1234567890/changeEvents/1759700000000000~0~0",
            "changeDateTime": "2026-10-05 20:00:00",
            "changeResourceType": "{{(objectName == "adGroupCriterion" ? "AD_GROUP_CRITERION" : "CAMPAIGN_CRITERION")}}",
            "changeResourceName": "customers/1234567890/{{collectionAndId}}",
            "clientType": "GOOGLE_ADS_WEB_CLIENT",
            "resourceChangeOperation": "UPDATE",
            "newResource": {{newResource}}
        } }
        """;
}
