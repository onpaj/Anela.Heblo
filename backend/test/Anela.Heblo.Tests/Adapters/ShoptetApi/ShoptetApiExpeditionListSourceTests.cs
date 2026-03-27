using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Logistics.Picking;
using FluentAssertions;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

/// <summary>
/// Unit tests for ShoptetApiExpeditionListSource.
/// Uses a fake HttpMessageHandler to control HTTP responses without real network calls.
/// </summary>
public class ShoptetApiExpeditionListSourceTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="ShoptetApiExpeditionClient"/> backed by a handler that
    /// routes requests based on URL patterns.
    /// </summary>
    private static ShoptetApiExpeditionClient BuildClient(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var msgHandler = new FakeDelegatingHandler(handler);
        var http = new HttpClient(msgHandler) { BaseAddress = new Uri("https://fake.shoptet.cz") };
        return new ShoptetApiExpeditionClient(http);
    }

    private static HttpResponseMessage Json(object obj, HttpStatusCode status = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>
    /// Creates a single-page list response with the given orders.
    /// </summary>
    private static ExpeditionOrderListResponse SinglePageList(params (string code, int shippingId)[] orders)
    {
        return new ExpeditionOrderListResponse
        {
            Data = new ExpeditionOrderListData
            {
                Paginator = new ExpeditionPaginator { PageCount = 1 },
                Orders = orders.Select(o => new ExpeditionOrderSummary
                {
                    Code = o.code,
                    Shipping = new ExpeditionShippingSummary { Id = o.shippingId },
                }).ToList(),
            },
        };
    }

    private static ExpeditionOrderDetailResponse DetailFor(string code, int shippingId = 21)
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
                    Items = new List<ExpeditionOrderItemDto>
                    {
                        new()
                        {
                            ItemType = "product",
                            Code = "P001",
                            Name = "Widget",
                            Amount = "1",
                            ItemPriceWithVat = "99.90",
                        },
                    },
                },
            },
        };
    }

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
        // Arrange — list has one Zasilkovna (id=21) and one PPL (id=6)
        var listResp = SinglePageList(("Z001", 21), ("P001", 6));

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

        var source = new ShoptetApiExpeditionListSource(client);
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
    public async Task CreatePickingList_BatchesByPageSize_8PerBatch()
    {
        // Arrange — 9 Zasilkovna orders → expect 2 batches (8 + 1)
        var codes = Enumerable.Range(1, 9).Select(i => $"Z{i:D3}").ToArray();
        var listResp = SinglePageList(codes.Select(c => (c, 21)).ToArray());

        var batchFilesReceived = new List<IList<string>>();
        var client = BuildClient(req =>
        {
            if (req.RequestUri!.PathAndQuery.StartsWith("/api/orders?"))
                return Json(listResp);

            var code = req.RequestUri.Segments.Last();
            return Json(DetailFor(code));
        });

        var source = new ShoptetApiExpeditionListSource(client);
        var request = DefaultRequest();

        // Act
        var result = await source.CreatePickingList(request, files =>
        {
            batchFilesReceived.Add(files);
            return Task.CompletedTask;
        });

        // Assert — 2 batches of PDFs
        batchFilesReceived.Should().HaveCount(2);
        result.TotalCount.Should().Be(9);

        // Cleanup temp files
        foreach (var file in result.ExportedFiles.Where(File.Exists))
            File.Delete(file);
    }

    [Fact]
    public async Task CreatePickingList_OsobaK_1PerBatch()
    {
        // Arrange — 2 Osobak orders (id=4, PageSize=1) → expect 2 batches of 1
        var listResp = SinglePageList(("O001", 4), ("O002", 4));

        var batchFilesReceived = new List<IList<string>>();
        var client = BuildClient(req =>
        {
            if (req.RequestUri!.PathAndQuery.StartsWith("/api/orders?"))
                return Json(listResp);

            var code = req.RequestUri.Segments.Last();
            return Json(DetailFor(code, 4));
        });

        var source = new ShoptetApiExpeditionListSource(client);
        var request = DefaultRequest();

        // Act
        var result = await source.CreatePickingList(request, files =>
        {
            batchFilesReceived.Add(files);
            return Task.CompletedTask;
        });

        // Assert — 2 batches, one order each
        batchFilesReceived.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);

        // Cleanup
        foreach (var file in result.ExportedFiles.Where(File.Exists))
            File.Delete(file);
    }

    [Fact]
    public async Task CreatePickingList_CallsUpdateStatus_WhenChangeOrderStateTrue()
    {
        // Arrange
        var listResp = SinglePageList(("Z001", 21));

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

        var source = new ShoptetApiExpeditionListSource(client);
        var request = DefaultRequest(changeState: true);

        // Act
        await source.CreatePickingList(request, null);

        // Assert
        patchedCodes.Should().ContainSingle().Which.Should().Be("Z001");

        // Cleanup
    }

    [Fact]
    public async Task CreatePickingList_DoesNotCallUpdateStatus_WhenChangeOrderStateFalse()
    {
        // Arrange
        var listResp = SinglePageList(("Z001", 21));

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

        var source = new ShoptetApiExpeditionListSource(client);
        var request = DefaultRequest(changeState: false);

        // Act
        await source.CreatePickingList(request, null);

        // Assert
        patchCallCount.Should().Be(0);

        // Cleanup temp files
    }

    [Fact]
    public async Task CreatePickingList_InvokesCallback_PerBatch()
    {
        // Arrange — 3 Zasilkovna + 1 Osobak → Zasilkovna fits in 1 batch, Osobak has 1 batch → 2 total callbacks
        var listResp = SinglePageList(("Z001", 21), ("Z002", 21), ("Z003", 21), ("O001", 4));

        var callbackInvocations = new List<IList<string>>();
        var client = BuildClient(req =>
        {
            if (req.RequestUri!.PathAndQuery.StartsWith("/api/orders?"))
                return Json(listResp);

            var code = req.RequestUri.Segments.Last();
            return Json(DetailFor(code));
        });

        var source = new ShoptetApiExpeditionListSource(client);
        var request = DefaultRequest();

        // Act
        await source.CreatePickingList(request, files =>
        {
            callbackInvocations.Add(files);
            return Task.CompletedTask;
        });

        // Assert — 1 batch for Zasilkovna (3 orders < 8) + 1 batch for Osobak = 2 callbacks
        callbackInvocations.Should().HaveCount(2);
        callbackInvocations.SelectMany(x => x).Should().AllSatisfy(f => File.Exists(f).Should().BeTrue());

        // Cleanup
        foreach (var file in callbackInvocations.SelectMany(x => x).Where(File.Exists))
            File.Delete(file);
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
