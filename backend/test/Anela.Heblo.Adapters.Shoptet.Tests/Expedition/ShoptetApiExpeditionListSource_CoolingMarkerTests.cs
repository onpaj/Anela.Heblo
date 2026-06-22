using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;
using Anela.Heblo.Application.Features.Logistics.Picking;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Expedition;

public class ShoptetApiExpeditionListSource_CoolingMarkerTests
{
    private const string ZasilkovnaDoRukyGuid = "f6610d4d-578d-11e9-beb1-002590dad85e";
    private const string CooledOrderCode = "TEST-COOLED";
    private const string NormalOrderCode = "TEST-NORMAL";
    private const string CooledProductCode = "PROD-COOL";
    private const string NormalProductCode = "PROD-NORMAL";

    private static Mock<IShoptetExpeditionOrderSource> BuildOrderSource(bool patchShouldThrow = false)
    {
        var mock = new Mock<IShoptetExpeditionOrderSource>();

        // GetOrdersByStatusAsync — return two orders on page 1
        mock.Setup(x => x.GetOrdersByStatusAsync(-2, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderListResponse
            {
                Data = new OrderListData
                {
                    Orders =
                    [
                        new OrderSummary
                        {
                            Code = CooledOrderCode,
                            Status = new OrderStatusSummary { Id = -2 },
                            Shipping = new OrderShippingSummary { Guid = ZasilkovnaDoRukyGuid, Name = "Zásilkovna (do ruky)" },
                            Price = new OrderPriceSummary { WithVat = 500.00m, CurrencyCode = "CZK" },
                        },
                        new OrderSummary
                        {
                            Code = NormalOrderCode,
                            Status = new OrderStatusSummary { Id = -2 },
                            Shipping = new OrderShippingSummary { Guid = ZasilkovnaDoRukyGuid, Name = "Zásilkovna (do ruky)" },
                            Price = new OrderPriceSummary { WithVat = 300.00m, CurrencyCode = "CZK" },
                        },
                    ],
                    Paginator = new Paginator { TotalCount = 2, Page = 1, PageCount = 1 },
                },
            });

        // GetExpeditionOrderDetailAsync — cooled order
        mock.Setup(x => x.GetExpeditionOrderDetailAsync(CooledOrderCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpeditionOrderDetail
            {
                Code = CooledOrderCode,
                FullName = "Cooled Customer",
                Phone = "+420111222333",
                BillingAddress = new ExpeditionAddress
                {
                    FullName = "Cooled Customer",
                    Street = "Chladna",
                    HouseNumber = "1",
                    City = "Praha",
                    Zip = "10000",
                },
                Items =
                [
                    new ExpeditionOrderItemDto
                    {
                        ItemType = "product",
                        ItemId = 1,
                        Code = CooledProductCode,
                        Name = "Cooled Product",
                        Amount = 1m,
                        Unit = "ks",
                        ItemPriceWithVat = "100.00",
                    },
                ],
                Completion = [],
            });

        // GetExpeditionOrderDetailAsync — normal order
        mock.Setup(x => x.GetExpeditionOrderDetailAsync(NormalOrderCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpeditionOrderDetail
            {
                Code = NormalOrderCode,
                FullName = "Normal Customer",
                Phone = "+420444555666",
                BillingAddress = new ExpeditionAddress
                {
                    FullName = "Normal Customer",
                    Street = "Normalni",
                    HouseNumber = "2",
                    City = "Brno",
                    Zip = "60200",
                },
                Items =
                [
                    new ExpeditionOrderItemDto
                    {
                        ItemType = "product",
                        ItemId = 2,
                        Code = NormalProductCode,
                        Name = "Normal Product",
                        Amount = 1m,
                        Unit = "ks",
                        ItemPriceWithVat = "80.00",
                    },
                ],
                Completion = [],
            });

        // SetAdditionalFieldAsync
        if (patchShouldThrow)
        {
            mock.Setup(x => x.SetAdditionalFieldAsync(
                    CooledOrderCode,
                    It.IsAny<int>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Simulated Shoptet PATCH failure"));
        }
        else
        {
            mock.Setup(x => x.SetAdditionalFieldAsync(
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        return mock;
    }

    private static ShoptetApiExpeditionListSource BuildSource(
        Mock<IShoptetExpeditionOrderSource> orderSource,
        ILogger<ShoptetApiExpeditionListSource>? logger = null,
        string? coolingText = null,
        Action<ExpeditionProtocolData>? captureData = null)
    {
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
            orderSource.Object,
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
        var orderSource = BuildOrderSource();
        var source = BuildSource(orderSource);

        await source.CreatePickingList(BuildRequest(), null, CancellationToken.None);

        orderSource.Verify(
            x => x.SetAdditionalFieldAsync(
                CooledOrderCode,
                PickingListBatchProcessor.CoolingAdditionalFieldIndex,
                PickingListBatchProcessor.CoolingMarkerValue,
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task CreatePickingList_NonCooledOrder_DoesNotPatchAdditionalField()
    {
        var orderSource = BuildOrderSource();
        var source = BuildSource(orderSource);

        await source.CreatePickingList(BuildRequest(), null, CancellationToken.None);

        orderSource.Verify(
            x => x.SetAdditionalFieldAsync(
                NormalOrderCode,
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task CreatePickingList_PatchFails_PdfStillCompletes()
    {
        var logger = new Mock<ILogger<ShoptetApiExpeditionListSource>>();
        var orderSource = BuildOrderSource(patchShouldThrow: true);
        var source = BuildSource(orderSource, logger.Object);

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
        var orderSource = BuildOrderSource();
        var source = BuildSource(orderSource, coolingText: "MRAZ", captureData: d => captured = d);

        await source.CreatePickingList(BuildRequest(), null, CancellationToken.None);

        captured.Should().NotBeNull();
        var cooledOrder = captured!.Orders.Single(o => o.Code == CooledOrderCode);
        cooledOrder.CoolingText.Should().Be("MRAZ");
    }

    [Fact]
    public async Task CreatePickingList_MultipleBatches_FilenamesContainSequentialBatchIndex()
    {
        // Two orders with 7 items each = 14 total items, exceeding MaxItems=13 for Zásilkovna,
        // forcing a mid-loop overflow flush. Locks in batchIndex pass-through behavior.
        var orderSource = new Mock<IShoptetExpeditionOrderSource>();

        var orderCodes = new[] { "BATCH-ORDER-A", "BATCH-ORDER-B" };

        orderSource.Setup(x => x.GetOrdersByStatusAsync(-2, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderListResponse
            {
                Data = new OrderListData
                {
                    Orders = orderCodes.Select(code => new OrderSummary
                    {
                        Code = code,
                        Status = new OrderStatusSummary { Id = -2 },
                        Shipping = new OrderShippingSummary { Guid = ZasilkovnaDoRukyGuid, Name = "Zásilkovna (do ruky)" },
                        Price = new OrderPriceSummary { WithVat = 300.00m, CurrencyCode = "CZK" },
                    }).ToList(),
                    Paginator = new Paginator { TotalCount = 2, Page = 1, PageCount = 1 },
                },
            });

        foreach (var code in orderCodes)
        {
            var capturedCode = code;
            orderSource.Setup(x => x.GetExpeditionOrderDetailAsync(capturedCode, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExpeditionOrderDetail
                {
                    Code = capturedCode,
                    FullName = $"Customer {capturedCode}",
                    Phone = "+420000000000",
                    BillingAddress = new ExpeditionAddress
                    {
                        FullName = $"Customer {capturedCode}",
                        Street = "Test",
                        HouseNumber = "1",
                        City = "Praha",
                        Zip = "10000",
                    },
                    Items = Enumerable.Range(1, 7).Select(i => new ExpeditionOrderItemDto
                    {
                        ItemType = "product",
                        ItemId = i,
                        Code = NormalProductCode,
                        Name = $"Item{i}",
                        Amount = 1m,
                        Unit = "ks",
                        ItemPriceWithVat = "10.00",
                    }).ToList(),
                    Completion = [],
                });
        }

        orderSource.Setup(x => x.SetAdditionalFieldAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var source = BuildSource(orderSource);
        var result = await source.CreatePickingList(BuildRequest(), null, CancellationToken.None);

        result.ExportedFiles.Should().HaveCount(2);
        result.ExportedFiles.Should().Contain(p => p.EndsWith("_0.pdf"));
        result.ExportedFiles.Should().Contain(p => p.EndsWith("_1.pdf"));
    }
}
