using System;
using System.Collections.Generic;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.ProductStatistics;

public class MonthRangeTests
{
    [Fact]
    public void Expand_SingleMonth_ReturnsThatMonthOnly()
    {
        var result = MonthRange.Expand("2025-03", "2025-03");

        result.Should().Equal("2025-03");
    }

    [Fact]
    public void Expand_RangeWithinOneYear_IsAscendingAndInclusive()
    {
        var result = MonthRange.Expand("2025-01", "2025-04");

        result.Should().Equal("2025-01", "2025-02", "2025-03", "2025-04");
    }

    [Fact]
    public void Expand_RangeCrossingYearBoundary_RollsOverCorrectly()
    {
        var result = MonthRange.Expand("2024-11", "2025-02");

        result.Should().Equal("2024-11", "2024-12", "2025-01", "2025-02");
    }

    [Fact]
    public void Expand_FromBeforeHistoryFloor_ClampsToFloor()
    {
        var result = MonthRange.Expand("2019-10", "2020-02");

        result.Should().Equal("2020-01", "2020-02");
    }

    [Fact]
    public void Expand_InvertedRange_ReturnsEmpty()
    {
        var result = MonthRange.Expand("2025-05", "2025-02");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Expand_ThirteenMonthRange_HasThirteenEntries()
    {
        var result = MonthRange.Expand("2024-09", "2025-09");

        result.Should().HaveCount(13);
    }

    [Theory]
    [InlineData("2025-01", true, 2025, 1)]
    [InlineData("2025-12", true, 2025, 12)]
    [InlineData("2025-13", false, 0, 0)]
    [InlineData("2025-00", false, 0, 0)]
    [InlineData("2025-1", false, 0, 0)]
    [InlineData("nonsense", false, 0, 0)]
    [InlineData("", false, 0, 0)]
    [InlineData("0000-01", false, 0, 0)]
    public void TryParse_ValidatesFormat(string input, bool expectedOk, int expectedYear, int expectedMonth)
    {
        var ok = MonthRange.TryParse(input, out var year, out var month);

        ok.Should().Be(expectedOk);
        year.Should().Be(expectedYear);
        month.Should().Be(expectedMonth);
    }

    [Fact]
    public void Key_FromDate_PadsToFourTwoFormat()
    {
        MonthRange.Key(new DateTime(2025, 7, 19)).Should().Be("2025-07");
    }

    [Fact]
    public void Expand_InvalidYearZero_ReturnsEmpty()
    {
        var result = MonthRange.Expand("0000-01", "2025-01");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Expand_RangeEntirelyBeforeHistoryFloor_ReturnsEmpty()
    {
        var result = MonthRange.Expand("2019-01", "2019-06");

        result.Should().BeEmpty();
    }
}
