using Anela.Heblo.Adapters.Flexi.Tests.Integration.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.Flexi.Tests.Integration;

[Collection("FlexiIntegration")]
public class FlexiCatalogSalesClientIntegrationTests : IClassFixture<FlexiIntegrationTestFixture>
{
    private readonly FlexiIntegrationTestFixture _fixture;
    private readonly ICatalogSalesClient _client;

    public FlexiCatalogSalesClientIntegrationTests(FlexiIntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _client = _fixture.ServiceProvider.GetRequiredService<ICatalogSalesClient>();
    }

    [Fact]
    public async Task GetAsync_WithValidDateRange_ReturnsSalesData()
    {
        // Arrange
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddDays(-30); // Last 30 days
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate;
        var limit = 20;

        // Act
        var result = await _client.GetAsync(dateFrom, dateTo, limit);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IList<CatalogSaleRecord>>();

        if (result.Any())
        {
            // Verify basic structure
            result.Should().OnlyContain(record => !string.IsNullOrWhiteSpace(record.ProductCode));
            result.Should().OnlyContain(record => !string.IsNullOrWhiteSpace(record.ProductName));
            // Convert UTC dates to local time for comparison
            result.Should().OnlyContain(record =>
                record.Date.ToLocalTime().Date >= dateFrom.Date &&
                record.Date.ToLocalTime().Date <= dateTo.Date,
                "Date should be within specified range when converted to local timezone");

            // Test limit parameter
            result.Count.Should().BeLessOrEqualTo(limit);
        }
    }

    [Fact]
    public async Task GetAsync_ValidatesSalesAmounts()
    {
        // Arrange
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddDays(-60);
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate;
        var limit = 25;

        // Act
        var result = await _client.GetAsync(dateFrom, dateTo, limit);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            foreach (var sale in result.Take(15))
            {
                // Basic validations
                sale.AmountTotal.Should().BeGreaterOrEqualTo(0, $"AmountTotal should be non-negative for product {sale.ProductCode}");
                sale.AmountB2B.Should().BeGreaterOrEqualTo(0, $"AmountB2B should be non-negative for product {sale.ProductCode}");
                sale.AmountB2C.Should().BeGreaterOrEqualTo(0, $"AmountB2C should be non-negative for product {sale.ProductCode}");

                // Total should be sum of B2B and B2C
                var expectedTotal = sale.AmountB2B + sale.AmountB2C;
                sale.AmountTotal.Should().BeApproximately(expectedTotal, 0.001,
                    $"AmountTotal ({sale.AmountTotal}) should equal AmountB2B ({sale.AmountB2B}) + AmountB2C ({sale.AmountB2C}) for product {sale.ProductCode}");

                // Amounts should be reasonable (not extremely high)
                sale.AmountTotal.Should().BeLessThan(1000000, "AmountTotal should be reasonable");
            }
        }
    }

    [Fact]
    public async Task GetAsync_ValidatesSalesSums()
    {
        // Arrange
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddDays(-45);
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate;
        var limit = 30;

        // Act
        var result = await _client.GetAsync(dateFrom, dateTo, limit);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            foreach (var sale in result.Take(15))
            {
                // Basic validations
                sale.SumTotal.Should().BeGreaterOrEqualTo(0, $"SumTotal should be non-negative for product {sale.ProductCode}");
                sale.SumB2B.Should().BeGreaterOrEqualTo(0, $"SumB2B should be non-negative for product {sale.ProductCode}");
                sale.SumB2C.Should().BeGreaterOrEqualTo(0, $"SumB2C should be non-negative for product {sale.ProductCode}");

                // Total should be sum of B2B and B2C
                var expectedSum = sale.SumB2B + sale.SumB2C;
                sale.SumTotal.Should().BeApproximately(expectedSum, 0.01m,
                    $"SumTotal ({sale.SumTotal}) should equal SumB2B ({sale.SumB2B}) + SumB2C ({sale.SumB2C}) for product {sale.ProductCode}");

                // Sums should be reasonable (not extremely high)
                sale.SumTotal.Should().BeLessThan(10000000m, "SumTotal should be reasonable");

                // Product identifiers should be valid
                sale.ProductCode.Should().NotBeNullOrWhiteSpace("ProductCode should not be empty");
                sale.ProductName.Should().NotBeNullOrWhiteSpace("ProductName should not be empty");
                sale.ProductCode.Should().Be(sale.ProductCode.Trim(), "ProductCode should be trimmed");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public async Task GetAsync_WithDifferentLimits_RespectsLimitParameter(int limit)
    {
        // Arrange
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddDays(-90); // Last 90 days for more data
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate;

        // Act
        var result = await _client.GetAsync(dateFrom, dateTo, limit);

        // Assert
        result.Should().NotBeNull();
        result.Count.Should().BeLessOrEqualTo(limit);
    }


    // TODO - Fix timezone issues in these tests
    [Fact(Skip = "Timezone issues")]
    public async Task GetAsync_WithRecentDateRange_ReturnsRecentSales()
    {
        // Arrange
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddDays(-7); // Last week
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate;

        // Act
        var result = await _client.GetAsync(dateFrom, dateTo);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            // All dates should be within the last week
            // Convert UTC dates to local time for comparison  
            result.Should().OnlyContain(record =>
                record.Date.ToLocalTime().Date >= dateFrom.Date,
                "All records should be after or on the start date when converted to local timezone");
            result.Should().OnlyContain(record =>
                record.Date.ToLocalTime().Date <= dateTo.Date,
                "All records should be before or on the end date when converted to local timezone");

            // Verify data consistency
            foreach (var record in result.Take(10))
            {
                record.ProductCode.Should().NotBeNullOrWhiteSpace();
                record.ProductName.Should().NotBeNullOrWhiteSpace();

                // If there are sales, amounts should be positive
                if (record.AmountTotal > 0)
                {
                    record.SumTotal.Should().BeGreaterThan(0,
                        $"If AmountTotal > 0, SumTotal should be positive for product {record.ProductCode}");
                }
            }
        }
    }

    [Fact]
    public async Task GetAsync_WithFutureDateRange_ReturnsEmptyResults()
    {
        // Arrange
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddYears(10).AddDays(1); // Tomorrow
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate.AddYears(10).AddDays(30); // Next month

        // Act
        var result = await _client.GetAsync(dateFrom, dateTo);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty("Future dates should not have sales data");
    }

    [Fact]
    public async Task GetAsync_ValidatesB2BAndB2CSplit()
    {
        // Arrange
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddDays(-60);
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate;
        var limit = 50;

        // Act
        var result = await _client.GetAsync(dateFrom, dateTo, limit);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            // Find records with B2B sales
            var b2bSales = result.Where(r => r.AmountB2B > 0).Take(10).ToList();

            // Find records with B2C sales
            var b2cSales = result.Where(r => r.AmountB2C > 0).Take(10).ToList();

            // Validate B2B sales
            foreach (var sale in b2bSales)
            {
                sale.AmountB2B.Should().BeGreaterThan(0);
                sale.SumB2B.Should().BeGreaterThan(0,
                    $"B2B sum should be positive when B2B amount is positive for product {sale.ProductCode}");

                // Average price check
                var avgPriceB2B = (double)sale.SumB2B / sale.AmountB2B;
                avgPriceB2B.Should().BeGreaterThan(0).And.BeLessThan(1000000,
                    "Average B2B price should be reasonable");
            }

            // Validate B2C sales
            foreach (var sale in b2cSales)
            {
                sale.AmountB2C.Should().BeGreaterThan(0);
                sale.SumB2C.Should().BeGreaterThan(0,
                    $"B2C sum should be positive when B2C amount is positive for product {sale.ProductCode}");

                // Average price check
                var avgPriceB2C = (double)sale.SumB2C / sale.AmountB2C;
                avgPriceB2C.Should().BeGreaterThan(0).And.BeLessThan(1000000,
                    "Average B2C price should be reasonable");
            }
        }
    }

    [Fact]
    public async Task GetAsync_ValidatesDateConsistency()
    {
        // Arrange
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddDays(-14);
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate;

        // Act
        var result = await _client.GetAsync(dateFrom, dateTo, 20);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            // Check for chronological consistency if multiple records
            if (result.Count > 1)
            {
                var sortedByDate = result.OrderBy(r => r.Date).ToList();
                for (int i = 1; i < sortedByDate.Count; i++)
                {
                    sortedByDate[i].Date.Should().BeOnOrAfter(sortedByDate[i - 1].Date,
                        "Records should be in valid chronological order");
                }
            }

            // Verify date format consistency
            foreach (var record in result)
            {
                record.Date.Should().BeAfter(DateTime.MinValue);
                record.Date.Should().BeBefore(DateTime.MaxValue);
                record.Date.Kind.Should().BeOneOf(DateTimeKind.Utc, DateTimeKind.Local, DateTimeKind.Unspecified);
            }
        }
    }

    [Fact]
    public async Task GetAsync_CancellationToken_CanBeCancelled()
    {
        // Arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddDays(-30);
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate;

        // Act & Assert
        var act = async () => await _client.GetAsync(dateFrom, dateTo, 10, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        // This might or might not throw depending on timing, but should not hang
        //await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(10));
    }

    // TODO - Fix timezone issues in these tests
    [Fact(Skip = "Timezone issues")]
    public async Task Integration_SalesWorkflow_ValidatesCompleteDataFlow()
    {
        // This test validates the complete workflow and data consistency

        // Step 1: Get sales for recent period
        // Use Date property to avoid timezone issues
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddDays(-30);
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate;
        var allSales = await _client.GetAsync(dateFrom, dateTo, 0); // No limit

        allSales.Should().NotBeNull();

        if (allSales.Any())
        {
            // Step 2: Verify overall data structure
            allSales.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.ProductCode));
            allSales.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.ProductName));
            // Convert UTC dates to local time for comparison
            allSales.Should().OnlyContain(s =>
                s.Date.ToLocalTime().Date >= dateFrom.Date &&
                s.Date.ToLocalTime().Date <= dateTo.Date,
                "Dates should be within specified range when converted to local timezone");

            // Step 3: Group by product and verify consistency
            var salesByProduct = allSales.GroupBy(s => s.ProductCode).ToList();

            foreach (var productGroup in salesByProduct.Take(5))
            {
                var productCode = productGroup.Key;
                var productSales = productGroup.ToList();

                // All sales for same product should have same product name
                var productNames = productSales.Select(s => s.ProductName).Distinct().ToList();
                productNames.Count.Should().Be(1,
                    $"Product {productCode} should have consistent product name across all records");

                // Calculate totals
                var totalAmount = productSales.Sum(s => s.AmountTotal);
                var totalSum = productSales.Sum(s => s.SumTotal);

                // Totals should be reasonable
                totalAmount.Should().BeGreaterOrEqualTo(0);
                totalSum.Should().BeGreaterOrEqualTo(0);

                // If there's amount, there should be sum
                if (totalAmount > 0)
                {
                    totalSum.Should().BeGreaterThan(0,
                        $"Product {productCode} has {totalAmount} units sold, so should have positive sum");
                }
            }

            // Step 4: Verify B2B/B2C split logic
            foreach (var sale in allSales.Take(20))
            {
                // Total equals sum of parts
                var expectedAmount = sale.AmountB2B + sale.AmountB2C;
                sale.AmountTotal.Should().BeApproximately(expectedAmount, 0.001);

                var expectedSum = sale.SumB2B + sale.SumB2C;
                sale.SumTotal.Should().BeApproximately(expectedSum, 0.01m);

                // Validate price consistency
                if (sale.AmountTotal > 0 && sale.SumTotal > 0)
                {
                    var avgPrice = (double)sale.SumTotal / sale.AmountTotal;
                    avgPrice.Should().BeGreaterThan(0).And.BeLessThan(1000000,
                        $"Average price for {sale.ProductCode} should be reasonable");
                }
            }

            // Step 5: Test date filtering with smaller range
            var recentFrom = FlexiIntegrationTestFixture.ReferenceDate.AddDays(-7);
            var recentSales = await _client.GetAsync(recentFrom, dateTo, 10);

            recentSales.Should().NotBeNull();
            if (recentSales.Any())
            {
                // Use Date property for comparison
                recentSales.Should().OnlyContain(s => s.Date.ToLocalTime() >= recentFrom.Date);
                recentSales.Count.Should().BeLessOrEqualTo(10);
            }
        }
    }
}