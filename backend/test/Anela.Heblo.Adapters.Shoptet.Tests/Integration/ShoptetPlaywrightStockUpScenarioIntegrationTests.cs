using Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;
using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Integration;

[Collection("ShoptetIntegration")]
[Trait("Category", "Integration")]
[Trait("Category", "Playwright")]
public class ShoptetPlaywrightStockUpScenarioIntegrationTests
{
    private readonly ShoptetIntegrationTestFixture _fixture;
    private readonly IConfiguration _configuration;
    private readonly StockUpScenario _stockUpScenario;
    private readonly ITestOutputHelper _output;

    public ShoptetPlaywrightStockUpScenarioIntegrationTests(
        ShoptetIntegrationTestFixture fixture,
        ITestOutputHelper output)
    {
        _fixture = fixture;
        _configuration = fixture.Configuration;
        _stockUpScenario = fixture.ServiceProvider.GetRequiredService<StockUpScenario>();
        _output = output;
    }

    [Fact(Skip = "Manual only")]
    public async Task RunAsync_WithBasicProductStockUp_ExecutesSuccessfully()
    {
        // Arrange - Skip if configuration is not available or contains test placeholders
        HasValidConfiguration().Should().BeTrue("Shoptet credentials not configured or contain test placeholders");

        var stockUpId = $"TEST-{DateTime.Now:yyyyMMdd-HHmmss}";
        var request = new StockUpRequest
        {
            StockUpId = stockUpId,
            Products = new List<StockUpProductRequest>
            {
                // Three products with quantity -1 (stock decrease)
                new StockUpProductRequest { ProductCode = "TEST_PRODUKT1", Amount = -1 },
                new StockUpProductRequest { ProductCode = "TEST_PRODUKT2", Amount = -1 },
                
                // One product with quantity +2 (stock increase)
                new StockUpProductRequest { ProductCode = "TEST_PRODUKT3", Amount = 2 }
            }
        };

        _output.WriteLine($"Testing StockUpScenario with ID: {stockUpId}");
        _output.WriteLine($"Products to process: {request.Products.Count}");

        // Act
        var result = await _stockUpScenario.RunAsync(request);

        // Assert
        result.Should().NotBeNull("StockUpScenario should return a result");
        result.Should().BeOfType<StockUpRecord>("Should return StockUpRecord type");

        _output.WriteLine("✅ StockUpScenario executed successfully");
        _output.WriteLine($"✅ Processed {request.Products.Count} products:");
        foreach (var product in request.Products)
        {
            _output.WriteLine($"   - {product.ProductCode}: {product.Amount:+0;-#}");
        }
    }


    [Fact(Skip = "Manual only")]
    public async Task RunAsync_WithSingleProduct_ProcessesCorrectly()
    {
        // Arrange - Skip if configuration is not available
        HasValidConfiguration().Should().BeTrue("Shoptet credentials not configured or contain test placeholders");

        var stockUpId = $"SINGLE-{DateTime.Now:yyyyMMdd-HHmmss}";
        var request = new StockUpRequest
        {
            StockUpId = stockUpId,
            Products = new List<StockUpProductRequest>
            {
                new StockUpProductRequest { ProductCode = "TEST_PRODUKT1", Amount = 10 }
            }
        };

        _output.WriteLine($"Testing single product scenario with ID: {stockUpId}");

        // Act
        var result = await _stockUpScenario.RunAsync(request);

        // Assert
        result.Should().NotBeNull("Should handle single product correctly");
        result.Should().BeOfType<StockUpRecord>();

        _output.WriteLine("✅ Single product scenario completed successfully");
    }

    [Fact(Skip = "Manual only")]
    public async Task RunAsync_WithEmptyProductList_HandlesGracefully()
    {
        // Arrange - Skip if configuration is not available
        HasValidConfiguration().Should().BeTrue("Shoptet credentials not configured or contain test placeholders");

        var stockUpId = $"EMPTY-{DateTime.Now:yyyyMMdd-HHmmss}";
        var request = new StockUpRequest
        {
            StockUpId = stockUpId,
            Products = new List<StockUpProductRequest>() // Empty list
        };

        _output.WriteLine($"Testing empty product list scenario with ID: {stockUpId}");

        // Act
        var result = await _stockUpScenario.RunAsync(request);

        // Assert
        result.Should().NotBeNull("Should handle empty product list gracefully");
        result.Should().BeOfType<StockUpRecord>();

        _output.WriteLine("✅ Empty product list scenario completed successfully");
    }

    private bool HasValidConfiguration()
    {
        var url = _configuration["ShoptetPlaywright:ShopEntryUrl"];
        var username = _configuration["ShoptetPlaywright:Login"];
        var password = _configuration["ShoptetPlaywright:Password"];

        return !string.IsNullOrEmpty(url) &&
               !string.IsNullOrEmpty(username) &&
               !string.IsNullOrEmpty(password) &&
               !url.Contains("your-shoptet") &&
               !username.Contains("test-username") &&
               !password.Contains("test-password");
    }
}