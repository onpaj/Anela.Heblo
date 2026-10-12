using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Application.Features.ShoptetOrders;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class ShoptetOrderClientItemsTests
{
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

    private static string OrderListPage(int page, int pageCount, int statusId, params string[] codes) =>
        JsonSerializer.Serialize(new
        {
            data = new
            {
                orders = codes.Select(c => new { code = c, status = new { id = statusId } }),
                paginator = new { page, pageCount, totalCount = codes.Length },
            },
        });

    [Fact]
    public async Task ListOrdersContainingProduct_SendsEncodedFiltersAndReadsAllPagesWithStatus()
    {
        var client = BuildClient(req => req.RequestUri!.Query.Contains("page=1&")
            ? Raw(HttpStatusCode.OK, OrderListPage(1, 2, -2, "126000001", "126000002"))
            : Raw(HttpStatusCode.OK, OrderListPage(2, 2, 26, "126000003")));

        var orders = await client.ListOrdersContainingProductAsync(
            "SA015030V", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(2)));

        orders.Select(o => o.Code).Should().Equal("126000001", "126000002", "126000003");
        orders.Select(o => o.StatusId).Should().Equal(-2, -2, 26);
        _requests.Should().HaveCount(2);
        var query = System.Web.HttpUtility.ParseQueryString(new Uri("https://x" + _requests[0].PathAndQuery).Query);
        query["productCode"].Should().Be("SA015030V");
        query["creationTimeFrom"].Should().Be("2026-10-01T00:00:00+02:00");
        query["itemsPerPage"].Should().Be("50");
        _requests[0].PathAndQuery.Should().Contain("%2B02%3A00", "Shoptet needs the '+' of the offset URL-encoded");
    }

    [Fact]
    public async Task GetOrderDetail_MapsStatusAndLines()
    {
        var client = BuildClient(_ => Raw(HttpStatusCode.OK, """
        {"data":{"order":{"code":"126000043","status":{"id":-2,"name":"Vyřizuje se"},
          "items":[
            {"itemId":1356563,"itemType":"product","code":"SA015030V","amount":"2.000","priceRatio":"0.9000","remark":null,
             "unitPrice":{"withVat":"1770.00","withoutVat":"1462.81","vat":"307.19","vatRate":"21.00"}},
            {"itemId":1356566,"itemType":"shipping","code":null,"amount":"1.000","priceRatio":"1.0000","remark":null,
             "unitPrice":{"withVat":"0.00","withoutVat":"0.00","vat":"0.00","vatRate":"21.00"}}]}}}
        """));

        var detail = await client.GetOrderDetailAsync("126000043");

        detail.Should().NotBeNull();
        detail!.Code.Should().Be("126000043");
        detail.StatusId.Should().Be(-2);
        detail.Lines.Should().HaveCount(2);
        var v = detail.Lines[0];
        v.ItemId.Should().Be(1356563);
        v.ItemType.Should().Be("product");
        v.Code.Should().Be("SA015030V");
        v.Amount.Should().Be(2m);
        v.UnitPriceWithVat.Should().Be(1770m);
        v.VatRate.Should().Be(21m);
        v.PriceRatio.Should().Be(0.9m);
        v.Remark.Should().BeNull();
        detail.Lines[1].Code.Should().BeNull();
        _requests[0].PathAndQuery.Should().Be("/api/orders/126000043");
    }

    [Fact]
    public async Task GetOrderDetail_ReturnsNull_On404()
    {
        var client = BuildClient(_ => Raw(HttpStatusCode.NotFound, """{"data":null,"errors":[]}"""));

        (await client.GetOrderDetailAsync("126999999")).Should().BeNull();
    }

    [Fact]
    public async Task AddOrderItem_PostsShoptetStringFormattedBody()
    {
        var client = BuildClient(_ => Raw(HttpStatusCode.Created, """{"data":{"order":{"code":"126000043"}}}"""));

        await client.AddOrderItemAsync("126000043", new NewEshopOrderItem
        {
            ItemType = "product-set",
            Code = "SA015030S",
            Amount = 2m,
            UnitPriceWithVat = 1770m,
            VatRate = 21m,
            PriceRatio = 0.9m,
            Remark = "auto-swap SA015030V #1356563",
        });

        _requests.Should().ContainSingle();
        _requests[0].Method.Should().Be(HttpMethod.Post);
        _requests[0].PathAndQuery.Should().Be("/api/orders/126000043/item");
        using var doc = JsonDocument.Parse(_requests[0].Body!);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("itemType").GetString().Should().Be("product-set");
        data.GetProperty("code").GetString().Should().Be("SA015030S");
        data.GetProperty("amount").GetString().Should().Be("2");
        data.GetProperty("unitPriceWithVat").GetString().Should().Be("1770.00");
        data.GetProperty("vatRate").GetString().Should().Be("21");
        data.GetProperty("priceRatio").GetString().Should().Be("0.9000");
        data.GetProperty("remark").GetString().Should().Be("auto-swap SA015030V #1356563");
        data.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "itemType", "code", "amount", "unitPriceWithVat", "vatRate", "priceRatio", "remark");
    }

    [Fact]
    public async Task AddOrderItem_Throws422WithShoptetErrorInMessage()
    {
        var client = BuildClient(_ => Raw(HttpStatusCode.UnprocessableEntity,
            """{"data":null,"errors":[{"errorCode":"invalid-request-data","message":"Product SA015030S not found.","instance":"data.code"}]}"""));

        var act = () => client.AddOrderItemAsync("126000043", new NewEshopOrderItem { ItemType = "product-set", Code = "SA015030S", Amount = 1m });

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        ex.Message.Should().Contain("invalid-request-data").And.Contain("Product SA015030S not found.");
    }

    [Fact]
    public async Task DeleteOrderItem_SendsDeleteToItemPath_AndSurfaces409()
    {
        var client = BuildClient(_ => Raw(HttpStatusCode.Conflict,
            """{"data":null,"errors":[{"errorCode":"record-already-changed","message":"Order has been changed before update.","instance":"payload"}]}"""));

        var act = () => client.DeleteOrderItemAsync("126000043", 1356563);

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _requests[0].Method.Should().Be(HttpMethod.Delete);
        _requests[0].PathAndQuery.Should().Be("/api/orders/126000043/item/1356563");
    }
}
