using Anela.Heblo.Domain.Features.ProductPricing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.ProductPricing;

public class ProductPriceTests
{
    [Theory]
    [InlineData(21, 190.00, 157.02)]
    [InlineData(15, 190.00, 165.22)]
    [InlineData(0, 190.00, 190.00)]
    public void derives_price_without_vat_from_the_canonical_with_vat_value(
        decimal vatRate, decimal priceWithVat, decimal expectedWithoutVat)
    {
        // Arrange
        var price = new ProductPrice { ProductCode = "OCH001030", PriceWithVat = priceWithVat, VatRate = vatRate };

        // Act
        var withoutVat = price.PriceWithoutVat;

        // Assert
        withoutVat.Should().Be(expectedWithoutVat);
    }

    [Fact]
    public void exposes_product_code_as_the_entity_identity()
    {
        // Arrange
        var price = new ProductPrice { ProductCode = "OCH001030" };

        // Act & Assert
        price.Id.Should().Be("OCH001030");
    }
}
