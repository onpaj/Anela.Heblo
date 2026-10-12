using System.Diagnostics.Metrics;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using Anela.Heblo.Persistence.ShoptetOrders;
using Anela.Heblo.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Anela.Heblo.Tests.Features.MarketingAds.Integration;

/// <summary>
/// Fresh, migrated Postgres databases for the MarketingAds integration tests. Contexts use the
/// production PollyExecutionStrategy: AdEntitySyncStep opens its transaction through
/// CreateExecutionStrategy(), which must be exercised the way production runs it.
/// </summary>
internal static class AdsPostgres
{
    private static readonly DbResilienceMetrics Metrics = new(new TestMeterFactory());

    private static readonly DbResiliencePipelineProvider PipelineProvider = new(
        Microsoft.Extensions.Options.Options.Create(new DbResilienceOptions()), Metrics, NullLogger<DbResiliencePipelineProvider>.Instance);

    /// <summary>Applies the ads Npgsql settings to a context options builder (also used by AddDbContext).</summary>
    public static void Configure(DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable(AdsDbContext.MigrationsHistoryTableName, AdsDbContext.SchemaName);
            npgsql.ExecutionStrategy(deps =>
                new PollyExecutionStrategy(deps, PipelineProvider, Metrics, NullLogger<PollyExecutionStrategy>.Instance));
        });

    public static DbContextOptions<AdsDbContext> Options(string connectionString) =>
        ConfigureAndBuild(connectionString);

    private static DbContextOptions<AdsDbContext> ConfigureAndBuild(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<AdsDbContext>();
        Configure(builder, connectionString);
        return builder.Options;
    }

    public static async Task<string> CreateMigratedAsync(PostgresSharedContainerFixture fixture, string hint, bool withShoptet)
    {
        var connectionString = await fixture.CreateDatabaseAsync(hint);
        await EnsureMetabaseRoleAsync(connectionString);

        if (withShoptet)
        {
            var shoptet = new DbContextOptionsBuilder<ShoptetOrdersDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ShoptetOrdersDbContext.SchemaName))
                .Options;
            await using var shoptetDb = new ShoptetOrdersDbContext(shoptet);
            await shoptetDb.Database.MigrateAsync();
        }

        await using var db = new AdsDbContext(Options(connectionString));
        await db.Database.MigrateAsync();
        return connectionString;
    }

    /// <summary>Roles are cluster-wide and the container is shared between test classes.</summary>
    private static async Task EnsureMetabaseRoleAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'metabase_ro') THEN CREATE ROLE metabase_ro; END IF; END $$;";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options.Name, options.Version);
        public void Dispose() { }
    }
}
