using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Purchase.PurchaseOrders;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class PurchaseOrderRepositoryHistorySqlShapeTests : IAsyncLifetime
{
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;
    private readonly CapturingCommandInterceptor _interceptor = new();
    private ApplicationDbContext _context = null!;
    private PurchaseOrderRepository _repository = null!;

    public PurchaseOrderRepositoryHistorySqlShapeTests(PostgresSharedContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync("purchase");

        // Minimal schema — only the PurchaseOrderHistory table the query touches.
        // No FKs / no PurchaseOrders / no PurchaseOrderLines, so any unintended JOIN would fail at execution time.
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE public."PurchaseOrderHistory" (
                "Id"              serial      NOT NULL PRIMARY KEY,
                "PurchaseOrderId" integer     NOT NULL,
                "Action"          varchar(50) NOT NULL,
                "OldValue"        varchar(2000),
                "NewValue"        varchar(2000),
                "ChangedBy"       varchar(200) NOT NULL,
                "ChangedAt"       timestamp   NOT NULL
            );
            CREATE INDEX "IX_PurchaseOrderHistory_PurchaseOrderId" ON public."PurchaseOrderHistory" ("PurchaseOrderId");
            CREATE INDEX "IX_PurchaseOrderHistory_ChangedAt"      ON public."PurchaseOrderHistory" ("ChangedAt");

            INSERT INTO public."PurchaseOrderHistory"
                ("PurchaseOrderId","Action","OldValue","NewValue","ChangedBy","ChangedAt") VALUES
                (42, 'Created', NULL, 'Draft', 'user-1', now() - interval '2 minute'),
                (42, 'StatusChanged', 'Draft', 'InTransit', 'user-2', now() - interval '1 minute'),
                (43, 'Created', NULL, 'Draft', 'user-3', now());
            """;
        await cmd.ExecuteNonQueryAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .AddInterceptors(_interceptor)
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PurchaseOrderRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task GetHistoryAsync_EmitsSqlThatTouchesOnlyHistoryTable()
    {
        _interceptor.Reset();

        var rows = await _repository.GetHistoryAsync(42, CancellationToken.None);

        rows.Should().HaveCount(2);

        var sql = _interceptor.Commands.Should().ContainSingle().Subject;
        sql.Should().Contain("PurchaseOrderHistory", "the query must read from the history table");
        sql.Should().NotContainEquivalentOf("\"PurchaseOrders\"", "GetHistoryAsync must not JOIN to PurchaseOrders");
        sql.Should().NotContainEquivalentOf("\"PurchaseOrderLines\"", "GetHistoryAsync must not JOIN to PurchaseOrderLines");
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
