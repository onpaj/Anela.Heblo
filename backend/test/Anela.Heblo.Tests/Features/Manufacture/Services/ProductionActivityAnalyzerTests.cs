using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Services;

public class ProductionActivityAnalyzerTests
{
    private static readonly DateTime FrozenNowUtc = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly ProductionActivityAnalyzer _analyzer;
    private readonly Mock<ILogger<ProductionActivityAnalyzer>> _loggerMock;
    private readonly FakeTimeProvider _timeProvider;

    public ProductionActivityAnalyzerTests()
    {
        _loggerMock = new Mock<ILogger<ProductionActivityAnalyzer>>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(FrozenNowUtc));
        _analyzer = new ProductionActivityAnalyzer(_loggerMock.Object, _timeProvider);
    }

    [Fact]
    public void IsInActiveProduction_WithRecentProduction_ReturnsTrue()
    {
        // Arrange
        var manufactureHistory = new List<ManufactureHistoryRecord>
        {
            new() { Date = FrozenNowUtc.AddDays(-15), Amount = 10 }, // Within 30 days
            new() { Date = FrozenNowUtc.AddDays(-45), Amount = 5 }   // Outside 30 days
        };

        // Act
        var result = _analyzer.IsInActiveProduction(manufactureHistory, dayThreshold: 30);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsInActiveProduction_WithOldProduction_ReturnsFalse()
    {
        // Arrange
        var manufactureHistory = new List<ManufactureHistoryRecord>
        {
            new() { Date = FrozenNowUtc.AddDays(-45), Amount = 10 }, // Outside 30 days
            new() { Date = FrozenNowUtc.AddDays(-60), Amount = 5 }   // Outside 30 days
        };

        // Act
        var result = _analyzer.IsInActiveProduction(manufactureHistory, dayThreshold: 30);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsInActiveProduction_WithZeroAmount_ReturnsFalse()
    {
        // Arrange
        var manufactureHistory = new List<ManufactureHistoryRecord>
        {
            new() { Date = FrozenNowUtc.AddDays(-15), Amount = 0 } // Recent but zero quantity
        };

        // Act
        var result = _analyzer.IsInActiveProduction(manufactureHistory, dayThreshold: 30);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsInActiveProduction_WithNoHistory_ReturnsFalse()
    {
        // Arrange
        var manufactureHistory = new List<ManufactureHistoryRecord>();

        // Act
        var result = _analyzer.IsInActiveProduction(manufactureHistory, dayThreshold: 30);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsInActiveProduction_WithCustomThreshold_RespectsThreshold()
    {
        // Arrange
        var manufactureHistory = new List<ManufactureHistoryRecord>
        {
            new() { Date = FrozenNowUtc.AddDays(-8), Amount = 10 } // Within 10 days but outside 5 days
        };

        // Act & Assert
        Assert.True(_analyzer.IsInActiveProduction(manufactureHistory, dayThreshold: 10));
        Assert.False(_analyzer.IsInActiveProduction(manufactureHistory, dayThreshold: 5));
    }

    [Fact]
    public void IsInActiveProduction_RecordExactlyAtCutoff_IsConsideredActive()
    {
        // Arrange — record date matches the cutoff exactly. The production code uses
        // m.Date >= cutoffDate, so equality is INCLUSIVE.
        const int dayThreshold = 30;
        var cutoffDate = FrozenNowUtc.AddDays(-dayThreshold);
        var manufactureHistory = new List<ManufactureHistoryRecord>
        {
            new() { Date = cutoffDate, Amount = 10 }
        };

        // Act
        var result = _analyzer.IsInActiveProduction(manufactureHistory, dayThreshold);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsInActiveProduction_RecordOneTickBeforeCutoff_IsNotConsideredActive()
    {
        // Arrange — one tick before the cutoff is OUTSIDE the inclusive window.
        const int dayThreshold = 30;
        var cutoffDate = FrozenNowUtc.AddDays(-dayThreshold);
        var manufactureHistory = new List<ManufactureHistoryRecord>
        {
            new() { Date = cutoffDate.AddTicks(-1), Amount = 10 }
        };

        // Act
        var result = _analyzer.IsInActiveProduction(manufactureHistory, dayThreshold);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetLastProductionDate_WithHistory_ReturnsLatestDate()
    {
        // Arrange
        var expectedDate = FrozenNowUtc.AddDays(-10);
        var manufactureHistory = new List<ManufactureHistoryRecord>
        {
            new() { Date = FrozenNowUtc.AddDays(-30), Amount = 5 },
            new() { Date = expectedDate, Amount = 10 }, // Latest
            new() { Date = FrozenNowUtc.AddDays(-20), Amount = 3 }
        };

        // Act
        var result = _analyzer.GetLastProductionDate(manufactureHistory);

        // Assert
        result.Should().Be(expectedDate);
    }

    [Fact]
    public void GetLastProductionDate_WithNoHistory_ReturnsNull()
    {
        // Arrange
        var manufactureHistory = new List<ManufactureHistoryRecord>();

        // Act
        var result = _analyzer.GetLastProductionDate(manufactureHistory);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetLastProductionDate_WithZeroAmountOnly_ReturnsNull()
    {
        // Arrange
        var manufactureHistory = new List<ManufactureHistoryRecord>
        {
            new() { Date = FrozenNowUtc.AddDays(-10), Amount = 0 },
            new() { Date = FrozenNowUtc.AddDays(-5), Amount = 0 }
        };

        // Act
        var result = _analyzer.GetLastProductionDate(manufactureHistory);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void CalculateAverageProductionFrequency_WithRegularProduction_ReturnsCorrectAverage()
    {
        // Arrange
        // Use relative dates to ensure test data falls within the analysis window
        var baseDate = FrozenNowUtc.AddMonths(-6); // Well within 12-month window
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
            new() { Date = FrozenNowUtc.AddDays(-10), Amount = 10 }
        };

        // Act
        var result = _analyzer.CalculateAverageProductionFrequency(manufactureHistory, analysisMonths: 12);

        // Assert
        result.Should().Be(double.PositiveInfinity);
    }

    [Fact]
    public void CalculateAverageProductionFrequency_WithNoData_ReturnsInfinity()
    {
        // Arrange
        var manufactureHistory = new List<ManufactureHistoryRecord>();

        // Act
        var result = _analyzer.CalculateAverageProductionFrequency(manufactureHistory, analysisMonths: 12);

        // Assert
        result.Should().Be(double.PositiveInfinity);
    }

    [Fact]
    public void CalculateAverageProductionFrequency_FiltersOldData_ReturnsCorrectResult()
    {
        // Arrange
        var recentDate = FrozenNowUtc.AddMonths(-1);
        var oldDate = FrozenNowUtc.AddMonths(-24); // Outside 12-month analysis window
        var manufactureHistory = new List<ManufactureHistoryRecord>
        {
            new() { Date = oldDate, Amount = 10 },                    // Should be ignored
            new() { Date = recentDate, Amount = 5 },
            new() { Date = recentDate.AddDays(15), Amount = 8 }       // 15 days gap
        };

        // Act
        var result = _analyzer.CalculateAverageProductionFrequency(manufactureHistory, analysisMonths: 12);

        // Assert
        result.Should().Be(15.0); // Only one interval: 15 days
    }
}
