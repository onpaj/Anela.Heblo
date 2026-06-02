using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Domain.Features.Analytics;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

public class MarginCalculatorTests
{
    private readonly MarginCalculator _calculator = new();

    private static AnalyticsProduct MakeProduct(decimal sellingPrice, decimal marginAmount) =>
        new() { SellingPrice = sellingPrice, MarginAmount = marginAmount };

    [Fact]
    public void CalculateForProduct_EmptySales_ReturnsAllZeros()
    {
        var result = _calculator.CalculateForProduct(MakeProduct(100m, 30m), []);

        result.UnitsSold.Should().Be(0);
        result.Revenue.Should().Be(0m);
        result.Cost.Should().Be(0m);
        result.Margin.Should().Be(0m);
        result.MarginPercentage.Should().Be(0m);
    }

    [Fact]
    public void CalculateForProduct_B2BOnly_ComputesCorrectly()
    {
        var sales = new List<SalesDataPoint> { new() { AmountB2B = 10, AmountB2C = 0 } };
        var result = _calculator.CalculateForProduct(MakeProduct(100m, 30m), sales);

        result.UnitsSold.Should().Be(10);
        result.Revenue.Should().Be(1000m);
        result.Cost.Should().Be(700m);
        result.Margin.Should().Be(300m);
        result.MarginPercentage.Should().BeApproximately(30m, 0.01m);
    }

    [Fact]
    public void CalculateForProduct_B2COnly_ComputesCorrectly()
    {
        var sales = new List<SalesDataPoint> { new() { AmountB2B = 0, AmountB2C = 5 } };
        var result = _calculator.CalculateForProduct(MakeProduct(200m, 50m), sales);

        result.UnitsSold.Should().Be(5);
        result.Revenue.Should().Be(1000m);
        result.Cost.Should().Be(750m);
        result.Margin.Should().Be(250m);
        result.MarginPercentage.Should().BeApproximately(25m, 0.01m);
    }

    [Fact]
    public void CalculateForProduct_MixedB2BAndB2C_SumsCorrectly()
    {
        var sales = new List<SalesDataPoint>
        {
            new() { AmountB2B = 10, AmountB2C = 5 },
            new() { AmountB2B = 20, AmountB2C = 10 }
        };
        var result = _calculator.CalculateForProduct(MakeProduct(150m, 100m), sales);

        result.UnitsSold.Should().Be(45);
        result.Revenue.Should().Be(6750m);
        result.Cost.Should().Be(2250m);
        result.Margin.Should().Be(4500m);
        result.MarginPercentage.Should().BeApproximately(66.67m, 0.01m);
    }

    [Fact]
    public void CalculateForProduct_ZeroSellingPrice_ReturnsZeroRevenueAndZeroMarginPercentage()
    {
        var sales = new List<SalesDataPoint> { new() { AmountB2B = 10, AmountB2C = 0 } };
        var result = _calculator.CalculateForProduct(MakeProduct(0m, 0m), sales);

        result.Revenue.Should().Be(0m);
        result.MarginPercentage.Should().Be(0m);
    }

    [Fact]
    public void CalculateForProduct_ZeroMarginAmount_ReturnsZeroMargin()
    {
        var sales = new List<SalesDataPoint> { new() { AmountB2B = 10, AmountB2C = 0 } };
        var result = _calculator.CalculateForProduct(MakeProduct(100m, 0m), sales);

        result.Margin.Should().Be(0m);
        result.MarginPercentage.Should().Be(0m);
    }

    [Fact]
    public void CalculateForProduct_NegativeMarginAmount_ComputesCorrectly()
    {
        var sales = new List<SalesDataPoint> { new() { AmountB2B = 10, AmountB2C = 0 } };
        var result = _calculator.CalculateForProduct(MakeProduct(100m, -20m), sales);

        result.Margin.Should().Be(-200m);
    }

    [Fact]
    public void CalculateForProduct_LargeValues_NoOverflow()
    {
        var sales = new List<SalesDataPoint> { new() { AmountB2B = 1_000_000, AmountB2C = 0 } };
        var result = _calculator.CalculateForProduct(MakeProduct(9999.99m, 5000m), sales);

        result.UnitsSold.Should().Be(1_000_000);
        result.Revenue.Should().BeGreaterThan(0m);
        result.Margin.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void CalculateForProduct_EnumeratesSequenceExactlyOnce()
    {
        var enumerationCount = 0;
        var sales = GetSalesWithCounter(ref enumerationCount);
        _calculator.CalculateForProduct(MakeProduct(100m, 30m), sales);

        enumerationCount.Should().Be(1);
    }

    private static IEnumerable<SalesDataPoint> GetSalesWithCounter(ref int counter)
    {
        counter++;
        yield return new SalesDataPoint { AmountB2B = 5, AmountB2C = 0 };
    }
}
