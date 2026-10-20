using Anela.Heblo.Adapters.ShoptetApi.Pricing;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class ShoptetEshopPriceClientTests
{
    private static readonly DateTimeOffset NoonInPrague = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    private static ShoptetEshopPriceClient CreateClient(
        IReadOnlyList<EshopPriceListEntry> entries,
        IReadOnlyDictionary<string, decimal> vatRates,
        DateTimeOffset? utcNow = null)
    {
        var priceList = new Mock<IEshopPriceListClient>();
        priceList
            .Setup(c => c.GetPriceListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);
        var vatRateProvider = new Mock<IProductVatRateProvider>();
        vatRateProvider
            .Setup(v => v.GetVatRatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(vatRates);
        return new ShoptetEshopPriceClient(
            priceList.Object, vatRateProvider.Object, new FakeTimeProvider(utcNow ?? NoonInPrague));
    }

    [Fact]
    public async Task maps_price_list_entries_to_catalog_eshop_prices()
    {
        // Arrange
        var client = CreateClient(
            new[] { new EshopPriceListEntry("OCH001030", 190.00m, null, null, null) },
            new Dictionary<string, decimal> { ["OCH001030"] = 21m });

        // Act
        var prices = (await client.GetAllAsync(CancellationToken.None)).ToList();

        // Assert
        prices.Should().ContainSingle();
        prices[0].ProductCode.Should().Be("OCH001030");
        prices[0].PriceWithVat.Should().Be(190.00m);
        prices[0].PriceWithoutVat.Should().Be(157.02m);
        prices[0].IsInAction.Should().BeFalse();
    }

    [Fact]
    public async Task falls_back_to_the_standard_vat_rate_when_the_erp_rate_is_unknown()
    {
        // Arrange
        var client = CreateClient(
            new[] { new EshopPriceListEntry("NEW001", 121.00m, null, null, null) },
            new Dictionary<string, decimal>());

        // Act
        var prices = (await client.GetAllAsync(CancellationToken.None)).ToList();

        // Assert
        prices[0].PriceWithoutVat.Should().Be(100.00m);
    }

    [Fact]
    public async Task uses_the_running_action_price_as_the_selling_price()
    {
        // Arrange: BAL0001M on 2026-10-01 — open-ended action, e-shop sells at 490
        var client = CreateClient(
            new[] { new EshopPriceListEntry("BAL0001M", 539.00m, 490.00m, null, null) },
            new Dictionary<string, decimal> { ["BAL0001M"] = 21m });

        // Act
        var price = (await client.GetAllAsync(CancellationToken.None)).Single();

        // Assert
        price.PriceWithVat.Should().Be(490.00m);
        price.PriceWithoutVat.Should().Be(404.96m);
        price.RegularPriceWithVat.Should().Be(539.00m);
        price.IsInAction.Should().BeTrue();
    }

    [Fact]
    public async Task judges_the_action_window_by_the_prague_date_not_the_utc_date()
    {
        // Arrange: 22:30 UTC on 30 Sep is already 1 Oct in Prague, the action's first day
        var client = CreateClient(
            new[] { new EshopPriceListEntry("A", 100.00m, 80.00m, new DateOnly(2026, 10, 1), null) },
            new Dictionary<string, decimal> { ["A"] = 21m },
            new DateTimeOffset(2026, 9, 30, 22, 30, 0, TimeSpan.Zero));

        // Act
        var price = (await client.GetAllAsync(CancellationToken.None)).Single();

        // Assert
        price.IsInAction.Should().BeTrue();
        price.PriceWithVat.Should().Be(80.00m);
    }
}
