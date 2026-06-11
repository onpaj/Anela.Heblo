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
}
