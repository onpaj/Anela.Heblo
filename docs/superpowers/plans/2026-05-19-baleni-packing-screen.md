# Balení Packing Screen Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the `/baleni/baleni` placeholder with a packing screen where a packer scans an order number and sees the order's header, items, shipping method, and cooling status on a single landscape screen.

**Architecture:** A new MediatR query (`GetPackingOrder`) exposed at `GET /api/shoptet-orders/{code}/packing`. A new adapter class `ShoptetApiPackingOrderClient` fetches the Shoptet order, reuses the existing expedition mapper (`MapToExpeditionOrder`, `ResolveCarrierCooling`, `ApplyEnrichment`) to expand product sets and compute cooling, and resolves product images from the catalog. The frontend reuses the existing `ScanInput` component and renders the order panel with an automatic photo-grid / dense-list switch.

**Tech Stack:** .NET 8, MediatR, MVC controllers, xUnit + FluentAssertions + Moq; React + TypeScript, @tanstack/react-query, Tailwind, Vitest.

**Spec:** `docs/superpowers/specs/2026-05-19-baleni-packing-screen-design.md`

---

## File Structure

**Backend — create:**
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs` — abstraction + `PackingOrder` / `PackingOrderItem` contract classes
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderHandler.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs`
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiPackingOrderClientTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/GetPackingOrderHandlerTests.cs`

**Backend — modify:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/Model/ExpeditionOrderDetailResponse.cs` — add `Shipping` to `ExpeditionOrderDetail`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` — change `MapToExpeditionOrder` from `private static` to `internal static`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` — register `IPackingOrderClient`
- `backend/src/Anela.Heblo.API/Controllers/ShoptetOrdersController.cs` — add `GET {code}/packing` endpoint

**Frontend — create:**
- `frontend/src/api/hooks/usePackingOrder.ts` — react-query hook + types
- `frontend/src/components/baleni/BaleniPacking.tsx` — packing page
- `frontend/src/components/baleni/PackingOrderMeta.tsx` — order meta block + cooling badge
- `frontend/src/components/baleni/PackingItems.tsx` — item area (photo grid vs dense list)
- `frontend/src/components/baleni/__tests__/BaleniPacking.test.tsx`
- `frontend/src/components/baleni/__tests__/PackingItems.test.tsx`
- `frontend/test/e2e/baleni/packing.spec.ts`

**Frontend — modify:**
- `frontend/src/App.tsx` — route `/baleni/baleni` to `BaleniPacking`

---

## Task 1: Expose shipping on the order detail model and unlock the mapper

The packing client needs the order's shipping method (GUID for cooling, name for display). The Shoptet `GET /api/orders/{code}` response includes a `shipping` object, but `ExpeditionOrderDetail` does not map it. It also needs `MapToExpeditionOrder`, which is currently `private`.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/Model/ExpeditionOrderDetailResponse.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs:201`

- [ ] **Step 1: Add the `Shipping` property to `ExpeditionOrderDetail`**

In `ExpeditionOrderDetailResponse.cs`, inside the `ExpeditionOrderDetail` class, add this property after the `Notes` property (the file already has `using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;`, so `OrderShippingSummary` resolves):

```csharp
    [JsonPropertyName("shipping")]
    public OrderShippingSummary? Shipping { get; set; }
```

- [ ] **Step 2: Make `MapToExpeditionOrder` reusable**

In `ShoptetApiExpeditionListSource.cs`, change line 201 from:

```csharp
    private static ExpeditionOrder MapToExpeditionOrder(Model.ExpeditionOrderDetail detail)
```

to:

```csharp
    internal static ExpeditionOrder MapToExpeditionOrder(Model.ExpeditionOrderDetail detail)
```

- [ ] **Step 3: Verify the project compiles**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition
git commit -m "feat(baleni): expose order shipping and reuse expedition mapper"
```

---

## Task 2: Define the packing order abstraction and contracts

A MediatR handler in the Application layer cannot see adapter internals (`GetExpeditionOrderDetailAsync`, `ExpeditionOrderDetail`). Define an interface in the Application layer that the adapter implements.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs`

- [ ] **Step 1: Create the interface and contract classes**

```csharp
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.ShoptetOrders;

/// <summary>
/// Loads a single eshop order enriched with cooling status and product images,
/// ready for the Balení packing screen.
/// </summary>
public interface IPackingOrderClient
{
    /// <summary>
    /// Returns the packing view of the order, or null if no order exists for the code.
    /// </summary>
    Task<PackingOrder?> GetPackingOrderAsync(string code, CancellationToken ct = default);
}

/// <summary>Packing view of an eshop order. Internal contract — not an API DTO.</summary>
public class PackingOrder
{
    public string Code { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ShippingMethodName { get; set; } = string.Empty;
    public Cooling Cooling { get; set; } = Cooling.None;
    public bool IsCooled { get; set; }
    public List<PackingOrderItem> Items { get; set; } = new();
}

/// <summary>A single line on the packing screen. Also serialized in the API response.</summary>
public class PackingOrderItem
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }

    /// <summary>Product image URL from the catalog; null when unavailable.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Parent set name when this item is a product-set component; null otherwise.</summary>
    public string? SetName { get; set; }
}
```

- [ ] **Step 2: Verify the project compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs
git commit -m "feat(baleni): add IPackingOrderClient abstraction"
```

---

## Task 3: Implement and test the Shoptet packing order adapter

`ShoptetApiPackingOrderClient` fetches the order, reuses the expedition mapper to expand product sets, computes cooling from the carrier matrix + per-product catalog cooling, and resolves product images.

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs`
- Test: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiPackingOrderClientTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `ShoptetApiPackingOrderClientTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter ShoptetApiPackingOrderClientTests`
Expected: FAIL — `ShoptetApiPackingOrderClient` does not exist (compile error).

- [ ] **Step 3: Implement `ShoptetApiPackingOrderClient`**

Create `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs`:

```csharp
using System.Net;
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

/// <summary>
/// Loads a single Shoptet order for the Balení packing screen. Reuses the expedition
/// mapper to expand product sets and compute carrier-aware cooling status.
/// </summary>
public class ShoptetApiPackingOrderClient : IPackingOrderClient
{
    // ShoptetOrderClient is the only IEshopOrderClient implementation — safe to cast,
    // mirroring ShoptetApiExpeditionListSource, to reach expedition-specific methods.
    private readonly ShoptetOrderClient _orderClient;
    private readonly ICatalogRepository _catalog;
    private readonly ICarrierCoolingRepository _carrierCooling;

    public ShoptetApiPackingOrderClient(
        IEshopOrderClient orderClient,
        ICatalogRepository catalog,
        ICarrierCoolingRepository carrierCooling)
    {
        _orderClient = (ShoptetOrderClient)orderClient;
        _catalog = catalog;
        _carrierCooling = carrierCooling;
    }

    public async Task<PackingOrder?> GetPackingOrderAsync(string code, CancellationToken ct = default)
    {
        ExpeditionOrderDetail detail;
        try
        {
            detail = await _orderClient.GetExpeditionOrderDetailAsync(code, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var order = ShoptetApiExpeditionListSource.MapToExpeditionOrder(detail);

        // Carrier cooling — resolve from the (carrier, delivery handling) matrix.
        var settings = await _carrierCooling.GetAllAsync(ct);
        var matrix = settings.ToDictionary(s => (s.Carrier, s.DeliveryHandling), s => s.Cooling);
        order.CarrierCooling = ShoptetApiExpeditionListSource.ResolveCarrierCooling(
            detail.Shipping?.Guid ?? string.Empty, matrix);

        // Per-product cooling and images from the catalog.
        var productCodes = order.Items.Select(i => i.ProductCode).Distinct().ToList();
        var catalogItems = await _catalog.GetByIdsAsync(productCodes, ct);
        var coolingByCode = catalogItems.ToDictionary(kv => kv.Key, kv => kv.Value.Properties.Cooling);

        ShoptetApiExpeditionListSource.ApplyEnrichment(
            order.Items,
            new Dictionary<string, decimal>(),
            new Dictionary<string, string>(),
            coolingByCode);

        var items = order.Items.Select(i => new PackingOrderItem
        {
            Name = i.Name,
            Quantity = i.Quantity,
            ImageUrl = catalogItems.TryGetValue(i.ProductCode, out var c) ? c.Image : null,
            SetName = i.IsFromSet ? i.SetName : null,
        }).ToList();

        return new PackingOrder
        {
            Code = order.Code,
            CustomerName = order.CustomerName,
            ShippingMethodName = detail.Shipping?.Name ?? string.Empty,
            Cooling = order.CarrierCooling,
            IsCooled = order.IsCooled,
            Items = items,
        };
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter ShoptetApiPackingOrderClientTests`
Expected: PASS — 4 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiPackingOrderClientTests.cs
git commit -m "feat(baleni): add Shoptet packing order adapter"
```

---

## Task 4: Register the packing order client in DI

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs:57`

- [ ] **Step 1: Register the service**

In `AddShoptetApiAdapter`, immediately after the line `services.AddTransient<IPickingListSource, ShoptetApiExpeditionListSource>();`, add:

```csharp
        services.AddTransient<IPackingOrderClient, ShoptetApiPackingOrderClient>();
```

The file already has `using Anela.Heblo.Adapters.ShoptetApi.Orders;` and `using Anela.Heblo.Application.Features.ShoptetOrders;`.

- [ ] **Step 2: Verify the project compiles**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs
git commit -m "feat(baleni): register packing order client"
```

---

## Task 5: Implement and test the GetPackingOrder query handler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/GetPackingOrderHandlerTests.cs`

- [ ] **Step 1: Create the request and response types**

`GetPackingOrderRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;

public class GetPackingOrderRequest : IRequest<GetPackingOrderResponse>
{
    public string Code { get; set; } = null!;
}
```

`GetPackingOrderResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;

public class GetPackingOrderResponse : BaseResponse
{
    public GetPackingOrderResponse()
    {
    }

    public GetPackingOrderResponse(ErrorCodes errorCode, Dictionary<string, string>? @params = null)
        : base(errorCode, @params)
    {
    }

    public string Code { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ShippingMethodName { get; set; } = string.Empty;
    public Cooling Cooling { get; set; } = Cooling.None;
    public bool IsCooled { get; set; }
    public List<PackingOrderItem> Items { get; set; } = new();
}
```

- [ ] **Step 2: Write the failing handler tests**

Create `GetPackingOrderHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Application.ShoptetOrders;

public class GetPackingOrderHandlerTests
{
    private readonly Mock<IPackingOrderClient> _clientMock = new();

    private GetPackingOrderHandler CreateHandler() =>
        new(_clientMock.Object, NullLogger<GetPackingOrderHandler>.Instance);

    [Fact]
    public async Task Handle_OrderFound_ReturnsMappedResponse()
    {
        _clientMock
            .Setup(c => c.GetPackingOrderAsync("250001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackingOrder
            {
                Code = "250001",
                CustomerName = "Jan Novák",
                ShippingMethodName = "PPL (do ruky)",
                Cooling = Cooling.L1,
                IsCooled = true,
                Items = new List<PackingOrderItem>
                {
                    new() { Name = "Krém", Quantity = 2, ImageUrl = "https://img/p.jpg" },
                },
            });

        var response = await CreateHandler().Handle(
            new GetPackingOrderRequest { Code = "250001" }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Code.Should().Be("250001");
        response.CustomerName.Should().Be("Jan Novák");
        response.ShippingMethodName.Should().Be("PPL (do ruky)");
        response.Cooling.Should().Be(Cooling.L1);
        response.IsCooled.Should().BeTrue();
        response.Items.Should().ContainSingle().Which.Name.Should().Be("Krém");
    }

    [Fact]
    public async Task Handle_OrderNotFound_ReturnsShoptetOrderNotFound()
    {
        _clientMock
            .Setup(c => c.GetPackingOrderAsync("999999", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackingOrder?)null);

        var response = await CreateHandler().Handle(
            new GetPackingOrderRequest { Code = "999999" }, CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShoptetOrderNotFound);
        response.Params.Should().ContainKey("orderCode").WhoseValue.Should().Be("999999");
    }

    [Fact]
    public async Task Handle_ClientThrows_ReturnsInternalServerError()
    {
        _clientMock
            .Setup(c => c.GetPackingOrderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet unavailable"));

        var response = await CreateHandler().Handle(
            new GetPackingOrderRequest { Code = "250001" }, CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter GetPackingOrderHandlerTests`
Expected: FAIL — `GetPackingOrderHandler` does not exist (compile error).

- [ ] **Step 4: Implement the handler**

Create `GetPackingOrderHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;

public class GetPackingOrderHandler : IRequestHandler<GetPackingOrderRequest, GetPackingOrderResponse>
{
    private readonly IPackingOrderClient _client;
    private readonly ILogger<GetPackingOrderHandler> _logger;

    public GetPackingOrderHandler(
        IPackingOrderClient client,
        ILogger<GetPackingOrderHandler> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<GetPackingOrderResponse> Handle(
        GetPackingOrderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var order = await _client.GetPackingOrderAsync(request.Code, cancellationToken);

            if (order == null)
            {
                return new GetPackingOrderResponse(
                    ErrorCodes.ShoptetOrderNotFound,
                    new Dictionary<string, string> { { "orderCode", request.Code } });
            }

            return new GetPackingOrderResponse
            {
                Code = order.Code,
                CustomerName = order.CustomerName,
                ShippingMethodName = order.ShippingMethodName,
                Cooling = order.Cooling,
                IsCooled = order.IsCooled,
                Items = order.Items,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load packing order {OrderCode}", request.Code);
            return new GetPackingOrderResponse(ErrorCodes.InternalServerError);
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter GetPackingOrderHandlerTests`
Expected: PASS — 3 tests.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/GetPackingOrderHandlerTests.cs
git commit -m "feat(baleni): add GetPackingOrder query handler"
```

---

## Task 6: Add the packing endpoint to the controller

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/ShoptetOrdersController.cs`

- [ ] **Step 1: Add the endpoint**

Add this `using` at the top of the file, next to the existing `BlockOrderProcessing` using:

```csharp
using Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;
```

Add this method inside `ShoptetOrdersController`, after the `BlockOrder` method:

```csharp
    /// <summary>
    /// Loads a single order for the Balení packing screen: header, customer, shipping
    /// method, cooling status, and expanded item list with product images.
    /// </summary>
    [HttpGet("{code}/packing")]
    public async Task<ActionResult<GetPackingOrderResponse>> GetPackingOrder(string code)
    {
        var response = await _mediator.Send(new GetPackingOrderRequest { Code = code });
        return HandleResponse(response);
    }
```

- [ ] **Step 2: Verify the backend builds and format is clean**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: Build succeeded, 0 errors.
Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes` (if it reports changes, run `dotnet format backend/Anela.Heblo.sln` and re-verify).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/ShoptetOrdersController.cs
git commit -m "feat(baleni): expose GET shoptet-orders/{code}/packing endpoint"
```

---

## Task 7: Create the frontend packing order hook

**Files:**
- Create: `frontend/src/api/hooks/usePackingOrder.ts`

- [ ] **Step 1: Implement the hook**

```typescript
import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

export type Cooling = 'None' | 'L1' | 'L2';

export interface PackingOrderItem {
  name: string;
  quantity: number;
  imageUrl: string | null;
  setName: string | null;
}

export interface PackingOrder {
  code: string;
  customerName: string;
  shippingMethodName: string;
  cooling: Cooling;
  isCooled: boolean;
  items: PackingOrderItem[];
}

/** Thrown when the scanned order code does not exist in Shoptet. */
export class PackingOrderNotFoundError extends Error {
  constructor(code: string) {
    super(`Order not found: ${code}`);
    this.name = 'PackingOrderNotFoundError';
  }
}

const fetchPackingOrder = async (code: string): Promise<PackingOrder> => {
  const apiClient = getAuthenticatedApiClient();
  const fullUrl = `${(apiClient as any).baseUrl}/api/shoptet-orders/${encodeURIComponent(code)}/packing`;
  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'GET',
    headers: { Accept: 'application/json' },
  });

  if (response.status === 404) {
    throw new PackingOrderNotFoundError(code);
  }
  if (!response.ok) {
    throw new Error(`Failed to load packing order: ${response.status}`);
  }
  return response.json();
};

/** Loads a packing order by scanned code. Disabled until a code is provided. */
export const usePackingOrder = (code: string | null) =>
  useQuery({
    queryKey: ['packingOrder', code],
    queryFn: () => fetchPackingOrder(code as string),
    enabled: !!code,
    retry: false,
    gcTime: 0,
  });
```

- [ ] **Step 2: Verify it type-checks**

Run: `cd frontend && npx tsc --noEmit`
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/usePackingOrder.ts
git commit -m "feat(baleni): add usePackingOrder hook"
```

---

## Task 8: Build and test the PackingItems component

`PackingItems` renders the item area: a photo grid for small orders, a dense 2-column list once the count exceeds `PHOTO_ITEM_LIMIT`.

**Files:**
- Create: `frontend/src/components/baleni/PackingItems.tsx`
- Test: `frontend/src/components/baleni/__tests__/PackingItems.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `PackingItems.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react';
import PackingItems, { PHOTO_ITEM_LIMIT } from '../PackingItems';
import type { PackingOrderItem } from '../../../api/hooks/usePackingOrder';

const makeItems = (count: number): PackingOrderItem[] =>
  Array.from({ length: count }, (_, i) => ({
    name: `Produkt ${i + 1}`,
    quantity: i + 1,
    imageUrl: null,
    setName: null,
  }));

describe('PackingItems', () => {
  it('renders a photo grid when item count is at or below the limit', () => {
    render(<PackingItems items={makeItems(PHOTO_ITEM_LIMIT)} />);
    expect(screen.getByTestId('packing-items-grid')).toBeInTheDocument();
  });

  it('renders a dense list when item count exceeds the limit', () => {
    render(<PackingItems items={makeItems(PHOTO_ITEM_LIMIT + 1)} />);
    expect(screen.getByTestId('packing-items-list')).toBeInTheDocument();
  });

  it('shows every item name and quantity', () => {
    render(<PackingItems items={makeItems(3)} />);
    expect(screen.getByText('Produkt 1')).toBeInTheDocument();
    expect(screen.getByText('Produkt 3')).toBeInTheDocument();
    expect(screen.getByText('3×')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npx vitest run src/components/baleni/__tests__/PackingItems.test.tsx`
Expected: FAIL — cannot resolve `../PackingItems`.

- [ ] **Step 3: Implement `PackingItems`**

Create `PackingItems.tsx`:

```tsx
import { ImageOff } from 'lucide-react';
import type { PackingOrderItem } from '../../api/hooks/usePackingOrder';

/** Above this item count, photos are dropped and a dense list is shown instead. */
export const PHOTO_ITEM_LIMIT = 12;

interface PackingItemsProps {
  items: PackingOrderItem[];
}

function ItemQuantity({ quantity }: { quantity: number }) {
  return <span className="font-bold text-primary-blue shrink-0">{quantity}×</span>;
}

function PhotoGrid({ items }: PackingItemsProps) {
  return (
    <div
      data-testid="packing-items-grid"
      className="grid grid-cols-2 gap-2"
    >
      {items.map((item, index) => (
        <div
          key={`${item.name}-${index}`}
          className="flex items-center gap-2 bg-white border border-border-light rounded-lg p-2"
        >
          <div className="w-12 h-12 rounded-md bg-gray-100 flex items-center justify-center shrink-0 overflow-hidden">
            {item.imageUrl ? (
              <img src={item.imageUrl} alt={item.name} className="w-full h-full object-cover" />
            ) : (
              <ImageOff className="h-5 w-5 text-neutral-gray" />
            )}
          </div>
          <div className="flex-1 min-w-0">
            <p className="text-sm text-neutral-slate leading-tight">{item.name}</p>
            {item.setName && (
              <p className="text-xs text-neutral-gray">ze setu: {item.setName}</p>
            )}
          </div>
          <ItemQuantity quantity={item.quantity} />
        </div>
      ))}
    </div>
  );
}

function DenseList({ items }: PackingItemsProps) {
  return (
    <div
      data-testid="packing-items-list"
      className="columns-2 gap-4"
    >
      {items.map((item, index) => (
        <div
          key={`${item.name}-${index}`}
          className="flex items-center justify-between gap-2 text-sm py-1 border-b border-border-light break-inside-avoid"
        >
          <span className="text-neutral-slate truncate">{item.name}</span>
          <ItemQuantity quantity={item.quantity} />
        </div>
      ))}
    </div>
  );
}

function PackingItems({ items }: PackingItemsProps) {
  return items.length > PHOTO_ITEM_LIMIT ? (
    <DenseList items={items} />
  ) : (
    <PhotoGrid items={items} />
  );
}

export default PackingItems;
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && npx vitest run src/components/baleni/__tests__/PackingItems.test.tsx`
Expected: PASS — 3 tests.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/baleni/PackingItems.tsx frontend/src/components/baleni/__tests__/PackingItems.test.tsx
git commit -m "feat(baleni): add PackingItems component"
```

---

## Task 9: Build the PackingOrderMeta component

`PackingOrderMeta` renders the order header block: order number, customer, shipping method, cooling badge.

**Files:**
- Create: `frontend/src/components/baleni/PackingOrderMeta.tsx`

- [ ] **Step 1: Implement `PackingOrderMeta`**

```tsx
import { Snowflake } from 'lucide-react';
import type { PackingOrder } from '../../api/hooks/usePackingOrder';

interface PackingOrderMetaProps {
  order: PackingOrder;
}

function CoolingBadge({ order }: PackingOrderMetaProps) {
  if (!order.isCooled) {
    return (
      <span className="inline-flex items-center rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-semibold text-neutral-gray">
        Bez chlazení
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-secondary-blue-pale px-2.5 py-0.5 text-xs font-bold text-primary-blue">
      <Snowflake className="h-3 w-3" />
      Chlazení {order.cooling}
    </span>
  );
}

function PackingOrderMeta({ order }: PackingOrderMetaProps) {
  return (
    <div data-testid="packing-order-meta">
      <h2 className="text-lg font-bold text-neutral-slate">Objednávka {order.code}</h2>
      <p className="text-sm text-neutral-gray">{order.customerName}</p>
      <p className="text-sm text-neutral-gray">
        Doprava: <span className="text-neutral-slate font-medium">{order.shippingMethodName}</span>
      </p>
      <div className="mt-1.5">
        <CoolingBadge order={order} />
      </div>
    </div>
  );
}

export default PackingOrderMeta;
```

- [ ] **Step 2: Verify it type-checks**

Run: `cd frontend && npx tsc --noEmit`
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/baleni/PackingOrderMeta.tsx
git commit -m "feat(baleni): add PackingOrderMeta component"
```

---

## Task 10: Build and test the BaleniPacking page

`BaleniPacking` owns the scan input, the scanned-code state, and the empty / loading / error / loaded states.

**Files:**
- Create: `frontend/src/components/baleni/BaleniPacking.tsx`
- Test: `frontend/src/components/baleni/__tests__/BaleniPacking.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `BaleniPacking.test.tsx`:

```tsx
import { render, screen, fireEvent } from '@testing-library/react';
import BaleniPacking from '../BaleniPacking';
import { usePackingOrder, PackingOrderNotFoundError } from '../../../api/hooks/usePackingOrder';

vi.mock('../../../api/hooks/usePackingOrder', async () => {
  const actual = await vi.importActual<typeof import('../../../api/hooks/usePackingOrder')>(
    '../../../api/hooks/usePackingOrder',
  );
  return { ...actual, usePackingOrder: vi.fn() };
});

const mockedUsePackingOrder = vi.mocked(usePackingOrder);

const baseResult = {
  data: undefined,
  isLoading: false,
  isError: false,
  error: null,
} as unknown as ReturnType<typeof usePackingOrder>;

describe('BaleniPacking', () => {
  beforeEach(() => {
    mockedUsePackingOrder.mockReturnValue(baseResult);
  });

  it('shows the empty state before any scan', () => {
    render(<BaleniPacking />);
    expect(screen.getByText('Naskenujte číslo objednávky')).toBeInTheDocument();
  });

  it('renders the order panel when data is loaded', () => {
    mockedUsePackingOrder.mockReturnValue({
      ...baseResult,
      data: {
        code: '250001',
        customerName: 'Jan Novák',
        shippingMethodName: 'PPL (do ruky)',
        cooling: 'None',
        isCooled: false,
        items: [{ name: 'Krém', quantity: 2, imageUrl: null, setName: null }],
      },
    } as unknown as ReturnType<typeof usePackingOrder>);

    render(<BaleniPacking />);
    expect(screen.getByText('Objednávka 250001')).toBeInTheDocument();
    expect(screen.getByText('Krém')).toBeInTheDocument();
  });

  it('shows a not-found message for an unknown order', () => {
    mockedUsePackingOrder.mockReturnValue({
      ...baseResult,
      isError: true,
      error: new PackingOrderNotFoundError('999999'),
    } as unknown as ReturnType<typeof usePackingOrder>);

    render(<BaleniPacking />);
    expect(screen.getByText('Objednávka nenalezena')).toBeInTheDocument();
  });

  it('shows a generic error message for other failures', () => {
    mockedUsePackingOrder.mockReturnValue({
      ...baseResult,
      isError: true,
      error: new Error('network down'),
    } as unknown as ReturnType<typeof usePackingOrder>);

    render(<BaleniPacking />);
    expect(screen.getByText('Nepodařilo se načíst objednávku')).toBeInTheDocument();
  });

  it('updates the scanned code when the scan input submits', () => {
    render(<BaleniPacking />);
    const input = screen.getByRole('textbox');
    fireEvent.change(input, { target: { value: '250001' } });
    fireEvent.submit(input.closest('form')!);
    expect(mockedUsePackingOrder).toHaveBeenLastCalledWith('250001');
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npx vitest run src/components/baleni/__tests__/BaleniPacking.test.tsx`
Expected: FAIL — cannot resolve `../BaleniPacking`.

- [ ] **Step 3: Implement `BaleniPacking`**

Create `BaleniPacking.tsx`:

```tsx
import { useState } from 'react';
import { ScanLine, Loader2 } from 'lucide-react';
import ScanInput from '../terminal/ScanInput';
import { usePackingOrder, PackingOrderNotFoundError } from '../../api/hooks/usePackingOrder';
import PackingOrderMeta from './PackingOrderMeta';
import PackingItems from './PackingItems';

function CenteredMessage({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex flex-col items-center justify-center py-24 text-center text-neutral-gray">
      {children}
    </div>
  );
}

function BaleniPacking() {
  const [scannedCode, setScannedCode] = useState<string | null>(null);
  const { data, isLoading, isError, error } = usePackingOrder(scannedCode);

  const handleScan = (value: string) => setScannedCode(value);

  const renderBody = () => {
    if (isLoading) {
      return (
        <CenteredMessage>
          <Loader2 className="h-8 w-8 animate-spin mb-3" />
          <p>Načítám objednávku…</p>
        </CenteredMessage>
      );
    }
    if (isError) {
      const notFound = error instanceof PackingOrderNotFoundError;
      return (
        <CenteredMessage>
          <p className="text-base font-semibold text-neutral-slate">
            {notFound ? 'Objednávka nenalezena' : 'Nepodařilo se načíst objednávku'}
          </p>
          <p className="text-sm mt-1">Naskenujte objednávku znovu.</p>
        </CenteredMessage>
      );
    }
    if (data) {
      return <PackingItems items={data.items} />;
    }
    return (
      <CenteredMessage>
        <ScanLine className="h-10 w-10 mb-3" />
        <p className="text-base font-semibold text-neutral-slate">Naskenujte číslo objednávky</p>
      </CenteredMessage>
    );
  };

  return (
    <div className="flex flex-col gap-4" data-testid="baleni-packing">
      <div className="flex items-start justify-between gap-4">
        <div className="flex-1 min-w-0">
          {data && <PackingOrderMeta order={data} />}
        </div>
        <div className="w-72 shrink-0">
          <ScanInput
            label="Sken čísla objednávky"
            placeholder="Naskenujte objednávku…"
            onScan={handleScan}
            loading={isLoading}
            autoFocusOnMount
            refocusOnBlur
            allowKeyboardToggle
          />
        </div>
      </div>
      {data && (
        <p className="text-xs uppercase tracking-wide text-neutral-gray">
          Položky ({data.items.length})
        </p>
      )}
      {renderBody()}
    </div>
  );
}

export default BaleniPacking;
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && npx vitest run src/components/baleni/__tests__/BaleniPacking.test.tsx`
Expected: PASS — 5 tests.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/baleni/BaleniPacking.tsx frontend/src/components/baleni/__tests__/BaleniPacking.test.tsx
git commit -m "feat(baleni): add BaleniPacking page"
```

---

## Task 11: Wire the route in App.tsx

**Files:**
- Modify: `frontend/src/App.tsx:78` and `frontend/src/App.tsx:369`

- [ ] **Step 1: Add the import**

After the line `import BaleniPlaceholder from "./components/baleni/BaleniPlaceholder";` (line 78), add:

```tsx
import BaleniPacking from "./components/baleni/BaleniPacking";
```

- [ ] **Step 2: Replace the placeholder route**

Change line 369 from:

```tsx
                        <Route path="baleni" element={<BaleniPlaceholder title="Balení" />} />
```

to:

```tsx
                        <Route path="baleni" element={<BaleniPacking />} />
```

(Leave the `zasilky` and `statistiky` placeholder routes and the `BaleniPlaceholder` import unchanged — they still use it.)

- [ ] **Step 3: Verify the frontend builds and lints**

Run: `cd frontend && npm run build`
Expected: Build succeeds.
Run: `cd frontend && npm run lint`
Expected: No errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/App.tsx
git commit -m "feat(baleni): route /baleni/baleni to packing screen"
```

---

## Task 12: Add an E2E test for the packing screen

A deterministic E2E: the page renders its empty state and scan input, and scanning a non-existent order shows the not-found message. This avoids depending on a specific live Shoptet order.

**Files:**
- Create: `frontend/test/e2e/baleni/packing.spec.ts`

- [ ] **Step 1: Write the E2E test**

Create `frontend/test/e2e/baleni/packing.spec.ts`. Match the auth/setup pattern of an existing spec under `frontend/test/e2e/` (use `navigateToApp()` for authentication — see `docs/testing/playwright-e2e-testing.md`):

```typescript
import { test, expect } from '@playwright/test';
import { navigateToApp } from '../helpers/auth';

test.describe('Balení — packing screen', () => {
  test('shows the empty state and scan input', async ({ page }) => {
    await navigateToApp(page, '/baleni/baleni');

    await expect(page.getByText('Naskenujte číslo objednávky')).toBeVisible();
    await expect(page.getByRole('textbox')).toBeFocused();
  });

  test('shows a not-found message for an unknown order code', async ({ page }) => {
    await navigateToApp(page, '/baleni/baleni');

    const input = page.getByRole('textbox');
    await input.fill('00000000');
    await input.press('Enter');

    await expect(page.getByText('Objednávka nenalezena')).toBeVisible({ timeout: 15000 });
  });
});
```

> If the auth helper import path or `navigateToApp` signature differs, adjust to match a sibling spec under `frontend/test/e2e/` — do not invent a new auth flow.

- [ ] **Step 2: Run the E2E test against staging**

Run: `./scripts/run-playwright-tests.sh baleni/packing.spec.ts`
Expected: Both tests pass. (The not-found test depends on Shoptet returning 404 for a bogus code.)

- [ ] **Step 3: Commit**

```bash
git add frontend/test/e2e/baleni/packing.spec.ts
git commit -m "test(baleni): add packing screen E2E test"
```

---

## Final Verification

- [ ] **Backend:** `dotnet build backend/Anela.Heblo.sln` — succeeds.
- [ ] **Backend format:** `dotnet format backend/Anela.Heblo.sln --verify-no-changes` — clean.
- [ ] **Backend tests:** `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "ShoptetApiPackingOrderClientTests|GetPackingOrderHandlerTests"` — all pass.
- [ ] **Frontend build:** `cd frontend && npm run build` — succeeds (regenerates the OpenAPI TypeScript client).
- [ ] **Frontend lint:** `cd frontend && npm run lint` — no errors.
- [ ] **Frontend tests:** `cd frontend && npx vitest run src/components/baleni` — all pass.
- [ ] **E2E:** `./scripts/run-playwright-tests.sh baleni/packing.spec.ts` against staging — passes.
- [ ] **Manual smoke test:** open `/baleni/baleni`, scan a known staging order code, confirm order number, customer, shipping method, cooling badge, and item list render on a single screen without scrolling; scan a large order and confirm photos drop to the dense list.

---

## Self-Review Notes

- **Spec coverage:** scan input (Task 10, reuses `ScanInput`); load order header + items (Tasks 3, 5, 6); cooling via product+carrier matrix (Task 3, reuses `ResolveCarrierCooling`/`ApplyEnrichment`); product images (Task 3); single-screen layout with photo-grid/dense-list switch (Task 8); shipping method shown (Tasks 1, 3, 9); error handling — not found / API error (Tasks 5, 10); empty + loading states (Task 10). All spec sections map to tasks.
- **Type consistency:** `PackingOrder` / `PackingOrderItem` (Task 2) are used unchanged by the adapter (Task 3), handler (Task 5), and response (Task 5). `Cooling` is the backend enum, serialized as the string union `'None' | 'L1' | 'L2'` on the frontend (Task 7) — matches the existing `useCarrierCooling.ts` convention. `PHOTO_ITEM_LIMIT` is defined once in `PackingItems.tsx` and imported by its test.
- **Product sets:** reusing `MapToExpeditionOrder` expands product sets into component products with `SetName` populated — confirmed with the user; consistent with the picking list.
