using System.Diagnostics.Metrics;
using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Anela.Heblo.Persistence.Infrastructure.Resilience;
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
/// like production, so the CreateExecutionStrategy().ExecuteAsync(transaction) path is really exercised.
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
        _connectionString = await _fixture.CreateDatabaseAsync("ads_entity_step_atomicity");
        await using var context = NewContext();
        await context.Database.MigrateAsync();
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

    private AdsDbContext NewContext()
    {
        var metrics = new DbResilienceMetrics(new TestMeterFactory());
        var pipelineProvider = new DbResiliencePipelineProvider(
            Options.Create(new DbResilienceOptions()), metrics, NullLogger<DbResiliencePipelineProvider>.Instance);
        return new AdsDbContext(new DbContextOptionsBuilder<AdsDbContext>()
            .UseNpgsql(_connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable(AdsDbContext.MigrationsHistoryTableName, AdsDbContext.SchemaName);
                npgsql.ExecutionStrategy(deps =>
                    new PollyExecutionStrategy(deps, pipelineProvider, metrics, NullLogger<PollyExecutionStrategy>.Instance));
            })
            .Options);
    }

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

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options.Name, options.Version);
        public void Dispose() { }
    }
}
