using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Persistence.Photobank;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class PhotobankRepositoryGetTagsSqlShapeTests : IAsyncLifetime
{
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;
    private readonly CapturingCommandInterceptor _interceptor = new();
    private ApplicationDbContext _context = null!;
    private PhotobankRepository _repository = null!;

    public PhotobankRepositoryGetTagsSqlShapeTests(PostgresSharedContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync("photobank");

        // Minimal schema — only the two tables the query touches, no FKs to keep seeding simple.
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE public."PhotobankTags" (
                "Id"   serial NOT NULL PRIMARY KEY,
                "Name" varchar(100) NOT NULL
            );
            CREATE UNIQUE INDEX "IX_PhotobankTags_Name" ON public."PhotobankTags" ("Name");

            CREATE TABLE public."PhotoTags" (
                "PhotoId"   integer NOT NULL,
                "TagId"     integer NOT NULL,
                "Source"    varchar(20) NOT NULL,
                "CreatedAt" timestamp NOT NULL,
                CONSTRAINT "PK_PhotoTags" PRIMARY KEY ("PhotoId", "TagId")
            );
            CREATE INDEX "IX_PhotoTags_TagId" ON public."PhotoTags" ("TagId");

            INSERT INTO public."PhotobankTags" ("Name") VALUES ('summer'), ('winter'), ('orphan');
            INSERT INTO public."PhotoTags" ("PhotoId","TagId","Source","CreatedAt") VALUES
                (1, 1, 'Manual', now()),
                (2, 1, 'Manual', now()),
                (1, 2, 'Manual', now());
            """;
        await cmd.ExecuteNonQueryAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .AddInterceptors(_interceptor)
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PhotobankRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task GetTagsWithCountsAsync_EmitsExactlyOneSqlCommand()
    {
        _interceptor.Reset();

        await _repository.GetTagsWithCountsAsync(CancellationToken.None);

        _interceptor.Commands.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetTagsWithCountsAsync_CountsAreAggregatedSqlSide()
    {
        _interceptor.Reset();

        await _repository.GetTagsWithCountsAsync(CancellationToken.None);

        var sql = _interceptor.Commands.Single();
        sql.Should().ContainEquivalentOf("count", "aggregation must happen SQL-side, not in memory");
        sql.Should().Contain("PhotoTags", "the query must reference the PhotoTags table");
    }

    [Fact]
    public async Task GetTagsWithCountsAsync_ReturnsCorrectCountsFromRealDatabase()
    {
        var result = await _repository.GetTagsWithCountsAsync(CancellationToken.None);

        result.Should().HaveCount(3, "there are exactly 3 seeded tags");
        result[0].Count.Should().Be(2, "summer has 2 photo-tags and sorts first (count desc)");
        result[2].Count.Should().Be(0, "orphan has no photo-tags and sorts last");
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
