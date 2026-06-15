using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.GridLayouts;
using Anela.Heblo.Persistence.Infrastructure;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Persistence.GridLayouts;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class GridLayoutRepositoryUpsertIntegrationTests : IAsyncLifetime
{
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;
    private ApplicationDbContext _context = null!;
    private PostgresExceptionTranslator _translator = null!;

    public GridLayoutRepositoryUpsertIntegrationTests(PostgresSharedContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync("gridlayouts");

        // Create only the GridLayouts table manually.
        // Do NOT use EnsureCreatedAsync — the project schema depends on the "vector" extension
        // which is not available in the plain postgres:16 image.
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE SCHEMA IF NOT EXISTS public;
            CREATE TABLE IF NOT EXISTS public."GridLayouts" (
                "Id"           serial                       PRIMARY KEY,
                "UserId"       character varying(255)       NOT NULL,
                "GridKey"      character varying(100)       NOT NULL,
                "LayoutJson"   text                         NOT NULL,
                "LastModified" timestamp without time zone  NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_GridLayouts_UserId_GridKey"
                ON public."GridLayouts" ("UserId", "GridKey");
            """;
        await cmd.ExecuteNonQueryAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        _context = new ApplicationDbContext(options);
        _translator = new PostgresExceptionTranslator(NullLogger<PostgresExceptionTranslator>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    private GridLayoutRepository CreateRepository(TimeProvider? timeProvider = null) =>
        new(_context, timeProvider ?? TimeProvider.System, _translator);

    private async Task<int> CountRowsAsync(string userId, string gridKey)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM public."GridLayouts"
            WHERE "UserId" = @userId AND "GridKey" = @gridKey
            """;
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("gridKey", gridKey);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private async Task<(int Id, string LayoutJson, DateTime LastModified)> ReadRowAsync(string userId, string gridKey)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT "Id", "LayoutJson", "LastModified"
            FROM public."GridLayouts"
            WHERE "UserId" = @userId AND "GridKey" = @gridKey
            """;
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("gridKey", gridKey);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException(
                $"Row for (UserId='{userId}', GridKey='{gridKey}') not found.");
        }
        return (reader.GetInt32(0), reader.GetString(1), reader.GetDateTime(2));
    }
}
