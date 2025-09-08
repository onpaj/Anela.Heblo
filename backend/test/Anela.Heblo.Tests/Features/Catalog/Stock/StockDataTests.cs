using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Stock;

public class StockDataTests
{
    [Fact]
    public void Total_WithErpAsPrimarySource_ReturnsAvailablePlusReserve()
    {
        // Arrange
        var stockData = new StockData
        {
            Erp = 100m,
            Eshop = 50m,
            Transport = 10m,
            Reserve = 25m,
            PrimaryStockSource = StockSource.Erp
        };

        // Act
        var total = stockData.Total;
        var available = stockData.Available;

        // Assert
        available.Should().Be(110m, "Available should be Erp (100) + Transport (10) = 110");
        total.Should().Be(135m, "Total should be Available (110) + Reserve (25) = 135");
    }

    [Fact]
    public void Total_WithEshopAsPrimarySource_ReturnsAvailablePlusReserve()
    {
        // Arrange
        var stockData = new StockData
        {
            Erp = 100m,
            Eshop = 80m,
            Transport = 15m,
            Reserve = 30m,
            PrimaryStockSource = StockSource.Eshop
        };

        // Act
        var total = stockData.Total;
        var available = stockData.Available;

        // Assert
        available.Should().Be(95m, "Available should be Eshop (80) + Transport (15) = 95");
        total.Should().Be(125m, "Total should be Available (95) + Reserve (30) = 125");
    }

    [Fact]
    public void Total_WithZeroReserve_ReturnsOnlyAvailable()
    {
        // Arrange
        var stockData = new StockData
        {
            Erp = 100m,
            Eshop = 50m,
            Transport = 10m,
            Reserve = 0m,
            PrimaryStockSource = StockSource.Erp
        };

        // Act
        var total = stockData.Total;
        var available = stockData.Available;

        // Assert
        available.Should().Be(110m);
        total.Should().Be(110m, "Total should equal Available when Reserve is zero");
    }
}