using Anela.Heblo.Application.Features.Manufacture.Model;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Services;

public class ManufactureAnalysisMapperTests
{
    private readonly ManufactureAnalysisMapper _mapper;
    private readonly Mock<ILogger<ManufactureAnalysisMapper>> _loggerMock;

    public ManufactureAnalysisMapperTests()
    {
        _loggerMock = new Mock<ILogger<ManufactureAnalysisMapper>>();
        _mapper = new ManufactureAnalysisMapper(_loggerMock.Object);
    }

    private CatalogAggregate CreateTestCatalogItem(
        string productCode = "TEST001",
        string productName = "Test Product",
        decimal availableStock = 50,
        int optimalStockDaysSetup = 30,
        decimal stockMinSetup = 10,
        int batchSize = 5)
    {
        return new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = productName,
            Stock = new StockData
            {
                Erp = availableStock, // Available is calculated as Erp + Transport when PrimaryStockSource is Erp
                Transport = 0
            },
            Properties = new CatalogProperties
            {
                OptimalStockDaysSetup = optimalStockDaysSetup,
                StockMinSetup = stockMinSetup,
                BatchSize = batchSize
            }
        };
    }

    [Fact]
    public void MapToDto_WithValidInputs_MapsAllPropertiesCorrectly()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem(
            productCode: "PROD123",
            productName: "Test Product Name",
            availableStock: 75,
            optimalStockDaysSetup: 25,
            stockMinSetup: 15,
            batchSize: 10);

        var severity = ManufacturingStockSeverity.Critical;
        double dailySalesRate = 2.5;
        double salesInPeriod = 100.0;
        double stockDaysAvailable = 30.0;
        double overstockPercentage = 120.0;
        bool isInProduction = true;

        // Act
        var result = _mapper.MapToDto(
            catalogItem,
            severity,
            dailySalesRate,
            salesInPeriod,
            stockDaysAvailable,
            overstockPercentage,
            isInProduction);

        // Assert
        Assert.Equal("PROD123", result.Code);
        Assert.Equal("Test Product Name", result.Name);
        Assert.Equal(75.0, result.CurrentStock);
        Assert.Equal(100.0, result.SalesInPeriod);
        Assert.Equal(2.5, result.DailySalesRate);
        Assert.Equal(25, result.OptimalDaysSetup);
        Assert.Equal(30.0, result.StockDaysAvailable);
        Assert.Equal(15.0, result.MinimumStock);
        Assert.Equal(120.0, result.OverstockPercentage);
        Assert.Equal("10", result.BatchSize);
        Assert.Equal("PROD12", result.ProductFamily); // First 6 characters of product code
        Assert.Equal(ManufacturingStockSeverity.Critical, result.Severity);
        Assert.True(result.IsConfigured); // OptimalStockDaysSetup > 0
    }

    [Fact]
    public void MapToDto_WithInfiniteStockDays_CapsAtDisplayLimit()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem();
        var severity = ManufacturingStockSeverity.Adequate;
        double dailySalesRate = 0.0;
        double salesInPeriod = 0.0;
        double stockDaysAvailable = double.PositiveInfinity;
        double overstockPercentage = 50.0;
        bool isInProduction = false;

        // Act
        var result = _mapper.MapToDto(
            catalogItem,
            severity,
            dailySalesRate,
            salesInPeriod,
            stockDaysAvailable,
            overstockPercentage,
            isInProduction);

        // Assert
        Assert.Equal(999999.0, result.StockDaysAvailable);
    }

    [Fact]
    public void MapToDto_WithInfiniteOverstockPercentage_SetsToZero()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem();
        var severity = ManufacturingStockSeverity.Unconfigured;
        double dailySalesRate = 1.0;
        double salesInPeriod = 30.0;
        double stockDaysAvailable = 50.0;
        double overstockPercentage = double.PositiveInfinity;
        bool isInProduction = true;

        // Act
        var result = _mapper.MapToDto(
            catalogItem,
            severity,
            dailySalesRate,
            salesInPeriod,
            stockDaysAvailable,
            overstockPercentage,
            isInProduction);

        // Assert
        Assert.Equal(0.0, result.OverstockPercentage);
    }

    [Fact]
    public void MapToDto_WithUnconfiguredItem_SetsIsConfiguredToFalse()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem(optimalStockDaysSetup: 0);
        var severity = ManufacturingStockSeverity.Unconfigured;
        double dailySalesRate = 1.0;
        double salesInPeriod = 30.0;
        double stockDaysAvailable = 50.0;
        double overstockPercentage = 0.0;
        bool isInProduction = false;

        // Act
        var result = _mapper.MapToDto(
            catalogItem,
            severity,
            dailySalesRate,
            salesInPeriod,
            stockDaysAvailable,
            overstockPercentage,
            isInProduction);

        // Assert
        Assert.False(result.IsConfigured);
    }

    [Fact]
    public void MapToDto_WithNullProductFamily_SetsToEmptyString()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem();
        // ProductFamily is derived from ProductCode, so test with empty code
        catalogItem.ProductCode = string.Empty;

        var severity = ManufacturingStockSeverity.Adequate;
        double dailySalesRate = 1.0;
        double salesInPeriod = 30.0;
        double stockDaysAvailable = 50.0;
        double overstockPercentage = 100.0;
        bool isInProduction = false;

        // Act
        var result = _mapper.MapToDto(
            catalogItem,
            severity,
            dailySalesRate,
            salesInPeriod,
            stockDaysAvailable,
            overstockPercentage,
            isInProduction);

        // Assert
        Assert.Equal(string.Empty, result.ProductFamily);
    }

    [Fact]
    public void MapToDto_WithShortProductCode_HandlesProductFamilyCorrectly()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem(productCode: "ABC"); // Shorter than 6 characters
        var severity = ManufacturingStockSeverity.Adequate;
        double dailySalesRate = 1.0;
        double salesInPeriod = 30.0;
        double stockDaysAvailable = 50.0;
        double overstockPercentage = 100.0;
        bool isInProduction = false;

        // Act
        var result = _mapper.MapToDto(
            catalogItem,
            severity,
            dailySalesRate,
            salesInPeriod,
            stockDaysAvailable,
            overstockPercentage,
            isInProduction);

        // Assert
        Assert.Equal("ABC", result.ProductFamily); // Should be the full code when shorter than 6
    }

    [Theory]
    [InlineData(ManufacturingStockSeverity.Critical)]
    [InlineData(ManufacturingStockSeverity.Major)]
    [InlineData(ManufacturingStockSeverity.Minor)]
    [InlineData(ManufacturingStockSeverity.Adequate)]
    [InlineData(ManufacturingStockSeverity.Unconfigured)]
    public void MapToDto_WithAllSeverityLevels_PreservesSeverity(ManufacturingStockSeverity severity)
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem();
        double dailySalesRate = 1.0;
        double salesInPeriod = 30.0;
        double stockDaysAvailable = 50.0;
        double overstockPercentage = 100.0;
        bool isInProduction = false;

        // Act
        var result = _mapper.MapToDto(
            catalogItem,
            severity,
            dailySalesRate,
            salesInPeriod,
            stockDaysAvailable,
            overstockPercentage,
            isInProduction);

        // Assert
        Assert.Equal(severity, result.Severity);
    }

    [Fact]
    public void MapToDto_WithZeroValues_HandlesCorrectly()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem(
            availableStock: 0,
            optimalStockDaysSetup: 0,
            stockMinSetup: 0,
            batchSize: 0);

        var severity = ManufacturingStockSeverity.Unconfigured;
        double dailySalesRate = 0.0;
        double salesInPeriod = 0.0;
        double stockDaysAvailable = 0.0;
        double overstockPercentage = 0.0;
        bool isInProduction = false;

        // Act
        var result = _mapper.MapToDto(
            catalogItem,
            severity,
            dailySalesRate,
            salesInPeriod,
            stockDaysAvailable,
            overstockPercentage,
            isInProduction);

        // Assert
        Assert.Equal(0.0, result.CurrentStock);
        Assert.Equal(0.0, result.SalesInPeriod);
        Assert.Equal(0.0, result.DailySalesRate);
        Assert.Equal(0, result.OptimalDaysSetup);
        Assert.Equal(0.0, result.StockDaysAvailable);
        Assert.Equal(0.0, result.MinimumStock);
        Assert.Equal(0.0, result.OverstockPercentage);
        Assert.Equal("0", result.BatchSize);
        Assert.False(result.IsConfigured);
    }
}