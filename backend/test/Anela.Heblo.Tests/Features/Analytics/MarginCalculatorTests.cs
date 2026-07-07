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
        new()
        {
            ProductCode = "TEST",
            ProductName = "Test Product",
            Type = AnalyticsProductType.Product,
            SalesHistory = [],
            SellingPrice = sellingPrice,
            MarginAmount = marginAmount
        };

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
        var sales = new List<SalesDataPoint> { new() { Date = default, AmountB2B = 10, AmountB2C = 0 } };
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
        var sales = new List<SalesDataPoint> { new() { Date = default, AmountB2B = 0, AmountB2C = 5 } };
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
            new() { Date = default, AmountB2B = 10, AmountB2C = 5 },
            new() { Date = default, AmountB2B = 20, AmountB2C = 10 }
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
        var sales = new List<SalesDataPoint> { new() { Date = default, AmountB2B = 10, AmountB2C = 0 } };
        var result = _calculator.CalculateForProduct(MakeProduct(0m, 0m), sales);

        result.Revenue.Should().Be(0m);
        result.MarginPercentage.Should().Be(0m);
    }

    [Fact]
    public void CalculateForProduct_ZeroMarginAmount_ReturnsZeroMargin()
    {
        var sales = new List<SalesDataPoint> { new() { Date = default, AmountB2B = 10, AmountB2C = 0 } };
        var result = _calculator.CalculateForProduct(MakeProduct(100m, 0m), sales);

        result.Margin.Should().Be(0m);
        result.MarginPercentage.Should().Be(0m);
    }

    [Fact]
    public void CalculateForProduct_NegativeMarginAmount_ComputesCorrectly()
    {
        var sales = new List<SalesDataPoint> { new() { Date = default, AmountB2B = 10, AmountB2C = 0 } };
        var result = _calculator.CalculateForProduct(MakeProduct(100m, -20m), sales);

        result.Margin.Should().Be(-200m);
    }

    [Fact]
    public void CalculateForProduct_LargeValues_NoOverflow()
    {
        var sales = new List<SalesDataPoint> { new() { Date = default, AmountB2B = 1_000_000, AmountB2C = 0 } };
        var result = _calculator.CalculateForProduct(MakeProduct(9999.99m, 5000m), sales);

        result.UnitsSold.Should().Be(1_000_000);
        result.Revenue.Should().BeGreaterThan(0m);
        result.Margin.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void CalculateForProduct_EnumeratesSequenceExactlyOnce()
    {
        var enumerationCount = new[] { 0 };
        var sales = GetSalesWithCounter(enumerationCount);
        _calculator.CalculateForProduct(MakeProduct(100m, 30m), sales);

        enumerationCount[0].Should().Be(1);
    }

    [Fact]
    public void GetGroupAggregatedMarginData_EmptyList_ReturnsDefaultGroupMarginData()
    {
        var result = _calculator.GetGroupAggregatedMarginData(new List<AnalyticsProduct>());

        result.M0Amount.Should().Be(0m);
        result.M1Amount.Should().Be(0m);
        result.M2Amount.Should().Be(0m);
        result.M0Percentage.Should().Be(0m);
        result.M1Percentage.Should().Be(0m);
        result.M2Percentage.Should().Be(0m);
        result.SellingPrice.Should().Be(0m);
        result.PurchasePrice.Should().Be(0m);
    }

    [Fact]
    public void GetGroupAggregatedMarginData_ZeroTotalSales_ReturnsSimpleAverage()
    {
        var products = new List<AnalyticsProduct>
        {
            new()
            {
                ProductCode = "A",
                ProductName = "Product A",
                Type = AnalyticsProductType.Product,
                MarginAmount = 10m,
                M0Amount = 10m,
                M1Amount = 20m,
                M2Amount = 30m,
                M0Percentage = 5m,
                M1Percentage = 15m,
                M2Percentage = 25m,
                SellingPrice = 100m,
                PurchasePrice = 40m,
                SalesHistory = []
            },
            new()
            {
                ProductCode = "B",
                ProductName = "Product B",
                Type = AnalyticsProductType.Product,
                MarginAmount = 20m,
                M0Amount = 30m,
                M1Amount = 40m,
                M2Amount = 50m,
                M0Percentage = 15m,
                M1Percentage = 25m,
                M2Percentage = 35m,
                SellingPrice = 200m,
                PurchasePrice = 60m,
                SalesHistory = []
            }
        };

        var result = _calculator.GetGroupAggregatedMarginData(products);

        result.M0Amount.Should().Be(20m); // (10+30)/2
        result.M1Amount.Should().Be(30m); // (20+40)/2
        result.M2Amount.Should().Be(40m); // (30+50)/2
        result.M0Percentage.Should().Be(10m); // (5+15)/2
        result.M1Percentage.Should().Be(20m); // (15+25)/2
        result.M2Percentage.Should().Be(30m); // (25+35)/2
        result.SellingPrice.Should().Be(150m); // (100+200)/2
        result.PurchasePrice.Should().Be(50m); // (40+60)/2
    }

    [Fact]
    public void GetGroupAggregatedMarginData_MultipleProductsWithSales_ReturnsWeightedAverage()
    {
        var products = new List<AnalyticsProduct>
        {
            new()
            {
                ProductCode = "A",
                ProductName = "Product A",
                Type = AnalyticsProductType.Product,
                MarginAmount = 10m,
                M0Amount = 10m,
                M1Amount = 20m,
                M2Amount = 30m,
                M0Percentage = 5m,
                M1Percentage = 15m,
                M2Percentage = 25m,
                SellingPrice = 100m,
                PurchasePrice = 40m,
                SalesHistory = new List<SalesDataPoint>
                {
                    new() { Date = default, AmountB2B = 10, AmountB2C = 0 } // 10 units
                }
            },
            new()
            {
                ProductCode = "B",
                ProductName = "Product B",
                Type = AnalyticsProductType.Product,
                MarginAmount = 20m,
                M0Amount = 30m,
                M1Amount = 40m,
                M2Amount = 50m,
                M0Percentage = 15m,
                M1Percentage = 25m,
                M2Percentage = 35m,
                SellingPrice = 200m,
                PurchasePrice = 60m,
                SalesHistory = new List<SalesDataPoint>
                {
                    new() { Date = default, AmountB2B = 30, AmountB2C = 0 } // 30 units
                }
            }
        };

        // totalSales = 40 units; weight A = 10/40, weight B = 30/40
        var result = _calculator.GetGroupAggregatedMarginData(products);

        result.M0Amount.Should().Be(25m); // (10*10 + 30*30) / 40
        result.M1Amount.Should().Be(35m); // (20*10 + 40*30) / 40
        result.M2Amount.Should().Be(45m); // (30*10 + 50*30) / 40
        result.M0Percentage.Should().Be(12.5m); // (5*10 + 15*30) / 40
        result.M1Percentage.Should().Be(22.5m); // (15*10 + 25*30) / 40
        result.M2Percentage.Should().Be(32.5m); // (25*10 + 35*30) / 40
        result.SellingPrice.Should().Be(175m); // (100*10 + 200*30) / 40
        result.PurchasePrice.Should().Be(55m); // (40*10 + 60*30) / 40
    }

    private static IEnumerable<SalesDataPoint> GetSalesWithCounter(int[] counter)
    {
        counter[0]++;
        yield return new SalesDataPoint { Date = default, AmountB2B = 5, AmountB2C = 0 };
    }
}
