using Anela.Heblo.Persistence;
using Anela.Heblo.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Persistence;

/// <summary>A fresh Postgres database holding only the C3 proposal tables (the AddMarketingAdsProposals migration).</summary>
internal static class MarketingAdsProposalsPostgres
{
    public static async Task<string> CreateMigratedAsync(PostgresSharedContainerFixture fixture, string databasePrefix)
    {
        var connectionString = await fixture.CreateDatabaseAsync(databasePrefix);
        await using var context = NewContext(connectionString);
        var migrations = context.Database.GetMigrations().ToList();
        var ours = migrations.Single(m => m.EndsWith("_AddMarketingAdsProposals", StringComparison.Ordinal));
        var previous = migrations[migrations.IndexOf(ours) - 1];
        var script = context.GetService<IMigrator>().GenerateScript(previous, ours);

        await ExecuteAsync(connectionString, """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" character varying(150) PRIMARY KEY,
                "ProductVersion" character varying(32) NOT NULL);
            """);
        await ExecuteAsync(connectionString, script);
        return connectionString;
    }

    public static ApplicationDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
