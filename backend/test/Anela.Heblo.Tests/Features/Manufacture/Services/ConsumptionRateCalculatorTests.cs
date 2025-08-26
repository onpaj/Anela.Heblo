using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Services;

public class ConsumptionRateCalculatorTests
{
    private readonly ConsumptionRateCalculator _calculator;
    private readonly Mock<ILogger<ConsumptionRateCalculator>> _loggerMock;

    public ConsumptionRateCalculatorTests()
    {
        _loggerMock = new Mock<ILogger<ConsumptionRateCalculator>>();
        _calculator = new ConsumptionRateCalculator(_loggerMock.Object);
    }

    [Fact]
    public void CalculateDailySalesRate_WithSalesData_ReturnsCorrectRate()
    {
        // Arrange
        var fromDate = new DateTime(2025, 1, 1);
        var toDate = new DateTime(2025, 1, 11); // 10 days
        var salesHistory = new List<CatalogSaleRecord>
        {
            new() { Date = new DateTime(2025, 1, 2), AmountB2B = 5, AmountB2C = 3 }, // Total: 8
            new() { Date = new DateTime(2025, 1, 5), AmountB2B = 2, AmountB2C = 4 }, // Total: 6
            new() { Date = new DateTime(2025, 1, 8), AmountB2B = 1, AmountB2C = 5 }  // Total: 6
        };
        // Total sales: 20, Days: 10, Expected rate: 2.0

        // Act
        var result = _calculator.CalculateDailySalesRate(salesHistory, fromDate, toDate);

        // Assert
        result.Should().Be(2.0);
    }

    [Fact]
    public void CalculateDailySalesRate_WithNoSalesData_ReturnsZero()
    {
        // Arrange
        var fromDate = new DateTime(2025, 1, 1);
        var toDate = new DateTime(2025, 1, 11);
        var salesHistory = new List<CatalogSaleRecord>();

        // Act
        var result = _calculator.CalculateDailySalesRate(salesHistory, fromDate, toDate);

        // Assert
        result.Should().Be(0.0);
    }

    [Fact]
    public void CalculateDailySalesRate_WithSalesOutsideRange_ReturnsZero()
    {
        // Arrange
        var fromDate = new DateTime(2025, 1, 1);
        var toDate = new DateTime(2025, 1, 11);
        var salesHistory = new List<CatalogSaleRecord>
        {
            new() { Date = new DateTime(2024, 12, 31), AmountB2B = 5, AmountB2C = 3 }, // Before range
            new() { Date = new DateTime(2025, 1, 12), AmountB2B = 2, AmountB2C = 4 }  // After range
        };

        // Act
        var result = _calculator.CalculateDailySalesRate(salesHistory, fromDate, toDate);

        // Assert
        result.Should().Be(0.0);
    }

    [Fact]
    public void CalculateDailyConsumptionRate_WithConsumptionData_ReturnsCorrectRate()
    {
        // Arrange
        var fromDate = new DateTime(2025, 1, 1);
        var toDate = new DateTime(2025, 1, 11); // 10 days
        var consumedHistory = new List<ConsumedMaterialRecord>
        {
            new() { Date = new DateTime(2025, 1, 2), Amount = 10 },
            new() { Date = new DateTime(2025, 1, 5), Amount = 15 },
            new() { Date = new DateTime(2025, 1, 8), Amount = 25 }
        };
        // Total consumption: 50, Days: 10, Expected rate: 5.0

        // Act
        var result = _calculator.CalculateDailyConsumptionRate(consumedHistory, fromDate, toDate);

        // Assert
        result.Should().Be(5.0);
    }

    [Fact]
    public void CalculateStockDaysAvailable_WithPositiveConsumption_ReturnsCorrectDays()
    {
        // Arrange
        decimal availableStock = 100m;
        double dailyConsumptionRate = 5.0;
        // Expected: 100 / 5 = 20 days

        // Act
        var result = _calculator.CalculateStockDaysAvailable(availableStock, dailyConsumptionRate);

        // Assert
        result.Should().Be(20.0);
    }

    [Fact]
    public void CalculateStockDaysAvailable_WithZeroConsumption_ReturnsInfinite()
    {
        // Arrange
        decimal availableStock = 100m;
        double dailyConsumptionRate = 0.0;

        // Act
        var result = _calculator.CalculateStockDaysAvailable(availableStock, dailyConsumptionRate);

        // Assert
        result.Should().Be(999999.0); // Defined as infinite stock days
    }

    [Fact]
    public void CalculateStockDaysAvailable_WithVeryLowConsumption_ReturnsInfinite()
    {
        // Arrange
        decimal availableStock = 100m;
        double dailyConsumptionRate = 0.0000001; // Below minimum threshold

        // Act
        var result = _calculator.CalculateStockDaysAvailable(availableStock, dailyConsumptionRate);

        // Assert
        result.Should().Be(999999.0);
    }

    [Fact]
    public void CalculateStockDaysAvailable_WithZeroStock_ReturnsZero()
    {
        // Arrange
        decimal availableStock = 0m;
        double dailyConsumptionRate = 5.0;

        // Act
        var result = _calculator.CalculateStockDaysAvailable(availableStock, dailyConsumptionRate);

        // Assert
        result.Should().Be(0.0);
    }

    [Theory]
    [InlineData(1)] // Single day period should work
    [InlineData(0)] // Zero day period should default to 1
    [InlineData(-1)] // Negative period should default to 1
    public void CalculateDailySalesRate_WithEdgeCasePeriods_HandlesCorrectly(int daysDiff)
    {
        // Arrange
        var fromDate = new DateTime(2025, 1, 1);
        var toDate = fromDate.AddDays(daysDiff);
        var salesHistory = new List<CatalogSaleRecord>
        {
            new() { Date = new DateTime(2025, 1, 1), AmountB2B = 10, AmountB2C = 0 }
        };
        // Expected: 10 sales / 1 day = 10.0 (regardless of daysDiff <= 0)

        // Act
        var result = _calculator.CalculateDailySalesRate(salesHistory, fromDate, toDate);

        // Assert
        result.Should().Be(10.0);
    }
}