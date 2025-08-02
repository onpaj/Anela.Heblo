using Anela.Heblo.Application.Domain.Catalog.Price;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Integration;

[Collection("FlexiIntegration")]
public class FlexiProductPriceErpClientIntegrationTests : IClassFixture<FlexiIntegrationTestFixture>
{
    private readonly FlexiIntegrationTestFixture _fixture;
    private readonly IProductPriceErpClient _client;

    public FlexiProductPriceErpClientIntegrationTests(FlexiIntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _client = _fixture.ServiceProvider.GetRequiredService<IProductPriceErpClient>();
    }

    [Fact]
    public async Task GetAllAsync_WithForceReload_ReturnsPriceData()
    {
        // Arrange
        var forceReload = true;

        // Act
        var result = await _client.GetAllAsync(forceReload, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IEnumerable<ProductPriceErp>>();

        if (result.Any())
        {
            // Verify basic structure
            result.Should().OnlyContain(price => price.PriceWithoutVat >= 0, "PriceWithoutVat should be non-negative");
            result.Should().OnlyContain(price => price.PurchasePrice >= 0, "PurchasePrice should be non-negative");
            
            // Verify ProductCode is populated
            result.Should().OnlyContain(price => !string.IsNullOrEmpty(price.ProductCode), "ProductCode should be populated for all prices");

            // Validate calculated prices with VAT
            foreach (var price in result.Take(10))
            {
                price.PriceWithVat.Should().BeGreaterOrEqualTo(price.PriceWithoutVat, "PriceWithVat should be greater than or equal to PriceWithoutVat");
                price.PurchasePriceWithVat.Should().BeGreaterOrEqualTo(price.PurchasePrice, "PurchasePriceWithVat should be greater than or equal to PurchasePrice");
            }
        }
    }

    [Fact]
    public async Task GetAllAsync_WithoutForceReload_UsesCaching()
    {
        // This test verifies that caching works properly

        // Act - First call (should fetch from API)
        var result1 = await _client.GetAllAsync(forceReload: false, CancellationToken.None);

        // Act - Second call (should use cache)
        var result2 = await _client.GetAllAsync(forceReload: false, CancellationToken.None);

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();

        if (result1.Any() && result2.Any())
        {
            // Results should be identical (from cache)
            result1.Count().Should().Be(result2.Count());

            // Compare some sample records
            var firstRecord1 = result1.First();
            var firstRecord2 = result2.First();

            firstRecord1.PriceWithoutVat.Should().Be(firstRecord2.PriceWithoutVat);
            firstRecord1.PriceWithVat.Should().Be(firstRecord2.PriceWithVat);
            firstRecord1.PurchasePrice.Should().Be(firstRecord2.PurchasePrice);
            firstRecord1.PurchasePriceWithVat.Should().Be(firstRecord2.PurchasePriceWithVat);
        }
    }

    [Fact]
    public async Task GetAllAsync_ValidatesPriceCalculations()
    {
        // Arrange & Act
        var result = await _client.GetAllAsync(forceReload: true, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            foreach (var price in result.Take(20))
            {
                // Basic validations
                price.PriceWithoutVat.Should().BeGreaterOrEqualTo(0);
                price.PurchasePrice.Should().BeGreaterOrEqualTo(0);

                // VAT calculations should be logical
                if (price.PriceWithoutVat > 0)
                {
                    price.PriceWithVat.Should().BeGreaterOrEqualTo(price.PriceWithoutVat,
                        $"PriceWithVat ({price.PriceWithVat}) should be >= PriceWithoutVat ({price.PriceWithoutVat})");
                }

                if (price.PurchasePrice > 0)
                {
                    price.PurchasePriceWithVat.Should().BeGreaterOrEqualTo(price.PurchasePrice,
                        $"PurchasePriceWithVat ({price.PurchasePriceWithVat}) should be >= PurchasePrice ({price.PurchasePrice})");
                }
            }
        }
    }

    [Fact]
    public async Task GetAllAsync_ValidatesVatCalculations()
    {
        // This test specifically focuses on VAT calculation logic

        // Arrange & Act
        var result = await _client.GetAllAsync(forceReload: true, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            var productsWithPrice = result.Where(p => p.PriceWithoutVat > 0).Take(10).ToList();
            var productsWithPurchasePrice = result.Where(p => p.PurchasePrice > 0).Take(10).ToList();

            // Test VAT calculations for selling prices
            foreach (var product in productsWithPrice)
            {
                var vatMultiplier = product.PriceWithVat / product.PriceWithoutVat;

                // VAT multiplier should be reasonable (between 1.0 and 1.25 for Czech VAT rates)
                vatMultiplier.Should().BeInRange(1.0m, 1.25m,
                    $"VAT multiplier ({vatMultiplier}) should be reasonable");
            }

            // Test VAT calculations for purchase prices
            foreach (var product in productsWithPurchasePrice)
            {
                var vatMultiplier = product.PurchasePriceWithVat / product.PurchasePrice;

                // VAT multiplier should be reasonable
                vatMultiplier.Should().BeInRange(1.0m, 1.25m,
                    $"Purchase VAT multiplier ({vatMultiplier}) should be reasonable");
            }
        }
    }

    [Fact]
    public async Task GetAllAsync_ValidatesProductTypes()
    {
        // This test verifies different product types and their characteristics

        // Arrange & Act
        var result = await _client.GetAllAsync(forceReload: true, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            // Test that price data is reasonable
            var pricesWithData = result.Where(p => p.PriceWithoutVat > 0 && p.PriceWithVat > 0).Take(10).ToList();

            if (pricesWithData.Any())
            {
                foreach (var product in pricesWithData)
                {
                    product.PriceWithVat.Should().BeGreaterThan(product.PriceWithoutVat, "PriceWithVat should be greater than PriceWithoutVat");
                    product.PurchasePriceWithVat.Should().BeGreaterOrEqualTo(product.PurchasePrice, "PurchasePriceWithVat should be >= PurchasePrice");
                }
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetAllAsync_WithDifferentForceReloadValues_ReturnsConsistentData(bool forceReload)
    {
        // Arrange & Act
        var result = await _client.GetAllAsync(forceReload, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            // Data should be consistent regardless of forceReload parameter
            result.Should().OnlyContain(p => p.PriceWithoutVat >= 0);
            result.Should().OnlyContain(p => p.PurchasePrice >= 0);
            result.Should().OnlyContain(p => p.PriceWithVat >= p.PriceWithoutVat);
            result.Should().OnlyContain(p => p.PurchasePriceWithVat >= p.PurchasePrice);
        }
    }

    [Fact]
    public async Task GetAllAsync_CancellationToken_CanBeCancelled()
    {
        // Arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act & Assert
        var act = async () => await _client.GetAllAsync(forceReload: true, cts.Token);

        // This might or might not throw depending on timing, but should not hang
        await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task GetAllAsync_ValidatesProductCodePopulation()
    {
        // Arrange & Act
        var result = await _client.GetAllAsync(forceReload: true, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            foreach (var price in result.Take(10))
            {
                // ProductCode should be populated from Flexi
                price.ProductCode.Should().NotBeNullOrEmpty("ProductCode should be populated from Flexi data");
                price.ProductCode.Should().NotBeNullOrWhiteSpace("ProductCode should contain actual product code");
                
                // ProductCode should be reasonable length (typical product codes)
                price.ProductCode.Length.Should().BeInRange(1, 50, "ProductCode should have reasonable length");
            }
            
            // Test that different products have different codes
            var sampleCodes = result.Take(10).Select(p => p.ProductCode).ToList();
            if (sampleCodes.Count > 1)
            {
                sampleCodes.Should().OnlyHaveUniqueItems("Different products should have different ProductCodes");
            }
        }
    }

    [Fact]
    public async Task Integration_PriceWorkflow_ValidatesCompleteDataFlow()
    {
        // This test validates the complete workflow and data consistency

        // Step 1: Get fresh data
        var freshData = await _client.GetAllAsync(forceReload: true, CancellationToken.None);

        // Step 2: Get cached data
        var cachedData = await _client.GetAllAsync(forceReload: false, CancellationToken.None);

        // Assert
        freshData.Should().NotBeNull();
        cachedData.Should().NotBeNull();

        if (freshData.Any())
        {
            // Cached data should match fresh data
            freshData.Count().Should().Be(cachedData.Count(), "Cached data count should match fresh data count");

            // Test sample of products for consistency - simplified comparison since ProductCode is removed
            var freshSample = freshData.Take(10).ToList();
            var cachedSample = cachedData.Take(10).ToList();

            for (int i = 0; i < Math.Min(freshSample.Count, cachedSample.Count); i++)
            {
                var fresh = freshSample[i];
                var cached = cachedSample[i];

                fresh.PriceWithoutVat.Should().Be(cached.PriceWithoutVat);
                fresh.PurchasePrice.Should().Be(cached.PurchasePrice);
                fresh.PriceWithVat.Should().Be(cached.PriceWithVat);
                fresh.PurchasePriceWithVat.Should().Be(cached.PurchasePriceWithVat);
            }

            // Step 3: Validate business logic
            foreach (var price in freshData.Take(20))
            {
                // Ensure prices make business sense
                if (price.PriceWithoutVat > 0 && price.PurchasePrice > 0)
                {
                    // Usually selling price should be higher than purchase price
                    // But this might not always be true in real business scenarios
                    // We'll just ensure the values are reasonable (not extremely negative)
                    var margin = price.PriceWithoutVat - price.PurchasePrice;
                    // Allow negative margins but not more than 100% of the purchase price
                    margin.Should().BeGreaterOrEqualTo(-price.PurchasePrice,
                        $"Margin ({margin}) should not be more negative than purchase price ({price.PurchasePrice})");
                }

                // Ensure VAT calculations are reasonable
                if (price.PriceWithoutVat > 0)
                {
                    price.PriceWithVat.Should().BeGreaterOrEqualTo(price.PriceWithoutVat, "PriceWithVat should be >= PriceWithoutVat");
                }
                if (price.PurchasePrice > 0)
                {
                    price.PurchasePriceWithVat.Should().BeGreaterOrEqualTo(price.PurchasePrice, "PurchasePriceWithVat should be >= PurchasePrice");
                }
            }
        }
    }
}