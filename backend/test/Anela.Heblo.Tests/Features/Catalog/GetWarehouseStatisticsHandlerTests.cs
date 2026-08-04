using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetWarehouseStatistics;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetWarehouseStatisticsHandlerTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly GetWarehouseStatisticsHandler _handler;

    public GetWarehouseStatisticsHandlerTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _timeProviderMock = new Mock<TimeProvider>();
        _handler = new GetWarehouseStatisticsHandler(_catalogRepositoryMock.Object, _timeProviderMock.Object);
    }

    [Fact]
    public async Task Handle_Should_Use_TimeProvider_For_LastUpdated()
    {
        // Arrange
        var currentDate = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        _timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(new DateTimeOffset(currentDate));
        _catalogRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate>());

        // Act
        var result = await _handler.Handle(new GetWarehouseStatisticsRequest(), CancellationToken.None);

        // Assert
        result.LastUpdated.Should().Be(currentDate);
    }

    [Fact]
    public async Task Handle_Should_Return_WarehouseCapacityKg_From_CatalogConstants()
    {
        // Arrange
        _timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(new DateTimeOffset(new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc)));
        _catalogRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate>());

        // Act
        var result = await _handler.Handle(new GetWarehouseStatisticsRequest(), CancellationToken.None);

        // Assert
        result.WarehouseCapacityKg.Should().Be(CatalogConstants.WarehouseCapacityKg);
    }

    [Fact]
    public async Task Handle_Should_Calculate_Utilization_Percentage_From_Weighted_Items()
    {
        // Arrange
        _timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(new DateTimeOffset(new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc)));

        var items = new List<CatalogAggregate>
        {
            new CatalogAggregate
            {
                Id = "PROD1",
                Type = ProductType.Product,
                GrossWeight = 500, // grams
                Stock = new StockData { Eshop = 10 } // 10 * 500 / 1000 = 5 kg
            },
            new CatalogAggregate
            {
                Id = "PROD2",
                Type = ProductType.Goods,
                GrossWeight = 1000, // grams
                Stock = new StockData { Eshop = 4 } // 4 * 1000 / 1000 = 4 kg
            },
            new CatalogAggregate
            {
                Id = "PROD3",
                Type = ProductType.Product,
                GrossWeight = null, // excluded from weight sum
                Stock = new StockData { Eshop = 100 }
            },
            new CatalogAggregate
            {
                Id = "MAT1",
                Type = ProductType.Material,
                GrossWeight = 2000,
                Stock = new StockData { Eshop = 1000 } // excluded: not Product/Goods
            }
        };

        _catalogRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await _handler.Handle(new GetWarehouseStatisticsRequest(), CancellationToken.None);

        // Assert
        var expectedTotalWeight = 9.0; // 5 + 4 kg
        var expectedUtilization = expectedTotalWeight / CatalogConstants.WarehouseCapacityKg * 100;

        result.TotalWeight.Should().Be(expectedTotalWeight);
        result.WarehouseUtilizationPercentage.Should().Be(expectedUtilization);
        result.TotalQuantity.Should().Be(114); // 10 + 4 + 100 (Product/Goods only)
        result.TotalProductCount.Should().Be(3); // Product/Goods items only
    }
}
