using Anela.Heblo.Adapters.Flexi.Sales;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Model.Products.Sets;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Sales;

public sealed class FlexiCatalogSetPartsClientTests
{
    private readonly Mock<IProductSetsClient> _productSetsClient = new();
    private readonly Mock<ILogger<FlexiCatalogSetPartsClient>> _logger = new();

    [Fact]
    public async Task GetAsync_FlattensPartsAcrossSetsAndStampsSetCode()
    {
        // Arrange
        _productSetsClient
            .Setup(c => c.GetAsync("BAL001", 0, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductSetFlexiDto>
            {
                BuildDto(quantity: 2, code: "KRM001", name: "Krém"),
            });
        _productSetsClient
            .Setup(c => c.GetAsync("BAL002", 0, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductSetFlexiDto>
            {
                BuildDto(quantity: 1, code: "MYD001", name: "Mýdlo"),
            });

        var sut = new FlexiCatalogSetPartsClient(_productSetsClient.Object, _logger.Object);

        // Act
        var result = await sut.GetAsync(new[] { "BAL001", "BAL002" }, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(new[]
        {
            new CatalogSetPart { SetCode = "BAL001", ComponentCode = "KRM001", ComponentName = "Krém", Amount = 2 },
            new CatalogSetPart { SetCode = "BAL002", ComponentCode = "MYD001", ComponentName = "Mýdlo", Amount = 1 },
        });
    }

    [Fact]
    public async Task GetAsync_LogsWarningAndSkipsSetWithNoParts()
    {
        // Arrange
        _productSetsClient
            .Setup(c => c.GetAsync("BAL003", 0, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductSetFlexiDto>());

        var sut = new FlexiCatalogSetPartsClient(_productSetsClient.Object, _logger.Object);

        // Act
        var result = await sut.GetAsync(new[] { "BAL003" }, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("BAL003")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAsync_LogsWarningAndSkipsSetWhenReturnedRowsHaveNoProducts()
    {
        // Arrange
        _productSetsClient
            .Setup(c => c.GetAsync("BAL004", 0, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductSetFlexiDto>
            {
                new() { Quantity = 1, ProductList = null! },
                new() { Quantity = 2, ProductList = new List<ProductSetsProductFlexiDto>() },
            });

        var sut = new FlexiCatalogSetPartsClient(_productSetsClient.Object, _logger.Object);

        // Act
        var result = await sut.GetAsync(new[] { "BAL004" }, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("BAL004")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static ProductSetFlexiDto BuildDto(double quantity, string code, string name) =>
        new()
        {
            Quantity = quantity,
            ProductList = new List<ProductSetsProductFlexiDto>
            {
                new() { Code = code, Name = name },
            },
        };
}
