using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class ShoptetApiPackingOrderClientTests
{
    // PPL "do ruky" GUID — present in ShippingMethodRegistry.
    private const string PplDoRukyGuid = "2ec88ea7-3fb0-11e2-a723-705ab6a2ba75";

    private static ShoptetOrderClient BuildOrderClient(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var http = new HttpClient(new FakeDelegatingHandler(handler))
        {
            BaseAddress = new Uri("https://fake.shoptet.cz"),
        };
        return new ShoptetOrderClient(http);
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

    private static ExpeditionOrderDetailResponse DetailResponse(
        string code, string shippingGuid, string shippingName)
    {
        return new ExpeditionOrderDetailResponse
        {
            Data = new ExpeditionOrderDetailData
            {
                Order = new ExpeditionOrderDetail
                {
                    Code = code,
                    FullName = "Jan Novák",
                    Shipping = new OrderShippingSummary { Guid = shippingGuid, Name = shippingName },
                    Items = new List<ExpeditionOrderItemDto>
                    {
                        new() { ItemType = "product", Code = "P001", Name = "Krém", Amount = 2m },
                    },
                },
            },
        };
    }

    private static ICatalogRepository CatalogWith(params CatalogAggregate[] items)
    {
        var mock = new Mock<ICatalogRepository>();
        mock.Setup(c => c.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items.ToDictionary(i => i.ProductCode, i => i));
        return mock.Object;
    }

    private static ICarrierCoolingRepository CoolingWith(params CarrierCoolingSetting[] settings)
    {
        var mock = new Mock<ICarrierCoolingRepository>();
        mock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);
        return mock.Object;
    }

    [Fact]
    public async Task GetPackingOrderAsync_ReturnsNull_WhenOrderNotFound()
    {
        var orderClient = BuildOrderClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = new ShoptetApiPackingOrderClient(
            orderClient, CatalogWith(), CoolingWith());

        var result = await sut.GetPackingOrderAsync("999999", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPackingOrderAsync_MapsHeaderAndItems()
    {
        var orderClient = BuildOrderClient(_ =>
            Json(DetailResponse("250001", PplDoRukyGuid, "PPL (do ruky)")));
        var catalog = CatalogWith(new CatalogAggregate
        {
            ProductCode = "P001",
            Image = "https://img/p001.jpg",
            Properties = new CatalogProperties { Cooling = Cooling.None },
        });
        var sut = new ShoptetApiPackingOrderClient(orderClient, catalog, CoolingWith());

        var result = await sut.GetPackingOrderAsync("250001", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Code.Should().Be("250001");
        result.CustomerName.Should().Be("Jan Novák");
        result.ShippingMethodName.Should().Be("PPL (do ruky)");
        result.Items.Should().ContainSingle();
        result.Items[0].Name.Should().Be("Krém");
        result.Items[0].Quantity.Should().Be(2);
        result.Items[0].ImageUrl.Should().Be("https://img/p001.jpg");
    }

    [Fact]
    public async Task GetPackingOrderAsync_ComputesCooling_FromCarrierMatrixAndCatalog()
    {
        var orderClient = BuildOrderClient(_ =>
            Json(DetailResponse("250002", PplDoRukyGuid, "PPL chlazený balík (do ruky)")));
        var catalog = CatalogWith(new CatalogAggregate
        {
            ProductCode = "P001",
            Properties = new CatalogProperties { Cooling = Cooling.L1 },
        });
        var cooling = CoolingWith(
            new CarrierCoolingSetting(Carriers.PPL, DeliveryHandling.NaRuky, Cooling.L1, "test"));
        var sut = new ShoptetApiPackingOrderClient(orderClient, catalog, cooling);

        var result = await sut.GetPackingOrderAsync("250002", CancellationToken.None);

        result!.Cooling.Should().Be(Cooling.L1);
        result.IsCooled.Should().BeTrue();
    }

    [Fact]
    public async Task GetPackingOrderAsync_NotCooled_WhenCarrierMatrixEmpty()
    {
        var orderClient = BuildOrderClient(_ =>
            Json(DetailResponse("250003", PplDoRukyGuid, "PPL (do ruky)")));
        var catalog = CatalogWith(new CatalogAggregate
        {
            ProductCode = "P001",
            Properties = new CatalogProperties { Cooling = Cooling.L1 },
        });
        var sut = new ShoptetApiPackingOrderClient(orderClient, catalog, CoolingWith());

        var result = await sut.GetPackingOrderAsync("250003", CancellationToken.None);

        result!.Cooling.Should().Be(Cooling.None);
        result.IsCooled.Should().BeFalse();
    }
}
