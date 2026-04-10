using System.Net;
using System.Text;
using Anela.Heblo.Adapters.Shoptet.Stock;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Unit;

public class ShoptetStockClientHttp500Tests
{
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<IOptions<ShoptetStockClientOptions>> _mockOptions;
    private readonly ShoptetStockClient _client;

    public ShoptetStockClientHttp500Tests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
        _mockOptions = new Mock<IOptions<ShoptetStockClientOptions>>();

        _mockOptions.Setup(o => o.Value).Returns(new ShoptetStockClientOptions
        {
            Url = "https://test.com/stock-export.csv"
        });

        _client = new ShoptetStockClient(_httpClient, _mockOptions.Object);
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
    public async Task ListAsync_WhenServerReturns500_ThrowsHttpRequestException()
    {
        // Arrange – simulate Shoptet returning an HTML error page with HTTP 500
        var htmlErrorBody = "<html><body><h1>Internal Server Error</h1></body></html>";
        SetupHttpResponse(HttpStatusCode.InternalServerError, htmlErrorBody);

        // Act
        Func<Task> act = async () => await _client.ListAsync(CancellationToken.None);

        // Assert – HttpRequestException must be thrown, NOT CsvHelper.MissingFieldException
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ListAsync_WhenServerReturns503_ThrowsHttpRequestException()
    {
        // Arrange
        var htmlErrorBody = "<html><body><h1>Service Unavailable</h1></body></html>";
        SetupHttpResponse(HttpStatusCode.ServiceUnavailable, htmlErrorBody);

        // Act
        Func<Task> act = async () => await _client.ListAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ListAsync_WhenServerReturns404_ThrowsHttpRequestException()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.NotFound, "Not Found");

        // Act
        Func<Task> act = async () => await _client.ListAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
