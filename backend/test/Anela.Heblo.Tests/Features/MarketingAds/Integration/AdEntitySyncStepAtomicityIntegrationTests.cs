using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Anela.Heblo.Tests.Common;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Anela.Heblo.Tests.Features.MarketingAds.Integration;

/// <summary>
/// For a source without a change log the entity upsert and the snapshot-diff event write must commit
/// together. Otherwise a failing event write leaves the new entity state stored, the next run sees no
/// diff, and the out-of-band change is lost for good. The context is built with PollyExecutionStrategy
/// like production, so the CreateExecutionStrategy().ExecuteAsync + single SaveChanges path is really exercised.
/// </summary>
[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class AdEntitySyncStepAtomicityIntegrationTests : IAsyncLifetime
{
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;

    public AdEntitySyncStepAtomicityIntegrationTests(PostgresSharedContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await AdsPostgres.CreateMigratedAsync(_fixture, "ads_entity_step_atomicity", withShoptet: false);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_failing_event_write_rolls_back_the_entity_upsert_so_the_next_run_still_records_the_change()
    {
        // Arrange - first run seeds the entities (no events: nothing to diff against yet)
        var time = new FakeTimeProvider(AdsTestData.Now);
        var lookup = new SwitchableLookup();
        var source = new ScriptedAdReadSource(AdPlatform.Sklik, searchTerms: false, changeLog: false) { Accounts = { AdsTestData.Account() } };
        source.Entities[AdsTestData.AccountId] = new List<AdEntitySnapshot>
        {
            AdsTestData.Campaign("c-1"), AdsTestData.AdGroup("g-1", "c-1"), AdsTestData.Ad("ad-1", "g-1"),
        };
        await using var db = NewContext();
        var account = (await NewAccounts(db, time).UpsertAsync(AdPlatform.Sklik, source.Accounts, default)).Single();
        (await NewStep(db, time, lookup).RunAsync(source, account, recordSnapshotDiffs: true, default)).Outcome.Succeeded.Should().BeTrue();

        // Act 1 - the ad is paused outside Heblo, and the event write fails
        source.Entities[AdsTestData.AccountId][2] = AdsTestData.Ad("ad-1", "g-1", AdEntityStatus.Paused);
        time.Advance(TimeSpan.FromHours(1));
        lookup.Fail = true;
        var failed = await NewStep(db, time, lookup).RunAsync(source, account, recordSnapshotDiffs: true, default);

        // Assert 1 - the stream failed and nothing of the change was committed
        failed.Outcome.Succeeded.Should().BeFalse();
        await using (var verify = NewContext())
        {
            (await verify.ChangeEvents.CountAsync()).Should().Be(0);
            (await verify.Entities.SingleAsync(e => e.ExternalId == "ad-1")).Status.Should().Be("Enabled");
            (await verify.SyncStates.SingleAsync(s => s.Stream == AdSyncStreams.Entities)).Status.Should().Be(AdsDbValues.StatusFailed);
        }

        // Act 2 - the next run works
        time.Advance(TimeSpan.FromHours(1));
        lookup.Fail = false;
        var recovered = await NewStep(db, time, lookup).RunAsync(source, account, recordSnapshotDiffs: true, default);

        // Assert 2 - exactly one snapshot-diff event for the change
        recovered.Outcome.Succeeded.Should().BeTrue();
        await using var after = NewContext();
        var change = await after.ChangeEvents.SingleAsync();
        change.Source.Should().Be("SnapshotDiff");
        change.ChangeType.Should().Be("Status");
        change.Origin.Should().Be("OutOfBand");
        (await after.Entities.SingleAsync(e => e.ExternalId == "ad-1")).Status.Should().Be("Paused");
    }

    [Fact]
    public async Task New_parents_children_and_their_Created_events_are_saved_together_with_stored_ids()
    {
        // Arrange - the first run seeds one campaign, so the next run reports creations
        var time = new FakeTimeProvider(AdsTestData.Now);
        var source = new ScriptedAdReadSource(AdPlatform.Sklik, searchTerms: false, changeLog: false) { Accounts = { AdsTestData.Account() } };
        source.Entities[AdsTestData.AccountId] = new List<AdEntitySnapshot> { AdsTestData.Campaign("c-1") };
        await using var db = NewContext();
        var account = (await NewAccounts(db, time).UpsertAsync(AdPlatform.Sklik, source.Accounts, default)).Single();
        (await NewStep(db, time, new SwitchableLookup()).RunAsync(source, account, recordSnapshotDiffs: true, default)).Outcome.Succeeded.Should().BeTrue();

        // Act - a new subtree under the existing campaign plus a new campaign with a child, children listed first
        source.Entities[AdsTestData.AccountId] = new List<AdEntitySnapshot>
        {
            AdsTestData.Ad("ad-1", "g-1"), AdsTestData.AdGroup("g-1", "c-1"), AdsTestData.Campaign("c-1"),
            AdsTestData.AdGroup("g-2", "c-2"), AdsTestData.Campaign("c-2"),
        };
        time.Advance(TimeSpan.FromHours(1));
        var result = await NewStep(db, time, new SwitchableLookup()).RunAsync(source, account, recordSnapshotDiffs: true, default);

        // Assert - stored ids everywhere: the returned map, parent links and every Created event's entity id
        result.Outcome.Succeeded.Should().BeTrue();
        await using var verify = NewContext();
        var rows = (await verify.Entities.ToListAsync()).ToDictionary(e => e.ExternalId);
        rows.Should().HaveCount(5);
        result.EntityIds.Should().HaveCount(5);
        result.EntityIds.Should().OnlyContain(p => rows[p.Key.ExternalId].Id == p.Value);
        rows["c-1"].ParentId.Should().BeNull();
        rows["g-1"].ParentId.Should().Be(rows["c-1"].Id);
        rows["ad-1"].ParentId.Should().Be(rows["g-1"].Id);
        rows["g-2"].ParentId.Should().Be(rows["c-2"].Id);
        var events = await verify.ChangeEvents.ToListAsync();
        events.Should().HaveCount(4).And.OnlyContain(e => e.ChangeType == AdSnapshotDiffer.CreatedChange && e.Source == "SnapshotDiff");
        events.Should().OnlyContain(e => e.EntityId == rows[e.EntityExternalRef!].Id);
        events.Select(e => e.EntityExternalRef).Should().BeEquivalentTo(new[] { "ad-1", "g-1", "g-2", "c-2" });
    }

    private AdsDbContext NewContext() => new(AdsPostgres.Options(_connectionString));

    private static AdAccountUpserter NewAccounts(AdsDbContext db, TimeProvider time) =>
        new(db, time, Options.Create(new AdsSyncOptions()), NullLogger<AdAccountUpserter>.Instance);

    private static AdEntitySyncStep NewStep(AdsDbContext db, TimeProvider time, IAdExecutionLookup lookup)
    {
        var options = Options.Create(new AdsSyncOptions());
        var state = new AdSyncStateStore(db, time);
        var runner = new AdStreamRunner(db, state, time, NullLogger<AdStreamRunner>.Instance);
        var writer = new AdChangeEventWriter(db, lookup, new AdChangeOriginMatcher(), time, options);
        return new AdEntitySyncStep(db, new AdEntityUpserter(db, time, options), writer, state, runner, time);
    }

    private sealed class SwitchableLookup : IAdExecutionLookup
    {
        public bool Fail { get; set; }

        public Task<IReadOnlyList<AdExecutedAction>> GetExecutedActionsAsync(AdPlatform platform, string accountExternalId,
            DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            Fail
                ? throw new InvalidOperationException("execution log unavailable")
                : Task.FromResult<IReadOnlyList<AdExecutedAction>>(Array.Empty<AdExecutedAction>());
    }
}
