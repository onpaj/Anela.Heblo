### task: add-gift-package-manufacture-atomicity-integration-tests

**Context:** Moq-based unit tests cannot prove real transactional rollback — that requires a real relational database. The codebase's established pattern for this is `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs`, which uses the shared `PostgresSharedContainerFixture` Testcontainers fixture, raw-SQL `CREATE TABLE IF NOT EXISTS` for a minimal schema, and a `SaveChangesInterceptor` that throws on a specific `SaveChangesAsync` call to simulate a mid-write failure — then asserts zero rows survive.

That existing test's `DbContext` is built the bare way (`new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_connectionString)`), which uses EF Core's default, non-retrying execution strategy — one that already permits `BeginTransactionAsync` directly. This new test **must not** copy that detail: FR-3's whole reason to route through `Context.Database.CreateExecutionStrategy().ExecuteAsync(...)` (instead of calling `BeginTransactionAsync` directly) is that the real app is configured with `PollyExecutionStrategy`, which reports `RetriesOnFailure => true` and therefore rejects a bare `BeginTransactionAsync`. If this test's `DbContext` doesn't also configure `PollyExecutionStrategy`, it would never exercise the one behavior (FR-3 acceptance criterion (c)) that `ExecuteInTransactionAsync` exists to satisfy. `PollyExecutionStrategy`'s constructor takes three dependencies, all cheaply constructible outside full DI:

```csharp
public PollyExecutionStrategy(
    ExecutionStrategyDependencies dependencies,     // supplied automatically by the `deps` parameter of npgsql.ExecutionStrategy(deps => ...)
    IDbResiliencePipelineProvider pipelineProvider,  // new DbResiliencePipelineProvider(Options.Create(new DbResilienceOptions()), metrics, NullLogger<DbResiliencePipelineProvider>.Instance)
    DbResilienceMetrics metrics,                     // new DbResilienceMetrics(a trivial IMeterFactory)
    ILogger<PollyExecutionStrategy> logger)          // NullLogger<PollyExecutionStrategy>.Instance
```

The failure-injection exception must be non-transient (per `TransientErrorClassifier.IsTransient`, a plain `InvalidOperationException` with no inner `PostgresException`/`SocketException`/etc. is never treated as transient), otherwise Polly's retry pipeline would retry the whole operation and it could spuriously succeed on a later attempt instead of demonstrating rollback. This mirrors the existing transport-box test's own `ThrowOnFirstSaveInterceptor`, which uses `InvalidOperationException` for exactly this reason.

The child entity's FK to the log is `ManufactureLogId` (not `LogId`) — see `GiftPackageManufactureItem.cs`.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufacture/GiftPackageManufactureAtomicityIntegrationTests.cs`

- [ ] **Step 1: Create the test file with schema setup, the Polly-configured context helper, and one happy-path test**

Create `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufacture/GiftPackageManufactureAtomicityIntegrationTests.cs`:

```csharp
using System.Diagnostics.Metrics;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.Contracts.Models;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Catalog.Stock;
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using Anela.Heblo.Persistence.Logistics.GiftPackageManufacture;
using Anela.Heblo.Tests.Common;
using AutoMapper;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.GiftPackageManufacture;

/// <summary>
/// Proves that CreateManufactureAsync/DisassembleGiftPackageAsync commit the
/// GiftPackageManufactureLog row, its GiftPackageManufactureItem children, and every
/// StockUpOperation row together — or none of them — even though several separate
/// SaveChangesAsync calls are involved. Unlike ChangeTransportBoxStateReceiveAtomicityIntegrationTests
/// (whose atomicity comes for free from one SaveChangesAsync call), this feature needs an
/// explicit transaction because the log's DB-generated Id must exist before the stock
/// operations that reference it can be created — so the DbContext here is deliberately built
/// WITH PollyExecutionStrategy configured (unlike that other test), to actually exercise the
/// CreateExecutionStrategy().ExecuteAsync(...) path FR-3 introduces.
/// </summary>
[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class GiftPackageManufactureAtomicityIntegrationTests : IAsyncLifetime
{
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;

    public GiftPackageManufactureAtomicityIntegrationTests(PostgresSharedContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync("giftpackage_manufacture_atomicity");

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS public."GiftPackageManufactureLogs" (
                "Id"                     serial NOT NULL PRIMARY KEY,
                "GiftPackageCode"        varchar(50) NOT NULL,
                "QuantityCreated"        integer NOT NULL,
                "StockOverrideApplied"   boolean NOT NULL,
                "CreatedAt"              timestamp without time zone NOT NULL,
                "CreatedBy"              text NOT NULL,
                "OperationType"          integer NOT NULL
            );

            CREATE TABLE IF NOT EXISTS public."GiftPackageManufactureItems" (
                "Id"                serial NOT NULL PRIMARY KEY,
                "ManufactureLogId"  integer NOT NULL REFERENCES public."GiftPackageManufactureLogs" ("Id") ON DELETE CASCADE,
                "ProductCode"       varchar(50) NOT NULL,
                "QuantityConsumed"  integer NOT NULL
            );

            CREATE INDEX IF NOT EXISTS "IX_GiftPackageManufactureLogs_CreatedAt"
                ON public."GiftPackageManufactureLogs" ("CreatedAt");

            CREATE INDEX IF NOT EXISTS "IX_GiftPackageManufactureLogs_GiftPackageCode"
                ON public."GiftPackageManufactureLogs" ("GiftPackageCode");

            CREATE INDEX IF NOT EXISTS "IX_GiftPackageManufactureLogs_OperationType"
                ON public."GiftPackageManufactureLogs" ("OperationType");

            CREATE INDEX IF NOT EXISTS "IX_GiftPackageManufactureItems_ManufactureLogId"
                ON public."GiftPackageManufactureItems" ("ManufactureLogId");

            CREATE INDEX IF NOT EXISTS "IX_GiftPackageManufactureItems_ProductCode"
                ON public."GiftPackageManufactureItems" ("ProductCode");

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
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options.Name, options.Version);
        public void Dispose() { }
    }

    /// <summary>
    /// Builds an ApplicationDbContext with PollyExecutionStrategy configured, exactly like
    /// production (PersistenceModule.AddPersistenceServices) — deliberately NOT the bare
    /// UseNpgsql(connectionString) pattern most other integration tests in this repo use,
    /// because BaseRepository.ExecuteInTransactionAsync's whole reason to route through
    /// CreateExecutionStrategy().ExecuteAsync(...) only gets exercised under a retrying strategy.
    /// </summary>
    private ApplicationDbContext CreateContext(IInterceptor? interceptor = null)
    {
        var metrics = new DbResilienceMetrics(new TestMeterFactory());
        var pipelineProvider = new DbResiliencePipelineProvider(
            Options.Create(new DbResilienceOptions()),
            metrics,
            NullLogger<DbResiliencePipelineProvider>.Instance);

        var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString, npgsql =>
            {
                npgsql.ExecutionStrategy(deps =>
                    new PollyExecutionStrategy(
                        deps,
                        pipelineProvider,
                        metrics,
                        NullLogger<PollyExecutionStrategy>.Instance));
            });

        if (interceptor != null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new ApplicationDbContext(builder.Options);
    }

    private static GiftPackageManufactureService CreateService(
        ApplicationDbContext context,
        Mock<IManufactureClient> manufactureClientMock,
        Mock<ILogisticsCatalogSource> catalogSourceMock,
        Mock<IMapper> mapperMock)
    {
        var giftPackageRepository = new GiftPackageManufactureRepository(context);
        var stockUpRepository = new StockUpOperationRepository(context, NullLogger<StockUpOperationRepository>.Instance);
        var stockUpProcessingService = new StockUpProcessingService(
            stockUpRepository, Mock.Of<IEshopStockDomainService>(), NullLogger<StockUpProcessingService>.Instance);
        var stockOperationAdapter = new LogisticsStockOperationAdapter(stockUpProcessingService);

        return new GiftPackageManufactureService(
            manufactureClientMock.Object,
            giftPackageRepository,
            catalogSourceMock.Object,
            stockOperationAdapter,
            mapperMock.Object,
            TimeProvider.System,
            NullLogger<GiftPackageManufactureService>.Instance);
    }

    /// <summary>
    /// Configures a 2-ingredient BOM for "SET001": ING001 (Amount 2.0) and ING002 (Amount 1.5).
    /// </summary>
    private static void SetupTwoIngredientBom(
        Mock<IManufactureClient> manufactureClientMock,
        Mock<ILogisticsCatalogSource> catalogSourceMock,
        string giftPackageCode)
    {
        var product = new LogisticsGiftPackageItem
        {
            ProductCode = giftPackageCode,
            ProductName = "Test Gift Set",
            AvailableStock = 100m,
            TotalSoldInPeriod = 50,
            StockMinSetup = 5,
            OptimalStockDaysSetup = 30,
        };

        catalogSourceMock
            .Setup(x => x.GetGiftPackageAsync(giftPackageCode, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        manufactureClientMock
            .Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPart>
            {
                new ProductPart { ProductCode = "ING001", ProductName = "Ingredient 1", Amount = 2.0 },
                new ProductPart { ProductCode = "ING002", ProductName = "Ingredient 2", Amount = 1.5 },
            });

        catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) => new LogisticsCatalogItem { ProductCode = code, AvailableStock = 100m });
    }

    [Fact]
    public async Task CreateManufactureAsync_Succeeds_CommitsLogItemsAndStockOperationsTogether()
    {
        var manufactureClientMock = new Mock<IManufactureClient>();
        var catalogSourceMock = new Mock<ILogisticsCatalogSource>();
        var mapperMock = new Mock<IMapper>();
        SetupTwoIngredientBom(manufactureClientMock, catalogSourceMock, "SET001");
        mapperMock
            .Setup(m => m.Map<GiftPackageManufactureDto>(It.IsAny<GiftPackageManufactureLog>()))
            .Returns((GiftPackageManufactureLog log) => new GiftPackageManufactureDto
            {
                Id = log.Id,
                GiftPackageCode = log.GiftPackageCode,
                QuantityCreated = log.QuantityCreated,
                CreatedBy = log.CreatedBy,
                CreatedAt = log.CreatedAt,
            });

        await using var context = CreateContext();
        var service = CreateService(context, manufactureClientMock, catalogSourceMock, mapperMock);

        var result = await service.CreateManufactureAsync("SET001", 5, false, "tester", CancellationToken.None);

        result.Id.Should().BeGreaterThan(0);

        await using var readContext = CreateContext();
        var logs = await readContext.Set<GiftPackageManufactureLog>()
            .Include(x => x.ConsumedItems)
            .ToListAsync();
        logs.Should().HaveCount(1);
        logs[0].ConsumedItems.Should().HaveCount(2);

        var stockOps = await readContext.Set<StockUpOperation>()
            .Where(op => op.SourceType == StockUpSourceType.GiftPackageManufacture && op.SourceId == logs[0].Id)
            .ToListAsync();
        stockOps.Should().HaveCount(3); // 2 ingredient stock-downs + 1 output stock-up
        stockOps.Should().Contain(op => op.DocumentNumber == $"GPM-{logs[0].Id:000000}-SET001" && op.Amount == 5);
        stockOps.Should().Contain(op => op.DocumentNumber == $"GPM-{logs[0].Id:000000}-ING001" && op.Amount == -10);
        stockOps.Should().Contain(op => op.DocumentNumber == $"GPM-{logs[0].Id:000000}-ING002" && op.Amount == -7);
    }
}
```

- [ ] **Step 2: Run the happy-path test**

Requires Docker running locally (Testcontainers pulls/starts a `postgres:16` container).

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureAtomicityIntegrationTests"
```

Expected: `Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1`

- [ ] **Step 3: Add the failure-injection interceptor and the `CreateManufactureAsync` rollback test**

In the same file, add the interceptor as a new private nested class (placed after `TestMeterFactory`, before `CreateContext`) and the new test method (placed after `CreateManufactureAsync_Succeeds_CommitsLogItemsAndStockOperationsTogether`):

```csharp
    /// <summary>
    /// Throws a non-transient exception on the Nth SavingChangesAsync call. Non-transient (per
    /// TransientErrorClassifier.IsTransient) so PollyExecutionStrategy's retry pipeline does not
    /// mask it by retrying — the same reasoning as the transport-box atomicity test's own
    /// ThrowOnFirstSaveInterceptor, generalized to a configurable call number.
    /// </summary>
    private sealed class ThrowOnNthSaveInterceptor : SaveChangesInterceptor
    {
        private readonly int _failOnCallNumber;
        private int _callCount;

        public ThrowOnNthSaveInterceptor(int failOnCallNumber)
        {
            _failOnCallNumber = failOnCallNumber;
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            _callCount++;
            if (_callCount == _failOnCallNumber)
            {
                throw new InvalidOperationException($"Simulated failure on save #{_failOnCallNumber}");
            }

            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            _callCount++;
            if (_callCount == _failOnCallNumber)
            {
                throw new InvalidOperationException($"Simulated failure on save #{_failOnCallNumber}");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
```

```csharp
    [Fact]
    public async Task CreateManufactureAsync_SaveChangesFailsOnFinalOperation_RollsBackLogItemsAndStockOperationsTogether()
    {
        // CreateManufactureAsync issues 4 SaveChangesAsync calls for a 2-ingredient BOM: the log
        // (#1), one per ingredient stock-down (#2, #3), and the output stock-up (#4). Failing on
        // the LAST call proves that writes from calls #1-#3 — already sent to Postgres but not yet
        // committed, since they share one ambient transaction — are rolled back too, not just that
        // the 4th write never landed.
        var manufactureClientMock = new Mock<IManufactureClient>();
        var catalogSourceMock = new Mock<ILogisticsCatalogSource>();
        var mapperMock = new Mock<IMapper>();
        SetupTwoIngredientBom(manufactureClientMock, catalogSourceMock, "SET001");
        mapperMock
            .Setup(m => m.Map<GiftPackageManufactureDto>(It.IsAny<GiftPackageManufactureLog>()))
            .Returns((GiftPackageManufactureLog log) => new GiftPackageManufactureDto { Id = log.Id, GiftPackageCode = log.GiftPackageCode });

        await using var context = CreateContext(new ThrowOnNthSaveInterceptor(failOnCallNumber: 4));
        var service = CreateService(context, manufactureClientMock, catalogSourceMock, mapperMock);

        var ex = await Record.ExceptionAsync(() =>
            service.CreateManufactureAsync("SET001", 5, false, "tester", CancellationToken.None));

        ex.Should().BeOfType<InvalidOperationException>();

        await using var readContext = CreateContext();
        (await readContext.Set<GiftPackageManufactureLog>().CountAsync()).Should().Be(0);
        (await readContext.Set<GiftPackageManufactureItem>().CountAsync()).Should().Be(0);
        (await readContext.Set<StockUpOperation>().CountAsync()).Should().Be(0);
    }
```

- [ ] **Step 4: Run both `CreateManufactureAsync` tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureAtomicityIntegrationTests"
```

Expected: `Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2`

- [ ] **Step 5: Add the `DisassembleGiftPackageAsync` happy-path and rollback tests**

Add both methods after `CreateManufactureAsync_SaveChangesFailsOnFinalOperation_RollsBackLogItemsAndStockOperationsTogether`:

```csharp
    [Fact]
    public async Task DisassembleGiftPackageAsync_Succeeds_CommitsLogItemsAndStockOperationsTogether()
    {
        var manufactureClientMock = new Mock<IManufactureClient>();
        var catalogSourceMock = new Mock<ILogisticsCatalogSource>();
        var mapperMock = new Mock<IMapper>();
        SetupTwoIngredientBom(manufactureClientMock, catalogSourceMock, "SET001");

        await using var context = CreateContext();
        var service = CreateService(context, manufactureClientMock, catalogSourceMock, mapperMock);

        var result = await service.DisassembleGiftPackageAsync("SET001", 3, "tester", CancellationToken.None);

        result.ReturnedComponents.Should().HaveCount(2);

        await using var readContext = CreateContext();
        var logs = await readContext.Set<GiftPackageManufactureLog>()
            .Include(x => x.ConsumedItems)
            .Where(x => x.OperationType == GiftPackageOperationType.Disassembly)
            .ToListAsync();
        logs.Should().HaveCount(1);
        logs[0].ConsumedItems.Should().HaveCount(2);

        var stockOps = await readContext.Set<StockUpOperation>()
            .Where(op => op.SourceType == StockUpSourceType.GiftPackageManufacture && op.SourceId == logs[0].Id)
            .ToListAsync();
        stockOps.Should().HaveCount(3); // 1 package stock-down + 2 component stock-ups
        stockOps.Should().Contain(op => op.DocumentNumber == $"GPD-{logs[0].Id:000000}-SET001" && op.Amount == -3);
        stockOps.Should().Contain(op => op.DocumentNumber == $"GPD-{logs[0].Id:000000}-ING001" && op.Amount == 6);
        stockOps.Should().Contain(op => op.DocumentNumber == $"GPD-{logs[0].Id:000000}-ING002" && op.Amount == 4);
    }

    [Fact]
    public async Task DisassembleGiftPackageAsync_SaveChangesFailsOnFinalOperation_RollsBackLogItemsAndStockOperationsTogether()
    {
        // Same 4-SaveChangesAsync shape as CreateManufactureAsync: log (#1), package stock-down
        // (#2), then one stock-up per component (#3, #4). Failing on the last call again proves
        // the earlier, already-sent writes roll back too.
        var manufactureClientMock = new Mock<IManufactureClient>();
        var catalogSourceMock = new Mock<ILogisticsCatalogSource>();
        var mapperMock = new Mock<IMapper>();
        SetupTwoIngredientBom(manufactureClientMock, catalogSourceMock, "SET001");

        await using var context = CreateContext(new ThrowOnNthSaveInterceptor(failOnCallNumber: 4));
        var service = CreateService(context, manufactureClientMock, catalogSourceMock, mapperMock);

        var ex = await Record.ExceptionAsync(() =>
            service.DisassembleGiftPackageAsync("SET001", 3, "tester", CancellationToken.None));

        ex.Should().BeOfType<InvalidOperationException>();

        await using var readContext = CreateContext();
        (await readContext.Set<GiftPackageManufactureLog>()
            .CountAsync(x => x.OperationType == GiftPackageOperationType.Disassembly)).Should().Be(0);
        (await readContext.Set<GiftPackageManufactureItem>().CountAsync()).Should().Be(0);
        (await readContext.Set<StockUpOperation>().CountAsync()).Should().Be(0);
    }
```

- [ ] **Step 6: Run the whole test class**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureAtomicityIntegrationTests"
```

Expected: `Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4`

- [ ] **Step 7: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufacture/GiftPackageManufactureAtomicityIntegrationTests.cs
git commit -m "test: add Postgres integration tests proving gift package manufacture/disassembly atomicity

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01LkwnHn1TLfFi9jP5okeVwP"
```

---
