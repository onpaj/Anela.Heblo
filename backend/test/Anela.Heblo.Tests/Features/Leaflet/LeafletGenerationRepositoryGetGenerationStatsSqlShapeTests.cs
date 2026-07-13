using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Leaflet;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class LeafletGenerationRepositoryGetGenerationStatsSqlShapeTests : IAsyncLifetime
{
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;
    private readonly CapturingCommandInterceptor _interceptor = new();
    private ApplicationDbContext _context = null!;
    private LeafletGenerationRepository _repository = null!;

    public LeafletGenerationRepositoryGetGenerationStatsSqlShapeTests(PostgresSharedContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync("leafletgenerations");

        // Minimal schema — only the columns GetGenerationStatsAsync touches.
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE public."LeafletGenerations" (
                "Id"             uuid NOT NULL PRIMARY KEY,
                "PrecisionScore" integer NULL,
                "StyleScore"     integer NULL
            );

            INSERT INTO public."LeafletGenerations" ("Id", "PrecisionScore", "StyleScore") VALUES
                (gen_random_uuid(), 4,    5),
                (gen_random_uuid(), 2,    3),
                (gen_random_uuid(), NULL, NULL),
                (gen_random_uuid(), 5,    NULL),
                (gen_random_uuid(), NULL, 1);
            """;
        await cmd.ExecuteNonQueryAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .AddInterceptors(_interceptor)
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new LeafletGenerationRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task GetGenerationStatsAsync_EmitsExactlyOneSqlCommand()
    {
        _interceptor.Reset();

        await _repository.GetGenerationStatsAsync(CancellationToken.None);

        _interceptor.Commands.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetGenerationStatsAsync_ReturnsCorrectStatsFromRealDatabase()
    {
        _interceptor.Reset();

        var stats = await _repository.GetGenerationStatsAsync(CancellationToken.None);

        stats.TotalGenerations.Should().Be(5);
        stats.TotalWithFeedback.Should().Be(4, "all rows except the fully-null one have at least one non-null score");
        stats.AvgPrecisionScore.Should().Be((4 + 2 + 5) / 3.0, "only 3 rows have a non-null PrecisionScore (4, 2, 5)");
        stats.AvgStyleScore.Should().Be((5 + 3 + 1) / 3.0, "only 3 rows have a non-null StyleScore (5, 3, 1)");
    }

    [Fact]
    public async Task GetGenerationStatsAsync_EmptyTable_ReturnsZeroedStatsWithoutThrowing()
    {
        await using (var conn = new NpgsqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """TRUNCATE TABLE public."LeafletGenerations";""";
            await cmd.ExecuteNonQueryAsync();
        }

        _interceptor.Reset();

        var stats = await _repository.GetGenerationStatsAsync(CancellationToken.None);

        stats.TotalGenerations.Should().Be(0);
        stats.TotalWithFeedback.Should().Be(0);
        stats.AvgPrecisionScore.Should().BeNull();
        stats.AvgStyleScore.Should().BeNull();
    }

    private sealed class CapturingCommandInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = new();

        public void Reset() => Commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
