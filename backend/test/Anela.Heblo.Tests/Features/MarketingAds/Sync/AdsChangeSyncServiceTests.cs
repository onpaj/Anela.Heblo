using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Persistence.Ads.Entities;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdsChangeSyncServiceTests
{
    private static ScriptedAdReadSource Source(AdPlatform platform, bool changeLog, TimeSpan? maxAge = null,
        AdChangeActorKind actorKind = AdChangeActorKind.User)
    {
        var source = new ScriptedAdReadSource(platform, searchTerms: true, changeLog: changeLog, changeLogMaxAge: maxAge)
        {
            Accounts = { AdsTestData.Account() },
        };
        source.Entities[AdsTestData.AccountId] = new List<AdEntitySnapshot>
        {
            AdsTestData.Campaign("c-1"), AdsTestData.AdGroup("g-1", "c-1"), AdsTestData.Ad("ad-1", "g-1"),
        };
        source.Changes[AdsTestData.AccountId] = new List<AdChangeEventRow>
        {
            AdsTestData.Change("g-evt-1", AdsTestData.Now.AddHours(-3), AdEntityLevel.Ad, "ad-1", "{\"status\":\"PAUSED\"}",
                actor: "agency@x.cz", kind: actorKind),
        };
        return source;
    }

    [Fact]
    public async Task First_change_log_pull_reaches_back_the_initial_lookback_and_sets_the_watermark()
    {
        // Arrange
        var harness = new AdsSyncHarness();
        var google = Source(AdPlatform.GoogleAds, changeLog: true);

        // Act
        var report = await harness.Change(google).RunAsync(default);

        // Assert
        report.HasFailures.Should().BeFalse();
        google.ChangeRequests.Should().ContainSingle().Which.Since.Should().Be(AdsTestData.Now.AddDays(-7));
        await using var verify = harness.Verify();
        var change = await verify.ChangeEvents.SingleAsync();
        change.Source.Should().Be("PlatformChangeLog");
        change.Origin.Should().Be("OutOfBand");
        change.Actor.Should().Be("agency@x.cz");
        var state = await verify.SyncStates.SingleAsync(s => s.Stream == AdSyncStreams.ChangeEvents);
        state.Watermark.Should().Be(AdsTestData.Now.AddHours(-3));
    }

    [Fact]
    public async Task Next_pull_starts_at_the_newest_event_and_does_not_duplicate()
    {
        // Arrange
        var harness = new AdsSyncHarness();
        var google = Source(AdPlatform.GoogleAds, changeLog: true);
        await harness.Change(google).RunAsync(default);
        harness.Time.Advance(TimeSpan.FromHours(1));

        // Act
        await harness.Change(google).RunAsync(default);

        // Assert
        google.ChangeRequests[1].Since.Should().Be(AdsTestData.Now.AddHours(-3));
        await using var verify = harness.Verify();
        (await verify.ChangeEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Old_watermark_is_passed_to_the_source_unclamped()
    {
        // Arrange — job was off for 40 days; the source (not C2) owns the 30-day clamp
        var harness = new AdsSyncHarness();
        await harness.State.RecordSuccessAsync(AdPlatform.GoogleAds, AdsTestData.AccountId, AdSyncStreams.ChangeEvents,
            AdsTestData.Now.AddDays(-40), AdsTestData.Now.AddDays(-40), default);
        var google = Source(AdPlatform.GoogleAds, changeLog: true, maxAge: TimeSpan.FromDays(30));

        // Act
        await harness.Change(google).RunAsync(default);

        // Assert
        google.ChangeRequests.Single().Since.Should().Be(AdsTestData.Now.AddDays(-40));
    }

    [Fact]
    public async Task Empty_pull_keeps_the_watermark()
    {
        // Arrange
        var watermark = AdsTestData.Now.AddDays(-2);
        var harness = new AdsSyncHarness();
        await harness.State.RecordSuccessAsync(AdPlatform.GoogleAds, AdsTestData.AccountId, AdSyncStreams.ChangeEvents,
            AdsTestData.Now.AddDays(-2), watermark, default);
        var google = Source(AdPlatform.GoogleAds, changeLog: true);
        google.Changes[AdsTestData.AccountId].Clear();

        // Act
        var report = await harness.Change(google).RunAsync(default);

        // Assert
        report.HasFailures.Should().BeFalse();
        await using var verify = harness.Verify();
        var state = await verify.SyncStates.SingleAsync(s => s.Stream == AdSyncStreams.ChangeEvents);
        state.Watermark.Should().Be(watermark);
        state.Status.Should().Be(AdsDbValues.StatusSucceeded);
    }

    [Fact]
    public async Task Watermark_is_the_newest_event_even_when_rows_are_unsorted()
    {
        // Arrange — newest first
        var harness = new AdsSyncHarness();
        var google = Source(AdPlatform.GoogleAds, changeLog: true);
        google.Changes[AdsTestData.AccountId] = new List<AdChangeEventRow>
        {
            AdsTestData.Change("g-evt-2", AdsTestData.Now.AddHours(-1), AdEntityLevel.Ad, "ad-1", "{\"status\":\"ENABLED\"}"),
            AdsTestData.Change("g-evt-1", AdsTestData.Now.AddHours(-3), AdEntityLevel.Ad, "ad-1", "{\"status\":\"PAUSED\"}"),
        };

        // Act
        await harness.Change(google).RunAsync(default);

        // Assert
        await using var verify = harness.Verify();
        var state = await verify.SyncStates.SingleAsync(s => s.Stream == AdSyncStreams.ChangeEvents);
        state.Watermark.Should().Be(AdsTestData.Now.AddHours(-1));
    }

    [Fact]
    public async Task Event_at_the_watermark_is_reread_and_written_once()
    {
        // Arrange — the watermark is exactly the event's time, so the event sits on the inclusive boundary
        var harness = new AdsSyncHarness();
        var eventTime = AdsTestData.Now.AddHours(-3);
        await harness.State.RecordSuccessAsync(AdPlatform.GoogleAds, AdsTestData.AccountId, AdSyncStreams.ChangeEvents,
            eventTime, eventTime, default);
        var google = Source(AdPlatform.GoogleAds, changeLog: true);

        // Act
        await harness.Change(google).RunAsync(default);
        await harness.Change(google).RunAsync(default);

        // Assert
        google.ChangeRequests.Should().OnlyContain(r => r.Since == eventTime).And.HaveCount(2);
        await using var verify = harness.Verify();
        (await verify.ChangeEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Source_without_change_log_falls_back_to_snapshot_diff()
    {
        // Arrange
        var harness = new AdsSyncHarness();
        var sklik = Source(AdPlatform.Sklik, changeLog: false);
        await harness.Change(sklik).RunAsync(default);
        sklik.Entities[AdsTestData.AccountId][2] = AdsTestData.Ad("ad-1", "g-1", AdEntityStatus.Paused);
        harness.Time.Advance(TimeSpan.FromHours(1));

        // Act
        await harness.Change(sklik).RunAsync(default);

        // Assert
        sklik.ChangeRequests.Should().BeEmpty();
        await using var verify = harness.Verify();
        var change = await verify.ChangeEvents.SingleAsync();
        change.Source.Should().Be("SnapshotDiff");
        change.OccurredAt.Should().Be(harness.Time.GetUtcNow());
    }

    [Fact]
    public async Task Failing_change_log_does_not_block_another_platform()
    {
        // Arrange
        var harness = new AdsSyncHarness();
        var google = Source(AdPlatform.GoogleAds, changeLog: true);
        google.FailChangesFor.Add(AdsTestData.AccountId);
        var meta = Source(AdPlatform.MetaAds, changeLog: true);

        // Act
        var report = await harness.Change(google, meta).RunAsync(default);

        // Assert
        report.Errors.Should().ContainSingle().Which.Should().Contain("GoogleAds").And.Contain("ChangeEvents");
        await using var verify = harness.Verify();
        (await verify.ChangeEvents.CountAsync()).Should().Be(1);
        var googleState = await verify.SyncStates.SingleAsync(s => s.Platform == "GoogleAds" && s.Stream == AdSyncStreams.ChangeEvents);
        googleState.Watermark.Should().BeNull();
    }

    [Fact]
    public async Task Heblo_executed_change_is_classified_as_heblo()
    {
        // Arrange
        var harness = new AdsSyncHarness();
        var lookup = new Mock<IAdExecutionLookup>();
        lookup.Setup(l => l.GetExecutedActionsAsync(AdPlatform.GoogleAds, AdsTestData.AccountId,
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AdExecutedAction("5", AdActionType.PauseAd, AdPlatform.GoogleAds, AdsTestData.AccountId, AdEntityLevel.Ad, "ad-1",
                    null, AdsTestData.Now.AddHours(-3).AddMinutes(-5), new Dictionary<string, string>()),
            });
        harness.Lookup = lookup.Object;

        // Act
        await harness.Change(Source(AdPlatform.GoogleAds, changeLog: true, actorKind: AdChangeActorKind.Heblo)).RunAsync(default);

        // Assert
        await using var verify = harness.Verify();
        var change = await verify.ChangeEvents.SingleAsync();
        change.Origin.Should().Be("Heblo");
        change.MatchedExecutionId.Should().Be("5");
    }
}
