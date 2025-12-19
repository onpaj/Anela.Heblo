using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Reflection;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Repositories;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Anela.Heblo.Domain.Features.Manufacture;
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
        var result = await _service.GetMarginAsync(product, new[] { product }, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().NotBeEmpty();
        result.MonthlyData.Should().BeInAscendingOrder(x => x.Month);

        // Verify each monthly data has proper margin calculations
        foreach (var monthlyData in result.MonthlyData)
        {
            monthlyData.M0.Should().NotBeNull();
            monthlyData.M1_A.Should().NotBeNull();
            monthlyData.M2.Should().NotBeNull();
            monthlyData.M3.Should().NotBeNull();
            monthlyData.CostsForMonth.Should().NotBeNull();
        }

        result.Averages.Should().NotBeNull();
        result.Averages.M0.Should().NotBeNull();
        result.Averages.M1_A.Should().NotBeNull();
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
        var result = await _service.GetMarginAsync(product, new[] { product }, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().BeEmpty();
        result.Averages.Should().NotBeNull();
        result.Averages.M0.Should().BeEquivalentTo(MarginLevel.Zero);
        result.Averages.M1_A.Should().BeEquivalentTo(MarginLevel.Zero);
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
        var result = await _service.GetMarginAsync(product, new[] { product }, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().BeEmpty();
        result.Averages.Should().NotBeNull();
        result.Averages.M0.Should().BeEquivalentTo(MarginLevel.Zero);
        result.Averages.M1_A.Should().BeEquivalentTo(MarginLevel.Zero);
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
        var result = await _service.GetMarginAsync(product, new[] { product }, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().BeEmpty();
        result.Averages.Should().NotBeNull();
        result.Averages.M0.Should().BeEquivalentTo(MarginLevel.Zero);
        result.Averages.M1_A.Should().BeEquivalentTo(MarginLevel.Zero);
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
        var result = await _service.GetMarginAsync(product, new[] { product }, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().BeEmpty();
        result.Averages.Should().NotBeNull();
        result.Averages.M0.Should().BeEquivalentTo(MarginLevel.Zero);
        result.Averages.M1_A.Should().BeEquivalentTo(MarginLevel.Zero);
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
        var result = await _service.GetMarginAsync(product, new[] { product }, dateFrom, dateTo, CancellationToken.None);

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
        var result = await _service.GetMarginAsync(product, new[] { product }, dateFrom, dateTo, CancellationToken.None);

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
        var result = await _service.GetMarginAsync(product, new[] { product }, sameDate, sameDate, CancellationToken.None);

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
        var result = await _service.GetMarginAsync(product, new[] { product }, dateFrom, dateTo, CancellationToken.None);

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
        var result = await _service.GetMarginAsync(product, new[] { product }, dateFrom, dateTo, CancellationToken.None);

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
        var result = await _service.GetMarginAsync(product, new[] { product }, dateFrom, dateTo, CancellationToken.None);

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
        var result = await _service.GetMarginAsync(product, new[] { product }, dateFrom, dateTo, CancellationToken.None);

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
        var dateFrom = DateOnly.FromDateTime(DateTime.Now.AddMonths(-3));
        var dateTo = DateOnly.FromDateTime(DateTime.Now);

        // Create product with production history to enable M1 calculation
        var productionRecords = new List<(DateTime, double)>();
        var currentMonth = dateFrom.ToDateTime(TimeOnly.MinValue);
        var endMonth = dateTo.ToDateTime(TimeOnly.MinValue);
        while (currentMonth <= endMonth)
        {
            productionRecords.Add((currentMonth, 50.0)); // 50 units per month
            currentMonth = currentMonth.AddMonths(1);
        }

        var product = CreateTestProductWithProduction("PROD001", 5.0, productionRecords);
        product.ErpPrice = new ProductPriceErp
        {
            ProductCode = "PROD001",
            PriceWithoutVat = 100m,
            PriceWithVat = 121m,
            PurchasePrice = 60m,
            PurchasePriceWithVat = 72.6m
        };

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
        var result = await _service.GetMarginAsync(product, new[] { product }, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().NotBeEmpty();

        // M0 and M2 use nearest available costs, but M1 is calculated dynamically based on production
        foreach (var monthlyData in result.MonthlyData)
        {
            monthlyData.CostsForMonth.M0CostLevel.Should().Be(15m);
            // M1CostLevel is now calculated based on company-wide CP, not from nearest cost
            monthlyData.CostsForMonth.M1CostLevel.Should().BeGreaterThanOrEqualTo(0);
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
        var result = await _service.GetMarginAsync(product, new[] { product }, dateFrom, dateTo, CancellationToken.None);

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
        var result = await _service.GetMarginAsync(product, new[] { product }, dateFrom, dateTo, CancellationToken.None);

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
        var result = await _service.GetMarginAsync(product, new[] { product }, dateFrom, dateTo, CancellationToken.None);

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

    private CatalogAggregate CreateTestProductWithProduction(
        string productCode,
        double complexityPoints,
        IEnumerable<(DateTime Date, double Amount)> productionRecords)
    {
        var product = new CatalogAggregate
        {
            ProductCode = productCode
        };

        // Set manufacture difficulty using Assign method
        var difficultySetting = new ManufactureDifficultySetting
        {
            DifficultyValue = (int)complexityPoints,
            ValidFrom = DateTime.MinValue,
            ValidTo = null
        };
        product.ManufactureDifficultySettings.Assign(new List<ManufactureDifficultySetting> { difficultySetting }, DateTime.UtcNow);

        var manufactureHistory = productionRecords
            .Select(pr => new ManufactureHistoryRecord
            {
                Date = pr.Date,
                Amount = pr.Amount,
                ProductCode = productCode,
                PricePerPiece = 0,
                PriceTotal = 0,
                DocumentNumber = "TEST"
            })
            .ToList();

        product.ManufactureHistory = manufactureHistory;

        return product;
    }

    // Task 13: Unit Test - Company-Wide CP Calculation
    [Fact]
    public async Task CalculateCompanyWideProducedCP_WithMultipleProducts_AggregatesCorrectly()
    {
        // Arrange
        var product1 = CreateTestProductWithProduction("PROD1", 5.0, new[]
        {
            (new DateTime(2025, 1, 15), 100.0)
        });

        var product2 = CreateTestProductWithProduction("PROD2", 8.0, new[]
        {
            (new DateTime(2025, 1, 20), 50.0)
        });

        var allProducts = new[] { product1, product2 };
        var dateFrom = new DateOnly(2025, 1, 1);
        var dateTo = new DateOnly(2025, 1, 31);

        // Use reflection to access private method
        var method = typeof(MarginCalculationService)
            .GetMethod("CalculateCompanyWideProducedCPAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);

        // Act
        var result = await (Task<Dictionary<DateTime, decimal>>)method.Invoke(
            _service, new object[] { allProducts, dateFrom, dateTo, CancellationToken.None });

        // Assert
        var jan2025 = new DateTime(2025, 1, 1);
        result.Should().ContainKey(jan2025);

        // Expected: (100 * 5.0) + (50 * 8.0) = 500 + 400 = 900
        result[jan2025].Should().Be(900m);
    }

    [Fact]
    public async Task CalculateCompanyWideProducedCP_WithNoProduction_ReturnsEmptyDictionary()
    {
        // Arrange
        var product1 = CreateTestProductWithProduction("PROD1", 5.0, Array.Empty<(DateTime, double)>());
        var allProducts = new[] { product1 };
        var dateFrom = new DateOnly(2025, 1, 1);
        var dateTo = new DateOnly(2025, 1, 31);

        var method = typeof(MarginCalculationService)
            .GetMethod("CalculateCompanyWideProducedCPAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);

        // Act
        var result = await (Task<Dictionary<DateTime, decimal>>)method.Invoke(
            _service, new object[] { allProducts, dateFrom, dateTo, CancellationToken.None });

        // Assert
        result.Should().BeEmpty();
    }

    // Task 14: Unit Test - M1_A Calculation
    [Fact]
    public void CalculateM1_A_WithFullYearData_ReturnsCorrectBaseline()
    {
        // Arrange
        var productCP = 5.0;

        // Company-wide produced CP for 12 months
        var companyWideProducedCP = new Dictionary<DateTime, decimal>();
        for (int i = 1; i <= 12; i++)
        {
            companyWideProducedCP[new DateTime(2024, i, 1)] = 1000m; // 1000 CP per month
        }

        // M1 costs for 12 months
        var m1Costs = new List<MonthlyCost>();
        for (int i = 1; i <= 12; i++)
        {
            m1Costs.Add(new MonthlyCost(new DateTime(2024, i, 1), 10000m)); // 10,000 CZK per month
        }

        var dateFrom = new DateOnly(2024, 12, 1);
        var dateTo = new DateOnly(2024, 12, 31);

        // Use reflection to access private method
        var method = typeof(MarginCalculationService)
            .GetMethod("CalculateM1_A_PerMonth",
                BindingFlags.NonPublic | BindingFlags.Instance);

        // Act
        var result = (Dictionary<DateTime, decimal>)method.Invoke(
            _service, new object[] { productCP, companyWideProducedCP, m1Costs, dateFrom, dateTo });

        // Assert
        var dec2024 = new DateTime(2024, 12, 1);
        result.Should().ContainKey(dec2024);

        // Expected: cost_per_CP = (10,000 * 12) / (1,000 * 12) = 120,000 / 12,000 = 10
        // M1_A = 5.0 * 10 = 50
        result[dec2024].Should().Be(50m);
    }

    [Fact]
    public void CalculateM1_A_WithPartialYearData_UsesAvailableData()
    {
        // Arrange
        var productCP = 10.0;

        // Only 6 months of production data
        var companyWideProducedCP = new Dictionary<DateTime, decimal>();
        for (int i = 7; i <= 12; i++)
        {
            companyWideProducedCP[new DateTime(2024, i, 1)] = 500m; // 500 CP per month
        }

        // 6 months of M1 costs
        var m1Costs = new List<MonthlyCost>();
        for (int i = 7; i <= 12; i++)
        {
            m1Costs.Add(new MonthlyCost(new DateTime(2024, i, 1), 5000m)); // 5,000 CZK per month
        }

        var dateFrom = new DateOnly(2024, 12, 1);
        var dateTo = new DateOnly(2024, 12, 31);

        var method = typeof(MarginCalculationService)
            .GetMethod("CalculateM1_A_PerMonth",
                BindingFlags.NonPublic | BindingFlags.Instance);

        // Act
        var result = (Dictionary<DateTime, decimal>)method.Invoke(
            _service, new object[] { productCP, companyWideProducedCP, m1Costs, dateFrom, dateTo });

        // Assert
        var dec2024 = new DateTime(2024, 12, 1);
        result.Should().ContainKey(dec2024);

        // Expected: cost_per_CP = (5,000 * 6) / (500 * 6) = 30,000 / 3,000 = 10
        // M1_A = 10.0 * 10 = 100
        result[dec2024].Should().Be(100m);
    }

    // Task 15: Unit Test - M1_B Calculation
    [Fact]
    public void CalculateM1_B_WhenProductProduced_ReturnsActualCost()
    {
        // Arrange
        var product = CreateTestProductWithProduction("PROD1", 5.0, new[]
        {
            (new DateTime(2025, 1, 15), 100.0)
        });

        var productCP = 5.0;

        var companyWideProducedCP = new Dictionary<DateTime, decimal>
        {
            { new DateTime(2025, 1, 1), 800m } // Total company CP for January
        };

        var m1Costs = new List<MonthlyCost>
        {
            new MonthlyCost(new DateTime(2025, 1, 1), 8000m)
        };

        // Use reflection to access private method
        var method = typeof(MarginCalculationService)
            .GetMethod("CalculateM1_B_PerMonth",
                BindingFlags.NonPublic | BindingFlags.Instance);

        // Act
        var result = (Dictionary<DateTime, decimal?>)method.Invoke(
            _service, new object[] { product, productCP, companyWideProducedCP, m1Costs });

        // Assert
        var jan2025 = new DateTime(2025, 1, 1);
        result.Should().ContainKey(jan2025);
        result[jan2025].Should().NotBeNull();

        // Expected: M1_B_per_CP = 8000 / 800 = 10
        // M1_B = 5.0 * 10 = 50
        result[jan2025].Value.Should().Be(50m);
    }

    [Fact]
    public void CalculateM1_B_WhenProductNotProduced_ReturnsNull()
    {
        // Arrange
        var product = CreateTestProductWithProduction("PROD1", 5.0, Array.Empty<(DateTime, double)>());

        var productCP = 5.0;

        var companyWideProducedCP = new Dictionary<DateTime, decimal>
        {
            { new DateTime(2025, 1, 1), 800m }
        };

        var m1Costs = new List<MonthlyCost>
        {
            new MonthlyCost(new DateTime(2025, 1, 1), 8000m)
        };

        // Use reflection
        var method = typeof(MarginCalculationService)
            .GetMethod("CalculateM1_B_PerMonth",
                BindingFlags.NonPublic | BindingFlags.Instance);

        // Act
        var result = (Dictionary<DateTime, decimal?>)method.Invoke(
            _service, new object[] { product, productCP, companyWideProducedCP, m1Costs });

        // Assert
        var jan2025 = new DateTime(2025, 1, 1);
        result.Should().ContainKey(jan2025);
        result[jan2025].Should().BeNull();
    }

    [Fact]
    public void CalculateM1_B_WithMultipleProductionMonths_CalculatesEachMonth()
    {
        // Arrange
        var product = CreateTestProductWithProduction("PROD1", 5.0, new[]
        {
            (new DateTime(2025, 1, 15), 100.0),
            (new DateTime(2025, 2, 20), 150.0)
        });

        var productCP = 5.0;

        var companyWideProducedCP = new Dictionary<DateTime, decimal>
        {
            { new DateTime(2025, 1, 1), 800m },
            { new DateTime(2025, 2, 1), 1000m }
        };

        var m1Costs = new List<MonthlyCost>
        {
            new MonthlyCost(new DateTime(2025, 1, 1), 8000m),
            new MonthlyCost(new DateTime(2025, 2, 1), 12000m)
        };

        var method = typeof(MarginCalculationService)
            .GetMethod("CalculateM1_B_PerMonth",
                BindingFlags.NonPublic | BindingFlags.Instance);

        // Act
        var result = (Dictionary<DateTime, decimal?>)method.Invoke(
            _service, new object[] { product, productCP, companyWideProducedCP, m1Costs });

        // Assert
        var jan2025 = new DateTime(2025, 1, 1);
        var feb2025 = new DateTime(2025, 2, 1);

        result.Should().ContainKey(jan2025);
        result[jan2025].Should().NotBeNull();
        result[jan2025].Value.Should().Be(50m); // (5.0 * 8000 / 800) = 50

        result.Should().ContainKey(feb2025);
        result[feb2025].Should().NotBeNull();
        result[feb2025].Value.Should().Be(60m); // (5.0 * 12000 / 1000) = 60
    }

    // Task 16: Integration Test - Full Margin Calculation
    [Fact]
    public async Task GetMarginAsync_WithM1Calculations_IncludesM1_A_AndM1_B()
    {
        // Arrange
        var product = CreateTestProductWithProduction("PROD001", 5.0, new[]
        {
            (new DateTime(2025, 1, 15), 100.0)
        });
        product.ErpPrice = new ProductPriceErp
        {
            ProductCode = "PROD001",
            PriceWithoutVat = 100m,
            PriceWithVat = 121m,
            PurchasePrice = 60m,
            PurchasePriceWithVat = 72.6m
        };

        var allProducts = new[] { product };

        var dateFrom = new DateOnly(2025, 1, 1);
        var dateTo = new DateOnly(2025, 1, 31);

        var manufactureCosts = new List<MonthlyCost>
        {
            new MonthlyCost(new DateTime(2025, 1, 1), 8000m)
        };

        SetupRepositoryMocks("PROD001", new List<MonthlyCost>(), manufactureCosts, new List<MonthlyCost>());

        // Act
        var result = await _service.GetMarginAsync(product, allProducts, dateFrom, dateTo, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MonthlyData.Should().NotBeEmpty();

        foreach (var monthData in result.MonthlyData)
        {
            monthData.M1_A.Should().NotBeNull(); // M1_A always present
            // M1_B may be null if product not produced that month
        }

        // Verify averages
        result.Averages.M1_A.Should().NotBeNull();
        result.Averages.M1_B.Should().NotBeNull();
    }

    // Task 17: Specification Compliance Test
    [Fact]
    public async Task LongTermAverage_M1_B_ApproximatesM1_A()
    {
        // Arrange - 12 months of consistent production
        var productionRecords = new List<(DateTime, double)>();
        for (int i = 1; i <= 12; i++)
        {
            productionRecords.Add((new DateTime(2024, i, 15), 100.0));
        }

        var product = CreateTestProductWithProduction("PROD001", 5.0, productionRecords);
        product.ErpPrice = new ProductPriceErp
        {
            ProductCode = "PROD001",
            PriceWithoutVat = 100m,
            PriceWithVat = 121m,
            PurchasePrice = 60m,
            PurchasePriceWithVat = 72.6m
        };

        var allProducts = new[] { product };

        var dateFrom = new DateOnly(2024, 1, 1);
        var dateTo = new DateOnly(2024, 12, 31);

        // Setup consistent M1 costs
        var manufactureCosts = new List<MonthlyCost>();
        for (int i = 1; i <= 12; i++)
        {
            manufactureCosts.Add(new MonthlyCost(new DateTime(2024, i, 1), 8000m));
        }

        SetupRepositoryMocks("PROD001", new List<MonthlyCost>(), manufactureCosts, new List<MonthlyCost>());

        // Act
        var result = await _service.GetMarginAsync(product, allProducts, dateFrom, dateTo, CancellationToken.None);

        // Assert
        var avgM1_A = result.Averages.M1_A.CostLevel;
        var avgM1_B = result.Averages.M1_B.CostLevel;

        // Spec: "long-term average M1_B ≈ M1_A"
        // Allow 5% tolerance
        avgM1_A.Should().BeGreaterThan(0);
        avgM1_B.Should().BeGreaterThan(0);

        var percentDifference = Math.Abs(avgM1_A - avgM1_B) / avgM1_A * 100;
        percentDifference.Should().BeLessThan(5,
            $"M1_B average ({avgM1_B}) should approximate M1_A average ({avgM1_A}) within 5%, but difference is {percentDifference}%");
    }
}