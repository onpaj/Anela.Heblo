using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class ProductPairingDqtComparerTests
{
    private readonly Mock<IEshopStockClient> _eshopMock = new();
    private readonly Mock<IErpStockClient> _erpMock = new();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private ProductPairingDqtComparer CreateSut() =>
        new(_eshopMock.Object, _erpMock.Object);

    private void SetupEshop(params EshopStock[] products) =>
        _eshopMock.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products.ToList());

    private void SetupErp(params ErpStock[] products) =>
        _erpMock.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ErpStock>)products.ToList());

    [Fact]
    public async Task CompareAsync_ReturnsEmpty_WhenAllProductsPaired()
    {
        // Arrange
        SetupEshop(new EshopStock { Code = "P001", PairCode = "", Name = "Product 1" });
        SetupErp(new ErpStock { ProductCode = "P001", ProductName = "Product 1", ProductTypeId = 1 }); // Goods=1

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().BeEmpty();
        result.TotalChecked.Should().Be(1);
    }

    [Fact]
    public async Task CompareAsync_ReturnsMissingInErp_WhenShoptetProductNotInErp()
    {
        // Arrange
        SetupEshop(new EshopStock { Code = "ESHOP_ONLY", PairCode = "", Name = "Eshop Only" });
        SetupErp(); // Empty ERP

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().HaveCount(1);
        result.Mismatches[0].EntityKey.Should().Be("ESHOP_ONLY");
        ((ProductPairingMismatch)result.Mismatches[0].MismatchCode)
            .Should().HaveFlag(ProductPairingMismatch.MissingInErp);
    }

    [Fact]
    public async Task CompareAsync_ReturnsMissingInErpAndPairCodeUnresolved_WhenPairCodeNotInErp()
    {
        // Arrange
        SetupEshop(new EshopStock { Code = "ESHOP001", PairCode = "ERP001", Name = "Pair Code Product" });
        SetupErp(); // ERP001 not in ERP

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        var mismatch = (ProductPairingMismatch)result.Mismatches.Single().MismatchCode;
        mismatch.Should().HaveFlag(ProductPairingMismatch.MissingInErp);
        mismatch.Should().HaveFlag(ProductPairingMismatch.PairCodeUnresolved);
    }

    [Fact]
    public async Task CompareAsync_ReturnsMissingInShoptet_OnlyForSellableErpProducts()
    {
        // Arrange
        SetupEshop(); // Empty Shoptet
        SetupErp(
            new ErpStock { ProductCode = "PROD001", ProductName = "Sellable", ProductTypeId = 8 },  // Product=8
            new ErpStock { ProductCode = "MAT001", ProductName = "Material", ProductTypeId = 3 }   // Material=3, not sellable
        );

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert — only PROD001 flagged; MAT001 is non-sellable and must be ignored
        result.Mismatches.Should().HaveCount(1);
        result.Mismatches[0].EntityKey.Should().Be("PROD001");
        ((ProductPairingMismatch)result.Mismatches[0].MismatchCode)
            .Should().HaveFlag(ProductPairingMismatch.MissingInShoptet);
    }
}
