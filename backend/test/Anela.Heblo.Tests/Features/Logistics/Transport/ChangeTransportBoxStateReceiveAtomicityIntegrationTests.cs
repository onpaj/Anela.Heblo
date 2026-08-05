using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Catalog.Stock;
using Anela.Heblo.Persistence.Logistics.TransportBoxes;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

/// <summary>
/// Proves that ChangeTransportBoxStateHandler.HandleReceived (InTransit/Reserve/Quarantine -&gt; Received)
/// commits StockUpOperation rows and the box's own state transition as a single atomic unit, and that
/// retrying Receive after a failure (or against a legacy pre-existing row) is idempotent. This is a
/// property of EF Core's implicit SaveChanges transaction against a real relational database, not of
/// handler control flow, so it must run against real Postgres rather than mocks/InMemory.
/// </summary>
[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class ChangeTransportBoxStateReceiveAtomicityIntegrationTests : IAsyncLifetime
{
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;

    public ChangeTransportBoxStateReceiveAtomicityIntegrationTests(PostgresSharedContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync("transportbox_receive");

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

    private ApplicationDbContext CreateContext(IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_connectionString);
        if (interceptor != null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new ApplicationDbContext(builder.Options);
    }

    private static ChangeTransportBoxStateHandler CreateHandler(ApplicationDbContext context, out TransportBoxRepository transportBoxRepository)
    {
        transportBoxRepository = new TransportBoxRepository(context, NullLogger<TransportBoxRepository>.Instance);
        var stockUpRepository = new StockUpOperationRepository(context, NullLogger<StockUpOperationRepository>.Instance);
        var stockUpProcessingService = new StockUpProcessingService(
            stockUpRepository, Mock.Of<IEshopStockDomainService>(), NullLogger<StockUpProcessingService>.Instance);
        var adapter = new LogisticsStockOperationAdapter(stockUpProcessingService);

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("tester", "Tester", "tester@test.com", true));

        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetTransportBoxByIdResponse());

        return new ChangeTransportBoxStateHandler(
            transportBoxRepository,
            Mock.Of<IInventoryReservationService>(),
            mediator.Object,
            NullLogger<ChangeTransportBoxStateHandler>.Instance,
            currentUserService.Object,
            adapter,
            TimeProvider.System);
    }

    private static async Task<TransportBox> SeedBoxInTransitAsync(
        ApplicationDbContext context, params (string ProductCode, double Amount)[] items)
    {
        var box = new TransportBox();
        box.Open("B001", DateTime.UtcNow, "seed-user");
        foreach (var (productCode, amount) in items)
        {
            box.AddItem(productCode, productCode, amount, DateTime.UtcNow, "seed-user");
        }

        box.ToTransit(DateTime.UtcNow, "seed-user");

        context.Set<TransportBox>().Add(box);
        await context.SaveChangesAsync();
        return box;
    }

    /// <summary>
    /// Throws a non-transient exception on the first SavingChangesAsync so it is never masked by
    /// PollyExecutionStrategy's transient-error retry (DbResiliencePipelineProvider only retries
    /// connection/socket/timeout-classed exceptions per TransientErrorClassifier). This context is
    /// built the bare way (no .ExecutionStrategy configured), so no retry layer is even in play —
    /// belt-and-braces per architecture-01.md's required test-design correction.
    /// </summary>
    private sealed class ThrowOnFirstSaveInterceptor : SaveChangesInterceptor
    {
        private bool _thrown;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (!_thrown)
            {
                _thrown = true;
                throw new InvalidOperationException("Simulated non-transient save failure");
            }

            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_thrown)
            {
                _thrown = true;
                throw new InvalidOperationException("Simulated non-transient save failure");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task Receive_SaveChangesFails_RollsBackBoxAndStockUpOperationsTogether()
    {
        // Arrange — seed a box with two products, InTransit
        int boxId;
        await using (var seedContext = CreateContext())
        {
            var box = await SeedBoxInTransitAsync(seedContext, ("PRODA", 3), ("PRODB", 2));
            boxId = box.Id;
        }

        // Act — Receive it through a context whose single SaveChangesAsync call is forced to fail
        await using (var failingContext = CreateContext(new ThrowOnFirstSaveInterceptor()))
        {
            var handler = CreateHandler(failingContext, out _);
            var request = new ChangeTransportBoxStateRequest { BoxId = boxId, NewState = TransportBoxState.Received };

            var result = await handler.Handle(request, CancellationToken.None);

            result.Success.Should().BeFalse();
            result.ErrorCode.Should().Be(ErrorCodes.TransportBoxStateChangeError);
        }

        // Assert — nothing from the failed attempt survived: box unchanged, zero StockUpOperation rows
        await using var readContext = CreateContext();
        var reloadedBox = await readContext.Set<TransportBox>().FirstAsync(b => b.Id == boxId);
        reloadedBox.State.Should().Be(TransportBoxState.InTransit);

        var operationCount = await readContext.Set<StockUpOperation>()
            .CountAsync(op => op.SourceType == StockUpSourceType.TransportBox && op.SourceId == boxId);
        operationCount.Should().Be(0);
    }

    [Fact]
    public async Task Receive_Retried_ExistingStockUpOperationIsSkippedAndMissingOnesAreCreated()
    {
        // Arrange — box InTransit with three products; product A already has a StockUpOperation row,
        // simulating either a legacy pre-fix wedge or the aftermath of a real (non-simulated) partial
        // failure that happened outside this app's control.
        int boxId;
        int preExistingOperationId;
        await using (var seedContext = CreateContext())
        {
            var box = await SeedBoxInTransitAsync(seedContext, ("PRODA", 3), ("PRODB", 2), ("PRODC", 1));
            boxId = box.Id;

            var preExisting = new StockUpOperation(
                $"BOX-{boxId:000000}-PRODA", "PRODA", 3, StockUpSourceType.TransportBox, boxId);
            seedContext.Set<StockUpOperation>().Add(preExisting);
            await seedContext.SaveChangesAsync();
            preExistingOperationId = preExisting.Id;
        }

        // Act
        await using (var context = CreateContext())
        {
            var handler = CreateHandler(context, out _);
            var request = new ChangeTransportBoxStateRequest { BoxId = boxId, NewState = TransportBoxState.Received };

            var result = await handler.Handle(request, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ErrorCode.Should().BeNull();
        }

        // Assert — box transitioned, exactly one operation per product, the pre-existing row untouched
        await using var readContext = CreateContext();
        var reloadedBox = await readContext.Set<TransportBox>().FirstAsync(b => b.Id == boxId);
        reloadedBox.State.Should().Be(TransportBoxState.Received);

        var operations = await readContext.Set<StockUpOperation>()
            .Where(op => op.SourceType == StockUpSourceType.TransportBox && op.SourceId == boxId)
            .ToListAsync();

        operations.Should().HaveCount(3);
        operations.Select(op => op.ProductCode).Should().BeEquivalentTo("PRODA", "PRODB", "PRODC");

        var productAOperation = operations.Single(op => op.ProductCode == "PRODA");
        productAOperation.Id.Should().Be(preExistingOperationId);
    }
}
