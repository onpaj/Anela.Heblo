using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Integration;

[Collection("ShoptetIntegration")]
[Trait("Category", "Integration")]
public class ShoptetStockClientIntegrationTests
{
    private readonly ShoptetIntegrationTestFixture _fixture;
    private readonly IConfiguration _configuration;
    private readonly IEshopStockClient _stockClient;

    public ShoptetStockClientIntegrationTests(ShoptetIntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _configuration = fixture.Configuration;
        _stockClient = fixture.ServiceProvider.GetRequiredService<IEshopStockClient>();
    }

    [Fact]
    public async Task ListAsync_WithValidConfiguration_ReturnsStockData()
    {
        // Arrange - Skip if configuration is not available
        var url = _configuration["StockClient:Url"];
        (string.IsNullOrEmpty(url) || url.Contains("your-shoptet")).Should().BeFalse(
            "Shoptet stock URL is not configured or contains placeholder. Please set a valid URL in appsettings.json");


        var cancellationToken = new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

        // Act
        var result = await _stockClient.ListAsync(cancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeOfType<List<EshopStock>>();

        if (result.Any())
        {
            // Validate structure of returned stock items
            result.Should().OnlyContain(stock =>
                !string.IsNullOrEmpty(stock.Code) &&
                stock.Stock >= 0
            );

            // Check that CSV mapping worked correctly
            var firstStock = result.First();
            firstStock.Code.Should().NotBeNullOrEmpty("Product code should be parsed from CSV");
            firstStock.Name.Should().NotBeNullOrEmpty("Product name should be parsed from CSV");
            firstStock.Stock.Should().BeGreaterOrEqualTo(0, "Stock level should be non-negative");

            // Log some statistics for debugging
            var totalProducts = result.Count;
            var productsInStock = result.Count(s => s.Stock > 0);
            var averageStock = result.Where(s => s.Stock > 0).Select(s => s.Stock).DefaultIfEmpty(0).Average();

            // These are informational - not assertions
            totalProducts.Should().BeGreaterThan(0, "Should have at least some products");
        }
    }

    [Fact]
    public async Task ListAsync_WithValidConfiguration_ParsesCsvDataCorrectly()
    {
        // Arrange - Skip if configuration is not available
        var url = _configuration["StockClient:Url"];
        (string.IsNullOrEmpty(url) || url.Contains("your-shoptet")).Should().BeFalse(
            "Shoptet stock URL is not configured or contains placeholder. Please set a valid URL in appsettings.json");

        var cancellationToken = new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

        // Act
        var result = await _stockClient.ListAsync(cancellationToken);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            // Test CSV column mapping is working correctly
            foreach (var stock in result.Take(5)) // Check first 5 items
            {
                // Code should be non-empty (column 0)
                stock.Code.Should().NotBeNullOrEmpty("Code should be mapped from column 0");

                // PairCode can be empty but should not be null (column 1)
                stock.PairCode.Should().NotBeNull("PairCode should be mapped from column 1");

                // Name should be non-empty (column 2)
                stock.Name.Should().NotBeNullOrEmpty("Name should be mapped from column 2");

                // Stock should be a valid decimal (column 17)
                stock.Stock.Should().BeGreaterOrEqualTo(0, "Stock should be non-negative decimal from column 17");

                // NameSuffix can be empty but should not be null (column 7)
                stock.NameSuffix.Should().NotBeNull("NameSuffix should be mapped from column 7");

                // Location can be empty but should not be null (column 17 - same as stock?)
                stock.Location.Should().NotBeNull("Location should be mapped from column 17");
            }
        }
    }

    [Fact]
    public async Task ListAsync_WithCancellationToken_RespectsCancellation()
    {
        // Arrange - Skip if configuration is not available
        var url = _configuration["StockClient:Url"];
        (string.IsNullOrEmpty(url) || url.Contains("your-shoptet")).Should().BeFalse(
            "Shoptet stock URL is not configured or contains placeholder. Please set a valid URL in appsettings.json");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1)); // Very short timeout
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        var act = async () => await _stockClient.ListAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ListAsync_WithValidConfiguration_HandlesEmptyStock()
    {
        // Arrange - Skip if configuration is not available
        var url = _configuration["StockClient:Url"];
        if (string.IsNullOrEmpty(url) || url.Contains("your-shoptet"))
        {
            return;
        }

        var cancellationToken = new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

        // Act
        var result = await _stockClient.ListAsync(cancellationToken);

        // Assert
        result.Should().NotBeNull();

        // Even if no products, the CSV structure should be parsed correctly
        // Each product should have valid decimal stock values (including 0)
        result.Should().OnlyContain(stock =>
            stock.Stock >= 0 &&
            !string.IsNullOrEmpty(stock.Code)
        );

        // Verify that default value mapping works for empty stock values
        var productsWithZeroStock = result.Where(s => s.Stock == 0).ToList();
        productsWithZeroStock.Should().OnlyContain(stock =>
            !string.IsNullOrEmpty(stock.Code) &&
            stock.Stock == 0
        );
    }

    [Fact]
    public async Task ListAsync_WithValidConfiguration_HandlesCsvEncoding()
    {
        // Arrange - Skip if configuration is not available
        var url = _configuration["StockClient:Url"];
        (string.IsNullOrEmpty(url) || url.Contains("your-shoptet")).Should().BeFalse(
            "Shoptet stock URL is not configured or contains placeholder. Please set a valid URL in appsettings.json");

        var cancellationToken = new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

        // Act
        var result = await _stockClient.ListAsync(cancellationToken);

        // Assert
        result.Should().NotBeNull();

        if (result.Any())
        {
            // Check that Czech characters are properly decoded (windows-1250 encoding)
            var productsWithCzechChars = result.Where(p =>
                p.Name.Contains('ř') || p.Name.Contains('š') || p.Name.Contains('č') ||
                p.Name.Contains('ž') || p.Name.Contains('ý') || p.Name.Contains('á') ||
                p.Name.Contains('í') || p.Name.Contains('é') || p.Name.Contains('ů')
            ).ToList();

            // If there are Czech characters, they should be properly decoded
            foreach (var product in productsWithCzechChars.Take(3))
            {
                product.Name.Should().NotContain("?", "Czech characters should be properly decoded from windows-1250");
                product.Name.Should().NotContain("�", "Czech characters should not contain replacement characters");
            }
        }
    }
}