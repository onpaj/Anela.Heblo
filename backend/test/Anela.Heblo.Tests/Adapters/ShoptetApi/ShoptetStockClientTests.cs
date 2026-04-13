using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Stock;
using FluentAssertions;
using Microsoft.Extensions.Options;

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
        return new ShoptetStockClient(http, settings);
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
}
