using Anela.Heblo.Adapters.ShoptetApi.Analytics;
using Anela.Heblo.Persistence.ShoptetOrders;
using Anela.Heblo.Persistence.ShoptetOrders.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Analytics;

public class ShoptetOrderStoreTests
{
    private static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
    private static readonly DateTimeOffset SyncedAt = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static ShoptetOrdersDbContext CreateContext(string name) =>
        new(new DbContextOptionsBuilder<ShoptetOrdersDbContext>()
            .UseInMemoryDatabase(name).Options);

    private static ShoptetOrder MapFrom(string json) =>
        ShoptetOrderMapper.Map(ShoptetOrderTestData.Parse(json), json, Prague, SyncedAt);

    [Fact]
    public async Task UpsertAsync_re_run_with_the_same_input_produces_no_duplicates()
    {
        // Arrange — the backfill is re-run routinely, so this is the property that makes it safe.
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        var store = new ShoptetOrderStore(ctx);

        // Act
        await store.UpsertAsync([MapFrom(ShoptetOrderTestData.ProductSetOrderJson)]);
        await store.UpsertAsync([MapFrom(ShoptetOrderTestData.ProductSetOrderJson)]);
        await store.UpsertAsync([MapFrom(ShoptetOrderTestData.ProductSetOrderJson)]);

        // Assert
        await using var verify = CreateContext(dbName);
        (await verify.Orders.CountAsync()).Should().Be(1);
        (await verify.OrderItems.CountAsync()).Should().Be(5);
    }

    [Fact]
    public async Task UpsertAsync_updates_a_mutated_order_in_place_instead_of_duplicating_it()
    {
        // Arrange — the nightly sync re-reads every changed order; a cancellation must overwrite,
        // not append, or the cancelled order keeps counting as revenue.
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        var store = new ShoptetOrderStore(ctx);
        await store.UpsertAsync([MapFrom(ShoptetOrderTestData.SimpleOrderJson)]);

        var cancelledJson = ShoptetOrderTestData.SimpleOrderJson
            .Replace("\"status\": { \"id\": 70, \"name\": \"Předáno přepravci\" }",
                     "\"status\": { \"id\": -4, \"name\": \"Stornována\" }")
            .Replace("\"withVat\": \"600.00\", \"withoutVat\": \"495.85\"",
                     "\"withVat\": \"0.00\", \"withoutVat\": \"0.00\"");

        // Act
        await store.UpsertAsync([MapFrom(cancelledJson)]);

        // Assert
        await using var verify = CreateContext(dbName);
        var stored = await verify.Orders.SingleAsync();
        stored.StatusId.Should().Be(-4);
        stored.StatusName.Should().Be("Stornována");
        stored.PriceWithVat.Should().Be(0m);
    }

    [Fact]
    public async Task UpsertAsync_replaces_the_lines_of_a_changed_order_rather_than_merging_them()
    {
        // Arrange — an edited order can lose lines; a merge would leave the removed ones behind.
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        var store = new ShoptetOrderStore(ctx);
        await store.UpsertAsync([MapFrom(ShoptetOrderTestData.ProductSetOrderJson)]);

        var reduced = MapFrom(ShoptetOrderTestData.ProductSetOrderJson);
        reduced.Items = reduced.Items.Take(2).ToList();

        // Act
        await store.UpsertAsync([reduced]);

        // Assert
        await using var verify = CreateContext(dbName);
        (await verify.OrderItems.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_order_and_its_lines()
    {
        // Arrange — the changes log is the only place a deletion surfaces.
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        var store = new ShoptetOrderStore(ctx);
        await store.UpsertAsync([MapFrom(ShoptetOrderTestData.ProductSetOrderJson)]);

        // Act
        var deleted = await store.DeleteAsync(["126014786"]);

        // Assert
        deleted.Should().Be(1);
        await using var verify = CreateContext(dbName);
        (await verify.Orders.CountAsync()).Should().Be(0);
        (await verify.OrderItems.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_with_no_codes_touches_nothing()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var ctx = CreateContext(dbName);
        var store = new ShoptetOrderStore(ctx);
        await store.UpsertAsync([MapFrom(ShoptetOrderTestData.SimpleOrderJson)]);

        var deleted = await store.DeleteAsync([]);

        deleted.Should().Be(0);
        await using var verify = CreateContext(dbName);
        (await verify.Orders.CountAsync()).Should().Be(1);
    }
}
