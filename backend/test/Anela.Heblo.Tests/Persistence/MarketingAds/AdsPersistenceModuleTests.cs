using System.Diagnostics.Metrics;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly.Timeout;

namespace Anela.Heblo.Tests.Persistence.MarketingAds;

public class AdsPersistenceModuleTests
{
    private const string ConnectionString = "Host=localhost;Database=test;Username=test;Password=test";

    private static readonly string AdsHistoryTable =
        $"{AdsDbContext.SchemaName}.\"{AdsDbContext.MigrationsHistoryTableName}\"";

    private static readonly string PublicHistoryTable =
        $"public.\"{AdsDbContext.MigrationsHistoryTableName}\"";

    [Fact]
    public async Task AdsResiliencePipeline_IsIsolatedFromTheRequestPathPipeline()
    {
        // Arrange — the request path's pipeline is tuned to a budget far too tight for a batch upsert.
        var services = new ServiceCollection();
        services.AddSingleton<IMeterFactory>(new TestMeterFactory());
        services.AddSingleton<DbResilienceMetrics>();
        services.AddSingleton<IDbResiliencePipelineProvider>(sp =>
            new DbResiliencePipelineProvider(
                Options.Create(new DbResilienceOptions { TotalTimeBudget = TimeSpan.FromMilliseconds(300) }),
                sp.GetRequiredService<DbResilienceMetrics>(),
                NullLogger<DbResiliencePipelineProvider>.Instance));
        services.AddAdsPersistenceServices(ConnectionString, maxPoolSize: 5);
        await using var provider = services.BuildServiceProvider();

        var adsPipeline = provider.GetRequiredKeyedService<IDbResiliencePipelineProvider>(AdsPersistenceModule.ServiceKey);
        var requestPathPipeline = provider.GetRequiredService<IDbResiliencePipelineProvider>();

        // Act
        var result = await adsPipeline.Pipeline.ExecuteAsync(async ct =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600), ct);
            return 42;
        });
        var requestPathAct = async () => await requestPathPipeline.Pipeline.ExecuteAsync(async ct =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600), ct);
            return 42;
        });

        // Assert
        adsPipeline.Should().NotBeSameAs(requestPathPipeline);
        result.Should().Be(42);
        await requestPathAct.Should().ThrowAsync<TimeoutRejectedException>();
    }

    [Fact]
    public async Task MigrationsHistoryTable_LivesInTheAdsSchema_AtRuntime()
    {
        // EF Core does not derive the history table's schema from HasDefaultSchema (ADR-007): without
        // the explicit pin this context would write into public."__EFMigrationsHistory".
        var services = new ServiceCollection();
        services.AddSingleton<IMeterFactory>(new TestMeterFactory());
        services.AddSingleton<DbResilienceMetrics>();
        services.AddLogging();
        services.AddSingleton<NpgsqlConnectionInterceptor>();
        services.AddAdsPersistenceServices(ConnectionString, maxPoolSize: 5);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AdsDbContext>();

        var createScript = context.GetInfrastructure().GetRequiredService<IHistoryRepository>().GetCreateScript();

        createScript.Should().Contain(AdsHistoryTable);
        createScript.Should().NotContain(PublicHistoryTable);
    }

    [Fact]
    public void DesignTimeFactory_AlsoPinsTheMigrationsHistoryTableToTheAdsSchema()
    {
        // `dotnet ef` goes through AdsDbContextFactory, not AddAdsPersistenceServices; migrations are
        // applied manually in this project, so this path matters as much as the runtime one.
        using var context = new AdsDbContextFactory().CreateDbContext([]);

        var createScript = context.GetInfrastructure().GetRequiredService<IHistoryRepository>().GetCreateScript();

        createScript.Should().Contain(AdsHistoryTable);
        createScript.Should().NotContain(PublicHistoryTable);
    }

    [Fact]
    public void Model_MapsExactlyTheSixAdsTables_IntoTheAdsSchema()
    {
        using var context = new AdsDbContextFactory().CreateDbContext([]);

        var tables = context.Model.GetEntityTypes()
            .Select(t => $"{t.GetSchema()}.{t.GetTableName()}")
            .ToList();

        tables.Should().BeEquivalentTo(
            "ads.ad_accounts", "ads.ad_entities", "ads.ad_daily_facts",
            "ads.ad_search_term_daily", "ads.ad_change_events", "ads.sync_state");
    }

    [Fact]
    public void EveryDateTimeOffsetProperty_IncludingNullableOnes_IsNormalisedToUtc()
    {
        // Npgsql refuses to write a DateTimeOffset with a non-zero offset to timestamptz. Platforms
        // report local offsets (+02:00), so the context normalises every such property.
        using var context = new AdsDbContextFactory().CreateDbContext([]);

        var properties = context.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?))
            .ToList();

        properties.Should().Contain(p => p.ClrType == typeof(DateTimeOffset?), "sync_state.watermark is nullable");
        properties.Should().AllSatisfy(p => p.GetValueConverter().Should().BeOfType<UtcDateTimeOffsetConverter>());
    }

    [Fact]
    public void UtcDateTimeOffsetConverter_StoresTheSameInstantWithAZeroOffset()
    {
        var converter = new UtcDateTimeOffsetConverter();
        var pragueMorning = new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(2));

        var stored = (DateTimeOffset)converter.ConvertToProvider(pragueMorning)!;

        stored.Offset.Should().Be(TimeSpan.Zero);
        stored.Should().Be(pragueMorning);
        stored.UtcDateTime.Should().Be(new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc));
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options.Name, options.Version);
        public void Dispose() { }
    }
}
