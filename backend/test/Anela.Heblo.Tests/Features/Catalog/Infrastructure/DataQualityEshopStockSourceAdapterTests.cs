using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class DataQualityEshopStockSourceAdapterTests
{
    private readonly Mock<IEshopStockClient> _inner = new();

    private DataQualityEshopStockSourceAdapter CreateAdapter() => new(_inner.Object);

    [Fact]
    public async Task ListAsync_ProjectsCodePairCodeAndName()
    {
        // Arrange
        _inner.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EshopStock>
            {
                new EshopStock { Code = "P001", PairCode = "ERP001", Name = "Product 1" },
            });

        // Act
        var result = await CreateAdapter().ListAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        result[0].Code.Should().Be("P001");
        result[0].PairCode.Should().Be("ERP001");
        result[0].Name.Should().Be("Product 1");
    }

    [Fact]
    public async Task ListAsync_WhenInnerReturnsEmpty_ReturnsEmptyList()
    {
        // Arrange
        _inner.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EshopStock>());

        // Act
        var result = await CreateAdapter().ListAsync(CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_ProjectsMultipleProductsInOrder()
    {
        // Arrange
        _inner.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EshopStock>
            {
                new EshopStock { Code = "A", PairCode = "", Name = "Alpha" },
                new EshopStock { Code = "B", PairCode = "B-ERP", Name = "Beta" },
            });

        // Act
        var result = await CreateAdapter().ListAsync(CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result[0].Code.Should().Be("A");
        result[1].Code.Should().Be("B");
    }
}
