using System;
using System.Collections.Generic;
using Anela.Heblo.Domain.Features.Analytics;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

public class AnalyticsProductExtensionsTests
{
    private static AnalyticsProduct CreateProduct(List<SalesDataPoint> salesHistory)
    {
        return new AnalyticsProduct
        {
            ProductCode = "PROD001",
            ProductName = "Test Product",
            Type = AnalyticsProductType.Product,
            MarginAmount = 0m,
            SalesHistory = salesHistory
        };
    }

    [Fact]
    public void HasSalesInPeriod_SaleWithinRange_ReturnsTrue()
    {
        var product = CreateProduct(new List<SalesDataPoint>
        {
            new() { Date = new DateTime(2024, 6, 15), AmountB2B = 1, AmountB2C = 0 }
        });

        var result = product.HasSalesInPeriod(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));

        result.Should().BeTrue();
    }

    [Fact]
    public void HasSalesInPeriod_SaleExactlyOnStartDate_ReturnsTrue()
    {
        var startDate = new DateTime(2024, 1, 1);
        var product = CreateProduct(new List<SalesDataPoint>
        {
            new() { Date = startDate, AmountB2B = 1, AmountB2C = 0 }
        });

        var result = product.HasSalesInPeriod(startDate, new DateTime(2024, 12, 31));

        result.Should().BeTrue();
    }

    [Fact]
    public void HasSalesInPeriod_SaleExactlyOnEndDate_ReturnsTrue()
    {
        var endDate = new DateTime(2024, 12, 31);
        var product = CreateProduct(new List<SalesDataPoint>
        {
            new() { Date = endDate, AmountB2B = 1, AmountB2C = 0 }
        });

        var result = product.HasSalesInPeriod(new DateTime(2024, 1, 1), endDate);

        result.Should().BeTrue();
    }

    [Fact]
    public void HasSalesInPeriod_SaleBeforeStartDate_ReturnsFalse()
    {
        var product = CreateProduct(new List<SalesDataPoint>
        {
            new() { Date = new DateTime(2023, 12, 31), AmountB2B = 1, AmountB2C = 0 }
        });

        var result = product.HasSalesInPeriod(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));

        result.Should().BeFalse();
    }

    [Fact]
    public void HasSalesInPeriod_SaleAfterEndDate_ReturnsFalse()
    {
        var product = CreateProduct(new List<SalesDataPoint>
        {
            new() { Date = new DateTime(2025, 1, 1), AmountB2B = 1, AmountB2C = 0 }
        });

        var result = product.HasSalesInPeriod(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));

        result.Should().BeFalse();
    }

    [Fact]
    public void HasSalesInPeriod_EmptySalesHistory_ReturnsFalse()
    {
        var product = CreateProduct(new List<SalesDataPoint>());

        var result = product.HasSalesInPeriod(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));

        result.Should().BeFalse();
    }

    [Fact]
    public void HasSalesInPeriod_OneSaleInRangeAmongOthersOutOfRange_ReturnsTrue()
    {
        var product = CreateProduct(new List<SalesDataPoint>
        {
            new() { Date = new DateTime(2023, 5, 1), AmountB2B = 1, AmountB2C = 0 },
            new() { Date = new DateTime(2024, 6, 1), AmountB2B = 1, AmountB2C = 0 },
            new() { Date = new DateTime(2025, 5, 1), AmountB2B = 1, AmountB2C = 0 }
        });

        var result = product.HasSalesInPeriod(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));

        result.Should().BeTrue();
    }
}
