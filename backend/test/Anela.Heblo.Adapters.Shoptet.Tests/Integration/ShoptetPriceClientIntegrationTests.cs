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
            // Validate structure of returned price items
            resultList.Should().OnlyContain(price =>
                !string.IsNullOrEmpty(price.Code)
            );

            // Check that CSV mapping worked correctly
            var firstPrice = resultList.First();
            firstPrice.Code.Should().NotBeNullOrEmpty("Product code should be parsed from CSV");
            firstPrice.Name.Should().NotBeNullOrEmpty("Product name should be parsed from CSV");

            // Check that original prices are set (business logic requirement)
            if (firstPrice.Price.HasValue)
            {
                firstPrice.OriginalPrice.Should().Be(firstPrice.Price, "OriginalPrice should be set to Price value");
            }

            if (firstPrice.PurchasePrice.HasValue)
            {
                firstPrice.OriginalPurchasePrice.Should().Be(firstPrice.PurchasePrice, "OriginalPurchasePrice should be set to PurchasePrice value");
            }

            // Check price parsing (decimal conversion with comma replacement)
            resultList.Where(p => p.Price.HasValue).Should().OnlyContain(price =>
                price.Price >= 0, "Prices should be non-negative");

            resultList.Where(p => p.PurchasePrice.HasValue).Should().OnlyContain(price =>
                price.PurchasePrice >= 0, "Purchase prices should be non-negative");
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
                // Code should be non-empty (column 0)
                price.Code.Should().NotBeNullOrEmpty("Code should be mapped from column 0");

                // PairCode can be empty but should not be null (column 1)
                price.PairCode.Should().NotBeNull("PairCode should be mapped from column 1");

                // Name should be non-empty (column 2)
                price.Name.Should().NotBeNullOrEmpty("Name should be mapped from column 2");

                // Price parsing with comma replacement (column 3)
                if (price.Price.HasValue)
                {
                    price.Price.Should().BeGreaterOrEqualTo(0, "Price should be non-negative decimal from column 3");
                }

                // PurchasePrice parsing with comma replacement (column 4)
                if (price.PurchasePrice.HasValue)
                {
                    price.PurchasePrice.Should().BeGreaterOrEqualTo(0, "PurchasePrice should be non-negative decimal from column 4");
                }
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
            // Check that Czech characters are properly decoded (windows-1250 encoding)
            var productsWithCzechChars = resultList.Where(p =>
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
                Code = "TEST001",
                PairCode = "PAIR001",
                Name = "Test Product 1",
                Price = 100.50m,
                PurchasePrice = 75.25m,
                OriginalPrice = 90.00m,
                OriginalPurchasePrice = 70.00m
            },
            new ProductPriceEshop
            {
                Code = "TEST002",
                PairCode = "PAIR002",
                Name = "Test Product 2",
                Price = 200.00m,
                PurchasePrice = 150.00m,
                OriginalPrice = 180.00m,
                OriginalPurchasePrice = 140.00m
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
        csvContent.Should().Contain("TEST001", "CSV should contain first test product code");
        csvContent.Should().Contain("TEST002", "CSV should contain second test product code");
        csvContent.Should().Contain("PAIR001", "CSV should contain first test pair code");
        csvContent.Should().Contain("75.25", "CSV should contain first test purchase price");

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
                Code = "ČR001",
                PairCode = "PÁRČR001",
                Name = "Tëst Prõdũkt čšřžýáíéů",
                Price = 150.75m,
                PurchasePrice = 100.50m
            }
        };

        var cancellationToken = CancellationToken.None;

        // Act
        var result = await _priceClient.SetAllAsync(testDataWithCzech, cancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Data.Should().NotBeNull();

        // Verify Czech characters are properly encoded in UTF-8
        var csvContent = System.Text.Encoding.UTF8.GetString(result.Data);
        csvContent.Should().Contain("ČR001", "CSV should contain Czech characters in code");
        csvContent.Should().Contain("PÁRČR001", "CSV should contain Czech characters in pair code");

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