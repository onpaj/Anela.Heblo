using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Logistics.Picking;
using FluentAssertions;
using Moq;
using QuestPDF.Infrastructure;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

/// <summary>
/// Unit tests for ShoptetApiExpeditionListSource.
/// Uses a fake HttpMessageHandler to control HTTP responses without real network calls.
/// </summary>
public class ShoptetApiExpeditionListSourceTests
{
    static ShoptetApiExpeditionListSourceTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="ShoptetOrderClient"/> backed by a handler that
    /// routes requests based on URL patterns.
    /// </summary>
    private static ShoptetOrderClient BuildClient(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var msgHandler = new FakeDelegatingHandler(handler);
        var http = new HttpClient(msgHandler) { BaseAddress = new Uri("https://fake.shoptet.cz") };
        return new ShoptetOrderClient(http);
    }

    private static HttpResponseMessage Json(object obj, HttpStatusCode status = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    // Known production GUIDs — must match ShiptetApiExpeditionListSource.ShippingList
    private const string ZasilkovnaDoRukyGuid = "f6610d4d-578d-11e9-beb1-002590dad85e";
    private const string PplDoRukyGuid = "2ec88ea7-3fb0-11e2-a723-705ab6a2ba75";

    /// <summary>
    /// Creates a single-page list response with the given orders.
    /// </summary>
    private static OrderListResponse SinglePageList(params (string code, string shippingGuid)[] orders)
    {
        return new OrderListResponse
        {
            Data = new OrderListData
            {
                Paginator = new Paginator { PageCount = 1 },
                Orders = orders.Select(o => new OrderSummary
                {
                    Code = o.code,
                    Shipping = new OrderShippingSummary { Guid = o.shippingGuid },
                }).ToList(),
            },
        };
    }

    private static ExpeditionOrderDetailResponse DetailFor(string code, int itemCount = 1)
    {
        return new ExpeditionOrderDetailResponse
        {
            Data = new ExpeditionOrderDetailData
            {
                Order = new ExpeditionOrderDetail
                {
                    Code = code,
                    FullName = $"Customer {code}",
                    Phone = "123",
                    Items = Enumerable.Range(1, itemCount).Select(i => new ExpeditionOrderItemDto
                    {
                        ItemType = "product",
                        Code = $"P{i:D3}",
                        Name = $"Widget {i}",
                        Amount = 1m,
                        ItemPriceWithVat = "99.90",
                    }).ToList(),
                },
            },
        };
    }

    private static ShoptetApiExpeditionListSource BuildSource(ShoptetOrderClient client) =>
        new(client, TimeProvider.System, new Mock<ICatalogRepository>().Object);

    private static PrintPickingListRequest DefaultRequest(bool changeState = false) =>
        new()
        {
            SourceStateId = 5,
            DesiredStateId = 6,
            Carriers = Array.Empty<Carriers>(),
            ChangeOrderState = changeState,
        };

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePickingList_GroupsByCarrier_OnlyZasilkovnaReturned_WhenFilterSet()
    {
        // Arrange — list has one Zasilkovna and one PPL order (identified by shipping GUID)
        var listResp = SinglePageList(("Z001", ZasilkovnaDoRukyGuid), ("P001", PplDoRukyGuid));

        var detailCallLog = new List<string>();
        var client = BuildClient(req =>
        {
            if (req.RequestUri!.PathAndQuery.StartsWith("/api/orders?"))
                return Json(listResp);

            // Detail endpoint
            var code = req.RequestUri.Segments.Last();
            detailCallLog.Add(code);
            return Json(DetailFor(code));
        });

        var source = BuildSource(client);
        var request = new PrintPickingListRequest
        {
            SourceStateId = 5,
            DesiredStateId = 6,
            Carriers = new[] { Carriers.Zasilkovna },
            ChangeOrderState = false,
        };

        // Act
        var result = await source.CreatePickingList(request, null);

        // Assert — only Zasilkovna order's detail was fetched
        detailCallLog.Should().ContainSingle().Which.Should().Be("Z001");
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task CreatePickingList_BatchesByItemCount_SplitsWhenLimitExceeded()
    {
        // Arrange — 3 orders: Z001 has 10 items, Z002 has 10 items, Z003 has 1 item.
        // Adding Z002 to Z001's batch would exceed the 15-item limit (10+10=20 > 15).
        // Expect 2 batches: [Z001] (10 items) and [Z002, Z003] (11 items).
        var listResp = SinglePageList(
            ("Z001", ZasilkovnaDoRukyGuid),
            ("Z002", ZasilkovnaDoRukyGuid),
            ("Z003", ZasilkovnaDoRukyGuid));

        var batchFilesReceived = new List<IList<string>>();
        var client = BuildClient(req =>
        {
            if (req.RequestUri!.PathAndQuery.StartsWith("/api/orders?"))
                return Json(listResp);

            var code = req.RequestUri.Segments.Last();
            // Z001 and Z002 have 10 items each; Z003 has 1 item
            var itemCount = code is "Z001" or "Z002" ? 10 : 1;
            return Json(DetailFor(code, itemCount));
        });

        var source = BuildSource(client);
        var request = DefaultRequest();

        // Act
        var result = await source.CreatePickingList(request, files =>
        {
            batchFilesReceived.Add(files);
            return Task.CompletedTask;
        });

        // Assert — 2 batches: [Z001] alone (adding Z002 would exceed 15-item limit), [Z002+Z003] together
        batchFilesReceived.Should().HaveCount(2);
        result.TotalCount.Should().Be(3);

        // Cleanup temp files
        foreach (var file in result.ExportedFiles.Where(File.Exists))
            File.Delete(file);
    }

    [Fact]
    public async Task CreatePickingList_SingleOrderExceedingItemLimit_GetsOwnBatch()
    {
        // Arrange — one order with 25 items (exceeds MaxItems=15); must still be processed
        var listResp = SinglePageList(("Z001", ZasilkovnaDoRukyGuid));

        var batchFilesReceived = new List<IList<string>>();
        var client = BuildClient(req =>
        {
            if (req.RequestUri!.PathAndQuery.StartsWith("/api/orders?"))
                return Json(listResp);
            return Json(DetailFor(req.RequestUri.Segments.Last(), itemCount: 25));
        });

        var source = BuildSource(client);

        // Act
        var result = await source.CreatePickingList(DefaultRequest(), files =>
        {
            batchFilesReceived.Add(files);
            return Task.CompletedTask;
        });

        // Assert — single order always gets its own batch even if it exceeds the item limit
        batchFilesReceived.Should().HaveCount(1);
        result.TotalCount.Should().Be(1);

        // Cleanup
        foreach (var file in result.ExportedFiles.Where(File.Exists))
            File.Delete(file);
    }

    [Fact]
    public async Task CreatePickingList_BatchesByOrderCount_SplitsWhenLimitExceeded()
    {
        // Arrange — 8 orders each with 1 item (total 8 items well under 15-item limit,
        // but 8 orders exceeds MaxOrders=7). Expect 2 batches: first 7 orders, then 1 order.
        var orders = Enumerable.Range(1, 8).Select(i => ($"Z{i:D3}", ZasilkovnaDoRukyGuid)).ToArray();
        var listResp = SinglePageList(orders);

        var batchFilesReceived = new List<IList<string>>();
        var client = BuildClient(req =>
        {
            if (req.RequestUri!.PathAndQuery.StartsWith("/api/orders?"))
                return Json(listResp);

            var code = req.RequestUri.Segments.Last();
            return Json(DetailFor(code, itemCount: 1));
        });

        var source = BuildSource(client);

        // Act
        var result = await source.CreatePickingList(DefaultRequest(), files =>
        {
            batchFilesReceived.Add(files);
            return Task.CompletedTask;
        });

        // Assert — 2 batches: first 7 orders hit the MaxOrders=7 limit, 8th order gets its own batch
        batchFilesReceived.Should().HaveCount(2);
        result.TotalCount.Should().Be(8);

        // Cleanup
        foreach (var file in result.ExportedFiles.Where(File.Exists))
            File.Delete(file);
    }

    [Fact]
    public async Task CreatePickingList_OsobaK_1PerBatch()
    {
        // Arrange — 2 Osobak orders with unknown GUID → both skipped (GUID not yet discovered)
        // TODO: once Osobak GUID is known, update this test to verify PageSize=1 batching
        // Discover via: GET /api/eshop?include=shippingMethods (match method "OSOBAK" by name)
        var listResp = SinglePageList(("O001", "unknown-guid"), ("O002", "unknown-guid"));

        var client = BuildClient(req =>
        {
            if (req.RequestUri!.PathAndQuery.StartsWith("/api/orders?"))
                return Json(listResp);

            var code = req.RequestUri.Segments.Last();
            return Json(DetailFor(code));
        });

        var source = BuildSource(client);
        var request = DefaultRequest();

        // Act
        var result = await source.CreatePickingList(request, null);

        // Assert — orders with unknown GUIDs are skipped
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task CreatePickingList_CallsUpdateStatus_WhenChangeOrderStateTrue()
    {
        // Arrange
        var listResp = SinglePageList(("Z001", ZasilkovnaDoRukyGuid));

        var patchedCodes = new List<string>();
        var client = BuildClient(req =>
        {
            if (req.RequestUri!.PathAndQuery.StartsWith("/api/orders?"))
                return Json(listResp);

            if (req.Method == HttpMethod.Patch)
            {
                // Extract code from /api/orders/{code}/status
                var segments = req.RequestUri.Segments;
                var code = segments[^2].TrimEnd('/');
                patchedCodes.Add(code);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            var orderCode = req.RequestUri.Segments.Last();
            return Json(DetailFor(orderCode));
        });

        var source = BuildSource(client);
        var request = DefaultRequest(changeState: true);

        // Act
        var result = await source.CreatePickingList(request, null);

        // Assert
        patchedCodes.Should().ContainSingle().Which.Should().Be("Z001");

        // Cleanup
        foreach (var file in result.ExportedFiles.Where(File.Exists))
            File.Delete(file);
    }

    [Fact]
    public async Task CreatePickingList_DoesNotCallUpdateStatus_WhenChangeOrderStateFalse()
    {
        // Arrange
        var listResp = SinglePageList(("Z001", ZasilkovnaDoRukyGuid));

        var patchCallCount = 0;
        var client = BuildClient(req =>
        {
            if (req.RequestUri!.PathAndQuery.StartsWith("/api/orders?"))
                return Json(listResp);

            if (req.Method == HttpMethod.Patch)
            {
                patchCallCount++;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            var code = req.RequestUri.Segments.Last();
            return Json(DetailFor(code));
        });

        var source = BuildSource(client);
        var request = DefaultRequest(changeState: false);

        // Act
        var result = await source.CreatePickingList(request, null);

        // Assert
        patchCallCount.Should().Be(0);

        // Cleanup
        foreach (var file in result.ExportedFiles.Where(File.Exists))
            File.Delete(file);
    }

    [Fact]
    public async Task CreatePickingList_InvokesCallback_PerBatch()
    {
        // Arrange — 3 Zasilkovna + 1 PPL → 1 batch each → 2 total callbacks
        var listResp = SinglePageList(
            ("Z001", ZasilkovnaDoRukyGuid),
            ("Z002", ZasilkovnaDoRukyGuid),
            ("Z003", ZasilkovnaDoRukyGuid),
            ("P001", PplDoRukyGuid));

        var callbackInvocations = new List<IList<string>>();
        var client = BuildClient(req =>
        {
            if (req.RequestUri!.PathAndQuery.StartsWith("/api/orders?"))
                return Json(listResp);

            var code = req.RequestUri.Segments.Last();
            return Json(DetailFor(code));
        });

        var source = BuildSource(client);
        var request = DefaultRequest();

        // Act
        await source.CreatePickingList(request, files =>
        {
            callbackInvocations.Add(files);
            return Task.CompletedTask;
        });

        // Assert — 1 batch for Zasilkovna (3 orders < 7) + 1 batch for PPL = 2 callbacks
        callbackInvocations.Should().HaveCount(2);
        callbackInvocations.SelectMany(x => x).Should().AllSatisfy(f => File.Exists(f).Should().BeTrue());

        // Cleanup
        foreach (var file in callbackInvocations.SelectMany(x => x).Where(File.Exists))
            File.Delete(file);
    }

    [Fact]
    public async Task CreatePickingList_PassesGeneratedPdfPathsToCallback()
    {
        // Arrange
        var callbackPaths = new List<string>();
        var listResp = SinglePageList(("Z001", ZasilkovnaDoRukyGuid));
        var client = BuildClient(req =>
        {
            if (req.RequestUri!.PathAndQuery.StartsWith("/api/orders?"))
                return Json(listResp);
            return Json(DetailFor(req.RequestUri.Segments.Last()));
        });

        var source = BuildSource(client);

        // Act
        var result = await source.CreatePickingList(DefaultRequest(), files =>
        {
            callbackPaths.AddRange(files);
            return Task.CompletedTask;
        });

        // Assert — callback received the same paths as the exported files
        result.ExportedFiles.Should().NotBeEmpty();
        callbackPaths.Should().BeEquivalentTo(result.ExportedFiles);

        // Cleanup
        foreach (var file in result.ExportedFiles.Where(File.Exists))
            File.Delete(file);
    }

    [Fact]
    public void MapOrderItems_GiftItemType_IncludedLikeProduct()
    {
        // "Darek" (gift) items have itemType="gift" — they must appear on the expedition list
        // the same as regular products, not be silently dropped.
        var detail = new ExpeditionOrderDetail
        {
            Code = "126000038",
            Items = new List<ExpeditionOrderItemDto>
            {
                new()
                {
                    ItemType = "product",
                    Code = "OCH009030",
                    Name = "Klidný dech balzám",
                    Amount = 1m,
                    ItemPriceWithVat = "340.00",
                },
                new()
                {
                    ItemType = "gift",
                    Code = "DAR001",
                    Name = "Darek",
                    Amount = 1m,
                    ItemPriceWithVat = "0.00",
                },
            },
            Completion = new List<ExpeditionCompletionItemDto>(),
        };

        var items = ShoptetApiExpeditionListSource.MapOrderItems(detail);

        items.Should().HaveCount(2);
        items.Should().ContainSingle(i => i.ProductCode == "DAR001" && i.Name == "Darek");
    }

    [Fact]
    public async Task CreatePickingList_WritesPdfsToSystemTempFolder()
    {
        // Arrange
        var listResp = SinglePageList(("Z001", ZasilkovnaDoRukyGuid));
        var client = BuildClient(req =>
        {
            if (req.RequestUri!.PathAndQuery.StartsWith("/api/orders?"))
                return Json(listResp);
            return Json(DetailFor(req.RequestUri.Segments.Last()));
        });

        var source = BuildSource(client);

        // Act
        var result = await source.CreatePickingList(DefaultRequest(), null);

        // Assert — files land in system temp directory
        result.ExportedFiles.Should().NotBeEmpty();
        result.ExportedFiles.Should().AllSatisfy(f =>
            Path.GetDirectoryName(f).Should().Be(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));

        // Cleanup
        foreach (var file in result.ExportedFiles.Where(File.Exists))
            File.Delete(file);
    }

    [Fact]
    public void ExpeditionOrder_IsCooled_False_WhenAllItemsHaveCoolingNone()
    {
        // Arrange
        var order = new ExpeditionOrder
        {
            Code = "ORD001",
            CustomerName = "Test",
            Address = "Praha",
            Phone = "123",
            CarrierCooling = Cooling.L2,
            Items = new List<ExpeditionOrderItem>
            {
                new() { ProductCode = "P001", Name = "A", Variant = string.Empty, WarehousePosition = "A1", Quantity = 1, Cooling = Cooling.None },
                new() { ProductCode = "P002", Name = "B", Variant = string.Empty, WarehousePosition = "A2", Quantity = 1, Cooling = Cooling.None },
            },
        };

        // Act + Assert
        order.IsCooled.Should().BeFalse();
    }

    [Fact]
    public void ExpeditionOrder_IsCooled_True_WhenAnyItemHasCoolingL1()
    {
        // Arrange
        var order = new ExpeditionOrder
        {
            Code = "ORD002",
            CustomerName = "Test",
            Address = "Praha",
            Phone = "123",
            CarrierCooling = Cooling.L1,
            Items = new List<ExpeditionOrderItem>
            {
                new() { ProductCode = "P001", Name = "A", Variant = string.Empty, WarehousePosition = "A1", Quantity = 1, Cooling = Cooling.None },
                new() { ProductCode = "P002", Name = "B", Variant = string.Empty, WarehousePosition = "A2", Quantity = 1, Cooling = Cooling.L1 },
            },
        };

        // Act + Assert
        order.IsCooled.Should().BeTrue();
    }

    [Fact]
    public void ExpeditionOrder_IsCooled_True_WhenAnyItemHasCoolingL2()
    {
        // Arrange
        var order = new ExpeditionOrder
        {
            Code = "ORD003",
            CustomerName = "Test",
            Address = "Praha",
            Phone = "123",
            CarrierCooling = Cooling.L2,
            Items = new List<ExpeditionOrderItem>
            {
                new() { ProductCode = "P001", Name = "A", Variant = string.Empty, WarehousePosition = "A1", Quantity = 1, Cooling = Cooling.L2 },
            },
        };

        // Act + Assert
        order.IsCooled.Should().BeTrue();
    }

    // ─── IsCooled truth table (carrier-aware) ─────────────────────────────────────

    [Theory]
    // Carrier None → never show ribbon regardless of product cooling
    [InlineData(Cooling.None, Cooling.None,  false)]
    [InlineData(Cooling.None, Cooling.L1,   false)]
    [InlineData(Cooling.None, Cooling.L2,   false)]
    // Carrier L1 → only L1 products trigger ribbon (L2 > L1 so does NOT match)
    [InlineData(Cooling.L1,   Cooling.None,  false)]
    [InlineData(Cooling.L1,   Cooling.L1,   true)]
    [InlineData(Cooling.L1,   Cooling.L2,   false)]
    // Carrier L2 → L1 and L2 products both trigger ribbon
    [InlineData(Cooling.L2,   Cooling.None,  false)]
    [InlineData(Cooling.L2,   Cooling.L1,   true)]
    [InlineData(Cooling.L2,   Cooling.L2,   true)]
    public void IsCooled_MatchesCarrierAwareRule(Cooling carrierCooling, Cooling itemCooling, bool expected)
    {
        var order = new ExpeditionOrder
        {
            Code = "ORD001",
            CustomerName = "Test",
            Address = "Praha",
            Phone = "123",
            CarrierCooling = carrierCooling,
            Items = [new ExpeditionOrderItem { ProductCode = "P001", Name = "A", Variant = string.Empty, WarehousePosition = "A1", Quantity = 1, Cooling = itemCooling }],
        };

        order.IsCooled.Should().Be(expected);
    }

    [Fact]
    public void IsCooled_True_WhenAtLeastOneItemMatchesCarrierLevel()
    {
        // L2 carrier, two items: L2 item and None item — the L2 item qualifies
        var order = new ExpeditionOrder
        {
            Code = "ORD002",
            CustomerName = "Test",
            Address = "Praha",
            Phone = "123",
            CarrierCooling = Cooling.L2,
            Items =
            [
                new() { ProductCode = "P001", Name = "A", Variant = string.Empty, WarehousePosition = "A1", Quantity = 1, Cooling = Cooling.None },
                new() { ProductCode = "P002", Name = "B", Variant = string.Empty, WarehousePosition = "A2", Quantity = 1, Cooling = Cooling.L2 },
            ],
        };

        order.IsCooled.Should().BeTrue();
    }

    [Fact]
    public async Task CreatePickingList_EnrichesCooling_FromCatalog()
    {
        // Arrange — one Zasilkovna order with product P001; catalog returns Cooling=L1 for P001
        var listResp = SinglePageList(("Z001", ZasilkovnaDoRukyGuid));

        var client = BuildClient(req =>
        {
            if (req.RequestUri!.PathAndQuery.StartsWith("/api/orders?"))
                return Json(listResp);
            return Json(DetailFor(req.RequestUri.Segments.Last()));  // P001 item
        });

        var catalogMock = new Mock<ICatalogRepository>();
        catalogMock
            .Setup(c => c.GetByIdAsync("P001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogAggregate
            {
                ProductCode = "P001",
                Properties = new CatalogProperties { Cooling = Cooling.L1 },
            });

        var source = new ShoptetApiExpeditionListSource(client, TimeProvider.System, catalogMock.Object);

        // Act
        var result = await source.CreatePickingList(DefaultRequest(), null);

        // Assert
        catalogMock.Verify(c => c.GetByIdAsync("P001", It.IsAny<CancellationToken>()), Times.Once);

        // Cleanup
        foreach (var file in result.ExportedFiles.Where(File.Exists))
            File.Delete(file);
    }

    [Fact]
    public void ApplyEnrichment_SetsCooling_FromDictionary()
    {
        // Arrange
        var item = new ExpeditionOrderItem
        {
            ProductCode = "P001",
            Name = "Test",
            Variant = string.Empty,
            WarehousePosition = "A1",
            Quantity = 1,
        };
        var coolingByCode = new Dictionary<string, Cooling> { ["P001"] = Cooling.L1 };

        // Act
        ShoptetApiExpeditionListSource.ApplyEnrichment(
            new[] { item },
            stockByCode: new Dictionary<string, decimal>(),
            locationByCode: new Dictionary<string, string>(),
            coolingByCode: coolingByCode);

        // Assert
        item.Cooling.Should().Be(Cooling.L1);
    }

    [Fact]
    public void ApplyEnrichment_LeavesDefaultCooling_WhenProductNotInCoolingDictionary()
    {
        // Arrange
        var item = new ExpeditionOrderItem
        {
            ProductCode = "P999",
            Name = "Unknown",
            Variant = string.Empty,
            WarehousePosition = "A1",
            Quantity = 1,
        };

        // Act
        ShoptetApiExpeditionListSource.ApplyEnrichment(
            new[] { item },
            stockByCode: new Dictionary<string, decimal>(),
            locationByCode: new Dictionary<string, string>(),
            coolingByCode: new Dictionary<string, Cooling>());

        // Assert
        item.Cooling.Should().Be(Cooling.None);
    }

    // ─── ResolveDeliveryHandling ──────────────────────────────────────────────────

    [Theory]
    [InlineData("ZASILKOVNA_DO_RUKY")]
    [InlineData("PPL_DO_RUKY")]
    [InlineData("PPL_DO_RUKY_CHLAZENY")]
    [InlineData("ZASILKOVNA_DO_RUKY_SK")]
    [InlineData("ZASILKOVNA_DO_RUKY_SK_CHLAZENY")]
    [InlineData("GLS_DO_RUKY")]
    public void ResolveDeliveryHandling_ReturnsNaRuky_ForDoRukyMethods(string name)
    {
        var method = new ShippingMethod { Carrier = Carriers.PPL, Name = name, Guids = [] };
        ShippingMethodRegistry.ResolveDeliveryHandling(method).Should().Be(DeliveryHandling.NaRuky);
    }

    [Theory]
    [InlineData("PPL_PARCELSHOP")]
    [InlineData("PPL_PARCELSHOP_CHLAZENY")]
    [InlineData("ZASILKOVNA_ZPOINT")]
    [InlineData("ZASILKOVNA_ZPOINT_CHLAZENY")]
    [InlineData("ZASILKOVNA_ZPOINT_ZDARMA")]
    [InlineData("ZASILKOVNA_ZPOINT_CHLAZENY_ZDARMA")]
    [InlineData("GLS_PARCELSHOP")]
    public void ResolveDeliveryHandling_ReturnsBox_ForParcelshopAndZpointMethods(string name)
    {
        var method = new ShippingMethod { Carrier = Carriers.PPL, Name = name, Guids = [] };
        ShippingMethodRegistry.ResolveDeliveryHandling(method).Should().Be(DeliveryHandling.Box);
    }

    [Theory]
    [InlineData("PPL_EXPORT")]
    [InlineData("PPL_EXPORT_CHLAZENY")]
    [InlineData("GLS_EXPORT")]
    [InlineData("OSOBAK")]
    public void ResolveDeliveryHandling_ReturnsNull_ForExportAndOsobakMethods(string name)
    {
        var method = new ShippingMethod { Carrier = Carriers.PPL, Name = name, Guids = [] };
        ShippingMethodRegistry.ResolveDeliveryHandling(method).Should().BeNull();
    }
}

/// <summary>
/// Minimal delegating handler that routes requests through a user-supplied function.
/// </summary>
internal class FakeDelegatingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(handler(request));
}
