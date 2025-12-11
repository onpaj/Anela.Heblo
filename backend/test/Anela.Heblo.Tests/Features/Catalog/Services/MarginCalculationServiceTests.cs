using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Repositories;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Services;

/// <summary>
/// Unit tests for MarginCalculationService with repository-based cost calculation
/// </summary>
public class MarginCalculationServiceTests
{
    private readonly Mock<IMaterialCostRepository> _materialCostRepositoryMock;
    private readonly Mock<IManufactureCostRepository> _manufactureCostRepositoryMock;
    private readonly Mock<ISalesCostCalculationService> _salesCostCalculationServiceMock;
    private readonly Mock<IOverheadCostCalculationService> _overheadCostCalculationServiceMock;
    private readonly Mock<ILogger<MarginCalculationService>> _loggerMock;
    private readonly MarginCalculationService _service;

    public MarginCalculationServiceTests()
    {
        _materialCostRepositoryMock = new Mock<IMaterialCostRepository>();
        _manufactureCostRepositoryMock = new Mock<IManufactureCostRepository>();
        _salesCostCalculationServiceMock = new Mock<ISalesCostCalculationService>();
        _overheadCostCalculationServiceMock = new Mock<IOverheadCostCalculationService>();
        _loggerMock = new Mock<ILogger<MarginCalculationService>>();

        _service = new MarginCalculationService(
            _materialCostRepositoryMock.Object,
            _manufactureCostRepositoryMock.Object,
            _salesCostCalculationServiceMock.Object,
            _overheadCostCalculationServiceMock.Object,
            _loggerMock.Object);
    }



    [Fact]
    public async Task GetMarginAsync_WithValidProduct_ReturnsCorrectHistory()
    {
        // Arrange
        var product = CreateTestProduct("PROD001", 100m);
        var dateFrom = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddMonths(-12));
        var dateTo = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var materialCosts = CreateMonthlyCostsForDateRange(dateFrom, dateTo);
        var manufactureCosts = CreateMonthlyCostsForDateRange(dateFrom, dateTo);
        var salesCosts = CreateMonthlyCostsForDateRange(dateFrom, dateTo);

        SetupRepositoryMocks("PROD001", materialCosts, manufactureCosts, salesCosts);

        // Act
        var result = await _service.GetMarginAsync(product, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().NotBeEmpty();
        result.MonthlyData.Should().BeInAscendingOrder(x => x.Month);

        // Verify each monthly data has proper margin calculations
        foreach (var monthlyData in result.MonthlyData)
        {
            monthlyData.M0.Should().NotBeNull();
            monthlyData.M1.Should().NotBeNull();
            monthlyData.M2.Should().NotBeNull();
            monthlyData.M3.Should().NotBeNull();
            monthlyData.CostsForMonth.Should().NotBeNull();
        }

        result.Averages.Should().NotBeNull();
        result.Averages.M0.Should().NotBeNull();
        result.Averages.M1.Should().NotBeNull();
        result.Averages.M2.Should().NotBeNull();
        result.Averages.M3.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMarginAsync_WithZeroSellingPrice_ReturnsEmptyHistory()
    {
        // Arrange
        var product = CreateTestProduct("PROD001", 0m);
        var dateFrom = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddMonths(-12));
        var dateTo = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // Act
        var result = await _service.GetMarginAsync(product, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().BeEmpty();
        result.Averages.Should().NotBeNull();
        result.Averages.M0.Should().BeEquivalentTo(MarginLevel.Zero);
        result.Averages.M1.Should().BeEquivalentTo(MarginLevel.Zero);
        result.Averages.M2.Should().BeEquivalentTo(MarginLevel.Zero);
        result.Averages.M3.Should().BeEquivalentTo(MarginLevel.Zero);
    }

    [Fact]
    public async Task GetMarginAsync_WithNegativeSellingPrice_ReturnsEmptyHistory()
    {
        // Arrange
        var product = CreateTestProduct("PROD001", -50m);
        var dateFrom = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddMonths(-12));
        var dateTo = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // Act
        var result = await _service.GetMarginAsync(product, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().BeEmpty();
        result.Averages.Should().NotBeNull();
        result.Averages.M0.Should().BeEquivalentTo(MarginLevel.Zero);
        result.Averages.M1.Should().BeEquivalentTo(MarginLevel.Zero);
        result.Averages.M2.Should().BeEquivalentTo(MarginLevel.Zero);
        result.Averages.M3.Should().BeEquivalentTo(MarginLevel.Zero);
    }

    [Fact]
    public async Task GetMarginAsync_WithNullSellingPrice_ReturnsEmptyHistory()
    {
        // Arrange
        var product = CreateTestProduct("PROD001", null);
        var dateFrom = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddMonths(-12));
        var dateTo = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // Act
        var result = await _service.GetMarginAsync(product, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().BeEmpty();
        result.Averages.Should().NotBeNull();
        result.Averages.M0.Should().BeEquivalentTo(MarginLevel.Zero);
        result.Averages.M1.Should().BeEquivalentTo(MarginLevel.Zero);
        result.Averages.M2.Should().BeEquivalentTo(MarginLevel.Zero);
        result.Averages.M3.Should().BeEquivalentTo(MarginLevel.Zero);
    }

    [Fact]
    public async Task GetMarginAsync_WithVerySmallSellingPrice_ReturnsEmptyHistory()
    {
        // Arrange
        var product = CreateTestProduct("PROD001", 0.001m);
        var dateFrom = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddMonths(-12));
        var dateTo = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // Act
        var result = await _service.GetMarginAsync(product, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().BeEmpty();
        result.Averages.Should().NotBeNull();
        result.Averages.M0.Should().BeEquivalentTo(MarginLevel.Zero);
        result.Averages.M1.Should().BeEquivalentTo(MarginLevel.Zero);
        result.Averages.M2.Should().BeEquivalentTo(MarginLevel.Zero);
        result.Averages.M3.Should().BeEquivalentTo(MarginLevel.Zero);
    }

    [Fact]
    public async Task GetMarginAsync_WithEmptyProductCode_ReturnsEmptyHistory()
    {
        // Arrange
        var product = CreateTestProduct("", 100m);
        var dateFrom = DateOnly.FromDateTime(DateTime.Now.AddMonths(-12));
        var dateTo = DateOnly.FromDateTime(DateTime.Now);

        // Act
        var result = await _service.GetMarginAsync(product, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().BeEmpty();
        result.Averages.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMarginAsync_WithInvalidDateRange_ReturnsEmptyHistory()
    {
        // Arrange
        var product = CreateTestProduct("PROD001", 100m);
        var dateFrom = DateOnly.FromDateTime(DateTime.Now.AddMonths(6)); // Budoucí datum
        var dateTo = DateOnly.FromDateTime(DateTime.Now); // Aktuální datum - dateFrom > dateTo

        // Act
        var result = await _service.GetMarginAsync(product, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().BeEmpty();
        result.Averages.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMarginAsync_WithSameDateFromAndDateTo_ReturnsSingleMonth()
    {
        // Arrange
        var product = CreateTestProduct("PROD001", 100m);
        var sameDate = DateOnly.FromDateTime(DateTime.Now);
        var materialCosts = CreateMonthlyCostsForDateRange(sameDate, sameDate);
        var manufactureCosts = CreateMonthlyCostsForDateRange(sameDate, sameDate);
        var salesCosts = CreateMonthlyCostsForDateRange(sameDate, sameDate);

        SetupRepositoryMocks("PROD001", materialCosts, manufactureCosts, salesCosts);

        // Act
        var result = await _service.GetMarginAsync(product, sameDate, sameDate, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().HaveCount(1);
        result.MonthlyData[0].Month.Year.Should().Be(sameDate.Year);
        result.MonthlyData[0].Month.Month.Should().Be(sameDate.Month);
    }

    [Fact]
    public async Task GetMarginAsync_WithVeryOldDates_ReturnsCorrectHistory()
    {
        // Arrange
        var product = CreateTestProduct("PROD001", 100m);
        var dateFrom = new DateOnly(2000, 1, 1);
        var dateTo = new DateOnly(2000, 3, 31);
        var materialCosts = CreateMonthlyCostsForDateRange(dateFrom, dateTo);
        var manufactureCosts = CreateMonthlyCostsForDateRange(dateFrom, dateTo);
        var salesCosts = CreateMonthlyCostsForDateRange(dateFrom, dateTo);

        SetupRepositoryMocks("PROD001", materialCosts, manufactureCosts, salesCosts);

        // Act
        var result = await _service.GetMarginAsync(product, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().HaveCount(3); // Jan, Feb, Mar 2000
        result.MonthlyData.Should().OnlyContain(m => m.Month.Year == 2000);
    }

    [Fact]
    public async Task GetMarginAsync_WithFutureDates_ReturnsCorrectHistory()
    {
        // Arrange
        var product = CreateTestProduct("PROD001", 100m);
        var dateFrom = new DateOnly(2030, 6, 1);
        var dateTo = new DateOnly(2030, 8, 31);
        var materialCosts = CreateMonthlyCostsForDateRange(dateFrom, dateTo);
        var manufactureCosts = CreateMonthlyCostsForDateRange(dateFrom, dateTo);
        var salesCosts = CreateMonthlyCostsForDateRange(dateFrom, dateTo);

        SetupRepositoryMocks("PROD001", materialCosts, manufactureCosts, salesCosts);

        // Act
        var result = await _service.GetMarginAsync(product, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().HaveCount(3); // Jun, Jul, Aug 2030
        result.MonthlyData.Should().OnlyContain(m => m.Month.Year == 2030);
    }

    [Fact]
    public async Task GetMarginAsync_WithPartialCostData_OnlyMaterialCosts_ReturnsCorrectMargins()
    {
        // Arrange
        var product = CreateTestProduct("PROD001", 100m);
        var dateFrom = DateOnly.FromDateTime(DateTime.Now.AddMonths(-3));
        var dateTo = DateOnly.FromDateTime(DateTime.Now);

        var materialCosts = CreateMonthlyCostsForDateRange(dateFrom, dateTo);
        var emptyCosts = new List<MonthlyCost>(); // Prázdné výrobní a prodejní náklady

        SetupRepositoryMocks("PROD001", materialCosts, emptyCosts, emptyCosts);

        // Act
        var result = await _service.GetMarginAsync(product, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().NotBeEmpty();

        // Všechny měsíce by měly mít pouze materiálové náklady (ostatní = 0)
        foreach (var monthlyData in result.MonthlyData)
        {
            monthlyData.CostsForMonth.M0CostLevel.Should().BeGreaterThan(0);
            monthlyData.CostsForMonth.M1CostLevel.Should().Be(0);
            monthlyData.CostsForMonth.M2CostLevel.Should().Be(0);
            monthlyData.CostsForMonth.M3CostLevel.Should().Be(25m, $"Expected 25m but got {monthlyData.CostsForMonth.M3CostLevel}m"); // Mock overhead cost
        }
    }

    [Fact]
    public async Task GetMarginAsync_WithAllEmptyCostRepositories_ReturnsZeroCostMargins()
    {
        // Arrange
        var product = CreateTestProduct("PROD001", 100m);
        var dateFrom = DateOnly.FromDateTime(DateTime.Now.AddMonths(-3));
        var dateTo = DateOnly.FromDateTime(DateTime.Now);

        var emptyCosts = new List<MonthlyCost>();

        SetupRepositoryMocksWithEmptyOverhead("PROD001", emptyCosts, emptyCosts, emptyCosts, emptyCosts);

        // Act
        var result = await _service.GetMarginAsync(product, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().NotBeEmpty();

        // Všechny měsíce by měly mít nulové náklady
        foreach (var monthlyData in result.MonthlyData)
        {
            monthlyData.CostsForMonth.M0CostLevel.Should().Be(0);
            monthlyData.CostsForMonth.M1CostLevel.Should().Be(0);
            monthlyData.CostsForMonth.M2CostLevel.Should().Be(0);
            monthlyData.CostsForMonth.M3CostLevel.Should().Be(0);
        }
    }

    [Fact]
    public async Task GetMarginAsync_WithCostsOutsideDateRange_UsesNearestAvailableCosts()
    {
        // Arrange
        var product = CreateTestProduct("PROD001", 100m);
        var dateFrom = DateOnly.FromDateTime(DateTime.Now.AddMonths(-3));
        var dateTo = DateOnly.FromDateTime(DateTime.Now);

        // Náklady pouze před požadovaným obdobím
        var oldDate = dateFrom.AddMonths(-6);
        var materialCosts = new List<MonthlyCost>
        {
            new MonthlyCost(oldDate.ToDateTime(TimeOnly.MinValue), 15m)
        };
        var manufactureCosts = new List<MonthlyCost>
        {
            new MonthlyCost(oldDate.ToDateTime(TimeOnly.MinValue), 8m)
        };
        var salesCosts = new List<MonthlyCost>
        {
            new MonthlyCost(oldDate.ToDateTime(TimeOnly.MinValue), 3m)
        };

        SetupRepositoryMocks("PROD001", materialCosts, manufactureCosts, salesCosts);

        // Act
        var result = await _service.GetMarginAsync(product, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().NotBeEmpty();

        // Všechny měsíce by měly používat nejbližší dostupné náklady
        foreach (var monthlyData in result.MonthlyData)
        {
            monthlyData.CostsForMonth.M0CostLevel.Should().Be(15m);
            monthlyData.CostsForMonth.M1CostLevel.Should().Be(8m);
            monthlyData.CostsForMonth.M2CostLevel.Should().Be(3m);
        }
    }

    [Fact]
    public async Task GetMarginAsync_WithInconsistentProductCodeInRepositories_ReturnsZeroCosts()
    {
        // Arrange
        var product = CreateTestProduct("PROD001", 100m);
        var dateFrom = DateOnly.FromDateTime(DateTime.Now.AddMonths(-3));
        var dateTo = DateOnly.FromDateTime(DateTime.Now);

        // Repository obsahují data pro jiný produkt
        var materialCostsForDifferentProduct = CreateMonthlyCostsForDateRange(dateFrom, dateTo);
        var emptyCosts = new List<MonthlyCost>();

        // Nastavíme pouze materiálové náklady pro "PROD002", ostatní prázdné
        var materialCostDict = new Dictionary<string, List<MonthlyCost>> { { "PROD002", materialCostsForDifferentProduct } };
        var emptyCostDict = new Dictionary<string, List<MonthlyCost>>();

        _materialCostRepositoryMock
            .Setup(x => x.GetCostsAsync(It.IsAny<List<string>>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(materialCostDict);

        _manufactureCostRepositoryMock
            .Setup(x => x.GetCostsAsync(It.IsAny<List<string>>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyCostDict);

        _salesCostCalculationServiceMock
            .Setup(x => x.GetCostsAsync(It.IsAny<List<string>>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyCostDict);

        _overheadCostCalculationServiceMock
            .Setup(x => x.GetCostsAsync(It.IsAny<List<string>>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyCostDict);

        // Act
        var result = await _service.GetMarginAsync(product, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().NotBeEmpty();

        // Protože žádná repository neobsahuje data pro "PROD001", všechny náklady by měly být nulové
        foreach (var monthlyData in result.MonthlyData)
        {
            monthlyData.CostsForMonth.M0CostLevel.Should().Be(0);
            monthlyData.CostsForMonth.M1CostLevel.Should().Be(0);
            monthlyData.CostsForMonth.M2CostLevel.Should().Be(0);
            monthlyData.CostsForMonth.M3CostLevel.Should().Be(0);
        }
    }



    [Fact]
    public async Task GetMarginAsync_WithRepositoryException_LogsErrorAndReturnsEmptyHistory()
    {
        // Arrange
        var product = CreateTestProduct("PROD001", 100m);
        var dateFrom = DateOnly.FromDateTime(DateTime.Now.AddMonths(-12));
        var dateTo = DateOnly.FromDateTime(DateTime.Now);
        var exception = new Exception("Repository error");

        _materialCostRepositoryMock
            .Setup(x => x.GetCostsAsync(It.IsAny<List<string>>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var result = await _service.GetMarginAsync(product, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().BeEmpty();

        // Verify error logging
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error calculating monthly margin history")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }



    [Theory]
    [InlineData(1, 1)]
    [InlineData(6, 6)]
    [InlineData(12, 12)]
    [InlineData(24, 24)]
    public async Task GetMarginAsync_WithDifferentDateRanges_ReturnsCorrectCount(int monthsBack, int expectedCount)
    {
        // Arrange
        var product = CreateTestProduct("PROD001", 100m);
        var dateTo = DateOnly.FromDateTime(DateTime.Now);
        var dateFrom = new DateOnly(dateTo.Year, dateTo.Month, 1).AddMonths(-monthsBack + 1); // Start from first day of month
        var materialCosts = CreateMonthlyCostsForDateRange(dateFrom, dateTo);
        var manufactureCosts = CreateMonthlyCostsForDateRange(dateFrom, dateTo);
        var salesCosts = CreateMonthlyCostsForDateRange(dateFrom, dateTo);

        SetupRepositoryMocks("PROD001", materialCosts, manufactureCosts, salesCosts);

        // Act
        var result = await _service.GetMarginAsync(product, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().HaveCount(expectedCount);
    }



    private CatalogAggregate CreateTestProduct(string productCode, decimal? sellingPrice)
    {
        var catalog = new CatalogAggregate
        {
            ProductCode = productCode
        };

        if (sellingPrice.HasValue)
        {
            catalog.ErpPrice = new ProductPriceErp
            {
                ProductCode = productCode,
                PriceWithoutVat = sellingPrice.Value,
                PriceWithVat = sellingPrice.Value * 1.21m, // 21% VAT
                PurchasePrice = sellingPrice.Value * 0.6m, // 60% of selling price
                PurchasePriceWithVat = sellingPrice.Value * 0.6m * 1.21m
            };
        }

        return catalog;
    }



    private List<MonthlyCost> CreateMonthlyCostsForDateRange(DateOnly dateFrom, DateOnly dateTo)
    {
        var result = new List<MonthlyCost>();
        var currentMonth = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var endMonth = new DateTime(dateTo.Year, dateTo.Month, 1);
        var monthIndex = 0;

        while (currentMonth <= endMonth)
        {
            var cost = 10m + (monthIndex % 5); // Varying costs: 10, 11, 12, 13, 14, 10, 11...
            result.Add(new MonthlyCost(currentMonth, cost));
            currentMonth = currentMonth.AddMonths(1);
            monthIndex++;
        }

        return result;
    }

    private void SetupRepositoryMocks(string productCode, List<MonthlyCost> materialCosts,
        List<MonthlyCost> manufactureCosts, List<MonthlyCost> salesCosts)
    {
        var materialCostDict = new Dictionary<string, List<MonthlyCost>> { { productCode, materialCosts } };
        var manufactureCostDict = new Dictionary<string, List<MonthlyCost>> { { productCode, manufactureCosts } };
        var salesCostDict = new Dictionary<string, List<MonthlyCost>> { { productCode, salesCosts } };

        // Create overhead costs that cover a wide date range to ensure they're found
        var overheadCosts = new List<MonthlyCost>
        {
            new MonthlyCost(DateTime.Now.AddMonths(-6), 25m),
            new MonthlyCost(DateTime.Now.AddMonths(-5), 25m),
            new MonthlyCost(DateTime.Now.AddMonths(-4), 25m),
            new MonthlyCost(DateTime.Now.AddMonths(-3), 25m),
            new MonthlyCost(DateTime.Now.AddMonths(-2), 25m),
            new MonthlyCost(DateTime.Now.AddMonths(-1), 25m),
            new MonthlyCost(DateTime.Now, 25m)
        };
        var overheadCostDict = new Dictionary<string, List<MonthlyCost>> { { productCode, overheadCosts } };

        _materialCostRepositoryMock
            .Setup(x => x.GetCostsAsync(It.IsAny<List<string>>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(materialCostDict);

        _manufactureCostRepositoryMock
            .Setup(x => x.GetCostsAsync(It.IsAny<List<string>>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(manufactureCostDict);

        _salesCostCalculationServiceMock
            .Setup(x => x.GetCostsAsync(It.IsAny<List<string>>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(salesCostDict);

        _overheadCostCalculationServiceMock
            .Setup(x => x.GetCostsAsync(It.IsAny<List<string>>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(overheadCostDict);
    }

    private void SetupRepositoryMocksWithEmptyOverhead(string productCode, List<MonthlyCost> materialCosts,
        List<MonthlyCost> manufactureCosts, List<MonthlyCost> salesCosts, List<MonthlyCost> overheadCosts)
    {
        var materialCostDict = new Dictionary<string, List<MonthlyCost>> { { productCode, materialCosts } };
        var manufactureCostDict = new Dictionary<string, List<MonthlyCost>> { { productCode, manufactureCosts } };
        var salesCostDict = new Dictionary<string, List<MonthlyCost>> { { productCode, salesCosts } };
        var overheadCostDict = new Dictionary<string, List<MonthlyCost>> { { productCode, overheadCosts } };

        _materialCostRepositoryMock
            .Setup(x => x.GetCostsAsync(It.IsAny<List<string>>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(materialCostDict);

        _manufactureCostRepositoryMock
            .Setup(x => x.GetCostsAsync(It.IsAny<List<string>>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(manufactureCostDict);

        _salesCostCalculationServiceMock
            .Setup(x => x.GetCostsAsync(It.IsAny<List<string>>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(salesCostDict);

        _overheadCostCalculationServiceMock
            .Setup(x => x.GetCostsAsync(It.IsAny<List<string>>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(overheadCostDict);
    }
}