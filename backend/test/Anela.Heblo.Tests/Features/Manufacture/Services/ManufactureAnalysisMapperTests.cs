using Anela.Heblo.Application.Features.Manufacture.Configuration;
using Anela.Heblo.Application.Features.Manufacture.Model;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Services;

public class ManufactureAnalysisMapperTests
{
    private readonly ManufactureAnalysisMapper _mapper;
    private readonly Mock<ILogger<ManufactureAnalysisMapper>> _loggerMock;
    private readonly Mock<IOptions<ManufactureAnalysisOptions>> _optionsMock;

    public ManufactureAnalysisMapperTests()
    {
        _loggerMock = new Mock<ILogger<ManufactureAnalysisMapper>>();
        _optionsMock = new Mock<IOptions<ManufactureAnalysisOptions>>();
        _optionsMock.Setup(o => o.Value).Returns(new ManufactureAnalysisOptions
        {
            InfiniteStockIndicator = 999999,
            DefaultMonthsBack = 12,
            MaxMonthsBack = 60,
            ProductionActivityDays = 30,
            CriticalStockMultiplier = 1.0m,
            HighStockMultiplier = 1.5m,
            MediumStockMultiplier = 2.0m
        });
        _mapper = new ManufactureAnalysisMapper(_optionsMock.Object, _loggerMock.Object);
    }

    private CatalogAggregate CreateTestCatalogItem(
        string productCode = "TEST001",
        string productName = "Test Product",
        decimal availableStock = 100,
        int optimalStockDaysSetup = 30,
        decimal stockMinSetup = 50,
        int batchSize = 10)
    {
        return new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = productName,
            Properties = new CatalogProperties
            {
                OptimalStockDaysSetup = optimalStockDaysSetup,
                StockMinSetup = stockMinSetup,
                BatchSize = batchSize
            },
            Stock = new StockData
            {
                Erp = availableStock, // Available is calculated as Erp + Transport when PrimaryStockSource is Erp
                Transport = 0
            }
        };
    }

    [Fact]
    public void MapToDto_WithValidInputs_MapsAllPropertiesCorrectly()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem(
            productCode: "PROD001",
            productName: "Product One",
            availableStock: 150,
            optimalStockDaysSetup: 30,
            stockMinSetup: 25,
            batchSize: 5);
        var severity = ManufacturingStockSeverity.Adequate;
        double dailySalesRate = 2.5;
        double salesInPeriod = 75.0;
        double stockDaysAvailable = 60.0;
        double overstockPercentage = 200.0;
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
        Assert.Equal("PROD001", result.Code);
        Assert.Equal("Product One", result.Name);
        Assert.Equal(150.0, result.CurrentStock);
        Assert.Equal(salesInPeriod, result.SalesInPeriod);
        Assert.Equal(dailySalesRate, result.DailySalesRate);
        Assert.Equal(30, result.OptimalDaysSetup);
        Assert.Equal(60.0, result.StockDaysAvailable);
        Assert.Equal(25.0, result.MinimumStock);
        Assert.Equal(200.0, result.OverstockPercentage);
        Assert.Equal("5", result.BatchSize);
        Assert.Equal(ManufacturingStockSeverity.Adequate, result.Severity);
        Assert.True(result.IsConfigured);
    }

    [Fact]
    public void MapToDto_WithInfiniteStockDays_UsesConfiguredIndicator()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem();
        var severity = ManufacturingStockSeverity.Adequate;
        double dailySalesRate = 0.0;
        double salesInPeriod = 0.0;
        double stockDaysAvailable = double.PositiveInfinity;
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
        Assert.Equal(999999.0, result.StockDaysAvailable);
    }

    [Fact]
    public void MapToDto_WithCustomInfiniteStockIndicator_UsesCustomValue()
    {
        // Arrange
        var customOptions = new Mock<IOptions<ManufactureAnalysisOptions>>();
        customOptions.Setup(o => o.Value).Returns(new ManufactureAnalysisOptions
        {
            InfiniteStockIndicator = 555555 // Custom value
        });
        var mapper = new ManufactureAnalysisMapper(customOptions.Object, _loggerMock.Object);

        var catalogItem = CreateTestCatalogItem();
        var severity = ManufacturingStockSeverity.Adequate;
        double dailySalesRate = 0.0;
        double salesInPeriod = 0.0;
        double stockDaysAvailable = double.PositiveInfinity;
        double overstockPercentage = 0.0;
        bool isInProduction = false;

        // Act
        var result = mapper.MapToDto(
            catalogItem,
            severity,
            dailySalesRate,
            salesInPeriod,
            stockDaysAvailable,
            overstockPercentage,
            isInProduction);

        // Assert
        Assert.Equal(555555.0, result.StockDaysAvailable);
    }

    [Fact]
    public void MapToDto_WithInfiniteOverstockPercentage_ReturnsZero()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem();
        var severity = ManufacturingStockSeverity.Adequate;
        double dailySalesRate = 0.0;
        double salesInPeriod = 0.0;
        double stockDaysAvailable = 100.0;
        double overstockPercentage = double.PositiveInfinity;
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
        Assert.Equal(0.0, result.OverstockPercentage);
    }

    [Fact]
    public void MapToDto_WithUnconfiguredOptimalDays_SetsIsConfiguredToFalse()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem(optimalStockDaysSetup: 0);
        var severity = ManufacturingStockSeverity.Unconfigured;
        double dailySalesRate = 1.0;
        double salesInPeriod = 30.0;
        double stockDaysAvailable = 100.0;
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
        Assert.Equal(0, result.OptimalDaysSetup);
    }

    [Fact]
    public void MapToDto_WithConfiguredOptimalDays_SetsIsConfiguredToTrue()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem(optimalStockDaysSetup: 30);
        var severity = ManufacturingStockSeverity.Adequate;
        double dailySalesRate = 1.0;
        double salesInPeriod = 30.0;
        double stockDaysAvailable = 30.0;
        double overstockPercentage = 100.0;
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
        Assert.True(result.IsConfigured);
        Assert.Equal(30, result.OptimalDaysSetup);
    }

    [Fact]
    public void MapToDto_WithNullProductFamily_ReturnsEmptyString()
    {
        // Arrange - Create catalog item with null ProductCode to trigger null ProductFamily
        var catalogItem = new CatalogAggregate
        {
            ProductCode = null!, // This will cause ProductFamily to be null
            ProductName = "Test Product",
            Stock = new StockData
            {
                Erp = 100,
                Transport = 0
            },
            Properties = new CatalogProperties
            {
                OptimalStockDaysSetup = 30,
                StockMinSetup = 50,
                BatchSize = 10
            }
        };
        var severity = ManufacturingStockSeverity.Adequate;
        double dailySalesRate = 1.0;
        double salesInPeriod = 30.0;
        double stockDaysAvailable = 100.0;
        double overstockPercentage = 333.33;
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
    public void MapToDto_WithProductFamily_ReturnsCorrectValue()
    {
        // Arrange - Test with valid ProductCode that generates ProductFamily
        var catalogItem = CreateTestCatalogItem(productCode: "TESTFAMILY01");
        var severity = ManufacturingStockSeverity.Adequate;
        double dailySalesRate = 1.0;
        double salesInPeriod = 30.0;
        double stockDaysAvailable = 100.0;
        double overstockPercentage = 333.33;
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

        // Assert - ProductFamily should be first 6 characters of ProductCode
        Assert.Equal("TESTFA", result.ProductFamily);
    }

    [Theory]
    [InlineData(ManufacturingStockSeverity.Critical)]
    [InlineData(ManufacturingStockSeverity.Major)]
    [InlineData(ManufacturingStockSeverity.Minor)]
    [InlineData(ManufacturingStockSeverity.Adequate)]
    [InlineData(ManufacturingStockSeverity.Unconfigured)]
    public void MapToDto_WithVariousSeverities_SetsSeverityCorrectly(ManufacturingStockSeverity severity)
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem();
        double dailySalesRate = 1.0;
        double salesInPeriod = 30.0;
        double stockDaysAvailable = 100.0;
        double overstockPercentage = 333.33;
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
    public void MapToDto_LogsDebugInformation()
    {
        // Arrange
        var catalogItem = CreateTestCatalogItem(productCode: "LOG001");
        var severity = ManufacturingStockSeverity.Critical;
        double dailySalesRate = 2.0;
        double salesInPeriod = 60.0;
        double stockDaysAvailable = 50.0;
        double overstockPercentage = 166.67;
        bool isInProduction = true;

        // Act
        _mapper.MapToDto(
            catalogItem,
            severity,
            dailySalesRate,
            salesInPeriod,
            stockDaysAvailable,
            overstockPercentage,
            isInProduction);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("LOG001") && o.ToString()!.Contains("Critical")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
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