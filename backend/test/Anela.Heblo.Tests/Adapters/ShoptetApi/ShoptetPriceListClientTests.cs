using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Pricing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class ShoptetPriceListClientTests
{
    private static ShoptetPriceListClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        List<HttpRequestMessage>? recorded = null,
        int? defaultPriceListId = 1)
    {
        var handler = new StubHandler(responder, recorded);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.myshoptet.com") };
        var settings = Options.Create(new ShoptetApiSettings
        {
            BaseUrl = "https://api.myshoptet.com",
            ApiToken = "token",
            DefaultPriceListId = defaultPriceListId,
        });
        return new ShoptetPriceListClient(httpClient, settings, NullLogger<ShoptetPriceListClient>.Instance);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task reads_all_pages_of_the_price_list()
    {
        // Arrange
        var page1 = """
        {"data":{"pricelist":[
            {"code":"A","includingVat":true,"vatRate":"21.00","price":{"price":"190.00"}},
            {"code":"B","includingVat":true,"vatRate":"21.00","price":{"price":"250.50"}}],
         "paginator":{"page":1,"pageCount":2}},"errors":null}
        """;
        var page2 = """
        {"data":{"pricelist":[{"code":"C","includingVat":true,"vatRate":"21.00","price":{"price":"99.00"}}],
         "paginator":{"page":2,"pageCount":2}},"errors":null}
        """;
        var client = CreateClient(req =>
            Json(req.RequestUri!.Query.Contains("page=2") ? page2 : page1));

        // Act
        var prices = await client.GetPricesWithVatAsync(CancellationToken.None);

        // Assert
        prices.Should().HaveCount(3);
        prices["A"].Should().Be(190.00m);
        prices["C"].Should().Be(99.00m);
    }

    [Fact]
    public async Task requests_the_price_list_detail_with_the_maximum_page_size()
    {
        // Arrange
        var recorded = new List<HttpRequestMessage>();
        var client = CreateClient(
            _ => Json("""{"data":{"pricelist":[],"paginator":{"page":1,"pageCount":1}},"errors":null}"""),
            recorded);

        // Act
        await client.GetPricesWithVatAsync(CancellationToken.None);

        // Assert
        recorded.Should().ContainSingle();
        recorded[0].RequestUri!.AbsolutePath.Should().Be("/api/pricelists/1");
        recorded[0].RequestUri!.Query.Should().Contain("itemsPerPage=100");
    }

    [Fact]
    public async Task throws_when_no_price_list_id_is_configured()
    {
        // Arrange
        var client = CreateClient(
            _ => Json("""{"data":{"pricelist":[],"paginator":{"page":1,"pageCount":1}},"errors":null}"""),
            defaultPriceListId: null);

        // Act
        var act = () => client.GetPricesWithVatAsync(CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .And.Message.Should().Contain("Shoptet:DefaultPriceListId");
    }

    [Fact]
    public async Task derives_the_with_vat_price_when_the_list_stores_prices_excluding_vat()
    {
        // Arrange
        var client = CreateClient(_ => Json("""
            {"data":{"pricelist":[{"code":"A","includingVat":false,"vatRate":"21.00","price":{"price":"100.00"}}],
             "paginator":{"page":1,"pageCount":1}},"errors":null}
            """));

        // Act
        var prices = await client.GetPricesWithVatAsync(CancellationToken.None);

        // Assert
        prices["A"].Should().Be(121.00m);
    }

    [Fact]
    public async Task skips_an_item_with_a_null_price_as_legitimately_unpriced()
    {
        // Arrange
        var client = CreateClient(_ => Json("""
            {"data":{"pricelist":[{"code":"A","includingVat":true,"vatRate":"21.00","price":{"price":null}}],
             "paginator":{"page":1,"pageCount":1}},"errors":null}
            """));

        // Act
        var prices = await client.GetPricesWithVatAsync(CancellationToken.None);

        // Assert
        prices.Should().BeEmpty();
    }

    [Fact]
    public async Task skips_but_counts_an_item_with_an_unparseable_price_and_keeps_reading_the_rest()
    {
        // Arrange: a non-null price that cannot be interpreted must be logged and skipped,
        // not silently dropped and not allowed to blow up the whole run.
        var client = CreateClient(_ => Json("""
            {"data":{"pricelist":[
                {"code":"BAD","includingVat":true,"vatRate":"21.00","price":{"price":"not-a-number"}},
                {"code":"OK","includingVat":true,"vatRate":"21.00","price":{"price":"50.00"}}],
             "paginator":{"page":1,"pageCount":1}},"errors":null}
            """));

        // Act
        var prices = await client.GetPricesWithVatAsync(CancellationToken.None);

        // Assert
        prices.Should().ContainKey("OK");
        prices.Should().NotContainKey("BAD");
    }

    [Fact]
    public async Task skips_an_item_excluding_vat_with_an_unparseable_vat_rate()
    {
        // Arrange
        var client = CreateClient(_ => Json("""
            {"data":{"pricelist":[{"code":"A","includingVat":false,"vatRate":null,"price":{"price":"100.00"}}],
             "paginator":{"page":1,"pageCount":1}},"errors":null}
            """));

        // Act
        var prices = await client.GetPricesWithVatAsync(CancellationToken.None);

        // Assert
        prices.Should().BeEmpty();
    }

    [Fact]
    public async Task sends_price_with_vat_on_patch()
    {
        // Arrange
        var recorded = new List<HttpRequestMessage>();
        var bodies = new List<string>();
        var handler = new StubHandler(_ => Json("""{"data":null,"errors":null}"""), recorded, bodies);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.myshoptet.com") };
        var client = new ShoptetPriceListClient(
            httpClient,
            Options.Create(new ShoptetApiSettings { BaseUrl = "https://api.myshoptet.com", ApiToken = "t", DefaultPriceListId = 1 }),
            NullLogger<ShoptetPriceListClient>.Instance);

        // Act
        await client.SetPriceWithVatAsync("OCH001030", 210.00m, CancellationToken.None);

        // Assert
        recorded.Should().ContainSingle();
        recorded[0].Method.Should().Be(HttpMethod.Patch);
        recorded[0].RequestUri!.AbsolutePath.Should().Be("/api/pricelists/1");
        // Structural, not substring: `priceWithVat` is an object group (same members as the
        // read-side `price`), not a scalar. A `Contain("210.00")` assertion passes for the
        // flat-string shape Shoptet rejects with 422 invalid-request-data.
        var item = JsonDocument.Parse(bodies[0]).RootElement.GetProperty("data")[0];
        item.GetProperty("code").GetString().Should().Be("OCH001030");
        item.GetProperty("priceWithVat").GetProperty("price").GetString().Should().Be("210.00");
        bodies[0].Should().NotContain("buyPrice");
    }

    [Fact]
    public async Task throws_with_the_response_body_when_shoptet_rejects_the_patch()
    {
        // Arrange
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("""{"errors":[{"message":"Invalid price"}]}""", Encoding.UTF8, "application/json"),
        });

        // Act
        var act = () => client.SetPriceWithVatAsync("OCH001030", 210.00m, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>()).And.Message.Should().Contain("Invalid price");
    }

    [Fact]
    public async Task reads_one_products_price_by_code()
    {
        // Arrange
        var recorded = new List<HttpRequestMessage>();
        var client = CreateClient(_ => Json("""
            {"data":{"pricelist":[{"code":"DEO007005","includingVat":true,"vatRate":"21.00",
             "price":{"price":"390.00"}}],"paginator":{"page":1,"pageCount":1}},"errors":null}
            """), recorded);

        // Act
        var price = await client.GetPriceWithVatAsync("DEO007005", CancellationToken.None);

        // Assert
        price.Should().Be(390.00m);
        recorded[0].RequestUri!.Query.Should().Contain("code=DEO007005");
    }

    [Fact]
    public async Task returns_null_when_the_product_is_absent_from_the_price_list()
    {
        // Arrange
        var client = CreateClient(_ => Json("""
            {"data":{"pricelist":[],"paginator":{"page":1,"pageCount":1}},"errors":null}
            """));

        // Act
        var price = await client.GetPriceWithVatAsync("NOPE", CancellationToken.None);

        // Assert
        price.Should().BeNull();
    }

    [Fact]
    public async Task throws_when_a_200_carries_no_data_block()
    {
        // Arrange
        // A 200 with no `data` block is a malformed response, not an empty price list.
        // Returning an empty snapshot would hand the comparison a Shoptet side with nothing
        // in it, classifying every in-scope product MissingInShoptet — which is exactly the
        // "compared nothing, reported healthy" state the caller must never be handed.
        var client = CreateClient(_ => Json("""{"data":null,"errors":null}"""));

        // Act
        var act = () => client.GetPricesWithVatAsync(CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>())
            .And.Message.Should().Contain("no data block");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public async Task rejects_a_non_positive_price_before_dispatching_the_write(decimal priceWithVat)
    {
        // Arrange: Shoptet treats a literal 0 as a genuine free price (from 2026-09-14), not
        // as "clear the price", so a zero must never leave this process.
        var recorded = new List<HttpRequestMessage>();
        var client = CreateClient(_ => Json("{}"), recorded);

        // Act
        var act = () => client.SetPriceWithVatAsync("A", priceWithVat, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        recorded.Should().BeEmpty();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        private readonly List<HttpRequestMessage>? _recorded;
        private readonly List<string>? _bodies;

        public StubHandler(
            Func<HttpRequestMessage, HttpResponseMessage> responder,
            List<HttpRequestMessage>? recorded = null,
            List<string>? bodies = null)
        {
            _responder = responder;
            _recorded = recorded;
            _bodies = bodies;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _recorded?.Add(request);
            if (_bodies is not null && request.Content is not null)
            {
                _bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }
            return _responder(request);
        }
    }
}
