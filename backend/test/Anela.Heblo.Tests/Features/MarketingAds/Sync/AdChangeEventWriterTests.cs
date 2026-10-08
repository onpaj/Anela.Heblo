using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Persistence.Ads.Entities;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdChangeEventWriterTests
{
    private static async Task<(AdsSyncHarness Harness, AdAccount Account, IReadOnlyDictionary<AdEntityKey, long> Ids)> ArrangeAsync()
    {
        var harness = new AdsSyncHarness();
        var (accountId, ids) = await harness.SeedTreeAsync();
        await using var read = harness.Verify();
        var account = await read.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId);
        return (harness, account, ids);
    }

    private static AdChangeEventCandidate Candidate(string id, string? newValue = "\"Paused\"", AdEntityLevel? level = AdEntityLevel.Ad,
        string? entity = "ad-1", DateTimeOffset? at = null, AdChangeActorKind kind = AdChangeActorKind.User) =>
        AdChangeEventCandidate.FromRow(AdsTestData.Change(id, at ?? AdsTestData.Now.AddHours(-2), level, entity, newValue, kind: kind));

    [Fact]
    public async Task Writes_events_out_of_band_with_entity_resolved()
    {
        var (harness, account, ids) = await ArrangeAsync();

        var written = await harness.Writer.WriteAsync(AdPlatform.GoogleAds, account, AdChangeSource.PlatformChangeLog, new[] { Candidate("e1") }, ids, default);

        written.Should().Be(1);
        await using var verify = harness.Verify();
        var row = await verify.ChangeEvents.SingleAsync();
        row.Origin.Should().Be("OutOfBand");
        row.Source.Should().Be("PlatformChangeLog");
        row.EntityId.Should().Be(ids[new AdEntityKey(AdEntityLevel.Ad, "ad-1")]);
        row.EntityLevel.Should().Be("Ad");
        row.EntityExternalRef.Should().Be("ad-1");
        row.MatchedExecutionId.Should().BeNull();
    }

    [Fact]
    public async Task Already_stored_and_repeated_ids_are_written_once()
    {
        var (harness, account, ids) = await ArrangeAsync();
        await harness.Writer.WriteAsync(AdPlatform.GoogleAds, account, AdChangeSource.PlatformChangeLog, new[] { Candidate("e1") }, ids, default);

        var written = await harness.Writer.WriteAsync(AdPlatform.GoogleAds, account, AdChangeSource.PlatformChangeLog,
            new[] { Candidate("e1"), Candidate("e2"), Candidate("e2") }, ids, default);

        written.Should().Be(1);
        await using var verify = harness.Verify();
        (await verify.ChangeEvents.Select(e => e.ExternalEventId).ToListAsync()).Should().BeEquivalentTo(new[] { "e1", "e2" });
    }

    [Fact]
    public async Task Rewriting_only_already_stored_events_writes_nothing()
    {
        var (harness, account, ids) = await ArrangeAsync();
        var batch = new[] { Candidate("e1"), Candidate("e2") };
        await harness.Writer.WriteAsync(AdPlatform.GoogleAds, account, AdChangeSource.PlatformChangeLog, batch, ids, default);

        var written = await harness.Writer.WriteAsync(AdPlatform.GoogleAds, account, AdChangeSource.PlatformChangeLog, batch, ids, default);

        written.Should().Be(0);
        await using var verify = harness.Verify();
        (await verify.ChangeEvents.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Same_external_id_from_another_source_is_a_separate_event()
    {
        var (harness, account, ids) = await ArrangeAsync();
        await harness.Writer.WriteAsync(AdPlatform.GoogleAds, account, AdChangeSource.PlatformChangeLog, new[] { Candidate("e1") }, ids, default);

        var written = await harness.Writer.WriteAsync(AdPlatform.GoogleAds, account, AdChangeSource.SnapshotDiff, new[] { Candidate("e1") }, ids, default);

        written.Should().Be(1);
    }

    [Fact]
    public async Task Stores_occurred_at_in_utc()
    {
        var (harness, account, ids) = await ArrangeAsync();
        var local = new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(2));

        await harness.Writer.WriteAsync(AdPlatform.GoogleAds, account, AdChangeSource.PlatformChangeLog, new[] { Candidate("e1", at: local) }, ids, default);

        await using var verify = harness.Verify();
        var stored = (await verify.ChangeEvents.SingleAsync()).OccurredAt;
        stored.Offset.Should().Be(TimeSpan.Zero);
        stored.Should().Be(local);
    }

    [Fact]
    public async Task Non_json_values_are_stored_as_json_strings()
    {
        var (harness, account, ids) = await ArrangeAsync();

        await harness.Writer.WriteAsync(AdPlatform.Sklik, account, AdChangeSource.PlatformChangeLog, new[] { Candidate("e1", newValue: "PAUSED") }, ids, default);

        await using var verify = harness.Verify();
        JsonDocument.Parse((await verify.ChangeEvents.SingleAsync()).NewValueJson!).RootElement.GetString().Should().Be("PAUSED");
    }

    [Fact]
    public async Task Ambiguous_external_id_without_level_keeps_only_the_reference()
    {
        var (harness, account, _) = await ArrangeAsync();
        var ids = (await harness.Entities.UpsertAsync(account.Id,
            new[] { AdsTestData.Campaign("x"), AdsTestData.AdGroup("x", "x") }, false, default)).EntityIds;

        await harness.Writer.WriteAsync(AdPlatform.Sklik, account, AdChangeSource.PlatformChangeLog,
            new[] { Candidate("e1", level: null, entity: "x") }, ids, default);

        await using var verify = harness.Verify();
        var row = await verify.ChangeEvents.SingleAsync();
        row.EntityId.Should().BeNull();
        row.EntityLevel.Should().BeNull();
        row.EntityExternalRef.Should().Be("x");
    }

    [Fact]
    public async Task Executed_action_in_window_marks_the_event_as_heblo()
    {
        var (harness, account, ids) = await ArrangeAsync();
        var occurredAt = AdsTestData.Now.AddHours(-2);
        var lookup = new Mock<IAdExecutionLookup>();
        lookup.Setup(l => l.GetExecutedActionsAsync(AdPlatform.GoogleAds, account.ExternalId,
                occurredAt.AddMinutes(-120), occurredAt.AddMinutes(120), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AdExecutedAction("42", AdActionType.PauseAd, AdPlatform.GoogleAds, account.ExternalId, AdEntityLevel.Ad, "ad-1",
                    null, occurredAt.AddMinutes(-10), new Dictionary<string, string>()),
            });
        harness.Lookup = lookup.Object;

        await harness.Writer.WriteAsync(AdPlatform.GoogleAds, account, AdChangeSource.SnapshotDiff,
            new[] { Candidate("e1", at: occurredAt, kind: AdChangeActorKind.Unknown) }, ids, default);

        await using var verify = harness.Verify();
        var row = await verify.ChangeEvents.SingleAsync();
        row.Origin.Should().Be("Heblo");
        row.MatchedExecutionId.Should().Be("42");
    }

    [Fact]
    public async Task Snapshot_candidate_has_a_deterministic_id_and_unknown_actor()
    {
        var change = new AdObservedChange(new AdEntityKey(AdEntityLevel.Ad, "ad-1"), 5,
            new AdFieldChange(AdSnapshotDiffer.StatusChange, "\"Enabled\"", "\"Paused\""));

        var candidate = AdChangeEventCandidate.FromSnapshot(change, AdsTestData.Now);

        candidate.ExternalEventId.Should().Be($"snapshot:Ad:ad-1:Status:{AdsTestData.Now.ToUnixTimeSeconds()}");
        candidate.ActorKind.Should().Be(AdChangeActorKind.Unknown);
        candidate.Actor.Should().BeNull();
    }
}
