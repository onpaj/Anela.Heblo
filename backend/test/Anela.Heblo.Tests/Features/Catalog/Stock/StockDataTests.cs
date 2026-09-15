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

    [Fact]
    public void Available_IncludesManufactured()
    {
        // Arrange
        var stockData = new StockData
        {
            Erp = 100m,
            Transport = 10m,
            Manufactured = 7m,
            Reserve = 25m,
            PrimaryStockSource = StockSource.Erp
        };

        // Act
        var available = stockData.Available;
        var total = stockData.Total;

        // Assert
        available.Should().Be(117m, "Available should be Erp (100) + Transport (10) + Manufactured (7)");
        total.Should().Be(142m, "Total should be Available (117) + Reserve (25)");
    }

    [Fact]
    public void WarehouseStock_ExcludesTransportAndManufactured()
    {
        // Arrange
        var stockData = new StockData
        {
            Erp = 184m,
            Transport = 12m,
            Manufactured = 165m,
            Reserve = 25m,
            PrimaryStockSource = StockSource.Erp
        };

        // Act
        var warehouseStock = stockData.WarehouseStock;

        // Assert
        warehouseStock.Should().Be(184m,
            "WarehouseStock is what is physically in the selling warehouse - goods still in transport "
            + "and goods written down into the manufacture warehouse are not there yet");
    }

    [Fact]
    public void WarehouseStock_UsesEshopWhenEshopIsPrimarySource()
    {
        // Arrange
        var stockData = new StockData
        {
            Erp = 200m,
            Eshop = 184m,
            Transport = 12m,
            Manufactured = 165m,
            PrimaryStockSource = StockSource.Eshop
        };

        // Act
        var warehouseStock = stockData.WarehouseStock;

        // Assert
        warehouseStock.Should().Be(184m, "an e-shop product takes its warehouse figure from the e-shop feed");
    }

    [Fact]
    public void Available_EqualsWarehouseStockPlusTransportAndManufactured()
    {
        // Arrange
        var stockData = new StockData
        {
            Erp = 184m,
            Transport = 12m,
            Manufactured = 165m,
            PrimaryStockSource = StockSource.Erp
        };

        // Act
        var available = stockData.Available;

        // Assert
        available.Should().Be(stockData.WarehouseStock + 12m + 165m);
    }
}
