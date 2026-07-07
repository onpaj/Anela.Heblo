using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Services;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

public class TopProductSorterTests
{
    private readonly TopProductSorter _sorter = new();

    // Values are monotonically increasing Low -> Mid -> High across every sortable
    // field, and GroupKey/DisplayName are alphabetically ordered the same way, so
    // ascending order is always [Low, Mid, High] and descending is always
    // [High, Mid, Low], regardless of which field is being sorted on.
    private static List<TopProductDto> MakeProducts() => new()
    {
        new TopProductDto
        {
            GroupKey = "B-Mid",
            DisplayName = "B Product",
            TotalMargin = 200m,
            M0Amount = 20m,
            M1Amount = 25m,
            M2Amount = 30m,
            M0Percentage = 10m,
            M1Percentage = 15m,
            M2Percentage = 20m,
            SellingPrice = 150m,
            PurchasePrice = 70m
        },
        new TopProductDto
        {
            GroupKey = "A-Low",
            DisplayName = "A Product",
            TotalMargin = 100m,
            M0Amount = 10m,
            M1Amount = 15m,
            M2Amount = 20m,
            M0Percentage = 5m,
            M1Percentage = 10m,
            M2Percentage = 15m,
            SellingPrice = 100m,
            PurchasePrice = 50m
        },
        new TopProductDto
        {
            GroupKey = "C-High",
            DisplayName = "C Product",
            TotalMargin = 300m,
            M0Amount = 30m,
            M1Amount = 35m,
            M2Amount = 40m,
            M0Percentage = 15m,
            M1Percentage = 20m,
            M2Percentage = 25m,
            SellingPrice = 200m,
            PurchasePrice = 90m
        }
    };

    [Theory]
    [InlineData("groupkey")]
    [InlineData("productcode")]
    [InlineData("displayname")]
    [InlineData("productname")]
    [InlineData("totalmargin")]
    [InlineData("m0amount")]
    [InlineData("m1amount")]
    [InlineData("m2amount")]
    [InlineData("m0percentage")]
    [InlineData("m1percentage")]
    [InlineData("m2percentage")]
    [InlineData("sellingprice")]
    [InlineData("purchaseprice")]
    public void Sort_NamedKey_Ascending_OrdersLowMidHigh(string sortBy)
    {
        var result = _sorter.Sort(MakeProducts(), sortBy, sortDescending: false);

        result.Select(p => p.GroupKey).Should().ContainInOrder("A-Low", "B-Mid", "C-High");
    }

    [Theory]
    [InlineData("groupkey")]
    [InlineData("productcode")]
    [InlineData("displayname")]
    [InlineData("productname")]
    [InlineData("totalmargin")]
    [InlineData("m0amount")]
    [InlineData("m1amount")]
    [InlineData("m2amount")]
    [InlineData("m0percentage")]
    [InlineData("m1percentage")]
    [InlineData("m2percentage")]
    [InlineData("sellingprice")]
    [InlineData("purchaseprice")]
    public void Sort_NamedKey_Descending_OrdersHighMidLow(string sortBy)
    {
        var result = _sorter.Sort(MakeProducts(), sortBy, sortDescending: true);

        result.Select(p => p.GroupKey).Should().ContainInOrder("C-High", "B-Mid", "A-Low");
    }

    [Fact]
    public void Sort_NullSortBy_DefaultsToTotalMarginDescending()
    {
        var result = _sorter.Sort(MakeProducts(), null, sortDescending: true);

        result.Select(p => p.GroupKey).Should().ContainInOrder("C-High", "B-Mid", "A-Low");
    }

    [Fact]
    public void Sort_EmptySortBy_DefaultsToTotalMarginAscending()
    {
        var result = _sorter.Sort(MakeProducts(), "", sortDescending: false);

        result.Select(p => p.GroupKey).Should().ContainInOrder("A-Low", "B-Mid", "C-High");
    }

    [Fact]
    public void Sort_WhitespaceSortBy_DefaultsToTotalMargin()
    {
        var result = _sorter.Sort(MakeProducts(), "   ", sortDescending: false);

        result.Select(p => p.GroupKey).Should().ContainInOrder("A-Low", "B-Mid", "C-High");
    }

    [Fact]
    public void Sort_UnrecognizedSortBy_FallsBackToTotalMargin()
    {
        var result = _sorter.Sort(MakeProducts(), "not-a-real-key", sortDescending: true);

        result.Select(p => p.GroupKey).Should().ContainInOrder("C-High", "B-Mid", "A-Low");
    }

    [Fact]
    public void Sort_UppercaseSortByKey_IsCaseInsensitive()
    {
        var result = _sorter.Sort(MakeProducts(), "TOTALMARGIN", sortDescending: false);

        result.Select(p => p.GroupKey).Should().ContainInOrder("A-Low", "B-Mid", "C-High");
    }
}
