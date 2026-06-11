using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperationsSummary;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Catalog.Stock;
using Anela.Heblo.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class GetStockUpOperationsSummaryIntegrationTests : IAsyncLifetime
{
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;
    private ApplicationDbContext _context = null!;
    private StockUpOperationRepository _repository = null!;
    private GetStockUpOperationsSummaryHandler _handler = null!;

    public GetStockUpOperationsSummaryIntegrationTests(PostgresSharedContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync("catalog");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        _context = new ApplicationDbContext(options);

        // Create only the StockUpOperations table + indexes we exercise.
        // Running full EF migrations brings in too many unrelated tables; this keeps the test fast.
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS public."StockUpOperations" (
                "Id"             serial NOT NULL PRIMARY KEY,
                "DocumentNumber" varchar(100) NOT NULL,
                "ProductCode"    varchar(50)  NOT NULL,
                "Amount"         integer NOT NULL,
                "SourceType"     integer NOT NULL,
                "SourceId"       integer NOT NULL,
                "State"          integer NOT NULL,
                "CreatedAt"      timestamp with time zone NOT NULL,
                "SubmittedAt"    timestamp with time zone NULL,
                "CompletedAt"    timestamp with time zone NULL,
                "ErrorMessage"   varchar(2000) NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_StockUpOperations_DocumentNumber_Unique"
                ON public."StockUpOperations" ("DocumentNumber");

            CREATE INDEX IF NOT EXISTS "IX_StockUpOperations_State"
                ON public."StockUpOperations" ("State");

            CREATE INDEX IF NOT EXISTS "IX_StockUpOperations_Source"
                ON public."StockUpOperations" ("SourceType", "SourceId");

            CREATE INDEX IF NOT EXISTS "IX_StockUpOperations_State_CreatedAt"
                ON public."StockUpOperations" ("State", "CreatedAt");

            CREATE INDEX IF NOT EXISTS "IX_StockUpOperations_State_Active"
                ON public."StockUpOperations" ("SourceType", "State")
                WHERE "State" IN (0, 1, 3);
            """;
        await cmd.ExecuteNonQueryAsync();

        _repository = new StockUpOperationRepository(
            _context,
            NullLogger<StockUpOperationRepository>.Instance);
        _handler = new GetStockUpOperationsSummaryHandler(
            _repository,
            NullLogger<GetStockUpOperationsSummaryHandler>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    private async Task SeedAsync(params (StockUpSourceType source, StockUpOperationState state)[] ops)
    {
        var n = 0;
        foreach (var (source, state) in ops)
        {
            n++;
            var op = new StockUpOperation($"DOC-{n:D6}", $"P{n}", 1, source, n);
            switch (state)
            {
                case StockUpOperationState.Pending:
                    break;
                case StockUpOperationState.Submitted:
                    op.MarkAsSubmitted(DateTime.UtcNow);
                    break;
                case StockUpOperationState.Completed:
                    op.MarkAsCompleted(DateTime.UtcNow);
                    break;
                case StockUpOperationState.Failed:
                    op.MarkAsSubmitted(DateTime.UtcNow);
                    op.MarkAsFailed(DateTime.UtcNow, "test");
                    break;
            }
            _context.Set<StockUpOperation>().Add(op);
        }
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_MixedStates_ReturnsCorrectCounts()
    {
        // Arrange
        await SeedAsync(
            (StockUpSourceType.GiftPackageManufacture, StockUpOperationState.Pending),
            (StockUpSourceType.GiftPackageManufacture, StockUpOperationState.Pending),
            (StockUpSourceType.GiftPackageManufacture, StockUpOperationState.Submitted),
            (StockUpSourceType.GiftPackageManufacture, StockUpOperationState.Failed),
            (StockUpSourceType.GiftPackageManufacture, StockUpOperationState.Completed),
            (StockUpSourceType.GiftPackageManufacture, StockUpOperationState.Completed));

        // Act
        var response = await _handler.Handle(
            new GetStockUpOperationsSummaryRequest(),
            CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(2, response.PendingCount);
        Assert.Equal(1, response.SubmittedCount);
        Assert.Equal(1, response.FailedCount);
    }

    [Fact]
    public async Task Handle_WithSourceTypeFilter_CountsOnlyMatchingSource()
    {
        // Arrange
        await SeedAsync(
            (StockUpSourceType.GiftPackageManufacture, StockUpOperationState.Pending),
            (StockUpSourceType.TransportBox, StockUpOperationState.Pending));

        // Act
        var response = await _handler.Handle(
            new GetStockUpOperationsSummaryRequest { SourceType = StockUpSourceType.GiftPackageManufacture },
            CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(1, response.PendingCount);
        Assert.Equal(0, response.SubmittedCount);
        Assert.Equal(0, response.FailedCount);
    }

    [Fact]
    public async Task Handle_NoActiveOperations_ReturnsZeroCounts()
    {
        // Arrange — only Completed rows; none should be counted
        await SeedAsync(
            (StockUpSourceType.GiftPackageManufacture, StockUpOperationState.Completed),
            (StockUpSourceType.GiftPackageManufacture, StockUpOperationState.Completed),
            (StockUpSourceType.GiftPackageManufacture, StockUpOperationState.Completed));

        // Act
        var response = await _handler.Handle(
            new GetStockUpOperationsSummaryRequest(),
            CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(0, response.PendingCount);
        Assert.Equal(0, response.SubmittedCount);
        Assert.Equal(0, response.FailedCount);
    }

    [Fact]
    public async Task Handle_QueryPlan_DoesNotUseSeqScan()
    {
        // Seed enough rows so the planner will prefer the partial index over a Seq Scan.
        // ~970 Completed rows + 30 active rows = planner should pick partial index for active states.
        for (var i = 0; i < 970; i++)
        {
            var op = new StockUpOperation($"DOC-C-{i:D6}", $"P{i}", 1, StockUpSourceType.GiftPackageManufacture, i);
            op.MarkAsCompleted(DateTime.UtcNow);
            _context.Set<StockUpOperation>().Add(op);
        }
        for (var i = 0; i < 30; i++)
        {
            _context.Set<StockUpOperation>().Add(
                new StockUpOperation($"DOC-A-{i:D6}", $"PA{i}", 1, StockUpSourceType.GiftPackageManufacture, 10000 + i));
        }
        await _context.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // ANALYZE so the planner has fresh statistics
        await using (var analyze = conn.CreateCommand())
        {
            analyze.CommandText = "ANALYZE public.\"StockUpOperations\";";
            await analyze.ExecuteNonQueryAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            EXPLAIN (FORMAT JSON)
            SELECT "State", COUNT(*)
            FROM public."StockUpOperations"
            WHERE "State" IN (0, 1, 3)
            GROUP BY "State";
            """;
        var planJson = (string)(await cmd.ExecuteScalarAsync())!;

        // The plan should reference the partial index. Seq Scan on StockUpOperations must not appear.
        Assert.DoesNotContain("\"Node Type\": \"Seq Scan\"", planJson);
        Assert.Contains("IX_StockUpOperations_State_Active", planJson);
    }
}
