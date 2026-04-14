using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Stock;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class ShoptetStockClientTests
{
    private static ShoptetStockClient BuildClient(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        int stockId = 1)
    {
        var http = new HttpClient(new FakeDelegatingHandler(handler))
        {
            BaseAddress = new Uri("https://fake.shoptet.cz"),
        };
        var settings = Options.Create(new ShoptetApiSettings { StockId = stockId });
        var stockClientOptions = Options.Create(new ShoptetStockClientOptions());
        var httpClientFactory = new Mock<IHttpClientFactory>().Object;
        return new ShoptetStockClient(http, httpClientFactory, settings, stockClientOptions);
    }

    private static HttpResponseMessage Json(object obj, HttpStatusCode status = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(obj,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public async Task UpdateStockAsync_SuccessWithNoErrors_DoesNotThrow()
    {
        var client = BuildClient(_ => Json(new { data = (object?)null, errors = (object?)null }));
        await client.Invoking(c => c.UpdateStockAsync("AKL001", 5))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpdateStockAsync_SuccessWithEmptyErrorsArray_DoesNotThrow()
    {
        var client = BuildClient(_ => Json(new { data = (object?)null, errors = Array.Empty<object>() }));
        await client.Invoking(c => c.UpdateStockAsync("AKL001", 5))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpdateStockAsync_ResponseContainsErrors_ThrowsHttpRequestException()
    {
        var client = BuildClient(_ => Json(new
        {
            data = (object?)null,
            errors = new[]
            {
                new { errorCode = "unknown-product", message = "Product \"AKL001\" does not exist. Skipped.", instance = "AKL001" },
            },
        }));

        var act = () => client.UpdateStockAsync("AKL001", 5);
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*AKL001*unknown-product*");
    }

    [Fact]
    public async Task UpdateStockAsync_Http400_ThrowsHttpRequestException()
    {
        var client = BuildClient(_ => Json(new
        {
            data = (object?)null,
            errors = new[]
            {
                new { errorCode = "stock-change-not-allowed", message = "Stock change not allowed for product set.", instance = "SET001" },
            },
        }, HttpStatusCode.BadRequest));

        var act = () => client.UpdateStockAsync("SET001", 10);
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*400*");
    }

    [Fact]
    public async Task UpdateStockAsync_UsesCorrectUrlWithStockId()
    {
        HttpRequestMessage? captured = null;
        var client = BuildClient(req =>
        {
            captured = req;
            return Json(new { data = (object?)null, errors = (object?)null });
        }, stockId: 7);

        await client.UpdateStockAsync("AKL001", 5);

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Patch);
        captured.RequestUri!.PathAndQuery.Should().Be("/api/stocks/7/movements");
    }

    [Fact]
    public async Task UpdateStockAsync_SerializesRequestBodyCorrectly()
    {
        string? capturedBody = null;
        var client = BuildClient(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(new { data = (object?)null, errors = (object?)null });
        });

        await client.UpdateStockAsync("OCH001030", 3);

        capturedBody.Should().Contain("\"productCode\"");
        capturedBody.Should().Contain("OCH001030");
        capturedBody.Should().Contain("\"amountChange\"");
        capturedBody.Should().Contain("3");
    }

    [Fact]
    public async Task UpdateStockAsync_NegativeAmount_SerializesCorrectly()
    {
        string? capturedBody = null;
        var client = BuildClient(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(new { data = (object?)null, errors = (object?)null });
        });

        await client.UpdateStockAsync("OCH001030", -2);

        capturedBody.Should().Contain("-2");
    }

    [Fact]
    public async Task GetSupplyAsync_ReturnsAmountAndClaim()
    {
        var client = BuildClient(_ => Json(new
        {
            data = new
            {
                supplies = new[]
                {
                    new { code = "AKL001", amount = "10.000", claim = "3.000" },
                },
            },
            errors = (object?)null,
        }));
        var result = await client.GetSupplyAsync("AKL001");
        result.Should().NotBeNull();
        result!.Code.Should().Be("AKL001");
        result.Amount.Should().Be(10);
        result.Claim.Should().Be(3);
    }

    [Fact]
    public async Task GetSupplyAsync_EmptySupplies_ReturnsNull()
    {
        var client = BuildClient(_ => Json(new
        {
            data = new { supplies = Array.Empty<object>() },
            errors = (object?)null,
        }));
        var result = await client.GetSupplyAsync("UNKNOWN");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSupplyAsync_UsesCorrectUrlWithCode()
    {
        HttpRequestMessage? captured = null;
        var client = BuildClient(
            req =>
            {
                captured = req;
                return Json(new { data = new { supplies = Array.Empty<object>() }, errors = (object?)null });
            },
            stockId: 1);
        await client.GetSupplyAsync("AKL001");
        captured!.Method.Should().Be(HttpMethod.Get);
        captured.RequestUri!.PathAndQuery.Should().Contain("/api/stocks/1/supplies");
        captured.RequestUri!.PathAndQuery.Should().Contain("code=AKL001");
    }

    [Fact]
    public async Task SetRealStockAsync_UsesRealStockField()
    {
        string? capturedBody = null;
        var client = BuildClient(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(new { data = (object?)null, errors = (object?)null });
        });
        await client.SetRealStockAsync("AKL001", 42);
        capturedBody.Should().Contain("\"realStock\"");
        capturedBody.Should().Contain("42");
        capturedBody.Should().NotContain("\"amountChange\"");
    }

    [Fact]
    public async Task SetRealStockAsync_ResponseWithErrors_Throws()
    {
        var client = BuildClient(_ => Json(new
        {
            data = (object?)null,
            errors = new[]
            {
                new { errorCode = "unknown-product", message = "Product does not exist.", instance = "AKL001" },
            },
        }));
        var act = () => client.SetRealStockAsync("AKL001", 42);
        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*AKL001*unknown-product*");
    }

    private static ShoptetStockClient BuildClientForCsv(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        string csvUrl = "https://test.com/stock-export.csv")
    {
        var dummyHttp = new HttpClient(new FakeDelegatingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)))
        {
            BaseAddress = new Uri("https://fake.shoptet.cz"),
        };

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new FakeDelegatingHandler(handler)));

        var settings = Options.Create(new ShoptetApiSettings { StockId = 1 });
        var stockClientOptions = Options.Create(new ShoptetStockClientOptions { Url = csvUrl });

        return new ShoptetStockClient(dummyHttp, factoryMock.Object, settings, stockClientOptions);
    }

    [Fact]
    public async Task ListAsync_WhenServerReturns500_ThrowsHttpRequestException()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Arrange
        var htmlErrorBody = "<html><body><h1>Internal Server Error</h1></body></html>";
        var client = BuildClientForCsv(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(htmlErrorBody, Encoding.UTF8),
        });

        // Act
        Func<Task> act = async () => await client.ListAsync(CancellationToken.None);

        // Assert – HttpRequestException must be thrown, NOT CsvHelper.MissingFieldException
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ListAsync_WhenServerReturns503_ThrowsHttpRequestException()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var htmlErrorBody = "<html><body><h1>Service Unavailable</h1></body></html>";
        var client = BuildClientForCsv(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(htmlErrorBody, Encoding.UTF8),
        });

        Func<Task> act = async () => await client.ListAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ListAsync_WhenServerReturns404_ThrowsHttpRequestException()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var client = BuildClientForCsv(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("Not Found", Encoding.UTF8),
        });

        Func<Task> act = async () => await client.ListAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
