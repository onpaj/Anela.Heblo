using System.Globalization;
using System.Text;
using Anela.Heblo.Adapters.Shoptet.Price;
using Anela.Heblo.Domain.Features.Catalog.Price;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Unit;

public class ShoptetPriceClientTests
{
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<IOptions<ProductPriceOptions>> _mockOptions;
    private readonly ShoptetPriceClient _client;

    public ShoptetPriceClientTests()
    {
        // Register encoding provider for windows-1250
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
        _mockOptions = new Mock<IOptions<ProductPriceOptions>>();

        _mockOptions.Setup(o => o.Value).Returns(new ProductPriceOptions
        {
            ProductExportUrl = "https://test.com/export.csv"
        });

        _client = new ShoptetPriceClient(_httpClient, _mockOptions.Object);
    }

    [Fact]
    public async Task GetAllAsync_WithValidCsvData_CalculatesPriceWithoutVatCorrectly()
    {
        // Arrange
        var csvData = "CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n" +
                     "PROD001;PAIR001;Product 1;120,00;80,00;true;20\n" +
                     "PROD002;PAIR002;Product 2;100,00;60,00;false;20\n";

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(CancellationToken.None);
        var products = result.ToList();

        // Assert
        products.Should().HaveCount(2);

        // Product with VAT included - should calculate price without VAT
        var product1 = products.First(p => p.ProductCode == "PROD001");
        product1.PriceWithVat.Should().Be(120.00m);
        product1.PriceWithoutVat.Should().BeApproximately(100.00m, 0.01m); // 120 / 1.20 = 100

        // Product with VAT not included - should add VAT to get price with VAT
        var product2 = products.First(p => p.ProductCode == "PROD002");
        product2.PriceWithVat.Should().Be(120.00m); // 100 * 1.20 = 120
        product2.PriceWithoutVat.Should().Be(100.00m);
    }

    [Fact]
    public async Task GetAllAsync_WithMissingPrice_HandlesGracefully()
    {
        // Arrange
        var csvData = "CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n" +
                     "PROD001;PAIR001;Product 1;;80,00;true;20\n" +
                     "PROD002;PAIR002;Product 2;100,00;60,00;false;20\n";

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(CancellationToken.None);
        var products = result.ToList();

        // Assert
        products.Should().HaveCount(2);

        var productWithoutPrice = products.First(p => p.ProductCode == "PROD001");
        productWithoutPrice.PriceWithVat.Should().BeNull();
        productWithoutPrice.PriceWithoutVat.Should().BeNull();
        productWithoutPrice.PurchasePrice.Should().Be(80.00m);

        var productWithPrice = products.First(p => p.ProductCode == "PROD002");
        productWithPrice.PriceWithVat.Should().Be(120.00m);
        productWithPrice.PriceWithoutVat.Should().Be(100.00m);
    }

    [Fact]
    public async Task GetAllAsync_WithMissingVatPercent_DoesNotCalculatePriceWithoutVat()
    {
        // Arrange
        var csvData = "CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n" +
                     "PROD001;PAIR001;Product 1;120,00;80,00;true;\n" +
                     "PROD002;PAIR002;Product 2;100,00;60,00;false;\n";

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(CancellationToken.None);
        var products = result.ToList();

        // Assert
        products.Should().HaveCount(2);

        foreach (var product in products)
        {
            product.PriceWithoutVat.Should().BeNull("Price without VAT should not be calculated when VAT percent is missing");
            product.PriceWithVat.Should().NotBeNull("Original price with VAT should still be preserved");
        }
    }

    [Fact]
    public async Task GetAllAsync_WithInvalidVatPercent_DoesNotCalculatePriceWithoutVat()
    {
        // Arrange
        var csvData = "CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n" +
                     "PROD001;PAIR001;Product 1;120,00;80,00;true;INVALID\n" +
                     "PROD002;PAIR002;Product 2;100,00;60,00;false;-5\n";

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(CancellationToken.None);
        var products = result.ToList();

        // Assert
        products.Should().HaveCount(2);

        foreach (var product in products)
        {
            product.PriceWithoutVat.Should().BeNull("Price without VAT should not be calculated with invalid VAT percent");
            product.PriceWithVat.Should().NotBeNull("Original price with VAT should still be preserved");
        }
    }

    [Fact]
    public async Task GetAllAsync_WithMissingIncludingVatFlag_DoesNotCalculatePriceWithoutVat()
    {
        // Arrange
        var csvData = "CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n" +
                     "PROD001;PAIR001;Product 1;120,00;80,00;;20\n" +
                     "PROD002;PAIR002;Product 2;100,00;60,00;INVALID;20\n";

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(CancellationToken.None);
        var products = result.ToList();

        // Assert
        products.Should().HaveCount(2);

        foreach (var product in products)
        {
            product.PriceWithoutVat.Should().BeNull("Price without VAT should not be calculated when including VAT flag is missing or invalid");
            product.PriceWithVat.Should().NotBeNull("Original price with VAT should still be preserved");
        }
    }

    [Fact]
    public async Task GetAllAsync_WithZeroVatPercent_CalculatesCorrectly()
    {
        // Arrange
        var csvData = "CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n" +
                     "PROD001;PAIR001;Product 1;100,00;80,00;true;0\n" +
                     "PROD002;PAIR002;Product 2;100,00;60,00;false;0\n";

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(CancellationToken.None);
        var products = result.ToList();

        // Assert
        products.Should().HaveCount(2);

        foreach (var product in products)
        {
            product.PriceWithVat.Should().Be(100.00m);
            product.PriceWithoutVat.Should().Be(100.00m, "With 0% VAT, price with and without VAT should be the same");
        }
    }

    [Fact]
    public async Task GetAllAsync_WithHighVatPercent_CalculatesCorrectly()
    {
        // Arrange
        var csvData = "CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n" +
                     "PROD001;PAIR001;Product 1;121,00;80,00;true;21\n" +
                     "PROD002;PAIR002;Product 2;100,00;60,00;false;21\n";

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(CancellationToken.None);
        var products = result.ToList();

        // Assert
        products.Should().HaveCount(2);

        // Product with VAT included
        var product1 = products.First(p => p.ProductCode == "PROD001");
        product1.PriceWithVat.Should().Be(121.00m);
        product1.PriceWithoutVat.Should().BeApproximately(100.00m, 0.01m); // 121 / 1.21 = 100

        // Product with VAT not included
        var product2 = products.First(p => p.ProductCode == "PROD002");
        product2.PriceWithVat.Should().Be(121.00m); // 100 * 1.21 = 121
        product2.PriceWithoutVat.Should().Be(100.00m);
    }

    [Fact]
    public async Task GetAllAsync_WithDecimalCommaFormat_ParsesCorrectly()
    {
        // Arrange
        var csvData = "CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n" +
                     "PROD001;PAIR001;Product 1;1234,56;789,12;true;20\n";

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(CancellationToken.None);
        var products = result.ToList();

        // Assert
        products.Should().HaveCount(1);

        var product = products.First();
        product.PriceWithVat.Should().Be(1234.56m);
        product.PurchasePrice.Should().Be(789.12m);
        product.PriceWithoutVat.Should().BeApproximately(1028.80m, 0.01m); // 1234.56 / 1.20
    }

    [Fact]
    public async Task GetAllAsync_WithInvalidPriceFormat_HandlesGracefully()
    {
        // Arrange
        var csvData = "CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n" +
                     "PROD001;PAIR001;Product 1;INVALID;789,12;true;20\n" +
                     "PROD002;PAIR002;Product 2;123,45;INVALID;false;20\n";

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(CancellationToken.None);
        var products = result.ToList();

        // Assert
        products.Should().HaveCount(2);

        var product1 = products.First(p => p.ProductCode == "PROD001");
        product1.PriceWithVat.Should().BeNull();
        product1.PriceWithoutVat.Should().BeNull();
        product1.PurchasePrice.Should().Be(789.12m);

        var product2 = products.First(p => p.ProductCode == "PROD002");
        product2.PriceWithVat.Should().Be(148.14m); // 123.45 * 1.20
        product2.PriceWithoutVat.Should().Be(123.45m);
        product2.PurchasePrice.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_WithEmptyProductCode_HandlesGracefully()
    {
        // Arrange
        var csvData = "CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n" +
                     ";PAIR001;Product 1;120,00;80,00;true;20\n" +
                     "PROD002;PAIR002;Product 2;100,00;60,00;false;20\n";

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(CancellationToken.None);
        var products = result.ToList();

        // Assert
        products.Should().HaveCount(2);

        var productWithEmptyCode = products.First(p => string.IsNullOrEmpty(p.ProductCode));
        productWithEmptyCode.ProductCode.Should().BeEmpty();
        productWithEmptyCode.PriceWithVat.Should().Be(120.00m);
        productWithEmptyCode.PriceWithoutVat.Should().BeApproximately(100.00m, 0.01m);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("FALSE", false)]
    public async Task GetAllAsync_WithDifferentBooleanFormats_ParsesCorrectly(string boolValue, bool expected)
    {
        // Arrange
        var csvData = $"CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n" +
                     $"PROD001;PAIR001;Product 1;120,00;80,00;{boolValue};20\n";

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(CancellationToken.None);
        var products = result.ToList();

        // Assert
        products.Should().HaveCount(1);

        var product = products.First();
        if (expected)
        {
            // VAT included - should calculate price without VAT
            product.PriceWithVat.Should().Be(120.00m);
            product.PriceWithoutVat.Should().BeApproximately(100.00m, 0.01m);
        }
        else
        {
            // VAT not included - should add VAT
            product.PriceWithVat.Should().Be(144.00m); // 120 * 1.20
            product.PriceWithoutVat.Should().Be(120.00m);
        }
    }

    private void SetupHttpResponse(string csvContent)
    {
        var responseMessage = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(csvContent, Encoding.GetEncoding("windows-1250"))
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(responseMessage);
    }

    [Fact]
    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}