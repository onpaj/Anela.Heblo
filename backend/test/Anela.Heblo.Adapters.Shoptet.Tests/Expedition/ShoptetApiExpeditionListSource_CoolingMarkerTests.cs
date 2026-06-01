using System.Net;
using System.Text;
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Domain.Features.Logistics.Picking;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Expedition;

public class ShoptetApiExpeditionListSource_CoolingMarkerTests
{
    private const string ZasilkovnaDoRukyGuid = "f6610d4d-578d-11e9-beb1-002590dad85e";
    private const string CooledOrderCode = "TEST-COOLED";
    private const string NormalOrderCode = "TEST-NORMAL";
    private const string CooledProductCode = "PROD-COOL";
    private const string NormalProductCode = "PROD-NORMAL";

    private static HttpResponseMessage OkJson(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static readonly string OrderListJson = $$"""
        {
          "data": {
            "orders": [
              {
                "code": "{{CooledOrderCode}}",
                "status": { "id": -2 },
                "shipping": { "guid": "{{ZasilkovnaDoRukyGuid}}", "name": "Zásilkovna (do ruky)" },
                "price": { "withVat": "500.00", "currencyCode": "CZK" }
              },
              {
                "code": "{{NormalOrderCode}}",
                "status": { "id": -2 },
                "shipping": { "guid": "{{ZasilkovnaDoRukyGuid}}", "name": "Zásilkovna (do ruky)" },
                "price": { "withVat": "300.00", "currencyCode": "CZK" }
              }
            ],
            "paginator": { "totalCount": 2, "page": 1, "pageCount": 1 }
          }
        }
        """;

    private static readonly string CooledOrderDetailJson = $$"""
        {
          "data": {
            "order": {
              "code": "{{CooledOrderCode}}",
              "fullName": "Cooled Customer",
              "phone": "+420111222333",
              "billingAddress": {
                "fullName": "Cooled Customer",
                "street": "Chladna",
                "houseNumber": "1",
                "city": "Praha",
                "zip": "10000"
              },
              "items": [
                {
                  "itemType": "product",
                  "itemId": 1,
                  "code": "{{CooledProductCode}}",
                  "name": "Cooled Product",
                  "amount": 1.000,
                  "unit": "ks",
                  "itemPriceWithVat": "100.00"
                }
              ],
              "completion": []
            }
          }
        }
        """;

    private static readonly string NormalOrderDetailJson = $$"""
        {
          "data": {
            "order": {
              "code": "{{NormalOrderCode}}",
              "fullName": "Normal Customer",
              "phone": "+420444555666",
              "billingAddress": {
                "fullName": "Normal Customer",
                "street": "Normalni",
                "houseNumber": "2",
                "city": "Brno",
                "zip": "60200"
              },
              "items": [
                {
                  "itemType": "product",
                  "itemId": 2,
                  "code": "{{NormalProductCode}}",
                  "name": "Normal Product",
                  "amount": 1.000,
                  "unit": "ks",
                  "itemPriceWithVat": "80.00"
                }
              ],
              "completion": []
            }
          }
        }
        """;

    private Mock<HttpMessageHandler> BuildHandler(bool patchShouldThrow = false)
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

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Get &&
                    r.RequestUri!.AbsolutePath == $"/api/orders/{CooledOrderCode}"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(OkJson(CooledOrderDetailJson));

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Get &&
                    r.RequestUri!.AbsolutePath == $"/api/orders/{NormalOrderCode}"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(OkJson(NormalOrderDetailJson));

        if (patchShouldThrow)
        {
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(r =>
                        r.Method == HttpMethod.Patch &&
                        r.RequestUri!.AbsolutePath == $"/api/orders/{CooledOrderCode}/notes"),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Simulated Shoptet PATCH failure"));
        }
        else
        {
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(r =>
                        r.Method == HttpMethod.Patch &&
                        r.RequestUri!.AbsolutePath == $"/api/orders/{CooledOrderCode}/notes"),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(OkJson("""{"data":null,"errors":null}"""));
        }

        return handler;
    }

    private ShoptetApiExpeditionListSource BuildSource(
        Mock<HttpMessageHandler> handler,
        ILogger<ShoptetApiExpeditionListSource>? logger = null,
        string? coolingText = null,
        Action<ExpeditionProtocolData>? captureData = null)
    {
        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("https://test.myshoptet.com") };
        var client = new ShoptetOrderClient(http);

        var catalog = new Mock<ICatalogRepository>();
        catalog.Setup(x => x.GetByIdAsync(CooledProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogAggregate
            {
                ProductCode = CooledProductCode,
                ProductName = "Cooled Product",
                Properties = new CatalogProperties { Cooling = Cooling.L1 },
            });
        catalog.Setup(x => x.GetByIdAsync(NormalProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogAggregate
            {
                ProductCode = NormalProductCode,
                ProductName = "Normal Product",
                Properties = new CatalogProperties { Cooling = Cooling.None },
            });

        var carrierCooling = new Mock<ICarrierCoolingRepository>();
        carrierCooling.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CarrierCoolingSetting>
            {
                new(Carriers.Zasilkovna, DeliveryHandling.NaRuky, Cooling.L1, "test", coolingText),
            });

        var giftSettings = new Mock<IGiftSettingRepository>();
        giftSettings.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GiftSetting.CreateDefault());

        return new ShoptetApiExpeditionListSource(
            client,
            TimeProvider.System,
            catalog.Object,
            carrierCooling.Object,
            giftSettings.Object,
            logger ?? Mock.Of<ILogger<ShoptetApiExpeditionListSource>>(),
            data =>
            {
                captureData?.Invoke(data);
                return new byte[] { 0x25, 0x50, 0x44, 0x46 };
            });
    }

    private static PrintPickingListRequest BuildRequest() => new()
    {
        SourceStateId = -2,
        DesiredStateId = 26,
        ChangeOrderState = false,
        Carriers = [],
    };

    [Fact]
    public async Task CreatePickingList_CooledOrder_PatchesShoptetAdditionalField()
    {
        var handler = BuildHandler();
        var source = BuildSource(handler);

        await source.CreatePickingList(BuildRequest(), null, CancellationToken.None);

        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Patch &&
                r.RequestUri!.AbsolutePath == $"/api/orders/{CooledOrderCode}/notes"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task CreatePickingList_NonCooledOrder_DoesNotPatchAdditionalField()
    {
        var handler = BuildHandler();
        var source = BuildSource(handler);

        await source.CreatePickingList(BuildRequest(), null, CancellationToken.None);

        handler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Patch &&
                r.RequestUri!.AbsolutePath == $"/api/orders/{NormalOrderCode}/notes"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task CreatePickingList_PatchFails_PdfStillCompletes()
    {
        var logger = new Mock<ILogger<ShoptetApiExpeditionListSource>>();
        var handler = BuildHandler(patchShouldThrow: true);
        var source = BuildSource(handler, logger.Object);

        var result = await source.CreatePickingList(BuildRequest(), null, CancellationToken.None);

        result.TotalCount.Should().Be(2);
        result.ExportedFiles.Should().NotBeEmpty("PDF generation must complete even when the Shoptet PATCH fails");
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(CooledOrderCode)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CreatePickingList_CooledOrder_UsesCustomCoolingTextFromSetting()
    {
        ExpeditionProtocolData? captured = null;
        var handler = BuildHandler();
        var source = BuildSource(handler, coolingText: "MRAZ", captureData: d => captured = d);

        await source.CreatePickingList(BuildRequest(), null, CancellationToken.None);

        captured.Should().NotBeNull();
        var cooledOrder = captured!.Orders.Single(o => o.Code == CooledOrderCode);
        cooledOrder.CoolingText.Should().Be("MRAZ");
    }
}
