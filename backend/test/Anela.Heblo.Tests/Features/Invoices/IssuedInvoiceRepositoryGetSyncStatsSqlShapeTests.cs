using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Invoices;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests : IAsyncLifetime
{
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;
    private readonly CapturingCommandInterceptor _interceptor = new();
    private ApplicationDbContext _context = null!;
    private IssuedInvoiceRepository _repository = null!;

    private static readonly DateTime FromDate = new(2026, 1, 1);
    private static readonly DateTime ToDate = new(2026, 1, 31);

    public IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests(PostgresSharedContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync("issuedinvoices");

        // Minimal schema — only the columns GetSyncStatsAsync touches, no FKs or audit
        // columns, matching how PhotobankRepositoryGetTagsSqlShapeTests scopes its CREATE TABLE.
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE public."IssuedInvoices" (
                "Id"            varchar(64) NOT NULL PRIMARY KEY,
                "InvoiceDate"   timestamp without time zone NOT NULL,
                "IsSynced"      boolean NOT NULL,
                "ErrorType"     integer NULL,
                "LastSyncTime"  timestamp without time zone NULL
            );

            INSERT INTO public."IssuedInvoices" ("Id", "InvoiceDate", "IsSynced", "ErrorType", "LastSyncTime") VALUES
                ('INV-SYNCED',   '2026-01-10', true,  NULL, '2026-01-10 08:00:00'),
                ('INV-UNSYNCED', '2026-01-11', false, NULL, NULL),
                ('INV-ERROR',    '2026-01-12', false, 0,    '2026-01-12 09:00:00'),
                ('INV-PAIRED',   '2026-01-13', false, 1,    '2026-01-13 10:00:00'),
                ('INV-OLD',      '2025-12-01', false, NULL, NULL);
            """;
        await cmd.ExecuteNonQueryAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .AddInterceptors(_interceptor)
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new IssuedInvoiceRepository(_context, Mock.Of<ILogger<IssuedInvoiceRepository>>());
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task GetSyncStatsAsync_EmitsExactlyOneSqlCommand()
    {
        _interceptor.Reset();

        await _repository.GetSyncStatsAsync(FromDate, ToDate, CancellationToken.None);

        _interceptor.Commands.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetSyncStatsAsync_ReturnsCorrectStatsFromRealDatabase()
    {
        _interceptor.Reset();

        var stats = await _repository.GetSyncStatsAsync(FromDate, ToDate, CancellationToken.None);

        stats.TotalInvoices.Should().Be(4, "INV-OLD is outside the [2026-01-01, 2026-01-31] range");
        stats.SyncedInvoices.Should().Be(1, "only INV-SYNCED has IsSynced = true");
        stats.UnsyncedInvoices.Should().Be(3);
        stats.InvoicesWithErrors.Should().Be(2, "INV-ERROR and INV-PAIRED both have a non-null ErrorType");
        stats.CriticalErrors.Should().Be(1, "INV-PAIRED's ErrorType is InvoicePaired, which is excluded from CriticalErrors");
        stats.LastSyncTime.Should().Be(new DateTime(2026, 1, 13, 10, 0, 0), "INV-PAIRED has the latest LastSyncTime among in-range rows that have one");
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
