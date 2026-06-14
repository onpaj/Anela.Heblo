using System.Net;
using System.Text;
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Application.Features.Logistics.Picking;
using Anela.Heblo.Application.Features.ShoptetOrders;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Expedition;

public class ShoptetApiExpeditionListSource_AddressValidationTests
{
    private const string DoRukyGuid = "f6610d4d-578d-11e9-beb1-002590dad85e"; // ZASILKOVNA_DO_RUKY (NaRuky)
    private const string ZPointGuid = "7878c138-578d-11e9-beb1-002590dad85e"; // ZASILKOVNA_ZPOINT (Box)

    private const string BadRuky = "BAD-RUKY";   // home delivery, missing zip -> skip
    private const string GoodRuky = "GOOD-RUKY"; // home delivery, complete -> print
    private const string BoxBad = "BOX-BAD";     // box, incomplete -> still print (exempt)

    private static HttpResponseMessage OkJson(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static readonly string OrderListJson = $$"""
        {
          "data": {
            "orders": [
              { "code": "{{BadRuky}}",  "status": { "id": -2 }, "shipping": { "guid": "{{DoRukyGuid}}" }, "price": { "withVat": "300.00", "currencyCode": "CZK" } },
              { "code": "{{GoodRuky}}", "status": { "id": -2 }, "shipping": { "guid": "{{DoRukyGuid}}" }, "price": { "withVat": "300.00", "currencyCode": "CZK" } },
              { "code": "{{BoxBad}}",   "status": { "id": -2 }, "shipping": { "guid": "{{ZPointGuid}}" }, "price": { "withVat": "300.00", "currencyCode": "CZK" } }
            ],
            "paginator": { "totalCount": 3, "page": 1, "pageCount": 1 }
          }
        }
        """;

    private static string OrderDetail(string code, string? street, string? houseNumber, string? city, string? zip) => $$"""
        {
          "data": {
            "order": {
              "code": "{{code}}",
              "fullName": "Customer {{code}}",
              "phone": "+420000000000",
              "deliveryAddress": {
                "fullName": "Customer {{code}}",
                "street": {{(street is null ? "null" : $"\"{street}\"")}},
                "houseNumber": {{(houseNumber is null ? "null" : $"\"{houseNumber}\"")}},
                "city": {{(city is null ? "null" : $"\"{city}\"")}},
                "zip": {{(zip is null ? "null" : $"\"{zip}\"")}}
              },
              "items": [
                { "itemType": "product", "itemId": 1, "code": "P1", "name": "Item", "amount": 1, "unit": "ks", "itemPriceWithVat": "10.00" }
              ],
              "completion": []
            }
          }
        }
        """;

    private static void SetupDetail(Mock<HttpMessageHandler> handler, string code, string json) =>
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Get &&
                    r.RequestUri!.AbsolutePath == $"/api/orders/{code}"),
                ItExpr.IsAny<CancellationToken>())
            .Returns(() => Task.FromResult(OkJson(json)));

    private Mock<HttpMessageHandler> BuildHandler()
    {
        var handler = new Mock<HttpMessageHandler>();

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Get &&
                    r.RequestUri!.AbsolutePath == "/api/orders" &&
                    r.RequestUri.Query.Contains("statusId")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(OkJson(OrderListJson));

        SetupDetail(handler, BadRuky, OrderDetail(BadRuky, "Hlavní", "1", "Praha", null));
        SetupDetail(handler, GoodRuky, OrderDetail(GoodRuky, "Hlavní", "2", "Praha", "11000"));
        SetupDetail(handler, BoxBad, OrderDetail(BoxBad, null, null, null, null));

        // Any PATCH (status / notes) succeeds.
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Patch),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(OkJson("""{"data":null,"errors":null}"""));

        return handler;
    }

    private ShoptetApiExpeditionListSource BuildSource(
        Mock<HttpMessageHandler> handler, List<ExpeditionProtocolData> captured)
    {
        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("https://test.myshoptet.com") };
        var client = new ShoptetOrderClient(http, Options.Create(new ShoptetOrdersSettings()));

        var catalog = new Mock<ICatalogRepository>();
        catalog.Setup(x => x.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogAggregate { ProductCode = "P1", ProductName = "Item" });

        var carrierCooling = new Mock<ICarrierCoolingRepository>();
        carrierCooling.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CarrierCoolingSetting>());

        var giftSettings = new Mock<IGiftSettingRepository>();
        giftSettings.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GiftSetting.CreateDefault());

        return new ShoptetApiExpeditionListSource(
            client, TimeProvider.System, catalog.Object, carrierCooling.Object, giftSettings.Object,
            Mock.Of<ILogger<ShoptetApiExpeditionListSource>>(),
            data => { captured.Add(data); return new byte[] { 0x25, 0x50, 0x44, 0x46 }; });
    }

    private static PrintPickingListRequest BuildRequest() => new()
    {
        SourceStateId = -2,
        DesiredStateId = 26,
        NoteStateId = 35,
        ChangeOrderState = false,
        Carriers = [],
    };

    [Fact]
    public async Task IncompleteHomeDeliveryOrder_IsSkippedAndFlagged()
    {
        var handler = BuildHandler();
        var captured = new List<ExpeditionProtocolData>();
        var source = BuildSource(handler, captured);

        var result = await source.CreatePickingList(BuildRequest(), null, CancellationToken.None);

        // Skipped: not in any PDF, not counted as processed.
        var printedCodes = captured.SelectMany(d => d.Orders).Select(o => o.Code).ToList();
        printedCodes.Should().NotContain(BadRuky);
        printedCodes.Should().Contain(GoodRuky);
        result.TotalCount.Should().Be(2);   // GoodRuky + BoxBad
        result.SkippedCount.Should().Be(1); // BadRuky

        // Moved to note state 35 and remark appended.
        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Patch &&
                r.RequestUri!.AbsolutePath == $"/api/orders/{BadRuky}/status"),
            ItExpr.IsAny<CancellationToken>());
        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Patch &&
                r.RequestUri!.AbsolutePath == $"/api/orders/{BadRuky}/notes"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task IncompleteBoxOrder_IsNotValidated_AndStillPrinted()
    {
        var handler = BuildHandler();
        var captured = new List<ExpeditionProtocolData>();
        var source = BuildSource(handler, captured);

        var result = await source.CreatePickingList(BuildRequest(), null, CancellationToken.None);

        captured.SelectMany(d => d.Orders).Select(o => o.Code).Should().Contain(BoxBad);
        handler.Protected().Verify("SendAsync", Times.Never(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Patch &&
                r.RequestUri!.AbsolutePath == $"/api/orders/{BoxBad}/status"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task CompleteHomeDeliveryOrder_IsNotFlagged()
    {
        var handler = BuildHandler();
        var captured = new List<ExpeditionProtocolData>();
        var source = BuildSource(handler, captured);

        await source.CreatePickingList(BuildRequest(), null, CancellationToken.None);

        handler.Protected().Verify("SendAsync", Times.Never(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Patch &&
                r.RequestUri!.AbsolutePath == $"/api/orders/{GoodRuky}/status"),
            ItExpr.IsAny<CancellationToken>());
    }
}
