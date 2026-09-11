using Anela.Heblo.Adapters.Flexi.Price;
using Anela.Heblo.Domain.Features.Catalog.Price;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.Flexi;

public class FlexiProductVatRateProviderTests
{
    private readonly Mock<IProductPriceErpClient> _erpClient = new();

    private FlexiProductVatRateProvider CreateSut() => new(_erpClient.Object);

    private void GivenErpPrices(params ProductPriceErp[] prices) =>
        _erpClient.Setup(c => c.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(prices);

    [Fact]
    public async Task returns_the_band_flexi_actually_reported()
    {
        // Arrange: a 12% item whose with/without-VAT pair would arithmetically recover 21
        // if the rate were derived from the prices instead of read from the band.
        GivenErpPrices(new ProductPriceErp
        {
            ProductCode = "A",
            PriceWithoutVat = 100m,
            PriceWithVat = 121m,
            VatRate = 12m,
        });

        // Act
        var rates = await CreateSut().GetVatRatesAsync(CancellationToken.None);

        // Assert
        rates["A"].Should().Be(12m);
    }

    [Fact]
    public async Task omits_a_product_whose_vat_band_was_not_recognised()
    {
        // Arrange
        GivenErpPrices(new ProductPriceErp
        {
            ProductCode = "A",
            PriceWithoutVat = 100m,
            PriceWithVat = 121m,
            VatRate = null,
        });

        // Act
        var rates = await CreateSut().GetVatRatesAsync(CancellationToken.None);

        // Assert: absent, never a fabricated 21.
        rates.Should().NotContainKey("A");
    }

    [Fact]
    public async Task keeps_the_recognised_products_when_another_row_is_unrecognised()
    {
        // Arrange
        GivenErpPrices(
            new ProductPriceErp { ProductCode = "A", PriceWithoutVat = 100m, PriceWithVat = 121m, VatRate = 21m },
            new ProductPriceErp { ProductCode = "B", PriceWithoutVat = 100m, PriceWithVat = 121m, VatRate = null });

        // Act
        var rates = await CreateSut().GetVatRatesAsync(CancellationToken.None);

        // Assert
        rates.Should().ContainKey("A").And.NotContainKey("B");
    }
}
