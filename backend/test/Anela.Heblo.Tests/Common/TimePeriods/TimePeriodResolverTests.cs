using Anela.Heblo.Application.Common.TimePeriods;
using FluentAssertions;

namespace Anela.Heblo.Tests.Common.TimePeriods;

public class TimePeriodResolverTests
{
    private readonly TimePeriodResolver _sut = new();

    [Fact]
    public void Resolve_PreviousQuarter_ReturnsOneRange()
    {
        // Act
        var result = _sut.Resolve(TimePeriod.PreviousQuarter);

        // Assert
        result.Should().HaveCount(1);
        result[0].From.Should().BeBefore(result[0].To);
        result[0].From.Should().BeBefore(DateTime.UtcNow);
    }

    [Fact]
    public void Resolve_FutureQuarter_ReturnsOneRange()
    {
        // Act
        var result = _sut.Resolve(TimePeriod.FutureQuarter);

        // Assert
        result.Should().HaveCount(1);
        result[0].From.Should().BeBefore(result[0].To);
        result[0].From.Should().BeBefore(DateTime.UtcNow);
    }

    [Fact]
    public void Resolve_Y2Y_ReturnsOneRange()
    {
        // Act
        var result = _sut.Resolve(TimePeriod.Y2Y);

        // Assert
        result.Should().HaveCount(1);
        result[0].From.Should().BeBefore(result[0].To);
        result[0].From.Should().BeBefore(DateTime.UtcNow);
    }

    [Fact]
    public void Resolve_PreviousSeason_ReturnsOneRange()
    {
        // Act
        var result = _sut.Resolve(TimePeriod.PreviousSeason);

        // Assert
        result.Should().HaveCount(1);
        result[0].From.Should().BeBefore(result[0].To);
        result[0].From.Should().BeBefore(DateTime.UtcNow);
    }

    [Fact]
    public void Resolve_Q9M_ReturnsTwoRanges()
    {
        // Act
        var result = _sut.Resolve(TimePeriod.Q9M);

        // Assert
        result.Should().HaveCount(2);

        result[0].From.Should().BeBefore(result[0].To);
        result[0].From.Should().BeBefore(DateTime.UtcNow);

        result[1].From.Should().BeBefore(result[1].To);
        result[1].From.Should().BeBefore(DateTime.UtcNow);

        result[1].From.Should().BeBefore(result[0].From);
    }

    [Fact]
    public void Resolve_CustomPeriod_WithBothDates_ReturnsOneRange()
    {
        // Arrange
        var from = new DateTime(2024, 1, 1);
        var to = new DateTime(2024, 3, 31);

        // Act
        var result = _sut.Resolve(TimePeriod.CustomPeriod, from, to);

        // Assert
        result.Should().HaveCount(1);
        result[0].From.Should().Be(from);
        result[0].To.Should().Be(to);
    }

    [Fact]
    public void Resolve_CustomPeriod_WithNullFrom_ReturnsEmptyList()
    {
        // Act
        var result = _sut.Resolve(TimePeriod.CustomPeriod, customFrom: null, customTo: new DateTime(2024, 3, 31));

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_CustomPeriod_WithNullTo_ReturnsEmptyList()
    {
        // Act
        var result = _sut.Resolve(TimePeriod.CustomPeriod, customFrom: new DateTime(2024, 1, 1), customTo: null);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_CustomPeriod_WithBothNull_ReturnsEmptyList()
    {
        // Act
        var result = _sut.Resolve(TimePeriod.CustomPeriod);

        // Assert
        result.Should().BeEmpty();
    }
}
