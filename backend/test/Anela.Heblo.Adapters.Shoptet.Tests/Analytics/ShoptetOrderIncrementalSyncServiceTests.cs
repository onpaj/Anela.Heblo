using Anela.Heblo.Adapters.ShoptetApi.Analytics;
using Anela.Heblo.Adapters.ShoptetApi.Analytics.Model;
using Anela.Heblo.Persistence.ShoptetOrders;
using Anela.Heblo.Persistence.ShoptetOrders.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Analytics;

public class ShoptetOrderIncrementalSyncServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 1, 30, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    private static ShoptetOrdersDbContext CreateContext(string name) =>
        new(new DbContextOptionsBuilder<ShoptetOrdersDbContext>()
            .UseInMemoryDatabase(name).Options);

    private static (ShoptetOrderIncrementalSyncService Service, FakeShoptetOrderAnalyticsClient Client)
        CreateService(ShoptetOrdersDbContext ctx, FakeTimeProvider clock)
    {
        var opts = Options.Create(new ShoptetOrdersSyncOptions
        {
            BatchSize = 50,
            WatermarkSafetyMarginHours = 2,
        });
        var client = new FakeShoptetOrderAnalyticsClient();
        var store = new ShoptetOrderStore(ctx);
        var ingestor = new ShoptetOrderIngestor(client, store, opts, clock, NullLogger<ShoptetOrderIngestor>.Instance);

        var service = new ShoptetOrderIncrementalSyncService(
            client, ingestor, store,
            new ShoptetSyncWatermarkRepository(ctx),
            opts, clock,
            NullLogger<ShoptetOrderIncrementalSyncService>.Instance);

        return (service, client);
    }

    [Fact]
    public async Task SyncAsync_reads_the_change_log_from_the_watermark_minus_the_safety_margin()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        ctx.SyncStates.Add(new ShoptetSyncState
        {
            EntityName = ShoptetOrderIncrementalSyncService.EntityNameConst,
            Watermark = Now.AddDays(-1),
        });
        await ctx.SaveChangesAsync();

        var (service, client) = CreateService(ctx, new FakeTimeProvider(Now));

        // Act
        await service.SyncAsync();

        // Assert — LedgerSyncService's AddHours(-1) pattern, widened to the configured margin.
        // Both sources are read from the same point: the log for deletions, the listing for
        // everything else (including creations, which the log is not documented to report).
        client.ChangeLogQueriesRequested.Should().ContainSingle()
            .Which.Should().Be(Now.AddDays(-1).AddHours(-2));
        client.ChangeQueriesRequested.Should().ContainSingle()
            .Which.Should().Be(Now.AddDays(-1).AddHours(-2));
    }

    [Fact]
    public async Task SyncAsync_upserts_an_edited_order_and_advances_the_watermark()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        var clock = new FakeTimeProvider(Now);
        var (service, client) = CreateService(ctx, clock);
        client.AddOrder("A1", Now.AddDays(-2), ShoptetOrderTestData.SimpleOrderJson);
        client.Changes.Add(new ShoptetOrderChangeDto
        {
            Code = "A1",
            ChangeTime = Now.AddHours(-3),
            ChangeType = "edit",
        });

        // Act
        var result = await service.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RowsUpserted.Should().Be(1);

        await using var verify = CreateContext(dbName);
        (await verify.Orders.CountAsync()).Should().Be(1);
        var state = await verify.SyncStates.SingleAsync(
            s => s.EntityName == ShoptetOrderIncrementalSyncService.EntityNameConst);
        state.Watermark.Should().Be(Now);
        state.LastRunStatus.Should().Be("OK");
    }

    [Fact]
    public async Task SyncAsync_removes_an_order_the_change_log_reports_as_deleted()
    {
        // Arrange — a deleted order simply stops appearing in GET /api/orders, so only the change
        // log can tell us to drop it.
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        var seeded = ShoptetOrderMapper.Map(
            ShoptetOrderTestData.Parse(ShoptetOrderTestData.SimpleOrderJson),
            "{}", Prague, Now);
        await new ShoptetOrderStore(ctx).UpsertAsync([seeded]);

        var (service, client) = CreateService(ctx, new FakeTimeProvider(Now));
        client.Changes.Add(new ShoptetOrderChangeDto
        {
            Code = seeded.Code,
            ChangeTime = Now.AddHours(-1),
            ChangeType = "delete",
        });

        // Act
        await service.SyncAsync();

        // Assert
        await using var verify = CreateContext(dbName);
        (await verify.Orders.CountAsync()).Should().Be(0);
        (await verify.OrderItems.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SyncAsync_falls_back_to_the_order_list_when_the_watermark_predates_the_change_log()
    {
        // Arrange — Shoptet guarantees only 30 days of change log; older than that it is useless.
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        ctx.SyncStates.Add(new ShoptetSyncState
        {
            EntityName = ShoptetOrderIncrementalSyncService.EntityNameConst,
            Watermark = Now.AddDays(-90),
        });
        await ctx.SaveChangesAsync();

        var (service, client) = CreateService(ctx, new FakeTimeProvider(Now));
        client.AddOrder("A1", Now.AddDays(-80), ShoptetOrderTestData.SimpleOrderJson);
        client.Changes.Add(new ShoptetOrderChangeDto
        {
            Code = "A1",
            ChangeTime = Now.AddDays(-80),
            ChangeType = "edit",
        });

        // Act
        var result = await service.SyncAsync();

        // Assert — the order still gets re-read through the list endpoint.
        result.RowsUpserted.Should().Be(1);
        client.DetailsRequested.Should().Contain("A1");
    }

    [Fact]
    public async Task SyncAsync_on_a_first_run_reads_one_change_log_window()
    {
        // Arrange — no watermark yet; the incremental sync only ever runs after the backfill, so
        // a full history re-read would be pure waste.
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        var (service, client) = CreateService(ctx, new FakeTimeProvider(Now));

        // Act
        await service.SyncAsync();

        // Assert
        client.ChangeLogQueriesRequested.Should().ContainSingle()
            .Which.Should().Be(Now.AddDays(-29));
    }

    [Fact]
    public async Task SyncAsync_ingests_a_new_order_the_change_log_never_reported()
    {
        // Arrange — the change log's documented changeType is only "edit" or "delete". If a newly
        // created order is not announced there, relying on the log alone would stop the mirror
        // taking in new orders the day the backfill completes, silently and for ever. The listing
        // does carry it, because a new order's changeTime is its creationTime.
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        ctx.SyncStates.Add(new ShoptetSyncState
        {
            EntityName = ShoptetOrderIncrementalSyncService.EntityNameConst,
            Watermark = Now.AddDays(-1),
        });
        await ctx.SaveChangesAsync();

        var (service, client) = CreateService(ctx, new FakeTimeProvider(Now));
        client.AddOrder("NEW1", Now.AddHours(-3), ShoptetOrderTestData.SimpleOrderJson);

        // The listing reports it; the change log deliberately does not.
        client.Changes.Add(new ShoptetOrderChangeDto
        {
            Code = "NEW1",
            ChangeTime = Now.AddHours(-3),
            ChangeType = "edit",
        });

        // Act
        await service.SyncAsync();

        // Assert
        await using var verify = CreateContext(dbName);
        (await verify.Orders.AnyAsync(o => o.Code == "NEW1")).Should()
            .BeTrue("an order reached only by the change-time listing must still be mirrored");
    }

    [Fact]
    public async Task SyncAsync_records_a_terminal_status_when_the_run_is_cancelled()
    {
        // Arrange — the job's own timeout, or an operator's Ctrl+C. Without a terminal status the
        // row stays "RUNNING" with no finish time for ever, which v_sync_health cannot tell apart
        // from a run still in flight.
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        var (service, client) = CreateService(ctx, new FakeTimeProvider(Now));
        client.AddOrder("A1", Now.AddHours(-3), ShoptetOrderTestData.SimpleOrderJson);
        client.Changes.Add(new ShoptetOrderChangeDto
        {
            Code = "A1",
            ChangeTime = Now.AddHours(-3),
            ChangeType = "edit",
        });

        // Cancel once the run is already under way — the status is only worth recording for a run
        // that actually started and persisted "RUNNING".
        using var cts = new CancellationTokenSource();
        client.OnDetail = () => cts.Cancel();

        // Act
        var act = () => service.SyncAsync(cts.Token);

        // Assert
        await act.Should().NotThrowAsync();

        await using var verify = CreateContext(dbName);
        var state = await verify.SyncStates
            .SingleAsync(s => s.EntityName == ShoptetOrderIncrementalSyncService.EntityNameConst);
        state.LastRunStatus.Should().Be("CANCELLED");
        state.LastRunFinishedAt.Should().NotBeNull();
        state.Watermark.Should().BeNull("a cancelled run must not claim it caught up");
    }
}
