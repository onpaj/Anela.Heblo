using Anela.Heblo.Application.Features.MarketingAds;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Tests.Common;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Anela.Heblo.Tests.Features.MarketingAds.Integration;

/// <summary>
/// What InMemory cannot show: jsonb normalisation, the timestamptz offset rule and real unique keys
/// under re-runs. Wired through AddMarketingAdsModule with the production execution strategy.
/// </summary>
[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class AdsSyncPostgresIntegrationTests : IAsyncLifetime
{
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;

    public AdsSyncPostgresIntegrationTests(PostgresSharedContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() =>
        _connectionString = await AdsPostgres.CreateMigratedAsync(_fixture, "ads_sync", withShoptet: false);

    public Task DisposeAsync() => Task.CompletedTask;

    private ServiceProvider Provider(ScriptedAdReadSource source, FakeTimeProvider time)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddDbContext<AdsDbContext>(o => AdsPostgres.Configure(o, _connectionString));
        services.AddSingleton<IAdPlatformReadSource>(source);
        services.AddMarketingAdsModule(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    private static ScriptedAdReadSource SklikWithoutChangeLog()
    {
        var source = new ScriptedAdReadSource(AdPlatform.Sklik, searchTerms: true, changeLog: false) { Accounts = { AdsTestData.Account() } };
        source.Entities[AdsTestData.AccountId] = new List<AdEntitySnapshot>
        {
            // keys deliberately unsorted and of different lengths: jsonb reorders them
            AdsTestData.Campaign("c-1", attributes: new Dictionary<string, string?> { ["zz_budget"] = "500", ["a"] = "x", ["bidding"] = null }),
            AdsTestData.AdGroup("g-1", "c-1"),
            AdsTestData.Ad("ad-1", "g-1"),
        };
        source.Facts[AdsTestData.AccountId] = new List<AdDailyFactRow> { AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", AdsTestData.Yesterday, 100m) };
        source.SearchTerms[AdsTestData.AccountId] = new List<AdSearchTermRow> { AdsTestData.Term("g-1", AdsTestData.Yesterday, "krém", 3m, matchType: null) };
        return source;
    }

    private static async Task RunDailyAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<IAdsDailySyncService>().RunAsync(default);
        report.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Second_identical_run_writes_no_snapshot_diff_events()
    {
        // Arrange
        var time = new FakeTimeProvider(AdsTestData.Now);
        await using var provider = Provider(SklikWithoutChangeLog(), time);

        // Act
        await RunDailyAsync(provider);
        time.Advance(TimeSpan.FromDays(1));
        await RunDailyAsync(provider);

        // Assert - through a fresh context
        await using var verify = new AdsDbContext(AdsPostgres.Options(_connectionString));
        (await verify.ChangeEvents.CountAsync()).Should().Be(0);
        (await verify.Entities.CountAsync()).Should().Be(3);
        (await verify.DailyFacts.CountAsync()).Should().Be(1);
        (await verify.SearchTermsDaily.SingleAsync()).MatchType.Should().Be(AdsDbValues.NoMatchType);
        (await verify.SyncStates.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Status_change_is_detected_against_jsonb_stored_state()
    {
        // Arrange
        var time = new FakeTimeProvider(AdsTestData.Now);
        var source = SklikWithoutChangeLog();
        await using var provider = Provider(source, time);
        await RunDailyAsync(provider);
        source.Entities[AdsTestData.AccountId][2] = AdsTestData.Ad("ad-1", "g-1", AdEntityStatus.Paused);
        time.Advance(TimeSpan.FromHours(1));

        // Act
        await RunDailyAsync(provider);

        // Assert
        await using var verify = new AdsDbContext(AdsPostgres.Options(_connectionString));
        var change = await verify.ChangeEvents.SingleAsync();
        change.ChangeType.Should().Be("Status");
        change.Origin.Should().Be("OutOfBand");
    }

    [Fact]
    public async Task Change_event_with_offset_timestamp_persists()
    {
        // Arrange
        var time = new FakeTimeProvider(AdsTestData.Now);
        await using var provider = Provider(SklikWithoutChangeLog(), time);
        await RunDailyAsync(provider);
        using var scope = provider.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<AdChangeEventWriter>();
        var db = scope.ServiceProvider.GetRequiredService<AdsDbContext>();
        var account = await db.Accounts.AsNoTracking().SingleAsync();
        var local = new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(2));
        var row = AdsTestData.Change("evt-1", local, AdEntityLevel.Ad, "ad-1", "PAUSED");

        // Act
        await writer.WriteAsync(AdPlatform.Sklik, account, AdChangeSource.PlatformChangeLog,
            new[] { AdChangeEventCandidate.FromRow(row) }, new Dictionary<AdEntityKey, long>(), default);

        // Assert
        await using var verify = new AdsDbContext(AdsPostgres.Options(_connectionString));
        var stored = await verify.ChangeEvents.SingleAsync();
        stored.OccurredAt.Should().Be(local);
        stored.NewValueJson.Should().Contain("PAUSED");
    }
}
