using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Application.Features.ShoptetOrders;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class ShoptetOrderClientInvoicesTests
{
    private static readonly DateTimeOffset CreatedFrom = new(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(1));

    private readonly List<(HttpMethod Method, string PathAndQuery, string? Body)> _requests = new();

    private ShoptetOrderClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeDelegatingHandler(req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            _requests.Add((req.Method, req.RequestUri!.PathAndQuery, body));
            return respond(req);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake.shoptet.cz") };
        return new ShoptetOrderClient(http, Options.Create(new ShoptetOrdersSettings()));
    }

    private static HttpResponseMessage Raw(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static System.Collections.Specialized.NameValueCollection QueryOf(string pathAndQuery) =>
        System.Web.HttpUtility.ParseQueryString(new Uri("https://x" + pathAndQuery).Query);

    private static bool IsPage(HttpRequestMessage req, int page) =>
        QueryOf(req.RequestUri!.PathAndQuery)["page"] == page.ToString();

    [Fact]
    public async Task ListOrdersByStatusCreatedFrom_SendsFiltersReadsAllPagesAndMapsPrice()
    {
        var client = BuildClient(req => IsPage(req, 1)
            ? Raw(HttpStatusCode.OK, """
                {"data":{"orders":[
                  {"code":"126000001","status":{"id":-3},"price":{"withVat":"350.00","currencyCode":"CZK"}},
                  {"code":"126000002","status":{"id":-3},"price":{"withVat":"0.00","currencyCode":"CZK"}}],
                 "paginator":{"page":1,"pageCount":2,"totalCount":3}}}
                """)
            : Raw(HttpStatusCode.OK, """
                {"data":{"orders":[{"code":"126000003","status":{"id":-3},"price":null}],
                 "paginator":{"page":2,"pageCount":2,"totalCount":3}}}
                """));

        var orders = await client.ListOrdersByStatusAsync(-3, CreatedFrom);

        orders.Select(o => o.Code).Should().Equal("126000001", "126000002", "126000003");
        orders.Select(o => o.PriceWithVat).Should().Equal(350m, 0m, null);
        _requests.Should().HaveCount(2);
        _requests[0].PathAndQuery.Should().StartWith("/api/orders?");
        var query = QueryOf(_requests[0].PathAndQuery);
        query["statusId"].Should().Be("-3");
        query["creationTimeFrom"].Should().Be("2026-01-01T00:00:00+01:00");
        query["itemsPerPage"].Should().Be("50");
    }

    [Fact]
    public async Task ListInvoicedOrderCodes_ReadsAllPagesAndKeepsOnlyOrderLinkedCodes()
    {
        var client = BuildClient(req => IsPage(req, 1)
            ? Raw(HttpStatusCode.OK, """
                {"data":{"invoices":[
                  {"code":"2026000001","orderCode":"126000001","isValid":true},
                  {"code":"2026000002","orderCode":null,"isValid":true}],
                 "paginator":{"page":1,"pageCount":2,"totalCount":3}}}
                """)
            : Raw(HttpStatusCode.OK, """
                {"data":{"invoices":[{"code":"2026000003","orderCode":"126000007","isValid":false}],
                 "paginator":{"page":2,"pageCount":2,"totalCount":3}}}
                """));

        var codes = await client.ListInvoicedOrderCodesAsync(CreatedFrom);

        codes.Should().BeEquivalentTo(new[] { "126000001", "126000007" },
            "an invalid (cancelled) invoice still means the order was invoiced");
        _requests.Should().HaveCount(2);
        _requests[0].PathAndQuery.Should().StartWith("/api/invoices?");
        var query = QueryOf(_requests[0].PathAndQuery);
        query["creationTimeFrom"].Should().Be("2026-01-01T00:00:00+01:00");
        query["itemsPerPage"].Should().Be("1000");
    }

    [Fact]
    public async Task CreateInvoice_PostsEmptyDataObject()
    {
        var client = BuildClient(_ => Raw(HttpStatusCode.Created, """{"data":{"invoice":{"code":"2026000009"}}}"""));

        await client.CreateInvoiceAsync("126000001");

        _requests.Should().ContainSingle();
        _requests[0].Method.Should().Be(HttpMethod.Post);
        _requests[0].PathAndQuery.Should().Be("/api/orders/126000001/invoice");
        using var body = JsonDocument.Parse(_requests[0].Body!);
        var root = body.RootElement;
        root.EnumerateObject().Select(p => p.Name).Should().Equal("data");
        root.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Object);
        root.GetProperty("data").EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public async Task CreateInvoice_ThrowsWithShoptetErrorText_OnFailure()
    {
        var client = BuildClient(_ => Raw(HttpStatusCode.UnprocessableEntity,
            """{"errors":[{"errorCode":"invalid-request-data","message":"Order already has an invoice."}]}"""));

        var act = () => client.CreateInvoiceAsync("126000001");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        ex.Which.Message.Should().Contain("Order already has an invoice.");
    }

    [Fact]
    public async Task ListInvoicedOrderCodes_Throws_WhenResponseBodyIsNull()
    {
        var client = BuildClient(_ => Raw(HttpStatusCode.OK, "null"));

        var act = () => client.ListInvoicedOrderCodesAsync(CreatedFrom);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ListInvoicedOrderCodes_Throws_WhenFewerInvoicesThanAnnounced()
    {
        // A missing paginator reads as pageCount 0, stopping after page 1 of a longer listing.
        var client = BuildClient(_ => Raw(HttpStatusCode.OK, """
            {"data":{"invoices":[{"code":"2026000001","orderCode":"126000001","isValid":true}],
             "paginator":{"page":1,"pageCount":1,"totalCount":1500}}}
            """));

        var act = () => client.ListInvoicedOrderCodesAsync(CreatedFrom);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("""{"data":{"invoices":[{"code":"2026000001","orderCode":"126000001","isValid":false}],"paginator":{"page":1,"pageCount":1,"totalCount":1}}}""", true)]
    [InlineData("""{"data":{"invoices":[],"paginator":{"page":1,"pageCount":0,"totalCount":0}}}""", false)]
    public async Task HasInvoice_FiltersByOrderCode(string json, bool expected)
    {
        var client = BuildClient(_ => Raw(HttpStatusCode.OK, json));

        var hasInvoice = await client.HasInvoiceAsync("126000001");

        hasInvoice.Should().Be(expected);
        _requests.Should().ContainSingle();
        _requests[0].PathAndQuery.Should().StartWith("/api/invoices?");
        QueryOf(_requests[0].PathAndQuery)["orderCode"].Should().Be("126000001");
    }
}
