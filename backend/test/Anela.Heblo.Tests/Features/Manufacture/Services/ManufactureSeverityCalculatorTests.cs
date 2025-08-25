using Anela.Heblo.Application.Features.Manufacture.Model;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using NSubstitute;
using Xunit;
using StockSource = Anela.Heblo.Domain.Features.Catalog.Stock.StockSource;

namespace Anela.Heblo.Tests.Features.Manufacture.Services;

public class ManufactureSeverityCalculatorTests
{
    private readonly ManufactureSeverityCalculator _sut;

    public ManufactureSeverityCalculatorTests()
    {
        _sut = new ManufactureSeverityCalculator();
    }

    [Fact]
    public void CalculateSeverity_WhenOptimalStockDaysSetupIsZero_ReturnsUnconfigured()
    {
        // Arrange
        var catalogAggregate = CreateCatalogAggregate(optimalStockDaysSetup: 0);
        var dailySalesRate = 10.0;
        var overstockPercentage = 50.0;

        // Act
        var result = _sut.CalculateSeverity(catalogAggregate, dailySalesRate, overstockPercentage);

        // Assert
        Assert.Equal(ManufacturingStockSeverity.Unconfigured, result);
    }

    [Fact]
    public void CalculateSeverity_WhenOverstockLessThan100AndDailySalesRateGreaterThanZero_ReturnsCritical()
    {
        // Arrange
        var catalogAggregate = CreateCatalogAggregate(optimalStockDaysSetup: 30);
        var dailySalesRate = 10.0;
        var overstockPercentage = 80.0; // Less than 100%

        // Act
        var result = _sut.CalculateSeverity(catalogAggregate, dailySalesRate, overstockPercentage);

        // Assert
        Assert.Equal(ManufacturingStockSeverity.Critical, result);
    }

    [Fact]
    public void CalculateSeverity_WhenOverstockLessThan100ButDailySalesRateIsZero_DoesNotReturnCritical()
    {
        // Arrange
        var catalogAggregate = CreateCatalogAggregate(optimalStockDaysSetup: 30);
        var dailySalesRate = 0.0;
        var overstockPercentage = 80.0; // Less than 100%

        // Act
        var result = _sut.CalculateSeverity(catalogAggregate, dailySalesRate, overstockPercentage);

        // Assert
        Assert.Equal(ManufacturingStockSeverity.Adequate, result);
    }

    [Fact]
    public void CalculateSeverity_WhenBelowMinimumStock_ReturnsMajor()
    {
        // Arrange
        var catalogAggregate = CreateCatalogAggregate(
            optimalStockDaysSetup: 30, 
            stockMinSetup: 100, 
            availableStock: 50); // Below minimum
        var dailySalesRate = 10.0;
        var overstockPercentage = 150.0; // Above 100%

        // Act
        var result = _sut.CalculateSeverity(catalogAggregate, dailySalesRate, overstockPercentage);

        // Assert
        Assert.Equal(ManufacturingStockSeverity.Major, result);
    }

    [Fact]
    public void CalculateSeverity_WhenAllConditionsOk_ReturnsAdequate()
    {
        // Arrange
        var catalogAggregate = CreateCatalogAggregate(
            optimalStockDaysSetup: 30, 
            stockMinSetup: 50, 
            availableStock: 100); // Above minimum
        var dailySalesRate = 10.0;
        var overstockPercentage = 150.0; // Above 100%

        // Act
        var result = _sut.CalculateSeverity(catalogAggregate, dailySalesRate, overstockPercentage);

        // Assert
        Assert.Equal(ManufacturingStockSeverity.Adequate, result);
    }

    [Fact]
    public void CalculateSeverity_WhenMinStockSetupIsZero_IgnoresMinimumStockCheck()
    {
        // Arrange
        var catalogAggregate = CreateCatalogAggregate(
            optimalStockDaysSetup: 30, 
            stockMinSetup: 0, 
            availableStock: 10); // Would be below minimum if it was set
        var dailySalesRate = 10.0;
        var overstockPercentage = 150.0; // Above 100%

        // Act
        var result = _sut.CalculateSeverity(catalogAggregate, dailySalesRate, overstockPercentage);

        // Assert
        Assert.Equal(ManufacturingStockSeverity.Adequate, result);
    }

    private static CatalogAggregate CreateCatalogAggregate(
        int optimalStockDaysSetup = 30,
        decimal stockMinSetup = 0,
        decimal availableStock = 100)
    {
        return new CatalogAggregate
        {
            Id = "TEST001",
            Properties = new CatalogProperties
            {
                OptimalStockDaysSetup = optimalStockDaysSetup,
                StockMinSetup = stockMinSetup
            },
            Stock = new StockData
            {
                Erp = availableStock,
                PrimaryStockSource = StockSource.Erp
            }
        };
    }
}