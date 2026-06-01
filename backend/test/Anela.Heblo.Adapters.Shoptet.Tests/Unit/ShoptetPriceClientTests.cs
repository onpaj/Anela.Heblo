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
                     "PROD002;PAIR002;Product 2;100,00;60,00;false;21\n";

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(CancellationToken.None);
        var products = result.ToList();

        // Assert
        products.Should().HaveCount(2);

        // All products have PriceWithoutVat calculated from PriceWithVat / (1 + VatRate)
        var product1 = products.First(p => p.ProductCode == "PROD001");
        product1.PriceWithVat.Should().Be(120.00m);
        product1.PriceWithoutVat.Should().BeApproximately(100.00m, 0.01m); // 120 / 1.20 = 100
        product1.PurchasePrice.Should().Be(80.00m);

        var product2 = products.First(p => p.ProductCode == "PROD002");
        product2.PriceWithVat.Should().Be(100.00m);
        product2.PriceWithoutVat.Should().BeApproximately(82.64m, 0.01m); // 100 / 1.21 ≈ 82.64
        product2.PurchasePrice.Should().Be(60.00m);
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

        // All products calculate PriceWithoutVat from PriceWithVat using VAT percentage
        var product1 = products.First(p => p.ProductCode == "PROD001");
        product1.PriceWithVat.Should().Be(121.00m);
        product1.PriceWithoutVat.Should().BeApproximately(100.00m, 0.01m); // 121 / 1.21 = 100
        product1.PurchasePrice.Should().Be(80.00m);

        var product2 = products.First(p => p.ProductCode == "PROD002");
        product2.PriceWithVat.Should().Be(100.00m);
        product2.PriceWithoutVat.Should().BeApproximately(82.64m, 0.01m); // 100 / 1.21 ≈ 82.64
        product2.PurchasePrice.Should().Be(60.00m);
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




    [Theory]
    [InlineData(15)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(25)]
    public async Task GetAllAsync_WithDifferentVatPercentages_CalculatesCorrectly(decimal vatPercent)
    {
        // Arrange
        var csvData = $"CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n" +
                     $"PROD001;PAIR001;Product 1;121,00;80,00;true;{vatPercent.ToString(CultureInfo.InvariantCulture)}\n";

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(CancellationToken.None);
        var products = result.ToList();

        // Assert
        products.Should().HaveCount(1);

        var product = products.First();
        product.PriceWithVat.Should().Be(121.00m);

        // Calculate expected price without VAT: PriceWithVat / (1 + VatRate)
        var expectedPriceWithoutVat = 121.00m / (1 + vatPercent / 100);
        product.PriceWithoutVat.Should().BeApproximately(expectedPriceWithoutVat, 0.01m);
        product.PurchasePrice.Should().Be(80.00m);
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
    public async Task GetAllAsync_WithNullPrices_HandlesGracefully()
    {
        // Arrange
        var csvData = "CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n" +
                     "PROD001;PAIR001;Product 1;;80,00;true;20\n" +
                     "PROD002;PAIR002;Product 2;100,00;;false;21\n";

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(CancellationToken.None);
        var products = result.ToList();

        // Assert
        products.Should().HaveCount(2);

        var product1 = products.First(p => p.ProductCode == "PROD001");
        product1.PriceWithVat.Should().BeNull();
        product1.PriceWithoutVat.Should().Be(0); // Fallback when PriceWithVat is null
        product1.PurchasePrice.Should().Be(80.00m);

        var product2 = products.First(p => p.ProductCode == "PROD002");
        product2.PriceWithVat.Should().Be(100.00m);
        product2.PriceWithoutVat.Should().BeApproximately(82.64m, 0.01m); // 100 / 1.21
        product2.PurchasePrice.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_WithNullVatPercent_UsesDefaultVat21()
    {
        // Arrange
        var csvData = "CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n" +
                     "PROD001;PAIR001;Product 1;121,00;80,00;true;\n";

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(CancellationToken.None);
        var products = result.ToList();

        // Assert
        products.Should().HaveCount(1);

        var product = products.First();
        product.PriceWithVat.Should().Be(121.00m);
        // Should use default VAT 21% when PercentVat is null
        product.PriceWithoutVat.Should().BeApproximately(100.00m, 0.01m); // 121 / 1.21 = 100
    }

    [Fact]
    public async Task GetAllAsync_WithZeroVatPercent_HandlesCorrectly()
    {
        // Arrange
        var csvData = "CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n" +
                     "PROD001;PAIR001;Product 1;100,00;80,00;true;0\n";

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(CancellationToken.None);
        var products = result.ToList();

        // Assert
        products.Should().HaveCount(1);

        var product = products.First();
        product.PriceWithVat.Should().Be(100.00m);
        // With 0% VAT, PriceWithoutVat should equal PriceWithVat
        product.PriceWithoutVat.Should().Be(100.00m);
    }

    [Fact]
    public async Task GetAllAsync_WithEmptyResponse_ReturnsEmptyList()
    {
        // Arrange
        var csvData = "CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n";

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(CancellationToken.None);
        var products = result.ToList();

        // Assert
        products.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_WithSpecialCharactersInProductCode_HandlesCorrectly()
    {
        // Arrange
        var csvData = "CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n" +
                     "PROD-001_TEST;PAIR001;Product with special chars;120,50;80,00;true;20\n";

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(CancellationToken.None);
        var products = result.ToList();

        // Assert
        products.Should().HaveCount(1);

        var product = products.First();
        product.ProductCode.Should().Be("PROD-001_TEST");
        product.PriceWithVat.Should().Be(120.50m);
        product.PriceWithoutVat.Should().BeApproximately(100.42m, 0.01m); // 120.50 / 1.20
    }

    [Theory]
    [InlineData("1,50", 1.50)]
    [InlineData("1.50", 1.50)]
    [InlineData("1234,56", 1234.56)]
    [InlineData("0,99", 0.99)]
    [InlineData("0", 0)]
    public async Task GetAllAsync_WithDifferentDecimalFormats_ParsesCorrectly(string priceString, decimal expectedPrice)
    {
        // Arrange
        var csvData = $"CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n" +
                     $"PROD001;PAIR001;Product 1;{priceString};80,00;true;20\n";

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(CancellationToken.None);
        var products = result.ToList();

        // Assert
        products.Should().HaveCount(1);

        var product = products.First();
        product.PriceWithVat.Should().Be(expectedPrice);

        var expectedPriceWithoutVat = expectedPrice > 0 ? expectedPrice / 1.20m : 0;
        product.PriceWithoutVat.Should().BeApproximately(expectedPriceWithoutVat, 0.01m);
    }

    [Fact]
    public async Task GetAllAsync_WithCancellationToken_PassesToHttpClient()
    {
        // Arrange
        var csvData = "CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n";
        var cancellationToken = new CancellationToken();

        SetupHttpResponse(csvData);

        // Act
        var result = await _client.GetAllAsync(cancellationToken);

        // Assert
        _mockHttpMessageHandler.Protected()
            .Verify("SendAsync", Times.Once(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SetAllAsync_WithValidData_CreatesCorrectCsvFile()
    {
        // Arrange
        var testData = new List<ProductPriceEshop>
        {
            new ProductPriceEshop
            {
                ProductCode = "PROD001",
                PriceWithVat = 120.00m,
                PriceWithoutVat = 100.00m,
                PurchasePrice = 80.00m
            },
            new ProductPriceEshop
            {
                ProductCode = "PROD002",
                PriceWithVat = 100.00m,
                PriceWithoutVat = 82.64m,
                PurchasePrice = 60.00m
            }
        };

        // Act
        var result = await _client.SetAllAsync(testData, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.FilePath.Should().NotBeNullOrEmpty();
        result.Data.Should().NotBeNullOrEmpty();

        // Verify file exists
        File.Exists(result.FilePath).Should().BeTrue();

        // Verify CSV content
        var csvContent = Encoding.UTF8.GetString(result.Data);
        csvContent.Should().Contain("PROD001");
        csvContent.Should().Contain("PROD002");
        csvContent.Should().Contain("120");
        csvContent.Should().Contain("100");
        csvContent.Should().Contain("80");
        csvContent.Should().Contain("60");

        // Cleanup
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
        var testData = new List<ProductPriceEshop>();

        // Act
        var result = await _client.SetAllAsync(testData, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.FilePath.Should().NotBeNullOrEmpty();
        result.Data.Should().NotBeNullOrEmpty();

        // Verify file exists and contains only header
        File.Exists(result.FilePath).Should().BeTrue();

        var csvContent = Encoding.UTF8.GetString(result.Data);
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().Be(1); // Only header line

        // Cleanup
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
    public async Task SetAllAsync_WithSpecialCharacters_HandlesCorrectly()
    {
        // Arrange
        var testData = new List<ProductPriceEshop>
        {
            new ProductPriceEshop
            {
                ProductCode = "PROD-001_TEST",
                PriceWithVat = 1234.56m,
                PriceWithoutVat = 1028.80m,
                PurchasePrice = 789.12m
            }
        };

        // Act
        var result = await _client.SetAllAsync(testData, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.FilePath.Should().NotBeNullOrEmpty();

        var csvContent = Encoding.UTF8.GetString(result.Data);
        csvContent.Should().Contain("PROD-001_TEST");
        csvContent.Should().Contain("1234.56");
        csvContent.Should().Contain("1028.80");
        csvContent.Should().Contain("789.12");

        // Cleanup
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
    public async Task SetAllAsync_WithNullPrices_HandlesCorrectly()
    {
        // Arrange
        var testData = new List<ProductPriceEshop>
        {
            new ProductPriceEshop
            {
                ProductCode = "PROD001",
                PriceWithVat = null,
                PriceWithoutVat = 100.00m,
                PurchasePrice = null
            }
        };

        // Act
        var result = await _client.SetAllAsync(testData, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.FilePath.Should().NotBeNullOrEmpty();

        var csvContent = Encoding.UTF8.GetString(result.Data);
        csvContent.Should().Contain("PROD001");

        // Cleanup
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
    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}