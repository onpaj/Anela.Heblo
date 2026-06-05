using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        return new ShoptetOrderClient(http, Options.Create(new ShoptetOrdersSettings()));
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

    private static ShoptetApiPackingOrderClient BuildSut(
        ShoptetOrderClient orderClient,
        ICatalogRepository catalog,
        ICarrierCoolingRepository cooling,
        int defaultWeightGrams = 500)
    {
        var settings = Options.Create(new ShoptetApiSettings { DefaultItemWeightGrams = defaultWeightGrams });
        var logger = NullLogger<ShoptetApiPackingOrderClient>.Instance;
        return new ShoptetApiPackingOrderClient(orderClient, catalog, cooling, logger, settings);
    }

    [Fact]
    public async Task GetPackingOrderAsync_ReturnsNull_WhenOrderNotFound()
    {
        var orderClient = BuildOrderClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = BuildSut(orderClient, CatalogWith(), CoolingWith());

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
        var sut = BuildSut(orderClient, catalog, CoolingWith());

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
        var sut = BuildSut(orderClient, catalog, cooling);

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
        var sut = BuildSut(orderClient, catalog, CoolingWith());

        var result = await sut.GetPackingOrderAsync("250003", CancellationToken.None);

        result!.Cooling.Should().Be(Cooling.None);
        result.IsCooled.Should().BeFalse();
    }

    [Fact]
    public async Task GetPackingOrderAsync_MapsCustomerAndEshopNotes()
    {
        var detail = DetailResponse("250004", PplDoRukyGuid, "PPL (do ruky)");
        detail.Data.Order.Notes = new OrderNotes
        {
            CustomerRemark = "Prosím zabalit jako dárek",
            EshopRemark = "Stálý zákazník",
        };
        var orderClient = BuildOrderClient(_ => Json(detail));
        var sut = BuildSut(orderClient, CatalogWith(), CoolingWith());

        var result = await sut.GetPackingOrderAsync("250004", CancellationToken.None);

        result!.CustomerNote.Should().Be("Prosím zabalit jako dárek");
        result.EshopNote.Should().Be("Stálý zákazník");
    }

    [Fact]
    public async Task GetPackingOrderAsync_LeavesNotesNull_WhenAbsent()
    {
        var orderClient = BuildOrderClient(_ =>
            Json(DetailResponse("250005", PplDoRukyGuid, "PPL (do ruky)")));
        var sut = BuildSut(orderClient, CatalogWith(), CoolingWith());

        var result = await sut.GetPackingOrderAsync("250005", CancellationToken.None);

        result!.CustomerNote.Should().BeNull();
        result.EshopNote.Should().BeNull();
    }

    [Fact]
    public async Task GetPackingOrderAsync_MapsOrderStatusId()
    {
        // The status is read from the base /api/orders/{code} endpoint (no ?include),
        // so route the fake handler: ?include= -> expedition detail, plain -> status.
        var orderClient = BuildOrderClient(req =>
            req.RequestUri!.Query.Contains("include")
                ? Json(DetailResponse("250006", PplDoRukyGuid, "PPL (do ruky)"))
                : Json(new { data = new { order = new { code = "250006", status = new { id = 26 } } } }));
        var sut = BuildSut(orderClient, CatalogWith(), CoolingWith());

        var result = await sut.GetPackingOrderAsync("250006", CancellationToken.None);

        result!.StatusId.Should().Be(26);
    }

    [Fact]
    public async Task GetPackingOrderAsync_PopulatesWeightGramsFromCatalogGrossWeight()
    {
        // Arrange — catalog has both GrossWeight and NetWeight; GrossWeight should win.
        var catalog = CatalogWith(new CatalogAggregate
        {
            ProductCode = "P001",
            GrossWeight = 350.0,
            NetWeight = 300.0,
            Properties = new CatalogProperties { Cooling = Cooling.None },
        });
        var orderClient = BuildOrderClient(_ =>
            Json(DetailResponse("250007", PplDoRukyGuid, "PPL (do ruky)")));
        var sut = BuildSut(orderClient, catalog, CoolingWith());

        // Act
        var result = await sut.GetPackingOrderAsync("250007", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Items.Should().ContainSingle();
        result.Items[0].WeightGrams.Should().Be(350); // GrossWeight = 350g wins
    }

    [Fact]
    public async Task GetPackingOrderAsync_FallsBackToNetWeightWhenGrossWeightIsNull()
    {
        // Arrange — catalog has only NetWeight.
        var catalog = CatalogWith(new CatalogAggregate
        {
            ProductCode = "P001",
            GrossWeight = null,
            NetWeight = 250.0,
            Properties = new CatalogProperties { Cooling = Cooling.None },
        });
        var orderClient = BuildOrderClient(_ =>
            Json(DetailResponse("250008", PplDoRukyGuid, "PPL (do ruky)")));
        var sut = BuildSut(orderClient, catalog, CoolingWith());

        // Act
        var result = await sut.GetPackingOrderAsync("250008", CancellationToken.None);

        // Assert
        result!.Items[0].WeightGrams.Should().Be(250); // NetWeight fallback
    }

    [Fact]
    public async Task GetPackingOrderAsync_FallsBackToDefaultWeightWhenCatalogHasNoWeight()
    {
        // Arrange — catalog entry has neither GrossWeight nor NetWeight.
        var catalog = CatalogWith(new CatalogAggregate
        {
            ProductCode = "P001",
            GrossWeight = null,
            NetWeight = null,
            Properties = new CatalogProperties { Cooling = Cooling.None },
        });
        var orderClient = BuildOrderClient(_ =>
            Json(DetailResponse("250009", PplDoRukyGuid, "PPL (do ruky)")));
        var sut = BuildSut(orderClient, catalog, CoolingWith());

        // Act
        var result = await sut.GetPackingOrderAsync("250009", CancellationToken.None);

        // Assert
        result!.Items[0].WeightGrams.Should().Be(500); // DefaultItemWeightGrams fallback
    }
}
