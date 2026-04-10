using System.Net;
using System.Text;
using Anela.Heblo.Adapters.Shoptet.Price;
using Anela.Heblo.Domain.Features.Catalog.Price;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Unit;

public class ShoptetPriceClientHttp500Tests
{
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<IOptions<ProductPriceOptions>> _mockOptions;
    private readonly ShoptetPriceClient _client;

    public ShoptetPriceClientHttp500Tests()
    {
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

    private void SetupHttpResponse(HttpStatusCode statusCode, string body)
    {
        var responseMessage = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8)
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
    public async Task GetAllAsync_WhenServerReturns500_ThrowsHttpRequestException()
    {
        // Arrange – simulate Shoptet returning an HTML error page with HTTP 500
        var htmlErrorBody = "<html><body><h1>Internal Server Error</h1></body></html>";
        SetupHttpResponse(HttpStatusCode.InternalServerError, htmlErrorBody);

        // Act
        Func<Task> act = async () => await _client.GetAllAsync(CancellationToken.None);

        // Assert – HttpRequestException must be thrown, NOT CsvHelper.MissingFieldException
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetAllAsync_WhenServerReturns503_ThrowsHttpRequestException()
    {
        // Arrange
        var htmlErrorBody = "<html><body><h1>Service Unavailable</h1></body></html>";
        SetupHttpResponse(HttpStatusCode.ServiceUnavailable, htmlErrorBody);

        // Act
        Func<Task> act = async () => await _client.GetAllAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetAllAsync_WhenServerReturns404_ThrowsHttpRequestException()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.NotFound, "Not Found");

        // Act
        Func<Task> act = async () => await _client.GetAllAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetAllAsync_WhenServerReturns200_DoesNotThrowHttpRequestException()
    {
        // Arrange – valid CSV response
        var csvData = "CODE;PAIR;NAME;PRICE;PURCHASE;INCLUDING_VAT;PERCENT_VAT\n" +
                      "PROD001;PAIR001;Product 1;120,00;80,00;true;20\n";

        SetupHttpResponse(HttpStatusCode.OK, csvData);

        // Act
        Func<Task> act = async () => await _client.GetAllAsync(CancellationToken.None);

        // Assert – no exception for a successful response
        await act.Should().NotThrowAsync<HttpRequestException>();
    }
}
