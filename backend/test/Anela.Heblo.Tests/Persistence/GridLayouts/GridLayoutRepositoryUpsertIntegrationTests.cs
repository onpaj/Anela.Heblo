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

    [Fact]
    public async Task UpsertAsync_WhenRowDoesNotExist_InsertsNewRow()
    {
        // Arrange
        var now = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(now, TimeSpan.Zero));
        var repository = CreateRepository(timeProvider);

        // Act
        await repository.UpsertAsync("user-1", "grid-1", "{\"col\":1}", CancellationToken.None);

        // Assert
        var row = await ReadRowAsync("user-1", "grid-1");
        row.LayoutJson.Should().Be("{\"col\":1}");
        row.LastModified.Should().Be(now);
        row.Id.Should().BePositive();
    }

    [Fact]
    public async Task UpsertAsync_WhenRowExists_UpdatesLayoutJsonAndTimestampWithoutChangingId()
    {
        // Arrange
        var first = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var second = new DateTime(2026, 6, 15, 10, 5, 0, DateTimeKind.Utc);
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(first, TimeSpan.Zero));
        var repository = CreateRepository(timeProvider);

        await repository.UpsertAsync("user-1", "grid-1", "{\"col\":1}", CancellationToken.None);
        var inserted = await ReadRowAsync("user-1", "grid-1");

        timeProvider.SetUtcNow(new DateTimeOffset(second, TimeSpan.Zero));

        // Act
        await repository.UpsertAsync("user-1", "grid-1", "{\"col\":2}", CancellationToken.None);

        // Assert
        (await CountRowsAsync("user-1", "grid-1")).Should().Be(1);
        var updated = await ReadRowAsync("user-1", "grid-1");
        updated.Id.Should().Be(inserted.Id);
        updated.LayoutJson.Should().Be("{\"col\":2}");
        updated.LastModified.Should().Be(second);
    }

    [Fact]
    public async Task UpsertAsync_ConcurrentInsertsForSameKey_AllSucceedAndProduceSingleRow()
    {
        // Arrange
        const int concurrency = 5;
        var payloads = Enumerable.Range(0, concurrency)
            .Select(i => $"{{\"v\":{i}}}")
            .ToArray();

        // Build one repository per task, each backed by its own DbContext + connection.
        // The shared in-test _context is single-threaded by design; concurrent SQL must
        // come from independent connections to actually race on the unique index.
        var contexts = new List<ApplicationDbContext>();
        try
        {
            var tasks = payloads.Select(payload =>
            {
                var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql(_connectionString)
                    .Options;
                var context = new ApplicationDbContext(options);
                contexts.Add(context);
                var repo = new GridLayoutRepository(context, TimeProvider.System, _translator);
                return repo.UpsertAsync("user-race", "grid-race", payload, CancellationToken.None);
            }).ToArray();

            // Act
            await Task.WhenAll(tasks);

            // Assert
            (await CountRowsAsync("user-race", "grid-race")).Should().Be(1);
            var row = await ReadRowAsync("user-race", "grid-race");
            payloads.Should().Contain(row.LayoutJson);
        }
        finally
        {
            foreach (var ctx in contexts)
            {
                await ctx.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task UpsertAsync_ConcurrentUpdatesForSameKey_AllSucceedAndProduceSingleRow()
    {
        // Arrange — seed an existing row so each task takes the UPDATE branch.
        await CreateRepository().UpsertAsync("user-update", "grid-update", "{\"seed\":true}", CancellationToken.None);
        var seededId = (await ReadRowAsync("user-update", "grid-update")).Id;

        const int concurrency = 5;
        var payloads = Enumerable.Range(0, concurrency)
            .Select(i => $"{{\"u\":{i}}}")
            .ToArray();

        var contexts = new List<ApplicationDbContext>();
        try
        {
            var tasks = payloads.Select(payload =>
            {
                var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql(_connectionString)
                    .Options;
                var context = new ApplicationDbContext(options);
                contexts.Add(context);
                var repo = new GridLayoutRepository(context, TimeProvider.System, _translator);
                return repo.UpsertAsync("user-update", "grid-update", payload, CancellationToken.None);
            }).ToArray();

            // Act
            await Task.WhenAll(tasks);

            // Assert
            (await CountRowsAsync("user-update", "grid-update")).Should().Be(1);
            var row = await ReadRowAsync("user-update", "grid-update");
            row.Id.Should().Be(seededId);
            payloads.Should().Contain(row.LayoutJson);
        }
        finally
        {
            foreach (var ctx in contexts)
            {
                await ctx.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task UpsertAsync_IssuesExactlyOneStatementPerCall()
    {
        // Arrange
        var statements = new List<string>();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .LogTo(message =>
            {
                if (message.Contains("Executed DbCommand", StringComparison.Ordinal))
                {
                    statements.Add(message);
                }
            }, new[] { Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted })
            .Options;
        await using var context = new ApplicationDbContext(options);
        var repository = new GridLayoutRepository(context, TimeProvider.System, _translator);

        // Act
        await repository.UpsertAsync("user-stmt", "grid-stmt", "{\"x\":1}", CancellationToken.None);

        // Assert
        statements.Should().HaveCount(1, "the upsert must be a single round-trip");
        statements[0].Should().Contain("ON CONFLICT", "the statement must use the atomic upsert");
    }

    [Fact]
    public async Task UpsertAsync_WhenCancellationTokenAlreadyCancelled_ThrowsOperationCanceled()
    {
        // Arrange
        var repository = CreateRepository();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = () => repository.UpsertAsync("user-cancel", "grid-cancel", "{}", cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        (await CountRowsAsync("user-cancel", "grid-cancel")).Should().Be(0);
    }

    [Fact]
    public async Task UpsertAsync_WhenPostgresRejectsValue_TranslatesToGridLayoutPersistenceException()
    {
        // Arrange — GridKey column is character varying(100); 101 chars triggers a Postgres
        // 22001 string-data-right-truncation error, which is not a unique-violation and
        // exercises the translator's non-race path.
        var repository = CreateRepository();
        var tooLongGridKey = new string('k', 101);

        // Act
        Func<Task> act = () => repository.UpsertAsync("user-bad", tooLongGridKey, "{}", CancellationToken.None);

        // Assert
        var thrown = await act.Should().ThrowAsync<GridLayoutPersistenceException>();
        thrown.Which.InnerException.Should().NotBeNull();
    }
}
