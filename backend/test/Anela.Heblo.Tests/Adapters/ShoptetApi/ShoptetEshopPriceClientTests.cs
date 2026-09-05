using Anela.Heblo.Adapters.ShoptetApi.Pricing;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class ShoptetEshopPriceClientTests
{
    [Fact]
    public async Task maps_price_list_entries_to_catalog_eshop_prices()
    {
        // Arrange
        var priceList = new Mock<IEshopPriceListClient>();
        priceList
            .Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { ["OCH001030"] = 190.00m });
        var vatRates = new Mock<IProductVatRateProvider>();
        vatRates
            .Setup(v => v.GetVatRatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { ["OCH001030"] = 21m });
        var client = new ShoptetEshopPriceClient(priceList.Object, vatRates.Object);

        // Act
        var prices = (await client.GetAllAsync(CancellationToken.None)).ToList();

        // Assert
        prices.Should().ContainSingle();
        prices[0].ProductCode.Should().Be("OCH001030");
        prices[0].PriceWithVat.Should().Be(190.00m);
        prices[0].PriceWithoutVat.Should().Be(157.02m);
    }

    [Fact]
    public async Task falls_back_to_the_standard_vat_rate_when_the_erp_rate_is_unknown()
    {
        // Arrange
        var priceList = new Mock<IEshopPriceListClient>();
        priceList
            .Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { ["NEW001"] = 121.00m });
        var vatRates = new Mock<IProductVatRateProvider>();
        vatRates
            .Setup(v => v.GetVatRatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal>());
        var client = new ShoptetEshopPriceClient(priceList.Object, vatRates.Object);

        // Act
        var prices = (await client.GetAllAsync(CancellationToken.None)).ToList();

        // Assert
        prices[0].PriceWithoutVat.Should().Be(100.00m);
    }
}
