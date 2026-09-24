using Anela.Heblo.Adapters.ShoptetApi.Analytics;
using Anela.Heblo.Persistence.ShoptetOrders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Analytics;

public class ShoptetOrderBackfillServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 2, 0, 0, TimeSpan.Zero);

    private static ShoptetOrdersDbContext CreateContext(string name) =>
        new(new DbContextOptionsBuilder<ShoptetOrdersDbContext>()
            .UseInMemoryDatabase(name).Options);

    private static ShoptetOrdersSyncOptions DefaultOptions() => new()
    {
        BackfillFrom = "2026-01-01",
        BackfillWindowDays = 31,
        BackfillMaxMinutesPerRun = 240,
        BatchSize = 50,
    };

    private static (ShoptetOrderBackfillService Service, FakeShoptetOrderAnalyticsClient Client)
        CreateService(ShoptetOrdersDbContext ctx, FakeTimeProvider clock, ShoptetOrdersSyncOptions? options = null)
    {
        options ??= DefaultOptions();
        var opts = Options.Create(options);
        var client = new FakeShoptetOrderAnalyticsClient();
        var ingestor = new ShoptetOrderIngestor(
            client, new ShoptetOrderStore(ctx), opts, clock,
            NullLogger<ShoptetOrderIngestor>.Instance);

        var service = new ShoptetOrderBackfillService(
            client,
            ingestor,
            new ShoptetSyncWatermarkRepository(ctx),
            opts,
            clock,
            NullLogger<ShoptetOrderBackfillService>.Instance);

        return (service, client);
    }

    [Fact]
    public async Task SyncAsync_walks_creation_time_windows_forward_and_marks_the_backfill_complete()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        var clock = new FakeTimeProvider(Now);
        var (service, client) = CreateService(ctx, clock);
        client.AddOrder("A1", new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero),
            ShoptetOrderTestData.SimpleOrderJson);
        client.AddOrder("A2", new DateTimeOffset(2026, 2, 20, 9, 0, 0, TimeSpan.Zero),
            ShoptetOrderTestData.ProductSetOrderJson);

        // Act
        var result = await service.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsComplete.Should().BeTrue();
        result.RowsUpserted.Should().Be(2);

        await using var verify = CreateContext(dbName);
        (await verify.Orders.CountAsync()).Should().Be(2);
        var state = await verify.SyncStates.SingleAsync();
        state.BackfillCompleted.Should().BeTrue();
        state.BackfillCursor.Should().BeOnOrAfter(new DateOnly(2026, 3, 11));
    }

    [Fact]
    public async Task SyncAsync_persists_the_cursor_after_every_window_so_an_interrupted_run_resumes()
    {
        // Arrange — the run is cut off by its wall-clock budget after the first window.
        var dbName = Guid.NewGuid().ToString();
        var clock = new FakeTimeProvider(Now);
        var options = DefaultOptions();
        options.BackfillMaxMinutesPerRun = 5;

        await using (var ctx = CreateContext(dbName))
        {
            var (service, client) = CreateService(ctx, clock, options);
            client.AddOrder("A1", new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero),
                ShoptetOrderTestData.SimpleOrderJson);
            client.AddOrder("A2", new DateTimeOffset(2026, 2, 20, 9, 0, 0, TimeSpan.Zero),
                ShoptetOrderTestData.ProductSetOrderJson);

            // Each window "takes" six minutes, so the budget is spent after the first one.
            client.OnCreationWindow = () => clock.Advance(TimeSpan.FromMinutes(6));

            // Act
            var first = await service.SyncAsync();

            // Assert
            first.IsComplete.Should().BeFalse();
        }

        await using (var verify = CreateContext(dbName))
        {
            var state = await verify.SyncStates.SingleAsync();
            state.BackfillCompleted.Should().BeFalse();
            state.BackfillCursor.Should().Be(new DateOnly(2026, 2, 1));
        }

        // Act — a second run resumes from the persisted cursor.
        await using (var ctx = CreateContext(dbName))
        {
            var (service, client) = CreateService(ctx, new FakeTimeProvider(Now), DefaultOptions());
            client.AddOrder("A1", new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero),
                ShoptetOrderTestData.SimpleOrderJson);
            client.AddOrder("A2", new DateTimeOffset(2026, 2, 20, 9, 0, 0, TimeSpan.Zero),
                ShoptetOrderTestData.ProductSetOrderJson);

            var second = await service.SyncAsync();

            // Assert — it never re-reads January.
            second.IsComplete.Should().BeTrue();
            client.CreationWindowsRequested.Should().NotContain(w => w.From.Month == 1);
        }

        await using (var verify = CreateContext(dbName))
        {
            (await verify.Orders.CountAsync()).Should()
                .Be(2, "the first run ingested January and the resumed run added February");
        }
    }

    [Fact]
    public async Task SyncAsync_does_nothing_once_the_backfill_is_marked_complete()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        var (service, client) = CreateService(ctx, new FakeTimeProvider(Now));
        await service.SyncAsync();
        var windowsAfterFirstRun = client.CreationWindowsRequested.Count;

        // Act
        var result = await service.SyncAsync();

        // Assert
        result.IsComplete.Should().BeTrue();
        client.CreationWindowsRequested.Should().HaveCount(windowsAfterFirstRun);
    }

    [Fact]
    public async Task SyncAsync_skips_an_order_that_disappeared_between_the_listing_and_the_detail_call()
    {
        // Arrange — over a multi-hour backfill an order can be deleted mid-run; the detail call
        // then answers 404 and must not abort the window.
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        var (service, client) = CreateService(ctx, new FakeTimeProvider(Now));
        client.AddOrder("A1", new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero),
            ShoptetOrderTestData.SimpleOrderJson);
        client.OrdersByCode["GONE"] = new DateTimeOffset(2026, 1, 6, 9, 0, 0, TimeSpan.Zero);

        // Act
        var result = await service.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RowsUpserted.Should().Be(1);
        client.DetailsRequested.Should().Contain("GONE");
    }
}
