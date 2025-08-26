using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Services;

public class ProductionActivityAnalyzerTests
{
    private readonly ProductionActivityAnalyzer _analyzer;
    private readonly Mock<ILogger<ProductionActivityAnalyzer>> _loggerMock;

    public ProductionActivityAnalyzerTests()
    {
        _loggerMock = new Mock<ILogger<ProductionActivityAnalyzer>>();
        _analyzer = new ProductionActivityAnalyzer(_loggerMock.Object);
    }

    [Fact]
    public void IsInActiveProduction_WithRecentProduction_ReturnsTrue()
    {
        // Arrange
        var manufactureHistory = new List<ManufactureHistoryRecord>
        {
            new() { Date = DateTime.UtcNow.AddDays(-15), Amount = 10 }, // Within 30 days
            new() { Date = DateTime.UtcNow.AddDays(-45), Amount = 5 }   // Outside 30 days
        };

        // Act
        var result = _analyzer.IsInActiveProduction(manufactureHistory, dayThreshold: 30);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsInActiveProduction_WithOldProduction_ReturnsFalse()
    {
        // Arrange
        var manufactureHistory = new List<ManufactureHistoryRecord>
        {
            new() { Date = DateTime.UtcNow.AddDays(-45), Amount = 10 }, // Outside 30 days
            new() { Date = DateTime.UtcNow.AddDays(-60), Amount = 5 }   // Outside 30 days
        };

        // Act
        var result = _analyzer.IsInActiveProduction(manufactureHistory, dayThreshold: 30);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsInActiveProduction_WithZeroAmount_ReturnsFalse()
    {
        // Arrange
        var manufactureHistory = new List<ManufactureHistoryRecord>
        {
            new() { Date = DateTime.UtcNow.AddDays(-15), Amount = 0 } // Recent but zero quantity
        };

        // Act
        var result = _analyzer.IsInActiveProduction(manufactureHistory, dayThreshold: 30);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsInActiveProduction_WithNoHistory_ReturnsFalse()
    {
        // Arrange
        var manufactureHistory = new List<ManufactureHistoryRecord>();

        // Act
        var result = _analyzer.IsInActiveProduction(manufactureHistory, dayThreshold: 30);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsInActiveProduction_WithCustomThreshold_RespectsThreshold()
    {
        // Arrange
        var manufactureHistory = new List<ManufactureHistoryRecord>
        {
            new() { Date = DateTime.UtcNow.AddDays(-8), Amount = 10 } // Within 10 days but outside 5 days
        };

        // Act & Assert
        Assert.True(_analyzer.IsInActiveProduction(manufactureHistory, dayThreshold: 10));
        Assert.False(_analyzer.IsInActiveProduction(manufactureHistory, dayThreshold: 5));
    }

    [Fact]
    public void GetLastProductionDate_WithHistory_ReturnsLatestDate()
    {
        // Arrange
        var expectedDate = DateTime.UtcNow.AddDays(-10);
        var manufactureHistory = new List<ManufactureHistoryRecord>
        {
            new() { Date = DateTime.UtcNow.AddDays(-30), Amount = 5 },
            new() { Date = expectedDate, Amount = 10 }, // Latest
            new() { Date = DateTime.UtcNow.AddDays(-20), Amount = 3 }
        };

        // Act
        var result = _analyzer.GetLastProductionDate(manufactureHistory);

        // Assert
        Assert.Equal(expectedDate, result);
    }

    [Fact]
    public void GetLastProductionDate_WithNoHistory_ReturnsNull()
    {
        // Arrange
        var manufactureHistory = new List<ManufactureHistoryRecord>();

        // Act
        var result = _analyzer.GetLastProductionDate(manufactureHistory);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetLastProductionDate_WithZeroAmountOnly_ReturnsNull()
    {
        // Arrange
        var manufactureHistory = new List<ManufactureHistoryRecord>
        {
            new() { Date = DateTime.UtcNow.AddDays(-10), Amount = 0 },
            new() { Date = DateTime.UtcNow.AddDays(-5), Amount = 0 }
        };

        // Act
        var result = _analyzer.GetLastProductionDate(manufactureHistory);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CalculateAverageProductionFrequency_WithRegularProduction_ReturnsCorrectAverage()
    {
        // Arrange
        var baseDate = new DateTime(2025, 1, 1);
        var manufactureHistory = new List<ManufactureHistoryRecord>
        {
            new() { Date = baseDate, Amount = 10 },
            new() { Date = baseDate.AddDays(10), Amount = 5 },    // 10 days gap
            new() { Date = baseDate.AddDays(25), Amount = 8 },    // 15 days gap
            new() { Date = baseDate.AddDays(35), Amount = 3 }     // 10 days gap
        };
        // Average: (10 + 15 + 10) / 3 = 11.67 days

        // Act
        var result = _analyzer.CalculateAverageProductionFrequency(manufactureHistory, analysisMonths: 12);

        // Assert
        result.Should().BeApproximately(11.666666666666666, 0.0000000001);
    }

    [Fact]
    public void CalculateAverageProductionFrequency_WithInsufficientData_ReturnsInfinity()
    {
        // Arrange
        var manufactureHistory = new List<ManufactureHistoryRecord>
        {
            new() { Date = DateTime.UtcNow.AddDays(-10), Amount = 10 }
        };

        // Act
        var result = _analyzer.CalculateAverageProductionFrequency(manufactureHistory, analysisMonths: 12);

        // Assert
        Assert.Equal(double.PositiveInfinity, result);
    }

    [Fact]
    public void CalculateAverageProductionFrequency_WithNoData_ReturnsInfinity()
    {
        // Arrange
        var manufactureHistory = new List<ManufactureHistoryRecord>();

        // Act
        var result = _analyzer.CalculateAverageProductionFrequency(manufactureHistory, analysisMonths: 12);

        // Assert
        Assert.Equal(double.PositiveInfinity, result);
    }

    [Fact]
    public void CalculateAverageProductionFrequency_FiltersOldData_ReturnsCorrectResult()
    {
        // Arrange
        var recentDate = DateTime.UtcNow.AddMonths(-1);
        var oldDate = DateTime.UtcNow.AddMonths(-24); // Outside 12-month analysis window
        var manufactureHistory = new List<ManufactureHistoryRecord>
        {
            new() { Date = oldDate, Amount = 10 },                    // Should be ignored
            new() { Date = recentDate, Amount = 5 },
            new() { Date = recentDate.AddDays(15), Amount = 8 }       // 15 days gap
        };

        // Act
        var result = _analyzer.CalculateAverageProductionFrequency(manufactureHistory, analysisMonths: 12);

        // Assert
        Assert.Equal(15.0, result); // Only one interval: 15 days
    }
}