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
        var ingestor = new ShoptetOrderIngestor(client, store, opts, NullLogger<ShoptetOrderIngestor>.Instance);

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
        client.ChangeQueriesRequested.Should().ContainSingle()
            .Which.Should().Be(Now.AddDays(-29));
    }
}
