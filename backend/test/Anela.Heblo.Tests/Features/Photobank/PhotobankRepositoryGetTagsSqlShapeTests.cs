using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using DotNet.Testcontainers.Configurations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

[Trait("Category", "Integration")]
public class PhotobankRepositoryGetTagsSqlShapeTests : IAsyncLifetime
{
    static PhotobankRepositoryGetTagsSqlShapeTests()
    {
        // Required on macOS with Podman: the Ryuk ResourceReaper container
        // cannot bind to the Docker socket and throws a NullReferenceException.
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    private readonly CapturingCommandInterceptor _interceptor = new();
    private ApplicationDbContext _context = null!;
    private PhotobankRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Minimal schema — only the two tables the query touches, no FKs to keep seeding simple.
        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
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
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .AddInterceptors(_interceptor)
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PhotobankRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task GetTagsWithCountsAsync_EmitsExactlyOneSqlCommand()
    {
        _interceptor.Reset();

        await _repository.GetTagsWithCountsAsync(CancellationToken.None);

        _interceptor.Commands.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetTagsWithCountsAsync_UsesLeftJoinAndGroupBy_NotCorrelatedSubquery()
    {
        _interceptor.Reset();

        await _repository.GetTagsWithCountsAsync(CancellationToken.None);

        var sql = _interceptor.Commands.Single();
        sql.Should().Contain("LEFT JOIN", "the rewrite should join PhotoTags rather than scan it per tag");
        sql.Should().Contain("GROUP BY", "counts should be produced by a single GROUP BY aggregation");
        sql.Should().NotMatchRegex(
            @"\(\s*SELECT\s+COUNT\s*\(",
            "a correlated COUNT subquery is the perf bug we just removed");
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
