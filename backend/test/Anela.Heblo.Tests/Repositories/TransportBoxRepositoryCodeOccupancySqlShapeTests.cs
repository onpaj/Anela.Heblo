using System.Collections.Generic;
using System.Data.Common;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Logistics.TransportBoxes;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Repositories;

/// <summary>
/// Proves that TransportBoxRepository.IsBoxCodeActiveAsync and GetByCodeAsync — both rewritten
/// to consume TransportBoxStateRules.OccupiesCodePredicate — actually translate to server-side
/// SQL against real PostgreSQL, rather than only "working" under UseInMemoryDatabase's in-memory
/// LINQ evaluation. In particular, a negated Contains() over a HasConversion&lt;string&gt; enum
/// inside an ORDER BY (GetByCodeAsync's occupancy-first sort) is not exercised anywhere else in
/// backend/src, so this is the one place this change could reach staging broken (issue #3887).
/// </summary>
[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class TransportBoxRepositoryCodeOccupancySqlShapeTests : IAsyncLifetime
{
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;
    private readonly CapturingCommandInterceptor _interceptor = new();
    private ApplicationDbContext _context = null!;
    private TransportBoxRepository _repository = null!;

    public TransportBoxRepositoryCodeOccupancySqlShapeTests(PostgresSharedContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync("transportbox_code_occupancy");

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS public."TransportBoxes" (
                "Id"                     serial NOT NULL PRIMARY KEY,
                "Code"                   text NULL,
                "State"                  text NOT NULL,
                "DefaultReceiveState"    text NOT NULL,
                "Description"            text NULL,
                "LastStateChanged"       timestamp without time zone NULL,
                "Location"               text NULL,
                "CreationTime"           timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                "CreatorId"              uuid NULL,
                "LastModificationTime"   timestamp without time zone NULL,
                "LastModifierId"         uuid NULL,
                "ConcurrencyStamp"       character varying(40) NOT NULL DEFAULT gen_random_uuid()::text,
                "ExtraProperties"        text NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS public."TransportBoxItems" (
                "Id"                 serial NOT NULL PRIMARY KEY,
                "ProductCode"        text NOT NULL,
                "ProductName"        text NOT NULL,
                "Amount"             double precision NOT NULL,
                "DateAdded"          timestamp without time zone NOT NULL,
                "UserAdded"          text NOT NULL,
                "LotNumber"          character varying(100) NULL,
                "ExpirationDate"     date NULL,
                "SourceInventoryId"  integer NULL,
                "TransportBoxId"     integer NOT NULL REFERENCES public."TransportBoxes" ("Id") ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS public."TransportBoxStateLogs" (
                "Id"              serial NOT NULL PRIMARY KEY,
                "State"           integer NOT NULL,
                "StateDate"       timestamp without time zone NOT NULL,
                "User"            text NULL,
                "Description"     text NULL,
                "TransportBoxId"  integer NOT NULL REFERENCES public."TransportBoxes" ("Id") ON DELETE CASCADE
            );
            """;
        await cmd.ExecuteNonQueryAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .AddInterceptors(_interceptor)
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new TransportBoxRepository(_context, NullLogger<TransportBoxRepository>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    private static async Task<TransportBox> SeedQuarantineBoxAsync(ApplicationDbContext context, string code)
    {
        var box = new TransportBox();
        box.Open(code, DateTime.UtcNow, "seed-user");
        box.ToQuarantine(DateTime.UtcNow, "seed-user");

        context.Set<TransportBox>().Add(box);
        await context.SaveChangesAsync();
        return box;
    }

    private static async Task<TransportBox> SeedStockedBoxAsync(ApplicationDbContext context, string code)
    {
        var box = new TransportBox();
        box.Open(code, DateTime.UtcNow, "seed-user");
        box.AddItem("PROD1", "Product 1", 1, DateTime.UtcNow, "seed-user");
        box.ToTransit(DateTime.UtcNow, "seed-user");
        box.Receive(DateTime.UtcNow, "seed-user");
        box.ToPick(DateTime.UtcNow, "seed-user");

        context.Set<TransportBox>().Add(box);
        await context.SaveChangesAsync();
        return box;
    }

    [Fact]
    public async Task IsBoxCodeActiveAsync_TranslatesServerSide_ForQuarantineBox()
    {
        await SeedQuarantineBoxAsync(_context, "B001");

        _interceptor.Reset();

        var isActive = await _repository.IsBoxCodeActiveAsync("B001");

        isActive.Should().BeTrue("a Quarantine box still occupies its code — this is the bug fix from issue #3887");

        var sql = _interceptor.Commands.Should().ContainSingle(
            "the check must be a single server-side round trip, not client-side evaluation").Subject;
        sql.Should().Contain("\"State\"");
        sql.ToUpperInvariant().Should().Contain("NOT", "the occupancy check is a negated set membership over the State column");
        (sql.Contains(" IN ", StringComparison.OrdinalIgnoreCase) || sql.Contains("= ANY", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("Npgsql renders the negated Contains() as either an inlined IN list or a parameterised = ANY(...) array — either is acceptable, the exact literal is not");
    }

    [Fact]
    public async Task GetByCodeAsync_EmitsOrderByOnState_AndDoesNotThrow()
    {
        await SeedQuarantineBoxAsync(_context, "B001");

        _interceptor.Reset();

        Func<Task> act = async () => await _repository.GetByCodeAsync("B001");

        await act.Should().NotThrowAsync<InvalidOperationException>(
            "an untranslatable ORDER BY throws InvalidOperationException at query execution time");

        var sql = _interceptor.Commands.Should().ContainSingle().Subject;
        sql.ToUpperInvariant().Should().Contain("ORDER BY");
        sql.Should().Contain("\"State\"");
    }

    [Fact]
    public async Task GetByCodeAsync_ResolvesToOccupyingBox_WhenMultipleBoxesShareCode()
    {
        // Quarantine box seeded first (lower Id) — still occupies the code.
        var quarantineBox = await SeedQuarantineBoxAsync(_context, "B001");
        // Stocked box seeded second (higher Id) — releases the code.
        await SeedStockedBoxAsync(_context, "B001");

        _interceptor.Reset();

        var result = await _repository.GetByCodeAsync("B001");

        result.Should().NotBeNull();
        result!.Id.Should().Be(quarantineBox.Id,
            "occupancy (false < true under DESC) must win over recency — the occupying Quarantine box is returned even though it was seeded first");
        result.State.Should().Be(TransportBoxState.Quarantine);
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
