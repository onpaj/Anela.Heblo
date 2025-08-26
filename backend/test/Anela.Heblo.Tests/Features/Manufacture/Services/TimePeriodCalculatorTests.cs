using Anela.Heblo.Application.Features.Manufacture.Model;
using FluentAssertions;
using Anela.Heblo.Application.Features.Manufacture.Services;
using FluentAssertions;
using Xunit;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.Manufacture.Services;

public class TimePeriodCalculatorTests
{
    private readonly TimePeriodCalculator _sut;

    public TimePeriodCalculatorTests()
    {
        _sut = new TimePeriodCalculator();
    }

    [Fact]
    public void CalculateTimePeriod_WithCustomPeriod_ReturnsCustomDates()
    {
        // Arrange
        var customFromDate = new DateTime(2023, 1, 1);
        var customToDate = new DateTime(2023, 3, 31);

        // Act
        var (fromDate, toDate) = _sut.CalculateTimePeriod(
            TimePeriodFilter.CustomPeriod, customFromDate, customToDate);

        // Assert
        fromDate.Should().Be(customFromDate);
        toDate.Should().Be(customToDate);
    }

    [Fact]
    public void CalculateTimePeriod_WithCustomPeriodButNoDates_FallsBackToDefault()
    {
        // Act
        var (fromDate, toDate) = _sut.CalculateTimePeriod(TimePeriodFilter.CustomPeriod);

        // Assert
        // Should fall back to default (previous quarter)
        (fromDate < toDate).Should().BeTrue();
        (toDate <= DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void CalculateTimePeriod_WithPreviousQuarter_ReturnsLast3Months()
    {
        // Act
        var (fromDate, toDate) = _sut.CalculateTimePeriod(TimePeriodFilter.PreviousQuarter);

        // Assert
        var now = DateTime.UtcNow;
        var expectedFrom = new DateTime(now.Year, now.Month, 1).AddMonths(-3);
        var expectedTo = new DateTime(now.Year, now.Month, 1).AddDays(-1);

        fromDate.Should().Be(expectedFrom);
        toDate.Should().Be(expectedTo);
    }

    [Fact]
    public void CalculateTimePeriod_WithY2Y_ReturnsLast12Months()
    {
        // Act
        var (fromDate, toDate) = _sut.CalculateTimePeriod(TimePeriodFilter.Y2Y);

        // Assert
        var now = DateTime.UtcNow;
        var expectedFrom = new DateTime(now.Year, now.Month, 1).AddMonths(-12);
        var expectedTo = new DateTime(now.Year, now.Month, 1).AddDays(-1);

        fromDate.Should().Be(expectedFrom);
        toDate.Should().Be(expectedTo);
    }

    [Fact]
    public void CalculateTimePeriod_WithFutureQuarter_ReturnsCorrespondingPeriodFromPreviousYear()
    {
        // Act
        var (fromDate, toDate) = _sut.CalculateTimePeriod(TimePeriodFilter.FutureQuarter);

        // Assert
        var now = DateTime.UtcNow;
        var expectedFrom = new DateTime(now.Year - 1, now.Month, 1);
        var expectedTo = expectedFrom.AddMonths(3).AddDays(-1);

        fromDate.Should().Be(expectedFrom);
        toDate.Should().Be(expectedTo);
    }

    [Fact]
    public void CalculateTimePeriod_WithPreviousSeason_ReturnsOctoberToJanuaryPeriod()
    {
        // Act
        var (fromDate, toDate) = _sut.CalculateTimePeriod(TimePeriodFilter.PreviousSeason);

        // Assert
        var now = DateTime.UtcNow;
        var expectedFrom = new DateTime(now.Year - 1, 10, 1);
        var expectedTo = new DateTime(now.Year, 1, 31);

        fromDate.Should().Be(expectedFrom);
        toDate.Should().Be(expectedTo);
    }

    [Theory]
    [InlineData((TimePeriodFilter)999)] // Invalid enum value
    public void CalculateTimePeriod_WithInvalidTimePeriod_ReturnsDefaultPeriod(TimePeriodFilter invalidPeriod)
    {
        // Act
        var (fromDate, toDate) = _sut.CalculateTimePeriod(invalidPeriod);

        // Assert
        var now = DateTime.UtcNow;
        var expectedFrom = new DateTime(now.Year, now.Month, 1).AddMonths(-3);
        var expectedTo = new DateTime(now.Year, now.Month, 1).AddDays(-1);

        fromDate.Should().Be(expectedFrom);
        toDate.Should().Be(expectedTo);
    }
}