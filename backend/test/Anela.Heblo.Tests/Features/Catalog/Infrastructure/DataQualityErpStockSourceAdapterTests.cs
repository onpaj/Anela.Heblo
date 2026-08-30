using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class DataQualityErpStockSourceAdapterTests
{
    private readonly Mock<IErpStockClient> _inner = new();

    private DataQualityErpStockSourceAdapter CreateAdapter() => new(_inner.Object);

    private void SetupErp(params ErpStock[] products) =>
        _inner.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ErpStock>)products.ToList());

    [Fact]
    public async Task ListAsync_ProjectsProductCodeAndProductName()
    {
        // Arrange
        SetupErp(new ErpStock { ProductCode = "P001", ProductName = "Product 1", ProductTypeId = 1 });

        // Act
        var result = await CreateAdapter().ListAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        result[0].ProductCode.Should().Be("P001");
        result[0].ProductName.Should().Be("Product 1");
    }

    [Theory]
    [InlineData(1, true)]   // Goods
    [InlineData(8, true)]   // Product
    [InlineData(3, false)]  // Material
    [InlineData(7, false)]  // SemiProduct
    [InlineData(99, false)] // Set
    [InlineData(0, false)]  // UNDEFINED
    public async Task ListAsync_MapsIsSellable_FromProductTypeId(int productTypeId, bool expectedSellable)
    {
        // Arrange
        SetupErp(new ErpStock { ProductCode = "P001", ProductName = "Product 1", ProductTypeId = productTypeId });

        // Act
        var result = await CreateAdapter().ListAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle().Which.IsSellable.Should().Be(expectedSellable);
    }

    [Fact]
    public async Task ListAsync_WhenProductTypeIdIsNull_IsSellableIsFalse()
    {
        // Arrange
        SetupErp(new ErpStock { ProductCode = "P001", ProductName = "Product 1", ProductTypeId = null });

        // Act
        var result = await CreateAdapter().ListAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle().Which.IsSellable.Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_WhenInnerReturnsEmpty_ReturnsEmptyList()
    {
        // Arrange
        SetupErp();

        // Act
        var result = await CreateAdapter().ListAsync(CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
