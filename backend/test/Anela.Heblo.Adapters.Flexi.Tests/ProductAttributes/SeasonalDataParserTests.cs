using Anela.Heblo.Adapters.Flexi.ProductAttributes;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.ProductAttributes;

public class SeasonalDataParserTests
{
    private readonly SeasonalDataParser _parser = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetSeasonalMonths_WhenInputIsNullOrEmpty_ReturnsEmptyArray(string? input)
    {
        // Act
        var result = _parser.GetSeasonalMonths(input);

        // Assert
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("1", new[] { 1 })]
    [InlineData("5", new[] { 5 })]
    [InlineData("12", new[] { 12 })]
    public void GetSeasonalMonths_WhenInputIsSingleValidMonth_ReturnsSingleMonth(string input, int[] expected)
    {
        // Act
        var result = _parser.GetSeasonalMonths(input);

        // Assert
        result.Should().BeEquivalentTo(expected);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("13")]
    [InlineData("-1")]
    [InlineData("25")]
    public void GetSeasonalMonths_WhenInputIsSingleInvalidMonth_ReturnsEmptyArray(string input)
    {
        // Act
        var result = _parser.GetSeasonalMonths(input);

        // Assert
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("1,3,5", new[] { 1, 3, 5 })]
    [InlineData("12,1,6", new[] { 1, 6, 12 })]
    [InlineData("5,3,5,3", new[] { 3, 5 })] // Duplicates should be removed
    public void GetSeasonalMonths_WhenInputIsMultipleSingleMonths_ReturnsOrderedUniqueMonths(string input, int[] expected)
    {
        // Act
        var result = _parser.GetSeasonalMonths(input);

        // Assert
        result.Should().BeEquivalentTo(expected);
        result.Should().BeInAscendingOrder();
    }

    [Theory]
    [InlineData("1-3", new[] { 1, 2, 3 })]
    [InlineData("5-8", new[] { 5, 6, 7, 8 })]
    [InlineData("10-12", new[] { 10, 11, 12 })]
    [InlineData("1-1", new[] { 1 })] // Same start and end
    public void GetSeasonalMonths_WhenInputIsValidRange_ReturnsRangeMonths(string input, int[] expected)
    {
        // Act
        var result = _parser.GetSeasonalMonths(input);

        // Assert
        result.Should().BeEquivalentTo(expected);
    }

    [Theory]
    [InlineData("11-2", new[] { 11, 12, 1, 2 })] // Cross-year range
    [InlineData("10-3", new[] { 10, 11, 12, 1, 2, 3 })]
    [InlineData("12-1", new[] { 12, 1 })]
    public void GetSeasonalMonths_WhenInputIsCrossYearRange_ReturnsCorrectMonths(string input, int[] expected)
    {
        // Act
        var result = _parser.GetSeasonalMonths(input);

        // Assert
        result.Should().BeEquivalentTo(expected);
        result.Should().BeInAscendingOrder();
    }

    [Theory]
    [InlineData("0-5")]
    [InlineData("1-13")]
    [InlineData("0-13")]
    [InlineData("-1-5")]
    [InlineData("5--1")]
    public void GetSeasonalMonths_WhenInputIsInvalidRange_ReturnsEmptyArray(string input)
    {
        // Act
        var result = _parser.GetSeasonalMonths(input);

        // Assert
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("1-")]
    [InlineData("-5")]
    [InlineData("1-5-8")]
    [InlineData("abc")]
    [InlineData("1a")]
    [InlineData("a1")]
    public void GetSeasonalMonths_WhenInputIsInvalidFormat_ReturnsEmptyArray(string input)
    {
        // Act
        var result = _parser.GetSeasonalMonths(input);

        // Assert
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("1,3-5,8", new[] { 1, 3, 4, 5, 8 })]
    [InlineData("1-3,6,8-10", new[] { 1, 2, 3, 6, 8, 9, 10 })]
    [InlineData("12,1-3,11", new[] { 1, 2, 3, 11, 12 })]
    [InlineData("5,3-5,7", new[] { 3, 4, 5, 7 })] // Overlapping should be deduplicated
    public void GetSeasonalMonths_WhenInputIsMixedSingleAndRanges_ReturnsCorrectMonths(string input, int[] expected)
    {
        // Act
        var result = _parser.GetSeasonalMonths(input);

        // Assert
        result.Should().BeEquivalentTo(expected);
        result.Should().BeInAscendingOrder();
    }

    [Theory]
    [InlineData("1,abc,5", new[] { 1, 5 })] // Invalid sections ignored
    [InlineData("1,0,5", new[] { 1, 5 })]
    [InlineData("1,13,5", new[] { 1, 5 })]
    [InlineData("1,1-15,5", new[] { 1, 5 })]
    public void GetSeasonalMonths_WhenInputContainsInvalidSections_IgnoresInvalidAndProcessesValid(string input, int[] expected)
    {
        // Act
        var result = _parser.GetSeasonalMonths(input);

        // Assert
        result.Should().BeEquivalentTo(expected);
        result.Should().BeInAscendingOrder();
    }

    [Theory]
    [InlineData("11-2,1-3", new[] { 11, 12, 1, 2, 3 })] // Cross-year range with overlap
    [InlineData("10-1,12-3", new[] { 10, 11, 12, 1, 2, 3 })]
    public void GetSeasonalMonths_WhenInputContainsMultipleCrossYearRanges_ReturnsCorrectMonths(string input, int[] expected)
    {
        // Act
        var result = _parser.GetSeasonalMonths(input);

        // Assert
        result.Should().BeEquivalentTo(expected);
        result.Should().BeInAscendingOrder();
    }

    [Fact]
    public void GetSeasonalMonths_WhenInputContainsAllMonths_ReturnsAllMonths()
    {
        // Arrange
        var input = "1-12";
        var expected = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

        // Act
        var result = _parser.GetSeasonalMonths(input);

        // Assert
        result.Should().BeEquivalentTo(expected);
        result.Should().BeInAscendingOrder();
    }

    [Theory]
    [InlineData("1, 3, 5", new[] { 1, 3, 5 })] // Spaces should be handled
    [InlineData(" 1-3 , 5 ", new[] { 1, 2, 3, 5 })]
    public void GetSeasonalMonths_WhenInputContainsSpaces_HandlesSpacesCorrectly(string input, int[] expected)
    {
        // Act
        var result = _parser.GetSeasonalMonths(input);

        // Assert
        result.Should().BeEquivalentTo(expected);
        result.Should().BeInAscendingOrder();
    }

    [Theory]
    [InlineData("abc,def,xyz")]
    [InlineData("invalid")]
    [InlineData("1a,2b,3c")]
    public void GetSeasonalMonths_WhenAllSectionsAreInvalid_ReturnsEmptyArray(string input)
    {
        // Act
        var result = _parser.GetSeasonalMonths(input);

        // Assert
        result.Should().BeEmpty();
    }
}