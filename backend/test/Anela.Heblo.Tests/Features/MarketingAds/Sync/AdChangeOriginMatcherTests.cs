using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdChangeOriginMatcherTests
{
    private static readonly DateTimeOffset ExecutedAt = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly AdChangeOriginMatcher Matcher = new();
    private static readonly AdOriginMatchingRules Rules =
        new(TimeSpan.FromHours(2));

    private static AdExecutedAction PauseAd(string id = "7", string adId = "ad-1", DateTimeOffset? at = null) =>
        new(id, AdActionType.PauseAd, AdPlatform.GoogleAds, "acc", AdEntityLevel.Ad, adId, null,
            at ?? ExecutedAt, new Dictionary<string, string>());

    private static AdExecutedAction AddNegative(string? resourceId, string text = "zdarma") =>
        new("9", AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, "acc", AdEntityLevel.Campaign, "camp-1", resourceId,
            ExecutedAt, new Dictionary<string, string> { [AdActionPayloadKeys.Text] = text, [AdActionPayloadKeys.MatchType] = "Exact" });

    private static AdChangeEventCandidate Change(string? entityId, string? newValueJson, DateTimeOffset? at = null,
        string? actor = null, AdEntityLevel? level = AdEntityLevel.Ad) =>
        new("evt", at ?? ExecutedAt.AddMinutes(30), actor, AdChangeActorKind.Unknown, level, entityId, "Status", null, newValueJson);

    [Fact]
    public void Without_executions_everything_is_out_of_band()
    {
        Matcher.Match(Change("ad-1", "\"Paused\""), Array.Empty<AdExecutedAction>(), Rules)
            .Should().Be(AdOriginMatch.OutOfBand);
    }

    [Fact]
    public void Snapshot_status_change_to_paused_matches_a_pause_executed_half_an_hour_earlier()
    {
        var match = Matcher.Match(Change("ad-1", "\"Paused\""), new[] { PauseAd() }, Rules);

        match.Should().Be(new AdOriginMatch(AdChangeOrigin.Heblo, "7"));
    }

    [Fact]
    public void Platform_object_values_match_case_insensitively()
    {
        var match = Matcher.Match(Change("ad-1", "{\"resource\":{\"status\":\"PAUSED\"}}"), new[] { PauseAd() }, Rules);

        match.Origin.Should().Be(AdChangeOrigin.Heblo);
    }

    [Fact]
    public void Change_outside_the_two_hour_window_is_out_of_band()
    {
        var match = Matcher.Match(Change("ad-1", "\"Paused\"", at: ExecutedAt.AddHours(3)), new[] { PauseAd() }, Rules);

        match.Should().Be(AdOriginMatch.OutOfBand);
    }

    [Fact]
    public void Change_on_another_entity_is_out_of_band()
    {
        Matcher.Match(Change("ad-2", "\"Paused\""), new[] { PauseAd() }, Rules).Should().Be(AdOriginMatch.OutOfBand);
    }

    [Fact]
    public void Re_enabling_the_ad_matches_a_heblo_revert_of_the_pause()
    {
        var revert = PauseAd(id: "8") with { IsRevert = true };

        var match = Matcher.Match(Change("ad-1", "\"Enabled\""), new[] { revert }, Rules);

        match.Should().Be(new AdOriginMatch(AdChangeOrigin.Heblo, "8"));
    }

    [Fact]
    public void Re_enabling_the_ad_is_not_the_same_change()
    {
        Matcher.Match(Change("ad-1", "\"Enabled\""), new[] { PauseAd() }, Rules).Should().Be(AdOriginMatch.OutOfBand);
    }

    [Fact]
    public void Google_ad_pause_matches_by_ad_group_ad_id()
    {
        var match = Matcher.Match(
            Change("222~333", "{\"adGroupAd\":{\"status\":\"PAUSED\"}}"),
            new[] { PauseAd(adId: "222~333") }, Rules);

        match.Should().Be(new AdOriginMatch(AdChangeOrigin.Heblo, "7"));
    }

    [Fact]
    public void Google_negative_keyword_create_matches_the_executed_criterion_resource_name()
    {
        var match = Matcher.Match(
            Change("campaignCriteria/111~555",
                "{\"campaignCriterion\":{\"keyword\":{\"text\":\"zdarma\",\"matchType\":\"EXACT\"},\"negative\":true}}",
                level: AdEntityLevel.NegativeKeyword),
            new[] { AddNegative("customers/1234567890/campaignCriteria/111~555") }, Rules);

        match.Should().Be(new AdOriginMatch(AdChangeOrigin.Heblo, "9"));
    }

    [Fact]
    public void Negative_keyword_matches_a_bare_criterion_id_by_suffix()
    {
        var match = Matcher.Match(
            Change("55~66", "{\"campaignCriterion\":{\"negative\":true}}", level: null),
            new[] { AddNegative("customers/1/campaignCriteria/55~66") }, Rules);

        match.Origin.Should().Be(AdChangeOrigin.Heblo);
    }

    [Fact]
    public void Same_id_on_another_level_is_out_of_band()
    {
        Matcher.Match(Change("ad-1", "\"Paused\"", level: AdEntityLevel.AdGroup), new[] { PauseAd() }, Rules)
            .Should().Be(AdOriginMatch.OutOfBand);
    }

    [Fact]
    public void User_or_platform_automation_actor_is_out_of_band_even_if_everything_else_matches()
    {
        foreach (var kind in new[] { AdChangeActorKind.User, AdChangeActorKind.PlatformAutomation })
        {
            var change = Change("ad-1", "\"Paused\"") with { ActorKind = kind, Actor = "specialist@agency.cz" };

            Matcher.Match(change, new[] { PauseAd() }, Rules).Should().Be(AdOriginMatch.OutOfBand, kind.ToString());
        }
    }

    [Fact]
    public void Heblo_or_unknown_actor_kind_can_match()
    {
        foreach (var kind in new[] { AdChangeActorKind.Heblo, AdChangeActorKind.Unknown })
        {
            var change = Change("ad-1", "\"Paused\"") with { ActorKind = kind };

            Matcher.Match(change, new[] { PauseAd() }, Rules).Origin.Should().Be(AdChangeOrigin.Heblo, kind.ToString());
        }
    }

    [Fact]
    public void Nearest_execution_in_time_wins()
    {
        var far = PauseAd(id: "1", at: ExecutedAt.AddMinutes(-60));
        var near = PauseAd(id: "2", at: ExecutedAt.AddMinutes(25));

        Matcher.Match(Change("ad-1", "\"Paused\""), new[] { far, near }, Rules).MatchedExecutionId.Should().Be("2");
    }

    [Fact]
    public void Candidate_from_row_is_utc_and_json_normalised()
    {
        // Arrange
        var row = new AdChangeEventRow("e1", new DateTimeOffset(2026, 10, 6, 11, 0, 0, TimeSpan.FromHours(2)), "x",
            AdChangeActorKind.Heblo, AdEntityLevel.Ad, "ad-1", "StatusChanged", "ENABLED", "PAUSED");

        // Act
        var candidate = AdChangeEventCandidate.FromRow(row);

        // Assert
        candidate.OccurredAt.Offset.Should().Be(TimeSpan.Zero);
        candidate.OccurredAt.Should().Be(row.OccurredAt);
        candidate.NewValueJson.Should().Be("\"PAUSED\"");
        Matcher.Match(candidate, new[] { PauseAd(at: candidate.OccurredAt) }, Rules).Origin.Should().Be(AdChangeOrigin.Heblo);
    }
}
