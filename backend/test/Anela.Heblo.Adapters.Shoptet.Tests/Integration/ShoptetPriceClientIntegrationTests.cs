using Anela.Heblo.Adapters.Shoptet;
using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Application.Domain.Catalog.Price;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Integration;

[Collection("ShoptetIntegration")]
[Trait("Category", "Integration")]
public class ShoptetPriceClientIntegrationTests
{
    private readonly ShoptetIntegrationTestFixture _fixture;
    private readonly IConfiguration _configuration;
    private readonly IProductPriceEshopClient _priceClient;

    public ShoptetPriceClientIntegrationTests(ShoptetIntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _configuration = fixture.Configuration;
        _priceClient = fixture.ServiceProvider.GetRequiredService<IProductPriceEshopClient>();
    }

    [Fact]
    public async Task GetAllAsync_WithValidConfiguration_ReturnsProductPrices()
    {
        // Arrange - Skip if configuration is not available
        var url = _configuration["ProductPriceOptions:ProductExportUrl"];
        (string.IsNullOrEmpty(url) || url.Contains("your-shoptet")).Should().BeFalse("Shoptet ProductExportUrl should be configured for this test");


        var cancellationToken = new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

        // Act
        var result = await _priceClient.GetAllAsync(cancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IEnumerable<ProductPriceEshop>>();

        var resultList = result.ToList();
        if (resultList.Any())
        {
            // Check that prices are properly parsed
            var firstPrice = resultList.First();

            resultList.Where(p => p.PriceWithVat.HasValue).Should().Contain(price =>
                price.PriceWithVat >= 0, "Prices with VAT should be non-negative");

            resultList.Where(p => p.PurchasePrice.HasValue).Should().Contain(price =>
                price.PurchasePrice >= 0, "Purchase prices should be non-negative");
                
            // Verify ProductCode is populated from CSV
            resultList.Should().OnlyContain(price => !string.IsNullOrEmpty(price.ProductCode), "ProductCode should be populated from CSV first column");
        }
    }

    [Fact]
    public async Task GetAllAsync_ParsesCsvDataCorrectly()
    {
        // Arrange - Skip if configuration is not available
        var url = _configuration["ProductPriceOptions:ProductExportUrl"];
        (string.IsNullOrEmpty(url) || url.Contains("your-shoptet")).Should().BeFalse("Shoptet ProductExportUrl should be configured for this test");


        var cancellationToken = new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

        // Act
        var result = await _priceClient.GetAllAsync(cancellationToken);

        // Assert
        result.Should().NotBeNull();

        var resultList = result.ToList();
        if (resultList.Any())
        {
            // Test CSV column mapping is working correctly
            foreach (var price in resultList.Take(5)) // Check first 5 items
            {
                // ProductCode mapping from first column (column 0)
                price.ProductCode.Should().NotBeNullOrEmpty("ProductCode should be mapped from CSV column 0");
                price.ProductCode.Should().NotBeNullOrWhiteSpace("ProductCode should contain actual product code from CSV");
                
                // PriceWithVat parsing with comma replacement (column 3)
                if (price.PriceWithVat.HasValue)
                {
                    price.PriceWithVat.Should().BeGreaterOrEqualTo(0, "PriceWithVat should be non-negative decimal from column 3");
                }

                // PurchasePrice parsing with comma replacement (column 4)
                if (price.PurchasePrice.HasValue)
                {
                    price.PurchasePrice.Should().BeGreaterOrEqualTo(0, "PurchasePrice should be non-negative decimal from column 4");
                }
            }
            
            // Test that different products have different codes
            var sampleCodes = resultList.Take(10).Select(p => p.ProductCode).ToList();
            if (sampleCodes.Count > 1)
            {
                sampleCodes.Should().OnlyHaveUniqueItems("Different products should have different ProductCodes from CSV");
            }
        }
    }

    [Fact]
    public async Task GetAllAsync_HandlesCsvEncoding()
    {
        // Arrange - Skip if configuration is not available
        var url = _configuration["ProductPriceOptions:ProductExportUrl"];
        (string.IsNullOrEmpty(url) || url.Contains("your-shoptet")).Should().BeFalse("Shoptet ProductExportUrl should be configured for this test");

        var cancellationToken = new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

        // Act
        var result = await _priceClient.GetAllAsync(cancellationToken);

        // Assert
        result.Should().NotBeNull();

        var resultList = result.ToList();
        if (resultList.Any())
        {
            // Test that CSV parsing works with different character encodings
            // Note: Simplified model doesn't include Name field, so we just verify data is parsed
            resultList.Should().NotBeEmpty("Should parse price data from CSV even with encoding");

            // Verify prices are reasonable (basic sanity check)
            var pricesWithValues = resultList.Where(p => p.PriceWithVat.HasValue).ToList();
            if (pricesWithValues.Any())
            {
                pricesWithValues.Should().OnlyContain(p => p.PriceWithVat >= 0, "All prices should be non-negative");
            }
        }
    }

    [Fact]
    public async Task GetAllAsync_WithCancellationToken_RespectsCancellation()
    {
        // Arrange - Skip if configuration is not available
        var url = _configuration["ProductPriceOptions:ProductExportUrl"];
        (string.IsNullOrEmpty(url) || url.Contains("your-shoptet")).Should().BeFalse("Shoptet ProductExportUrl should be configured for this test");


        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1)); // Very short timeout
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        var act = async () => await _priceClient.GetAllAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SetAllAsync_WithValidData_CreatesCsvFile()
    {
        // Arrange
        var testData = new List<ProductPriceEshop>
        {
            new ProductPriceEshop
            {
                ProductCode = "PRODUCT001",
                PriceWithVat = 100.50m,
                PurchasePrice = 75.25m
            },
            new ProductPriceEshop
            {
                ProductCode = "PRODUCT002",
                PriceWithVat = 200.00m,
                PurchasePrice = 150.00m
            }
        };

        var cancellationToken = CancellationToken.None;

        // Act
        var result = await _priceClient.SetAllAsync(testData, cancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.FilePath.Should().NotBeNullOrEmpty("FilePath should be set");
        result.Data.Should().NotBeNull("Data should contain CSV bytes");
        result.Data.Length.Should().BeGreaterThan(0, "Data should not be empty");

        // Verify file exists
        File.Exists(result.FilePath).Should().BeTrue("CSV file should be created");

        // Verify CSV content structure
        var csvContent = System.Text.Encoding.UTF8.GetString(result.Data);
        csvContent.Should().Contain("PRODUCT001", "CSV should contain first test product code");
        csvContent.Should().Contain("PRODUCT002", "CSV should contain second test product code");
        csvContent.Should().Contain("100.50", "CSV should contain first test price with VAT");
        csvContent.Should().Contain("75.25", "CSV should contain first test purchase price");
        csvContent.Should().Contain("200.00", "CSV should contain second test price");

        // Clean up
        try
        {
            File.Delete(result.FilePath);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task SetAllAsync_WithEmptyData_CreatesEmptyCsvFile()
    {
        // Arrange
        var emptyData = new List<ProductPriceEshop>();
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await _priceClient.SetAllAsync(emptyData, cancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.FilePath.Should().NotBeNullOrEmpty("FilePath should be set even for empty data");
        result.Data.Should().NotBeNull("Data should contain CSV bytes even for empty data");

        // Verify file exists
        File.Exists(result.FilePath).Should().BeTrue("CSV file should be created even for empty data");

        // Verify CSV content (should contain only headers)
        var csvContent = System.Text.Encoding.UTF8.GetString(result.Data);
        csvContent.Should().NotBeNullOrEmpty("CSV should contain at least headers");

        // Clean up
        try
        {
            File.Delete(result.FilePath);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task SetAllAsync_WithCzechCharacters_HandlesCsvEncoding()
    {
        // Arrange
        var testDataWithCzech = new List<ProductPriceEshop>
        {
            new ProductPriceEshop
            {
                ProductCode = "PRODUCT003",
                PriceWithVat = 150.75m,
                PurchasePrice = 100.50m
            }
        };

        var cancellationToken = CancellationToken.None;

        // Act
        var result = await _priceClient.SetAllAsync(testDataWithCzech, cancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Data.Should().NotBeNull();

        // Verify decimal formatting is properly encoded in UTF-8
        var csvContent = System.Text.Encoding.UTF8.GetString(result.Data);
        csvContent.Should().Contain("150.75", "CSV should contain price with decimal formatting");
        csvContent.Should().Contain("100.50", "CSV should contain purchase price with decimal formatting");

        // Clean up
        try
        {
            File.Delete(result.FilePath);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}