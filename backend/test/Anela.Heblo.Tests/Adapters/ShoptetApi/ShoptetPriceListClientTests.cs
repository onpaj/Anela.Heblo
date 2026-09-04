using System.Net;
using System.Text;
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
        {"data":{"pricelist":[{"code":"A","priceWithVat":"190.00"},{"code":"B","priceWithVat":"250.50"}],
         "paginator":{"page":1,"pageCount":2}},"errors":null}
        """;
        var page2 = """
        {"data":{"pricelist":[{"code":"C","priceWithVat":"99.00"}],
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
    public async Task resolves_the_default_price_list_when_none_is_configured()
    {
        // Arrange
        var recorded = new List<HttpRequestMessage>();
        var client = CreateClient(req =>
                req.RequestUri!.AbsolutePath == "/api/pricelists"
                    ? Json("""{"data":{"pricelists":[{"id":7,"name":"Velkoobchod","default":false},{"id":3,"name":"Základní","default":true}]},"errors":null}""")
                    : Json("""{"data":{"pricelist":[],"paginator":{"page":1,"pageCount":1}},"errors":null}"""),
            recorded, defaultPriceListId: null);

        // Act
        await client.GetPricesWithVatAsync(CancellationToken.None);

        // Assert
        recorded.Last().RequestUri!.AbsolutePath.Should().Be("/api/pricelists/3");
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
        bodies[0].Should().Contain("OCH001030").And.Contain("210.00");
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
    public async Task throws_when_a_200_carries_no_data_block()
    {
        // Arrange
        // Returning an empty snapshot here would make every product decide MissingRemote
        // and be marked Failed in one run, instead of leaving the sync states untouched.
        var client = CreateClient(_ => Json("""{"data":null,"errors":null}"""));

        // Act
        var act = () => client.GetPricesWithVatAsync(CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>())
            .And.Message.Should().Contain("no data block");
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
