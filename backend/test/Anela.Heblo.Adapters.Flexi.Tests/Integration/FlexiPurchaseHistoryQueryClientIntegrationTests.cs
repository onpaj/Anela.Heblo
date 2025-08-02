using Anela.Heblo.Adapters.Flexi.Tests.Integration.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.Flexi.Tests.Integration;

[Collection("FlexiIntegration")]
public class FlexiPurchaseHistoryQueryClientIntegrationTests : IClassFixture<FlexiIntegrationTestFixture>
{
    private readonly FlexiIntegrationTestFixture _fixture;
    private readonly IPurchaseHistoryClient _client;

    public FlexiPurchaseHistoryQueryClientIntegrationTests(FlexiIntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _client = _fixture.ServiceProvider.GetRequiredService<IPurchaseHistoryClient>();
    }

    // TODO Fix timezone issues
    [Fact(Skip = "Timezone issues")]
    public async Task GetHistoryAsync_WithValidDateRange_ReturnsPurchaseHistory()
    {
        // Arrange
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddDays(-90); // Last 90 days
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate;
        var limit = 20;

        // Act
        var result = await _client.GetHistoryAsync(null, dateFrom, dateTo, limit);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IReadOnlyList<CatalogPurchaseRecord>>();

        if (result.Any())
        {
            // Verify basic structure
            result.Should().OnlyContain(record => !string.IsNullOrWhiteSpace(record.ProductCode));
            result.Should().OnlyContain(record => !string.IsNullOrWhiteSpace(record.SupplierName));
            result.Should().OnlyContain(record => record.Amount > 0, "Amount should be positive");
            result.Should().OnlyContain(record => record.PricePerPiece >= 0, "PricePerPiece should be non-negative");
            // Note: Some records might be slightly outside the exact range due to cache or system behavior
            // so we'll be more lenient with date validation
            // Convert UTC dates to local time for comparison
            result.Should().OnlyContain(record =>
                record.Date.ToLocalTime().Date >= dateFrom.Date &&
                record.Date.ToLocalTime().Date <= dateTo.Date,
                "Date should be within specified range when converted to local timezone");

            // Test limit parameter
            result.Count.Should().BeLessOrEqualTo(limit);

            // Verify calculated total price
            foreach (var record in result.Take(10))
            {
                var expectedTotal = record.PricePerPiece * (decimal)record.Amount;
                record.PriceTotal.Should().BeApproximately(expectedTotal, 0.01m,
                    $"PriceTotal should equal PricePerPiece * Amount for product {record.ProductCode}");
            }
        }
    }

    [Fact]
    public async Task GetHistoryAsync_WithSpecificProduct_FiltersCorrectly()
    {
        // Arrange
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddDays(-180); // Last 180 days for more data
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate;
        var specificProductCode = "HYD007"; // Use a known product code
        var limit = 10;

        // Act
        var result = await _client.GetHistoryAsync(specificProductCode, dateFrom, dateTo, limit);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            // All records should be for the specified product
            result.Should().OnlyContain(record => record.ProductCode == specificProductCode);

            // Verify other properties
            result.Should().OnlyContain(record => !string.IsNullOrWhiteSpace(record.SupplierName));
            result.Should().OnlyContain(record => record.Amount > 0);
            result.Should().OnlyContain(record => record.PricePerPiece >= 0);
            // Convert UTC dates to local time for comparison
            result.Should().OnlyContain(record =>
                record.Date.ToLocalTime().Date >= dateFrom.Date &&
                record.Date.ToLocalTime().Date <= dateTo.Date);

            result.Count.Should().BeLessOrEqualTo(limit);
        }
    }

    [Fact]
    public async Task GetHistoryAsync_WithCaching_UsesCacheOnSecondCall()
    {
        // Arrange
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddDays(-30);
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate;
        var limit = 5;

        // Act - First call (should fetch from API and cache)
        var result1 = await _client.GetHistoryAsync(null, dateFrom, dateTo, limit);

        // Act - Second call with same parameters (should use cache)
        var result2 = await _client.GetHistoryAsync(null, dateFrom, dateTo, limit);

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();

        if (result1.Any() && result2.Any())
        {
            // Results should be identical (from cache)
            result1.Count.Should().Be(result2.Count());

            // Compare first few records
            for (int i = 0; i < Math.Min(3, Math.Min(result1.Count, result2.Count)); i++)
            {
                var record1 = result1.ElementAt(i);
                var record2 = result2.ElementAt(i);

                record1.ProductCode.Should().Be(record2.ProductCode);
                record1.SupplierName.Should().Be(record2.SupplierName);
                record1.PricePerPiece.Should().Be(record2.PricePerPiece);
                record1.Amount.Should().Be(record2.Amount);
                record1.Date.Should().Be(record2.Date);
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(15)]
    public async Task GetHistoryAsync_WithDifferentLimits_RespectsLimitParameter(int limit)
    {
        // Arrange
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddDays(-365); // Last year for more data
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate;

        // Act
        var result = await _client.GetHistoryAsync(null, dateFrom, dateTo, limit);

        // Assert
        result.Should().NotBeNull();
        result.Count.Should().BeLessOrEqualTo(limit);
    }

    [Fact]
    public async Task GetHistoryAsync_ValidatesSupplierInformation()
    {
        // Arrange
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddDays(-60);
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate;
        var limit = 20;

        // Act
        var result = await _client.GetHistoryAsync(null, dateFrom, dateTo, limit);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            foreach (var record in result.Take(10))
            {
                // Supplier information should be valid
                record.SupplierName.Should().NotBeNullOrWhiteSpace("SupplierName should not be empty");
                record.SupplierName.Should().Be(record.SupplierName.Trim(), "SupplierName should be trimmed");

                // SupplierId should be valid if present
                if (record.SupplierId.HasValue)
                {
                    record.SupplierId.Value.Should().BeGreaterThan(0, "SupplierId should be positive if present");
                }

                // DocumentNumber should be present
                record.DocumentNumber.Should().NotBeNullOrWhiteSpace("DocumentNumber should not be empty");
            }
        }
    }

    [Fact]
    public async Task GetHistoryAsync_ValidatesPriceAndAmountCalculations()
    {
        // Arrange
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddDays(-120);
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate;
        var limit = 25;

        // Act
        var result = await _client.GetHistoryAsync(null, dateFrom, dateTo, limit);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            foreach (var record in result.Take(15))
            {
                // Basic validations
                record.Amount.Should().BeGreaterThan(0, $"Amount should be positive for product {record.ProductCode}");
                record.PricePerPiece.Should().BeGreaterOrEqualTo(0, $"PricePerPiece should be non-negative for product {record.ProductCode}");

                // Price calculation should be accurate
                var expectedTotal = record.PricePerPiece * (decimal)record.Amount;
                record.PriceTotal.Should().BeApproximately(expectedTotal, 0.001m,
                    $"PriceTotal ({record.PriceTotal}) should equal PricePerPiece ({record.PricePerPiece}) * Amount ({record.Amount}) for product {record.ProductCode}");

                // Prices should be reasonable (not extremely high)
                record.PricePerPiece.Should().BeLessThan(1000000m, "PricePerPiece should be reasonable");
                record.PriceTotal.Should().BeLessThan(10000000m, "PriceTotal should be reasonable");
            }
        }
    }

    [Fact]
    public async Task GetHistoryAsync_WithNonExistentProduct_ReturnsEmptyResults()
    {
        // Arrange
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddDays(-30);
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate;
        var nonExistentProductCode = "NON_EXISTENT_PRODUCT_12345";

        // Act
        var result = await _client.GetHistoryAsync(nonExistentProductCode, dateFrom, dateTo);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty("Non-existent product should return no records");
    }

    [Fact]
    public async Task GetHistoryAsync_WithFutureDateRange_ReturnsEmptyResults()
    {
        // Arrange
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddYears(10).AddDays(1); // Tomorrow
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate.AddYears(10).AddDays(30); // Next month

        // Act
        var result = await _client.GetHistoryAsync(null, dateFrom, dateTo);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty("Future dates should not have purchase history");
    }

    [Fact]
    public async Task GetHistoryAsync_ValidatesDateConsistency()
    {
        // Arrange
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddDays(-7);
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate;

        // Act
        var result = await _client.GetHistoryAsync(null, dateFrom, dateTo, 10);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            // All dates should be within the specified range
            // Convert UTC dates to local time for comparison
            result.Should().OnlyContain(record =>
                record.Date.ToLocalTime().Date >= dateFrom.Date,
                "All records should be after or on the start date when converted to local timezone");
            result.Should().OnlyContain(record =>
                record.Date.ToLocalTime().Date <= dateTo.Date,
                "All records should be before or on the end date when converted to local timezone");

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
        }
    }

    [Fact]
    public async Task Integration_PurchaseHistoryWorkflow_ValidatesCompleteDataFlow()
    {
        // This test validates the complete workflow and data consistency

        // Step 1: Get all purchase history for recent period
        var dateFrom = FlexiIntegrationTestFixture.ReferenceDate.AddDays(-60);
        var dateTo = FlexiIntegrationTestFixture.ReferenceDate;
        var allRecords = await _client.GetHistoryAsync(null, dateFrom, dateTo, 0); // No limit

        allRecords.Should().NotBeNull();

        if (allRecords.Any())
        {
            // Step 2: Get purchase history for a specific product that appears in the data
            var sampleProduct = allRecords.First().ProductCode;
            var productRecords = await _client.GetHistoryAsync(sampleProduct, dateFrom, dateTo, 0);

            productRecords.Should().NotBeNull();
            productRecords.Should().OnlyContain(record => record.ProductCode == sampleProduct);

            // Step 3: Verify filtering works correctly
            var productRecordsFromAll = allRecords.Where(r => r.ProductCode == sampleProduct).ToList();
            productRecords.Count.Should().Be(productRecordsFromAll.Count,
                "Filtered results should match records from unfiltered query");

            // Step 4: Test caching with limited results
            var cachedRecords = await _client.GetHistoryAsync(null, dateFrom, dateTo, 10);
            cachedRecords.Should().NotBeNull();
            cachedRecords.Count.Should().BeLessOrEqualTo(10);

            // Step 5: Validate business logic across all records
            foreach (var record in allRecords.Take(20))
            {
                // Product code format validation
                record.ProductCode.Should().NotBeNullOrWhiteSpace();
                record.ProductCode.Should().Be(record.ProductCode.Trim());

                // Supplier validation
                record.SupplierName.Should().NotBeNullOrWhiteSpace();

                // Financial data consistency
                if (record.PricePerPiece > 0 && record.Amount > 0)
                {
                    record.PriceTotal.Should().BeGreaterThan(0, "PriceTotal should be positive when both price and amount are positive");
                }

                // Document number should be present
                record.DocumentNumber.Should().NotBeNullOrWhiteSpace();
            }

            // Step 6: Verify unique suppliers exist
            var suppliers = allRecords.Select(r => r.SupplierName).Distinct().ToList();
            suppliers.Should().NotBeEmpty("Should have at least one supplier");
            suppliers.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s), "All supplier names should be valid");
        }
    }
}