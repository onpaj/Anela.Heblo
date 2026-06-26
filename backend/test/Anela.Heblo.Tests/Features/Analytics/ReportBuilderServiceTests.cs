using Anela.Heblo.Application.Features.Analytics;
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Models;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Domain.Features.Analytics;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

public class ReportBuilderServiceTests
{
    private readonly ReportBuilderService _service = new(new MarginCalculator());

    [Fact]
    public void BuildMonthlyBreakdown_MonthWithZeroSales_ProducesRowWithZeroValues()
    {
        var product = new AnalyticsProduct
        {
            ProductCode = "TEST",
            ProductName = "Test Product",
            Type = AnalyticsProductType.Product,
            SalesHistory = [],
            SellingPrice = 100m,
            MarginAmount = 30m
        };
        // Sales exist only in January; February is empty
        var sales = new List<SalesDataPoint>
        {
            new() { Date = new DateTime(2024, 1, 15), AmountB2B = 10, AmountB2C = 5 }
        };

        var result = _service.BuildMonthlyBreakdown(
            sales,
            product,
            new DateTime(2024, 1, 1),
            new DateTime(2024, 2, 29));

        result.Should().HaveCount(2);
        var feb = result.Single(r => r.Month.Month == 2);
        feb.UnitsSold.Should().Be(0);
        feb.Revenue.Should().Be(0m);
        feb.Cost.Should().Be(0m);
        feb.MarginAmount.Should().Be(0m);
    }

    [Fact]
    public void BuildMonthlyBreakdown_ValidSales_ComputesCorrectTotals()
    {
        var product = new AnalyticsProduct
        {
            ProductCode = "TEST",
            ProductName = "Test Product",
            Type = AnalyticsProductType.Product,
            SalesHistory = [],
            SellingPrice = 150m,
            MarginAmount = 100m
        };
        var sales = new List<SalesDataPoint>
        {
            new() { Date = new DateTime(2024, 3, 15), AmountB2B = 10, AmountB2C = 5 }
        };

        var result = _service.BuildMonthlyBreakdown(
            sales,
            product,
            new DateTime(2024, 3, 1),
            new DateTime(2024, 3, 31));

        result.Should().HaveCount(1);
        var march = result[0];
        march.UnitsSold.Should().Be(15);
        march.Revenue.Should().Be(2250m);
        march.Cost.Should().Be(750m);
        march.MarginAmount.Should().Be(1500m);
    }

    [Fact]
    public void BuildMonthlyBreakdown_StartEqualsEndSameMonth_ProducesSingleEntry()
    {
        var product = new AnalyticsProduct
        {
            ProductCode = "TEST",
            ProductName = "Test Product",
            Type = AnalyticsProductType.Product,
            SalesHistory = [],
            SellingPrice = 150m,
            MarginAmount = 100m
        };
        var sales = new List<SalesDataPoint>
        {
            new() { Date = new DateTime(2024, 5, 15), AmountB2B = 10, AmountB2C = 5 }
        };

        var result = _service.BuildMonthlyBreakdown(
            sales,
            product,
            new DateTime(2024, 5, 20),
            new DateTime(2024, 5, 20));

        result.Should().HaveCount(1);
        result[0].Month.Year.Should().Be(2024);
        result[0].Month.Month.Should().Be(5);
        result[0].UnitsSold.Should().Be(15);
    }

    [Fact]
    public void BuildMonthlyBreakdown_StartAfterEnd_ReturnsEmptyWithoutThrowing()
    {
        var product = new AnalyticsProduct
        {
            ProductCode = "TEST",
            ProductName = "Test Product",
            Type = AnalyticsProductType.Product,
            SalesHistory = [],
            SellingPrice = 150m,
            MarginAmount = 100m
        };
        var sales = new List<SalesDataPoint>();

        List<MonthlyMarginBreakdownDto>? result = null;
        Action act = () => result = _service.BuildMonthlyBreakdown(
            sales,
            product,
            new DateTime(2024, 3, 10),
            new DateTime(2024, 1, 10));

        act.Should().NotThrow();
        result.Should().NotBeNull();
        result!.Should().BeEmpty();
    }

    [Fact]
    public void BuildMonthlyBreakdown_MultiMonthRangeWithInteriorEmptyMonth_ProducesEntryPerMonth()
    {
        var product = new AnalyticsProduct
        {
            ProductCode = "TEST",
            ProductName = "Test Product",
            Type = AnalyticsProductType.Product,
            SalesHistory = [],
            SellingPrice = 150m,
            MarginAmount = 100m
        };
        // Sales in January and March; February is the interior empty month.
        var sales = new List<SalesDataPoint>
        {
            new() { Date = new DateTime(2024, 1, 15), AmountB2B = 10, AmountB2C = 5 },
            new() { Date = new DateTime(2024, 3, 10), AmountB2B = 4, AmountB2C = 6 }
        };

        var result = _service.BuildMonthlyBreakdown(
            sales,
            product,
            new DateTime(2024, 1, 1),
            new DateTime(2024, 3, 31));

        result.Should().HaveCount(3);
        result.Select(r => r.Month.Month).Should().ContainInOrder(1, 2, 3);

        var february = result.Single(r => r.Month.Month == 2);
        february.UnitsSold.Should().Be(0);
        february.Revenue.Should().Be(0m);
        february.Cost.Should().Be(0m);
        february.MarginAmount.Should().Be(0m);

        var january = result.Single(r => r.Month.Month == 1);
        january.UnitsSold.Should().Be(15);
    }

    [Fact]
    public void BuildCategorySummaries_ZeroRevenue_ReturnsZeroPercentageWithoutThrowing()
    {
        var categoryTotals = new Dictionary<string, CategoryData>
        {
            ["EmptyCategory"] = new CategoryData
            {
                TotalMargin = 50m,
                TotalRevenue = 0m,
                ProductCount = 1,
                TotalUnitsSold = 0
            }
        };

        List<CategoryMarginSummaryDto>? result = null;
        Action act = () => result = _service.BuildCategorySummaries(categoryTotals);

        act.Should().NotThrow();
        result.Should().NotBeNull();
        result!.Should().ContainSingle();
        var summary = result![0];
        summary.Category.Should().Be("EmptyCategory");
        summary.AverageMarginPercentage.Should().Be(0m);
    }

    [Fact]
    public void BuildCategorySummaries_PositiveRevenue_ComputesAverageMarginPercentage()
    {
        var categoryTotals = new Dictionary<string, CategoryData>
        {
            ["Soaps"] = new CategoryData
            {
                TotalMargin = 50m,
                TotalRevenue = 200m,
                ProductCount = 2,
                TotalUnitsSold = 10
            }
        };

        var result = _service.BuildCategorySummaries(categoryTotals);

        result.Should().ContainSingle();
        var summary = result[0];
        summary.Category.Should().Be("Soaps");
        summary.TotalMargin.Should().Be(50m);
        summary.TotalRevenue.Should().Be(200m);
        summary.AverageMarginPercentage.Should().Be(25m);
        summary.ProductCount.Should().Be(2);
        summary.TotalUnitsSold.Should().Be(10);
    }

    [Fact]
    public void BuildProductSummary_NullProductCategory_UsesDefaultCategory()
    {
        var product = new AnalyticsProduct
        {
            ProductCode = "TEST",
            ProductName = "Test Product",
            Type = AnalyticsProductType.Product,
            SalesHistory = [],
            MarginAmount = 30m,
            ProductCategory = null
        };
        var marginData = new AnalysisMarginData
        {
            Revenue = 100m,
            Cost = 70m,
            Margin = 30m,
            MarginPercentage = 30m,
            UnitsSold = 5
        };

        var summary = _service.BuildProductSummary(product, marginData);

        summary.ProductId.Should().Be("TEST");
        summary.Category.Should().Be(AnalyticsConstants.DEFAULT_CATEGORY);
        summary.Category.Should().Be("Uncategorized");
        summary.MarginAmount.Should().Be(30m);
        summary.UnitsSold.Should().Be(5);
    }

    [Fact]
    public void BuildProductSummary_NonNullProductCategory_PassesCategoryThrough()
    {
        var product = new AnalyticsProduct
        {
            ProductCode = "TEST",
            ProductName = "Test Product",
            Type = AnalyticsProductType.Product,
            SalesHistory = [],
            MarginAmount = 30m,
            ProductCategory = "Soaps"
        };
        var marginData = new AnalysisMarginData
        {
            Revenue = 100m,
            Cost = 70m,
            Margin = 30m,
            MarginPercentage = 30m,
            UnitsSold = 5
        };

        var summary = _service.BuildProductSummary(product, marginData);

        summary.Category.Should().Be("Soaps");
    }
}
