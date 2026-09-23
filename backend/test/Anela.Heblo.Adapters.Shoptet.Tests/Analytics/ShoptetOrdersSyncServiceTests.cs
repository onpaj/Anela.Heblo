using Anela.Heblo.Adapters.ShoptetApi.Analytics;
using Anela.Heblo.Persistence.ShoptetOrders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Analytics;

public class ShoptetOrdersSyncServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 2, 0, 0, TimeSpan.Zero);

    private static ShoptetOrdersDbContext CreateContext(string name) =>
        new(new DbContextOptionsBuilder<ShoptetOrdersDbContext>()
            .UseInMemoryDatabase(name).Options);

    private static (ShoptetOrdersSyncService Service, FakeShoptetOrderAnalyticsClient Client,
                    Mock<IShoptetOrderFactRefresher> Refresher)
        Create(ShoptetOrdersDbContext ctx, FakeTimeProvider clock, int backfillBudgetMinutes = 240,
               Mock<IShoptetOrderFactRefresher>? refresher = null)
    {
        var options = Options.Create(new ShoptetOrdersSyncOptions
        {
            BackfillFrom = "2026-01-01",
            BackfillWindowDays = 31,
            BackfillMaxMinutesPerRun = backfillBudgetMinutes,
            BatchSize = 50,
        });

        var client = new FakeShoptetOrderAnalyticsClient();
        var store = new ShoptetOrderStore(ctx);
        var watermarks = new ShoptetSyncWatermarkRepository(ctx);
        var ingestor = new ShoptetOrderIngestor(client, store, options, clock, NullLogger<ShoptetOrderIngestor>.Instance);

        var backfill = new ShoptetOrderBackfillService(
            client, ingestor, watermarks, options, clock,
            NullLogger<ShoptetOrderBackfillService>.Instance);
        var incremental = new ShoptetOrderIncrementalSyncService(
            client, ingestor, store, watermarks, options, clock,
            NullLogger<ShoptetOrderIncrementalSyncService>.Instance);

        refresher ??= new Mock<IShoptetOrderFactRefresher>();

        return (new ShoptetOrdersSyncService(
                    backfill, incremental, refresher.Object, NullLogger<ShoptetOrdersSyncService>.Instance),
                client, refresher);
    }

    [Fact]
    public async Task SyncAsync_spends_the_whole_run_on_the_backfill_while_it_is_unfinished()
    {
        // Arrange — the budget runs out before the first window completes.
        var clock = new FakeTimeProvider(Now);
        await using var ctx = CreateContext(Guid.NewGuid().ToString());
        var (service, client, _) = Create(ctx, clock, backfillBudgetMinutes: 5);
        client.OnCreationWindow = () => clock.Advance(TimeSpan.FromMinutes(6));

        // Act
        var report = await service.SyncAsync();

        // Assert — no change-log call is made until the history is in.
        report.BackfillCompleted.Should().BeFalse();
        client.ChangeQueriesRequested.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncAsync_runs_the_incremental_catch_up_once_the_backfill_is_complete()
    {
        // Arrange
        var clock = new FakeTimeProvider(Now);
        await using var ctx = CreateContext(Guid.NewGuid().ToString());
        var (service, client, refresher) = Create(ctx, clock);

        // Act
        var report = await service.SyncAsync();

        // Assert
        report.BackfillCompleted.Should().BeTrue();
        report.IsFullSuccess.Should().BeTrue();
        client.ChangeQueriesRequested.Should().ContainSingle();
        refresher.Verify(r => r.RefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_still_reports_success_when_the_order_fact_refresh_fails()
    {
        // Arrange — the rows are already committed and the next run refreshes again, so a failed
        // refresh must not turn a good sync into a failed one.
        var refresher = new Mock<IShoptetOrderFactRefresher>();
        refresher.Setup(r => r.RefreshAsync(It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("view is locked"));

        await using var ctx = CreateContext(Guid.NewGuid().ToString());
        var (service, _, _) = Create(ctx, new FakeTimeProvider(Now), refresher: refresher);

        // Act
        var report = await service.SyncAsync();

        // Assert
        report.IsFullSuccess.Should().BeTrue();
    }
}
