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
    public async Task UpdateStockAsync_ResponseWithErrors_Throws()
    {
        var client = BuildClient(_ => Json(new
        {
            errors = new[] { new { errorCode = "INVALID", message = "Product not found", instance = "AKL001" } }
        }));
        await client.Invoking(c => c.UpdateStockAsync("AKL001", 5))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AKL001*");
    }

    [Fact]
    public async Task UpdateStockAsync_HttpError_Throws()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
        await client.Invoking(c => c.UpdateStockAsync("AKL001", 5))
            .Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task UpdateStockAsync_UsesCorrectStockIdInUrl()
    {
        string? capturedUrl = null;
        var client = BuildClient(req =>
        {
            capturedUrl = req.RequestUri?.PathAndQuery;
            return Json(new { errors = (object?)null });
        }, stockId: 42);

        await client.UpdateStockAsync("AKL001", 5);

        capturedUrl.Should().Contain("/api/stocks/42/movements");
    }

    [Fact]
    public async Task UpdateStockAsync_SendsCorrectRequestBody()
    {
        string? capturedBody = null;
        var client = BuildClient(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(new { errors = (object?)null });
        });

        await client.UpdateStockAsync("AKL001", 7.5);

        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("AKL001");
        capturedBody.Should().Contain("7.5");
    }
}
