using Anela.Heblo.Adapters.ShoptetApi.Analytics;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Analytics;

public class ShoptetOrderMapperTests
{
    private static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
    private static readonly DateTimeOffset SyncedAt = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Map_normalizes_the_creation_time_to_a_utc_offset()
    {
        // Arrange
        var dto = ShoptetOrderTestData.Parse(ShoptetOrderTestData.SimpleOrderJson);

        // Act
        var order = ShoptetOrderMapper.Map(dto, "{}", Prague, SyncedAt);

        // Assert — Npgsql rejects any other offset on a timestamptz column.
        order.CreationTime.Offset.Should().Be(TimeSpan.Zero);
        order.ChangeTime!.Value.Offset.Should().Be(TimeSpan.Zero);
        order.CreationTime.Should().Be(new DateTimeOffset(2026, 9, 21, 18, 36, 22, TimeSpan.Zero));
    }

    [Fact]
    public void Map_buckets_the_order_date_by_the_store_timezone_not_utc()
    {
        // Arrange — 2026-09-21 20:36 Prague is 18:36 UTC; both fall on the 21st, so use a late
        // evening order where the two would disagree if UTC were used.
        var json = ShoptetOrderTestData.SimpleOrderJson
            .Replace("2026-09-21T20:36:22+0200", "2026-09-21T23:30:00+0200");
        var dto = ShoptetOrderTestData.Parse(json);

        // Act
        var order = ShoptetOrderMapper.Map(dto, "{}", Prague, SyncedAt);

        // Assert
        order.CreationTime.UtcDateTime.Day.Should().Be(21);
        order.OrderDate.Should().Be(new DateOnly(2026, 9, 21));
    }

    [Fact]
    public void Map_lowercases_and_trims_the_customer_email()
    {
        var dto = ShoptetOrderTestData.Parse(ShoptetOrderTestData.SimpleOrderJson);

        var order = ShoptetOrderMapper.Map(dto, "{}", Prague, SyncedAt);

        order.CustomerEmail.Should().Be("buyer@example.com");
    }

    [Fact]
    public void Map_splits_the_line_totals_into_product_shipping_payment_and_discount()
    {
        // Arrange
        var dto = ShoptetOrderTestData.Parse(ShoptetOrderTestData.SimpleOrderJson);

        // Act
        var order = ShoptetOrderMapper.Map(dto, "{}", Prague, SyncedAt);

        // Assert — itemPrice is the line total, not the unit price: 2 × 350 = 700.
        order.ProductPriceWithVat.Should().Be(700.00m);
        order.ShippingPriceWithVat.Should().Be(69.00m);
        order.BillingPriceWithVat.Should().Be(39.00m);
        order.DiscountWithVat.Should().Be(-208.00m);
        order.ProductUnits.Should().Be(2.000m);
        // Which is exactly the header total Shoptet reports.
        (order.ProductPriceWithVat + order.ShippingPriceWithVat
            + order.BillingPriceWithVat + order.DiscountWithVat)
            .Should().Be(order.PriceWithVat);
    }

    [Fact]
    public void Map_keeps_the_line_price_and_the_unit_price_apart()
    {
        var dto = ShoptetOrderTestData.Parse(ShoptetOrderTestData.SimpleOrderJson);

        var order = ShoptetOrderMapper.Map(dto, "{}", Prague, SyncedAt);

        var product = order.Items.Single(i => i.ProductCode == "ODL001100" && i.SourceArray == "items");
        product.UnitPriceWithVat.Should().Be(350.00m);
        product.LinePriceWithVat.Should().Be(700.00m);
        product.Amount.Should().Be(2.000m);
    }

    [Fact]
    public void Map_takes_only_the_set_components_from_completion_not_the_duplicated_products()
    {
        // Arrange — completion[] repeats every items[] line and adds the product-set-item entries.
        var dto = ShoptetOrderTestData.Parse(ShoptetOrderTestData.ProductSetOrderJson);

        // Act
        var order = ShoptetOrderMapper.Map(dto, "{}", Prague, SyncedAt);

        // Assert
        order.Items.Where(i => i.SourceArray == "completion")
            .Should().OnlyContain(i => i.ItemType == "product-set-item");
        order.Items.Count(i => i.SourceArray == "items").Should().Be(3);
        order.Items.Count(i => i.SourceArray == "completion").Should().Be(2);
    }

    [Fact]
    public void Map_stores_the_set_component_amount_as_the_order_total_without_multiplying_by_the_set_quantity()
    {
        // Arrange — 2× set SA010, one of each component per set. Shoptet already reports 2.
        var dto = ShoptetOrderTestData.Parse(ShoptetOrderTestData.ProductSetOrderJson);

        // Act
        var order = ShoptetOrderMapper.Map(dto, "{}", Prague, SyncedAt);

        // Assert — the bug this guards against multiplied by the parent amount and produced 4.
        var component = order.Items.Single(i => i.ProductCode == "SER004005");
        component.Amount.Should().Be(2.000m);
        component.ParentItemId.Should().Be(1475182);
        component.LinePriceWithVat.Should().BeNull("Shoptet attributes no revenue to a set component");
    }

    [Fact]
    public void Map_counts_the_set_header_once_towards_product_revenue_and_units()
    {
        // Arrange
        var dto = ShoptetOrderTestData.Parse(ShoptetOrderTestData.ProductSetOrderJson);

        // Act
        var order = ShoptetOrderMapper.Map(dto, "{}", Prague, SyncedAt);

        // Assert — 150 (loose product) + 1780 (2 sets); components add nothing.
        order.ProductPriceWithVat.Should().Be(1930.00m);
        order.ProductUnits.Should().Be(3.000m);
    }

    [Fact]
    public void Map_gives_every_line_a_unique_key_within_the_order()
    {
        // Arrange — a product-set-item's itemId is the catalogue id and repeats across orders, so
        // line_no is what makes the primary key unique.
        var dto = ShoptetOrderTestData.Parse(ShoptetOrderTestData.ProductSetOrderJson);

        // Act
        var order = ShoptetOrderMapper.Map(dto, "{}", Prague, SyncedAt);

        // Assert
        order.Items.Select(i => i.LineNo).Should().OnlyHaveUniqueItems();
        order.Items.Select(i => i.LineNo).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Map_handles_a_cash_desk_order_with_null_flags_and_no_identity()
    {
        // Arrange — the live store returns "paid": null and "vatPayer": null on these.
        var dto = ShoptetOrderTestData.Parse(ShoptetOrderTestData.CashDeskOrderJson);

        // Act
        var order = ShoptetOrderMapper.Map(dto, "{}", Prague, SyncedAt);

        // Assert
        order.CashDeskOrder.Should().BeTrue();
        order.IsPaid.Should().BeFalse();
        order.VatPayer.Should().BeFalse();
        order.CustomerEmail.Should().BeNull();
        order.CustomerGuid.Should().BeNull();
        order.ShippingGuid.Should().BeNull();
    }

    [Fact]
    public void Map_keeps_the_raw_payload_verbatim()
    {
        var dto = ShoptetOrderTestData.Parse(ShoptetOrderTestData.SimpleOrderJson);

        var order = ShoptetOrderMapper.Map(dto, ShoptetOrderTestData.SimpleOrderJson, Prague, SyncedAt);

        order.RawPayload.Should().Be(ShoptetOrderTestData.SimpleOrderJson);
    }
}
