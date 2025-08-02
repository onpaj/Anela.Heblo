using Anela.Heblo.Adapters.Flexi.Tests.Integration.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.Flexi.Tests.Integration;

[Collection("FlexiIntegration")]
public class FlexiProductAttributesQueryClientIntegrationTests : IClassFixture<FlexiIntegrationTestFixture>
{
    private readonly FlexiIntegrationTestFixture _fixture;
    private readonly ICatalogAttributesClient _client;

    public FlexiProductAttributesQueryClientIntegrationTests(FlexiIntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _client = _fixture.ServiceProvider.GetRequiredService<ICatalogAttributesClient>();
    }

    [Fact]
    public async Task GetAttributesAsync_WithNoLimit_ReturnsAllAttributes()
    {
        // Arrange
        var limit = 0; // No limit

        // Act
        var result = await _client.GetAttributesAsync(limit);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IList<CatalogAttributes>>();

        if (result.Any())
        {
            // Verify basic structure
            result.Should().OnlyContain(attr => attr.ProductId > 0);
            result.Should().OnlyContain(attr => !string.IsNullOrWhiteSpace(attr.ProductCode));

            // Each product should appear only once (grouped by ProductId, ProductCode, ProductType)
            var duplicates = result.GroupBy(g => new { g.ProductId, g.ProductCode, g.ProductType })
                .Where(g => g.Count() > 1);
            duplicates.Should().BeEmpty("because each product should appear only once in the results");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task GetAttributesAsync_WithLimit_RespectsLimitParameter(int limit)
    {
        // Arrange & Act
        var result = await _client.GetAttributesAsync(limit);

        // Assert
        result.Should().NotBeNull();
        result.Count.Should().BeLessOrEqualTo(limit);
    }

    [Fact]
    public async Task GetAttributesAsync_ValidatesProductTypes()
    {
        // Arrange
        var limit = 20;

        // Act
        var result = await _client.GetAttributesAsync(limit);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            // Check that all product types are valid enum values
            result.Should().OnlyContain(attr => Enum.IsDefined(typeof(ProductType), attr.ProductType));

            // Should contain at least one product type
            var productTypes = result.Select(r => r.ProductType).Distinct().ToList();
            productTypes.Should().NotBeEmpty();
        }
    }

    [Fact]
    public async Task GetAttributesAsync_ValidatesNumericAttributes()
    {
        // Arrange
        var limit = 50;

        // Act
        var result = await _client.GetAttributesAsync(limit);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            // All numeric attributes should be non-negative
            result.Should().OnlyContain(attr => attr.OptimalStockDays >= 0, "OptimalStockDays should be non-negative");
            result.Should().OnlyContain(attr => attr.StockMin >= 0, "StockMin should be non-negative");
            result.Should().OnlyContain(attr => attr.BatchSize >= 0, "BatchSize should be non-negative");
            result.Should().OnlyContain(attr => attr.MinimalManufactureQuantity >= 0, "MinimalManufactureQuantity should be non-negative");

            // SeasonMonthsArray should be valid (if not null and not empty)
            foreach (var attr in result.Where(a => a.SeasonMonthsArray?.Any() == true))
            {
                attr.SeasonMonthsArray.Should().OnlyContain(month => month >= 1 && month <= 12, "SeasonMonthsArray should contain valid months (1-12)");
            }
        }
    }

    [Fact]
    public async Task GetAttributesAsync_ValidatesProductCodeFormat()
    {
        // Arrange
        var limit = 30;

        // Act
        var result = await _client.GetAttributesAsync(limit);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            // Product codes should not be null or whitespace
            result.Should().OnlyContain(attr => !string.IsNullOrWhiteSpace(attr.ProductCode));

            // Product codes should be trimmed and not contain obvious prefix artifacts
            result.Should().OnlyContain(attr => attr.ProductCode.Trim() == attr.ProductCode, "ProductCode should be trimmed");
        }
    }

    [Fact]
    public async Task GetAttributesAsync_ChecksSpecificAttributes()
    {
        // This test looks for products with specific attribute values to verify parsing

        // Arrange
        var limit = 100;

        // Act
        var result = await _client.GetAttributesAsync(limit);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            // Find products with some attributes set
            var productsWithOptimalStock = result.Where(r => r.OptimalStockDays > 0).ToList();
            var productsWithStockMin = result.Where(r => r.StockMin > 0).ToList();
            var productsWithBatchSize = result.Where(r => r.BatchSize > 0).ToList();
            var productsWithMMQ = result.Where(r => r.MinimalManufactureQuantity > 0).ToList();
            var productsWithSeasons = result.Where(r => r.SeasonMonthsArray?.Any() == true).ToList();

            // Log some statistics for debugging
            if (productsWithOptimalStock.Any())
            {
                var sampleProduct = productsWithOptimalStock.First();
                sampleProduct.ProductCode.Should().NotBeNullOrWhiteSpace();
                sampleProduct.OptimalStockDays.Should().BeGreaterThan(0);
            }

            if (productsWithSeasons.Any())
            {
                var seasonalProduct = productsWithSeasons.First();
                seasonalProduct.SeasonMonthsArray.Should().NotBeEmpty();
                seasonalProduct.SeasonMonthsArray.Should().OnlyContain(month => month >= 1 && month <= 12);
            }
        }
    }

    [Fact]
    public async Task GetAttributesAsync_ComparesDifferentLimits()
    {
        // This test validates that limit parameter works correctly

        // Arrange & Act
        var smallResult = await _client.GetAttributesAsync(5);
        var largeResult = await _client.GetAttributesAsync(20);

        // Assert
        smallResult.Should().NotBeNull();
        largeResult.Should().NotBeNull();

        smallResult.Count.Should().BeLessOrEqualTo(5);
        largeResult.Count.Should().BeLessOrEqualTo(20);

        if (smallResult.Any() && largeResult.Any())
        {
            // Small result should be a subset of large result (or have different items if ordering is different)
            // At minimum, both should contain valid data
            smallResult.Should().OnlyContain(attr => attr.ProductId > 0);
            largeResult.Should().OnlyContain(attr => attr.ProductId > 0);

            // If we have enough data, large result should have more items
            if (smallResult.Count == 5)
            {
                largeResult.Count.Should().BeGreaterOrEqualTo(smallResult.Count);
            }
        }
    }

    [Fact]
    public async Task Integration_AttributesWorkflow_ValidatesDataConsistency()
    {
        // This test validates the complete workflow and data consistency

        // Step 1: Get attributes with reasonable limit
        var attributes = await _client.GetAttributesAsync(30);

        attributes.Should().NotBeNull();

        if (attributes.Any())
        {
            // Step 2: Validate data consistency across different product types
            var products = attributes.Where(a => a.ProductType == ProductType.Product).ToList();
            var materials = attributes.Where(a => a.ProductType == ProductType.Material).ToList();

            // At least one type should exist, but both might not be present in test data
            var allTypes = attributes.Select(a => a.ProductType).Distinct().ToList();
            allTypes.Should().NotBeEmpty("System should contain at least one product type");

            // Step 3: Validate attribute patterns for available data
            foreach (var item in attributes.Take(10))
            {
                item.ProductId.Should().BeGreaterThan(0);
                item.ProductCode.Should().NotBeNullOrWhiteSpace();
                Enum.IsDefined(typeof(ProductType), item.ProductType).Should().BeTrue();

                // Validate numeric attributes are non-negative
                item.OptimalStockDays.Should().BeGreaterOrEqualTo(0);
                item.StockMin.Should().BeGreaterOrEqualTo(0);
                item.BatchSize.Should().BeGreaterOrEqualTo(0);
                item.MinimalManufactureQuantity.Should().BeGreaterOrEqualTo(0);
            }

            // Step 4: Validate that ProductIds are unique within the result set
            var productIds = attributes.Select(a => a.ProductId).ToList();
            var uniqueProductIds = productIds.Distinct().ToList();
            uniqueProductIds.Count.Should().Be(productIds.Count, "ProductIds should be unique in the result set");
        }
    }
}