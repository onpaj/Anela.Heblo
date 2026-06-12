using System.Net;
using System.Text;
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Expedition;

public class PickingListBatchProcessorTests
{
    private const string CooledProductCode = "PROD-COOL";
    private const string NormalProductCode = "PROD-NORMAL";
    private const string CooledOrderCode = "ORDER-COOL";
    private const string NormalOrderCode = "ORDER-NORMAL";

    private static HttpResponseMessage OkJson(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static Mock<HttpMessageHandler> BuildHandler(bool patchShouldThrow = false)
    {
        var handler = new Mock<HttpMessageHandler>();

        if (patchShouldThrow)
        {
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Patch),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Simulated Shoptet PATCH failure"));
        }
        else
        {
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Patch),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(OkJson("""{"data":null,"errors":null}"""));
        }

        return handler;
    }

    private static ShoptetOrderClient BuildClient(Mock<HttpMessageHandler> handler)
    {
        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("https://test.myshoptet.com") };
        return new ShoptetOrderClient(http, Options.Create(new ShoptetOrdersSettings()));
    }

    private static Mock<ICatalogRepository> BuildCatalog()
    {
        var catalog = new Mock<ICatalogRepository>();
        catalog.Setup(x => x.GetByIdAsync(CooledProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogAggregate
            {
                ProductCode = CooledProductCode,
                ProductName = "Cooled Product",
                Location = "A-1",
                Properties = new CatalogProperties { Cooling = Cooling.L1 },
            });
        catalog.Setup(x => x.GetByIdAsync(NormalProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogAggregate
            {
                ProductCode = NormalProductCode,
                ProductName = "Normal Product",
                Location = "B-2",
                Properties = new CatalogProperties { Cooling = Cooling.None },
            });
        return catalog;
    }

    private static ShippingMethod BuildMethod() => new()
    {
        Carrier = Carriers.Zasilkovna,
        Id = 1,
        Name = "zas",
        DisplayName = "Zásilkovna",
        MaxItems = 100,
        MaxOrders = 100,
        Guids = ["guid-1"],
    };

    private static ExpeditionOrder BuildCooledOrder() => new()
    {
        Code = CooledOrderCode,
        CustomerName = "Cooled Customer",
        Address = "Chladna 1, 10000 Praha",
        Phone = "+420111222333",
        CarrierCooling = Cooling.L1,
        Items =
        {
            new ExpeditionOrderItem
            {
                ProductCode = CooledProductCode,
                Name = "Cooled Product",
                Variant = string.Empty,
                WarehousePosition = string.Empty,
                Quantity = 1,
                Unit = "ks",
                Cooling = Cooling.L1,
            },
        },
    };

    private static ExpeditionOrder BuildNormalOrder() => new()
    {
        Code = NormalOrderCode,
        CustomerName = "Normal Customer",
        Address = "Normalni 2, 60200 Brno",
        Phone = "+420444555666",
        CarrierCooling = Cooling.None,
        Items =
        {
            new ExpeditionOrderItem
            {
                ProductCode = NormalProductCode,
                Name = "Normal Product",
                Variant = string.Empty,
                WarehousePosition = string.Empty,
                Quantity = 1,
                Unit = "ks",
                Cooling = Cooling.None,
            },
        },
    };

    private static PickingListBatchProcessor BuildProcessor(
        Mock<HttpMessageHandler> handler,
        Mock<ICatalogRepository> catalog,
        ILogger? logger = null,
        Func<ExpeditionProtocolData, byte[]>? generate = null) =>
        new(
            catalog.Object,
            BuildClient(handler),
            generate ?? (_ => new byte[] { 0x25, 0x50, 0x44, 0x46 }),
            logger ?? Mock.Of<ILogger<ShoptetApiExpeditionListSource>>());

    [Fact]
    public async Task FlushAsync_InvokesCallbackOnceWithSingleElementList()
    {
        var handler = BuildHandler();
        var catalog = BuildCatalog();
        var processor = BuildProcessor(handler, catalog);

        var callbackInvocations = new List<IList<string>>();
        Func<IList<string>, Task> callback = paths =>
        {
            callbackInvocations.Add(paths);
            return Task.CompletedTask;
        };

        var path = await processor.FlushAsync(
            new[] { BuildNormalOrder() },
            BuildMethod(),
            batchIndex: 0,
            timestamp: "20260609_120000",
            onBatchFilesReady: callback,
            cancellationToken: CancellationToken.None);

        callbackInvocations.Should().HaveCount(1);
        callbackInvocations[0].Should().ContainSingle().Which.Should().Be(path);
    }

    [Fact]
    public async Task FlushAsync_DoesNotThrow_WhenCallbackIsNull()
    {
        var handler = BuildHandler();
        var catalog = BuildCatalog();
        var processor = BuildProcessor(handler, catalog);

        var act = async () => await processor.FlushAsync(
            new[] { BuildNormalOrder() },
            BuildMethod(),
            batchIndex: 0,
            timestamp: "20260609_120000",
            onBatchFilesReady: null,
            cancellationToken: CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FlushAsync_AppliesCatalogEnrichmentToBatchItems()
    {
        ExpeditionProtocolData? captured = null;
        var handler = BuildHandler();
        var catalog = new Mock<ICatalogRepository>();
        catalog.Setup(x => x.GetByIdAsync(NormalProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogAggregate
            {
                ProductCode = NormalProductCode,
                ProductName = "Normal Product",
                Location = "AISLE-7",
                Properties = new CatalogProperties { Cooling = Cooling.L2 },
            });

        var processor = BuildProcessor(
            handler,
            catalog,
            generate: data =>
            {
                captured = data;
                return new byte[] { 0x25, 0x50, 0x44, 0x46 };
            });

        var orderWithBlankPosition = new ExpeditionOrder
        {
            Code = "ORDER-ENRICH",
            CustomerName = "Customer",
            Address = "Addr",
            Phone = "+420",
            Items =
            {
                new ExpeditionOrderItem
                {
                    ProductCode = NormalProductCode,
                    Name = "Normal Product",
                    Variant = string.Empty,
                    WarehousePosition = string.Empty,
                    Quantity = 1,
                    Unit = "ks",
                },
            },
        };

        await processor.FlushAsync(
            new[] { orderWithBlankPosition },
            BuildMethod(),
            batchIndex: 0,
            timestamp: "20260609_120000",
            onBatchFilesReady: null,
            cancellationToken: CancellationToken.None);

        captured.Should().NotBeNull();
        var item = captured!.Orders.Single().Items.Single();
        item.WarehousePosition.Should().Be("AISLE-7");
        item.Cooling.Should().Be(Cooling.L2);
    }

    [Fact]
    public async Task FlushAsync_PatchesEachCooledOrderOnce_AndSkipsNonCooled()
    {
        var handler = BuildHandler();
        var catalog = BuildCatalog();
        var processor = BuildProcessor(handler, catalog);

        await processor.FlushAsync(
            new[] { BuildCooledOrder(), BuildNormalOrder() },
            BuildMethod(),
            batchIndex: 0,
            timestamp: "20260609_120000",
            onBatchFilesReady: null,
            cancellationToken: CancellationToken.None);

        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Patch &&
                r.RequestUri!.AbsolutePath == $"/api/orders/{CooledOrderCode}/notes"),
            ItExpr.IsAny<CancellationToken>());

        handler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Patch &&
                r.RequestUri!.AbsolutePath == $"/api/orders/{NormalOrderCode}/notes"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task FlushAsync_PatchFailure_LogsWarning_AndCompletesNormally()
    {
        var logger = new Mock<ILogger<ShoptetApiExpeditionListSource>>();
        var handler = BuildHandler(patchShouldThrow: true);
        var catalog = BuildCatalog();
        var processor = BuildProcessor(handler, catalog, logger: logger.Object);

        var path = await processor.FlushAsync(
            new[] { BuildCooledOrder() },
            BuildMethod(),
            batchIndex: 0,
            timestamp: "20260609_120000",
            onBatchFilesReady: null,
            cancellationToken: CancellationToken.None);

        path.Should().NotBeNullOrEmpty();
        File.Exists(path).Should().BeTrue("PDF must be written even when PATCH fails");

        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(CooledOrderCode)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
