using Anela.Heblo.Application.Features.Manufacture.Configuration;
using Anela.Heblo.Application.Features.Manufacture.Model;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Services;

public class ManufactureAnalysisMapperTests
{
    private readonly ManufactureAnalysisMapper _sut;
    private readonly IOptions<ManufactureAnalysisOptions> _options;

    public ManufactureAnalysisMapperTests()
    {
        _options = Substitute.For<IOptions<ManufactureAnalysisOptions>>();
        _options.Value.Returns(new ManufactureAnalysisOptions
        {
            InfiniteStockIndicator = 999999,
            DefaultMonthsBack = 12,
            MaxMonthsBack = 60,
            ProductionActivityDays = 30,
            CriticalStockMultiplier = 1.0m,
            HighStockMultiplier = 1.5m,
            MediumStockMultiplier = 2.0m
        });
        _sut = new ManufactureAnalysisMapper(_options);
    }

    [Fact]
    public void MapToDto_WhenStockDaysAvailableIsInfinity_SetsInfiniteStockIndicator()
    {
        // Arrange
        var item = CreateCatalogAggregate();
        var fromDate = DateTime.Today.AddMonths(-1);
        var toDate = DateTime.Today;
        var severity = ManufacturingStockSeverity.Adequate;

        // Make the sales history empty so dailySalesRate is 0 and stockDaysAvailable becomes infinity
        item.SaleHistorySummary = new Anela.Heblo.Domain.Features.Catalog.Sales.SaleHistorySummary
        {
            MonthlyData = new Dictionary<string, Anela.Heblo.Domain.Features.Catalog.Sales.MonthlySalesSummary>()
        };

        // Act
        var result = _sut.MapToDto(item, fromDate, toDate, severity);

        // Assert
        Assert.Equal(999999, result.StockDaysAvailable);
    }

    [Fact]
    public void MapToDto_WhenStockDaysAvailableIsFinite_SetsActualValue()
    {
        // Arrange
        var item = CreateCatalogAggregate(availableStock: 100);
        var fromDate = DateTime.Today.AddMonths(-1);
        var toDate = DateTime.Today;
        var severity = ManufacturingStockSeverity.Adequate;

        // Set up some sales history to get a finite dailySalesRate
        var monthKey = $"{fromDate.Year:D4}-{fromDate.Month:D2}";
        item.SaleHistorySummary = new Anela.Heblo.Domain.Features.Catalog.Sales.SaleHistorySummary
        {
            MonthlyData = new Dictionary<string, Anela.Heblo.Domain.Features.Catalog.Sales.MonthlySalesSummary>
            {
                [monthKey] = new Anela.Heblo.Domain.Features.Catalog.Sales.MonthlySalesSummary
                {
                    Year = fromDate.Year,
                    Month = fromDate.Month,
                    TotalAmount = 10, // 10 units sold
                    TotalRevenue = 100
                }
            }
        };

        // Act
        var result = _sut.MapToDto(item, fromDate, toDate, severity);

        // Assert
        Assert.NotEqual(999999, result.StockDaysAvailable);
        Assert.True(result.StockDaysAvailable > 0);
        Assert.True(result.StockDaysAvailable < 999999);
    }

    [Fact]
    public void MapToDto_WithCustomInfiniteStockIndicator_UsesConfiguredValue()
    {
        // Arrange
        var customOptions = Substitute.For<IOptions<ManufactureAnalysisOptions>>();
        customOptions.Value.Returns(new ManufactureAnalysisOptions
        {
            InfiniteStockIndicator = 555555 // Custom value
        });
        var mapper = new ManufactureAnalysisMapper(customOptions);

        var item = CreateCatalogAggregate();
        var fromDate = DateTime.Today.AddMonths(-1);
        var toDate = DateTime.Today;
        var severity = ManufacturingStockSeverity.Adequate;

        // Make sales history empty so stockDaysAvailable becomes infinity
        item.SaleHistorySummary = new Anela.Heblo.Domain.Features.Catalog.Sales.SaleHistorySummary
        {
            MonthlyData = new Dictionary<string, Anela.Heblo.Domain.Features.Catalog.Sales.MonthlySalesSummary>()
        };

        // Act
        var result = mapper.MapToDto(item, fromDate, toDate, severity);

        // Assert
        Assert.Equal(555555, result.StockDaysAvailable);
    }

    private static CatalogAggregate CreateCatalogAggregate(
        int optimalStockDaysSetup = 30,
        decimal stockMinSetup = 50,
        decimal availableStock = 100)
    {
        return new CatalogAggregate
        {
            Id = "TEST001",
            ProductCode = "TEST001",
            ProductName = "Test Product",
            Properties = new CatalogProperties
            {
                OptimalStockDaysSetup = optimalStockDaysSetup,
                StockMinSetup = stockMinSetup,
                BatchSize = 1
            },
            Stock = new StockData
            {
                Erp = availableStock,
                PrimaryStockSource = StockSource.Erp
            },
            SaleHistorySummary = new Anela.Heblo.Domain.Features.Catalog.Sales.SaleHistorySummary
            {
                MonthlyData = new Dictionary<string, Anela.Heblo.Domain.Features.Catalog.Sales.MonthlySalesSummary>()
            }
        };
    }
}