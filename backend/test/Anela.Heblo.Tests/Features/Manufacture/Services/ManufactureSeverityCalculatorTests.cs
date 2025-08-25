using Anela.Heblo.Application.Features.Manufacture.Model;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Services;

public class ManufactureSeverityCalculatorTests
{
    private readonly ManufactureSeverityCalculator _calculator;
    private readonly Mock<ILogger<ManufactureSeverityCalculator>> _loggerMock;

    public ManufactureSeverityCalculatorTests()
    {
        _loggerMock = new Mock<ILogger<ManufactureSeverityCalculator>>();
        _calculator = new ManufactureSeverityCalculator(_loggerMock.Object);
    }

    private CatalogAggregate CreateTestCatalogItem(
        int optimalStockDaysSetup = 30,
        decimal stockMinSetup = 10,
        decimal availableStock = 50)
    {
        return new CatalogAggregate
        {
            ProductCode = "TEST001",
            ProductName = "Test Product",
            Properties = new CatalogProperties
            {
                OptimalStockDaysSetup = optimalStockDaysSetup,
                StockMinSetup = stockMinSetup
            },
            Stock = new StockData
            {
                Erp = availableStock, // Available is calculated as Erp + Transport when PrimaryStockSource is Erp
                Transport = 0
            }
        };
    }

    [Fact]
    public void CalculateSeverity_WithoutConfiguration_ReturnsUnconfigured()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem(optimalStockDaysSetup: 0);
        double dailySalesRate = 2.0;
        double stockDaysAvailable = 25.0;

        // Act
        var result = _calculator.CalculateSeverity(catalogItem, dailySalesRate, stockDaysAvailable);

        // Assert
        Assert.Equal(ManufacturingStockSeverity.Unconfigured, result);
    }

    [Fact]
    public void CalculateSeverity_WithLowOverstock_ReturnsCritical()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem(optimalStockDaysSetup: 30, availableStock: 50);
        double dailySalesRate = 2.0; // Must be > 0 for critical
        double stockDaysAvailable = 20.0; // stockDaysAvailable < optimalStockDaysSetup (20 < 30)
        // Overstock percentage: (20 / 30) * 100 = 66.67% < 100%

        // Act
        var result = _calculator.CalculateSeverity(catalogItem, dailySalesRate, stockDaysAvailable);

        // Assert
        Assert.Equal(ManufacturingStockSeverity.Critical, result);
    }

    [Fact]
    public void CalculateSeverity_WithZeroSalesRate_DoesNotReturnCritical()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem(optimalStockDaysSetup: 30, availableStock: 50);
        double dailySalesRate = 0.0; // Zero sales rate
        double stockDaysAvailable = 20.0; // Would be critical if dailySalesRate > 0

        // Act
        var result = _calculator.CalculateSeverity(catalogItem, dailySalesRate, stockDaysAvailable);

        // Assert
        Assert.NotEqual(ManufacturingStockSeverity.Critical, result);
    }

    [Fact]
    public void CalculateSeverity_BelowMinimumStock_ReturnsMajor()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem(
            optimalStockDaysSetup: 30,
            stockMinSetup: 20,
            availableStock: 15); // Below minimum stock (15 < 20)
        double dailySalesRate = 1.0;
        double stockDaysAvailable = 40.0; // Above optimal, but below minimum stock

        // Act
        var result = _calculator.CalculateSeverity(catalogItem, dailySalesRate, stockDaysAvailable);

        // Assert
        Assert.Equal(ManufacturingStockSeverity.Major, result);
    }

    [Fact]
    public void CalculateSeverity_AboveOptimalStock_ReturnsAdequate()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem(
            optimalStockDaysSetup: 30,
            stockMinSetup: 10,
            availableStock: 50);
        double dailySalesRate = 1.0;
        double stockDaysAvailable = 40.0; // Above optimal (40 > 30)
        // Overstock percentage: (40 / 30) * 100 = 133.33% > 100%

        // Act
        var result = _calculator.CalculateSeverity(catalogItem, dailySalesRate, stockDaysAvailable);

        // Assert
        Assert.Equal(ManufacturingStockSeverity.Adequate, result);
    }

    [Fact]
    public void CalculateOverstockPercentage_WithValidInputs_ReturnsCorrectPercentage()
    {
        // Arrange
        double stockDaysAvailable = 45.0;
        int optimalStockDaysSetup = 30;
        // Expected: (45 / 30) * 100 = 150%

        // Act
        var result = _calculator.CalculateOverstockPercentage(stockDaysAvailable, optimalStockDaysSetup);

        // Assert
        Assert.Equal(150.0, result);
    }

    [Fact]
    public void CalculateOverstockPercentage_WithZeroOptimal_ReturnsZero()
    {
        // Arrange
        double stockDaysAvailable = 45.0;
        int optimalStockDaysSetup = 0;

        // Act
        var result = _calculator.CalculateOverstockPercentage(stockDaysAvailable, optimalStockDaysSetup);

        // Assert
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void CalculateOverstockPercentage_WithInfiniteStock_ReturnsZero()
    {
        // Arrange
        double stockDaysAvailable = double.PositiveInfinity;
        int optimalStockDaysSetup = 30;

        // Act
        var result = _calculator.CalculateOverstockPercentage(stockDaysAvailable, optimalStockDaysSetup);

        // Assert
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void IsConfiguredForAnalysis_WithPositiveOptimalDays_ReturnsTrue()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem(optimalStockDaysSetup: 30);

        // Act
        var result = _calculator.IsConfiguredForAnalysis(catalogItem);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsConfiguredForAnalysis_WithZeroOptimalDays_ReturnsFalse()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem(optimalStockDaysSetup: 0);

        // Act
        var result = _calculator.IsConfiguredForAnalysis(catalogItem);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsConfiguredForAnalysis_WithNegativeOptimalDays_ReturnsFalse()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem(optimalStockDaysSetup: -5);

        // Act
        var result = _calculator.IsConfiguredForAnalysis(catalogItem);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(10.0, 30, 33.33)] // Below optimal
    [InlineData(30.0, 30, 100.0)] // Exactly optimal
    [InlineData(45.0, 30, 150.0)] // Above optimal
    [InlineData(0.0, 30, 0.0)]    // No stock
    public void CalculateOverstockPercentage_WithVariousInputs_ReturnsExpectedResults(
        double stockDaysAvailable,
        int optimalStockDaysSetup,
        double expectedPercentage)
    {
        // Act
        var result = _calculator.CalculateOverstockPercentage(stockDaysAvailable, optimalStockDaysSetup);

        // Assert
        Assert.Equal(expectedPercentage, result, precision: 2);
    }

    [Fact]
    public void CalculateSeverity_PrioritizesUnconfiguredOverOtherConditions()
    {
        // Arrange - Item that would be critical/major if configured, but isn't configured
        var catalogItem = CreateTestCatalogItem(
            optimalStockDaysSetup: 0, // Not configured
            stockMinSetup: 20,
            availableStock: 5); // Below minimum stock
        double dailySalesRate = 2.0;
        double stockDaysAvailable = 2.5;

        // Act
        var result = _calculator.CalculateSeverity(catalogItem, dailySalesRate, stockDaysAvailable);

        // Assert
        Assert.Equal(ManufacturingStockSeverity.Unconfigured, result);
    }

    [Fact]
    public void CalculateSeverity_PrioritizesCriticalOverMajor()
    {
        // Arrange - Item that is both critical (low overstock) and major (below min stock)
        var catalogItem = CreateTestCatalogItem(
            optimalStockDaysSetup: 30,
            stockMinSetup: 20,
            availableStock: 15); // Below minimum stock
        double dailySalesRate = 1.0; // > 0 for critical check
        double stockDaysAvailable = 20.0; // Below optimal (20 < 30), so < 100% overstock

        // Act
        var result = _calculator.CalculateSeverity(catalogItem, dailySalesRate, stockDaysAvailable);

        // Assert - Should return Critical, not Major
        Assert.Equal(ManufacturingStockSeverity.Critical, result);
    }
}