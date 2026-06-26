# Decouple Packaging from ShoptetOrdersSettings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the direct dependency the `Packaging` module has on `ShoptetOrdersSettings` by moving packing-eligibility and the "mark as packed" status transition behind contracts already exposed by `ShoptetOrders`.

**Architecture:** Add a precomputed `IsEligibleForPacking` flag on the `PackingOrder` contract DTO (set by the Shoptet adapter), and add a single-purpose `MarkAsPackedAsync` method to `IEshopOrderClient` (delegating to the existing `UpdateStatusAsync` path). Then strip `ShoptetOrdersSettings` from both handlers. A reflection-based architecture test pins the new boundary in place so the rule cannot silently drift back.

**Tech Stack:** .NET 8, C#, xUnit, FluentAssertions, Moq, MediatR, Microsoft.Extensions.Options.

---

## File Structure

The change is **strictly additive on the contract side** and **subtractive on the consumer side**. No new files except optionally the architecture test rule (which already exists as a reflection-based theory; we just add a new `ModuleBoundaryRule` row).

| File | Responsibility | Change |
|---|---|---|
| `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs` | Contract DTO + interface for fetching the packing view of an order. | Add `bool IsEligibleForPacking { get; set; }` to `PackingOrder`. Tighten XML doc on `StatusId`. |
| `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs` | Contract for eshop-order operations. | Add `Task MarkAsPackedAsync(string orderCode, CancellationToken ct = default);`. |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs` | Loads a Shoptet order for the Balení screen; implements `IPackingOrderClient`. | Inject `IOptions<ShoptetOrdersSettings>`. Set `IsEligibleForPacking = statusId == settings.PackingStateId` while constructing the returned `PackingOrder`. |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs` | Implements `IEshopOrderClient` against Shoptet REST. | Inject `IOptions<ShoptetOrdersSettings>`. Implement `MarkAsPackedAsync` as a one-line delegate to `UpdateStatusAsync(orderCode, settings.PackedStateId, ct)`. |
| `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs` | Handles `POST /api/packaging/scan`. | Drop the `_orderSettings` field + ctor parameter + the `Microsoft.Extensions.Options` using. Use `order.IsEligibleForPacking` and `_eshopOrderClient.MarkAsPackedAsync(...)`. Adjust the warning log to `"Failed to mark order {OrderCode} as packed"`. |
| `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderHandler.cs` | Handles `GET /api/packing-orders/{code}`. | Drop the `_settings` field + ctor parameter + the `Microsoft.Extensions.Options` using. Use `order.IsEligibleForPacking`. |
| `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs` | Unit tests for `ScanPackingOrderHandler`. | Drop `DefaultOrderSettings` + the `orderSettings` parameter on `CreateHandler`. Set `IsEligibleForPacking = true` in `EligibleOrder()`. For ineligible-path tests, set `IsEligibleForPacking = false` explicitly. Change assertions on `UpdateStatusAsync` to `MarkAsPackedAsync`. |
| `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/GetPackingOrderHandlerTests.cs` | Unit tests for `GetPackingOrderHandler`. | Drop the `ShoptetOrdersSettings` setup in `CreateHandler`. Set `IsEligibleForPacking` on mocked `PackingOrder` per scenario. |
| `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` | Reflection-based architecture rules enforcing module boundaries. | Append a new `ModuleBoundaryRule` row: `"Packaging -> ShoptetOrders"` so `Packaging` types cannot reference `Anela.Heblo.Application.Features.ShoptetOrders` namespaces (with an allowlist for the three legitimate contracts: `IPackingOrderClient`, `PackingOrder`, `PackingOrderItem`, `IEshopOrderClient`, plus all reachable `CreateEshopOrderRequest`/`EshopOrderSummary`/`EshopOrderInfo` etc. that are part of the contract surface). |

Files **not** changed:
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` — both adapters are already constructed through DI; `IOptions<ShoptetOrdersSettings>` resolves through the binding done by `ShoptetOrdersModule.AddShoptetOrdersModule`.
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersSettings.cs` — settings stay in place. Spec confirms.
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrder/BlockOrderProcessingHandler.cs` — same module owns the settings; no boundary crossed.

---

## Task 1: Add `IsEligibleForPacking` to the `PackingOrder` DTO

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs`

This is a contract-only change: no consumers read the new flag yet, no producers set it yet. It is a self-contained step so the codebase keeps compiling.

- [ ] **Step 1: Add the new property and tighten the existing XML doc on `StatusId`**

Edit `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs`. Replace the existing `StatusId` line (and the XML doc immediately above it) so the class reads as follows. **Only** the doc comment on `StatusId` and the new `IsEligibleForPacking` block are added; every other property and the line ordering are preserved.

```csharp
    /// <summary>
    /// Shoptet order status ID. Kept for diagnostic/logging purposes. Do <b>not</b>
    /// derive packing eligibility from this value — use <see cref="IsEligibleForPacking"/>.
    /// </summary>
    public int StatusId { get; set; }

    /// <summary>
    /// True when the order is in the configured packing state (Shoptet "Balí se").
    /// Computed by the adapter so callers do not need to know the status-id rule.
    /// </summary>
    public bool IsEligibleForPacking { get; set; }
```

Insert the `IsEligibleForPacking` block **immediately after** the `StatusId` property, before `CustomerNote`.

- [ ] **Step 2: Build the solution to confirm the contract compiles**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: succeeds. No callers have to change yet (existing handlers still set/read `StatusId` only).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs
git commit -m "feat: add IsEligibleForPacking to PackingOrder contract"
```

---

## Task 2: Add `MarkAsPackedAsync` to `IEshopOrderClient`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs`

Contract addition only. Implementations and consumers are wired in subsequent tasks.

- [ ] **Step 1: Declare the new method on the interface**

Edit `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs`. Append (immediately before the closing `}` of the interface, after `GetOrderStatusNamesAsync`):

```csharp
    /// <summary>
    /// Transitions the order to the configured "packed" state
    /// (Shoptet "Zabaleno", id 52 by default). Called by the Balení screen
    /// after a successful scan + shipment creation.
    /// </summary>
    Task MarkAsPackedAsync(string orderCode, CancellationToken ct = default);
```

- [ ] **Step 2: Run the build — expect a failure in `ShoptetOrderClient`**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: **fails** with CS0535 in `ShoptetOrderClient.cs` (`'ShoptetOrderClient' does not implement interface member 'IEshopOrderClient.MarkAsPackedAsync(string, CancellationToken)'`). This is the failing test for Task 3.

Do **not** commit yet — the next task fixes the build.

---

## Task 3: Implement `MarkAsPackedAsync` in `ShoptetOrderClient`

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`
- Test: existing unit tests under `backend/test/Anela.Heblo.Tests/` (no new test file; behavioural contract for `MarkAsPackedAsync` is verified at the handler level in Task 7).

Inject `IOptions<ShoptetOrdersSettings>` and delegate to the existing `UpdateStatusAsync` path so the HTTP request is byte-for-byte identical to today's "mark as packed" call.

- [ ] **Step 1: Add the `Microsoft.Extensions.Options` using**

Edit `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`. Add (with the other `using`s, alphabetically):

```csharp
using Microsoft.Extensions.Options;
```

Note: `Anela.Heblo.Application.Features.ShoptetOrders` is already imported (line 6), so `ShoptetOrdersSettings` resolves without an extra using.

- [ ] **Step 2: Inject `IOptions<ShoptetOrdersSettings>` and store the settings**

Replace the existing field declaration and constructor body at the top of the class:

```csharp
public class ShoptetOrderClient : IEshopOrderClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private const int AdditionalFieldMinIndex = 1;
    private const int AdditionalFieldMaxIndex = 6;
    private const int AdditionalFieldShortTextMaxIndex = 3;
    private const int AdditionalFieldShortTextMaxLength = 255;

    public ShoptetOrderClient(HttpClient http)
    {
        _http = http;
    }
```

with:

```csharp
public class ShoptetOrderClient : IEshopOrderClient
{
    private readonly HttpClient _http;
    private readonly ShoptetOrdersSettings _orderSettings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private const int AdditionalFieldMinIndex = 1;
    private const int AdditionalFieldMaxIndex = 6;
    private const int AdditionalFieldShortTextMaxIndex = 3;
    private const int AdditionalFieldShortTextMaxLength = 255;

    public ShoptetOrderClient(HttpClient http, IOptions<ShoptetOrdersSettings> orderSettings)
    {
        _http = http;
        _orderSettings = orderSettings.Value;
    }
```

- [ ] **Step 3: Implement `MarkAsPackedAsync`**

Append the method at the **end of the class body**, immediately before the closing `}` of the `ShoptetOrderClient` class (i.e. right after the last existing method `GetExpeditionOrderDetailAsync` /`SetAdditionalFieldAsync`/`MapToOrderInfo` — wherever ends up last in the file you have):

```csharp
    public Task MarkAsPackedAsync(string orderCode, CancellationToken ct = default) =>
        UpdateStatusAsync(orderCode, _orderSettings.PackedStateId, ct);
```

- [ ] **Step 4: Run the build to verify it now compiles**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: succeeds. The CS0535 from Task 2 is gone. `AddHttpClient<ShoptetOrderClient>` in `ShoptetApiAdapterServiceCollectionExtensions` will resolve the new `IOptions<ShoptetOrdersSettings>` parameter through DI (the singleton is bound in `ShoptetOrdersModule`).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs \
       backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs
git commit -m "feat: add MarkAsPackedAsync to IEshopOrderClient and Shoptet implementation"
```

---

## Task 4: Set `IsEligibleForPacking` in `ShoptetApiPackingOrderClient`

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs`

The adapter already loads `statusId` from the order detail. We add an `IOptions<ShoptetOrdersSettings>` dependency and set `IsEligibleForPacking = statusId == _shoptetOrdersSettings.PackingStateId` on the returned DTO.

- [ ] **Step 1: Inject `ShoptetOrdersSettings` and store the value**

Edit `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs`. Replace the existing field/constructor block:

```csharp
public class ShoptetApiPackingOrderClient : IPackingOrderClient
{
    private readonly ShoptetOrderClient _orderClient;
    private readonly ICatalogRepository _catalog;
    private readonly ICarrierCoolingRepository _carrierCooling;
    private readonly ILogger<ShoptetApiPackingOrderClient> _logger;
    private readonly int _defaultItemWeightGrams;

    public ShoptetApiPackingOrderClient(
        ShoptetOrderClient orderClient,
        ICatalogRepository catalog,
        ICarrierCoolingRepository carrierCooling,
        ILogger<ShoptetApiPackingOrderClient> logger,
        IOptions<ShoptetApiSettings> settings)
    {
        _orderClient = orderClient;
        _catalog = catalog;
        _carrierCooling = carrierCooling;
        _logger = logger;
        _defaultItemWeightGrams = settings.Value.DefaultItemWeightGrams;
    }
```

with:

```csharp
public class ShoptetApiPackingOrderClient : IPackingOrderClient
{
    private readonly ShoptetOrderClient _orderClient;
    private readonly ICatalogRepository _catalog;
    private readonly ICarrierCoolingRepository _carrierCooling;
    private readonly ILogger<ShoptetApiPackingOrderClient> _logger;
    private readonly int _defaultItemWeightGrams;
    private readonly ShoptetOrdersSettings _orderSettings;

    public ShoptetApiPackingOrderClient(
        ShoptetOrderClient orderClient,
        ICatalogRepository catalog,
        ICarrierCoolingRepository carrierCooling,
        ILogger<ShoptetApiPackingOrderClient> logger,
        IOptions<ShoptetApiSettings> settings,
        IOptions<ShoptetOrdersSettings> orderSettings)
    {
        _orderClient = orderClient;
        _catalog = catalog;
        _carrierCooling = carrierCooling;
        _logger = logger;
        _defaultItemWeightGrams = settings.Value.DefaultItemWeightGrams;
        _orderSettings = orderSettings.Value;
    }
```

`Microsoft.Extensions.Options` is already imported (line 8) and `Anela.Heblo.Application.Features.ShoptetOrders` is already imported (line 4) — no extra usings.

- [ ] **Step 2: Compute and set `IsEligibleForPacking` on the returned DTO**

In the same file, change the existing `return new PackingOrder { … }` literal at the bottom of `GetPackingOrderAsync` so that **immediately after** the line:

```csharp
            StatusId = statusId,
```

it includes:

```csharp
            IsEligibleForPacking = statusId == _orderSettings.PackingStateId,
```

The final literal should read:

```csharp
        return new PackingOrder
        {
            Code = order.Code,
            CustomerName = order.CustomerName,
            ShippingMethodName = detail.Shipping?.Name ?? string.Empty,
            Cooling = order.CarrierCooling,
            IsCooled = order.IsCooled,
            StatusId = statusId,
            IsEligibleForPacking = statusId == _orderSettings.PackingStateId,
            CustomerNote = string.IsNullOrWhiteSpace(order.CustomerRemark) ? null : order.CustomerRemark,
            EshopNote = string.IsNullOrWhiteSpace(order.EshopRemark) ? null : order.EshopRemark,
            ShippingStreet = shippingStreet,
            ShippingCity = NormalizeAddressField(deliveryAddress?.City),
            ShippingZip = NormalizeAddressField(deliveryAddress?.Zip),
            Items = items,
        };
```

- [ ] **Step 3: Build**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: succeeds. `IOptions<ShoptetOrdersSettings>` is already a registered singleton (bound by `ShoptetOrdersModule.AddShoptetOrdersModule`), so DI composes the adapter normally.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs
git commit -m "feat: compute IsEligibleForPacking in ShoptetApiPackingOrderClient"
```

---

## Task 5: Update `GetPackingOrderHandlerTests` to drive the contract change

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/GetPackingOrderHandlerTests.cs`

The handler is the consumer that will be changed in the next task. Per TDD: we change tests first so they describe the new behaviour, run them to see them fail, then change the production handler.

- [ ] **Step 1: Replace the entire file contents**

Overwrite `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/GetPackingOrderHandlerTests.cs` with:

```csharp
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Application.ShoptetOrders;

public class GetPackingOrderHandlerTests
{
    private readonly Mock<IPackingOrderClient> _clientMock = new();

    private GetPackingOrderHandler CreateHandler() =>
        new(
            _clientMock.Object,
            NullLogger<GetPackingOrderHandler>.Instance);

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
                StatusId = 26,
                IsEligibleForPacking = true,
                CustomerNote = "Zabalit jako dárek",
                EshopNote = "Stálý zákazník",
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
        response.CustomerNote.Should().Be("Zabalit jako dárek");
        response.EshopNote.Should().Be("Stálý zákazník");
        response.Items.Should().ContainSingle().Which.Name.Should().Be("Krém");
    }

    [Fact]
    public async Task Handle_WhenOrderIsInPackingState_ReturnsEligibleWithNullWarning()
    {
        _clientMock
            .Setup(c => c.GetPackingOrderAsync("ORD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackingOrder
            {
                Code = "ORD001",
                CustomerName = "Jan Novák",
                ShippingMethodName = "PPL",
                StatusId = 26,
                IsEligibleForPacking = true,
                Items = [],
            });

        var result = await CreateHandler().Handle(
            new GetPackingOrderRequest { Code = "ORD001" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Eligibility.IsEligible.Should().BeTrue();
        result.Eligibility.WarningTitle.Should().BeNull();
        result.Eligibility.WarningBody.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenOrderIsNotInPackingState_ReturnsIneligibleWithCzechWarning()
    {
        _clientMock
            .Setup(c => c.GetPackingOrderAsync("ORD002", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackingOrder
            {
                Code = "ORD002",
                CustomerName = "Jana Nováková",
                ShippingMethodName = "PPL",
                StatusId = 99,
                IsEligibleForPacking = false,
                Items = [],
            });

        var result = await CreateHandler().Handle(
            new GetPackingOrderRequest { Code = "ORD002" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Eligibility.IsEligible.Should().BeFalse();
        result.Eligibility.WarningTitle.Should().Be("Objednávka není ve stavu „Balí se“");
        result.Eligibility.WarningBody.Should().Be("Tuto objednávku nezpracovávejte, dokud nebude ve správném stavu.");
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

Notes on the diff vs. previous file:
- `using Microsoft.Extensions.Options;` removed.
- `CreateHandler(int packingStateId = 26)` simplified — no settings parameter, no `Options.Create(...)` call.
- Each test that constructs a `PackingOrder` mock now sets `IsEligibleForPacking` explicitly so the test is not silently dependent on the `StatusId` comparison being done in the handler.

- [ ] **Step 2: Run the tests — expect the test project to fail to compile**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~GetPackingOrderHandlerTests" --no-restore
```
Expected: **compile failure** — `GetPackingOrderHandler` constructor still takes `IOptions<ShoptetOrdersSettings>`, so `new(_clientMock.Object, NullLogger<...>.Instance)` is wrong-arity. This is the failing-test signal for Task 6.

Do **not** commit yet.

---

## Task 6: Simplify `GetPackingOrderHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderHandler.cs`

Drop `IOptions<ShoptetOrdersSettings>` and the recomputed comparison; consume `order.IsEligibleForPacking` directly.

- [ ] **Step 1: Overwrite the handler with the simplified version**

Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderHandler.cs` with:

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

            var isEligible = order.IsEligibleForPacking;

            return new GetPackingOrderResponse
            {
                Code = order.Code,
                CustomerName = order.CustomerName,
                ShippingMethodName = order.ShippingMethodName,
                Cooling = order.Cooling,
                IsCooled = order.IsCooled,
                Eligibility = new PackingEligibility
                {
                    IsEligible = isEligible,
                    WarningTitle = isEligible ? null : "Objednávka není ve stavu „Balí se“",
                    WarningBody = isEligible ? null : "Tuto objednávku nezpracovávejte, dokud nebude ve správném stavu.",
                },
                CustomerNote = order.CustomerNote,
                EshopNote = order.EshopNote,
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

Diff vs. previous file:
- `using Microsoft.Extensions.Options;` removed.
- `_settings` field and the `IOptions<ShoptetOrdersSettings>` ctor parameter removed.
- `var isEligible = order.StatusId == _settings.Value.PackingStateId;` becomes `var isEligible = order.IsEligibleForPacking;`. The Czech warning strings are unchanged.

- [ ] **Step 2: Run the targeted tests — expect them to pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~GetPackingOrderHandlerTests"
```
Expected: all 5 tests in `GetPackingOrderHandlerTests` pass.

- [ ] **Step 3: Build the full solution to surface any other consumers**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: succeeds. The only consumer of `GetPackingOrderHandler`'s constructor is its DI registration, which works via reflection — no change required.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderHandler.cs \
       backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/GetPackingOrderHandlerTests.cs
git commit -m "refactor: GetPackingOrderHandler consumes IsEligibleForPacking from contract"
```

---

## Task 7: Update `ScanPackingOrderHandlerTests` to drive the contract change

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs`

The handler tests today encode the rule `StatusId == 26 → eligible` directly via `DefaultOrderSettings`. After the refactor the rule lives on `PackingOrder.IsEligibleForPacking` and the assertions move from `UpdateStatusAsync(orderCode, 52, ct)` to `MarkAsPackedAsync(orderCode, ct)`.

Replace the existing test file with the version below. All tests are kept (no coverage loss); only the seams change.

- [ ] **Step 1: Overwrite the test file**

Replace the contents of `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs` with:

```csharp
using Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.Packaging;

public class ScanPackingOrderHandlerTests
{
    private readonly Mock<IShipmentClient> _shipmentClient = new();
    private readonly Mock<IPackingOrderClient> _orderClient = new();
    private readonly Mock<IEshopOrderClient> _eshopOrderClient = new();
    private readonly Mock<IPackageRepository> _packageRepository = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();

    private static readonly ShipmentLabelsSettings DefaultLabelSettings = new()
    {
        DefaultPackageWidthMm = 300,
        DefaultPackageHeightMm = 200,
        DefaultPackageDepthMm = 150,
        MinPackageWeightGrams = 100,
    };

    private ScanPackingOrderHandler CreateHandler(ShipmentLabelsSettings? labelSettings = null)
    {
        _currentUserService.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("uid-1", "Operator", "op@example.com", IsAuthenticated: true));
        return new(
            _shipmentClient.Object,
            _orderClient.Object,
            _eshopOrderClient.Object,
            Options.Create(labelSettings ?? DefaultLabelSettings),
            new Mock<ILogger<ScanPackingOrderHandler>>().Object,
            _packageRepository.Object,
            _currentUserService.Object);
    }

    private static PackingOrder EligibleOrder(params (string name, int qty, int weightGrams)[] items) =>
        new()
        {
            Code = "0001234",
            StatusId = 26,
            IsEligibleForPacking = true,
            Items = items.Select(i => new PackingOrderItem
            {
                Name = i.name,
                Quantity = i.qty,
                WeightGrams = i.weightGrams,
            }).ToList(),
        };

    // Test 1: Order not found → ErrorCodes.ShoptetOrderNotFound
    [Fact]
    public async Task Handle_OrderNotFound_ReturnsShoptetOrderNotFound()
    {
        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackingOrder?)null);

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShoptetOrderNotFound);
    }

    // Test 2: Order in wrong state, no existing labels → ineligible response with no shipment
    [Fact]
    public async Task Handle_OrderNotInPackingState_WithoutExistingLabels_ReturnsIneligibleWithNoShipment()
    {
        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackingOrder { Code = "0001234", StatusId = 99, IsEligibleForPacking = false });
        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Order.Should().NotBeNull();
        response.Order!.Eligibility.IsEligible.Should().BeFalse();
        response.Order.Eligibility.WarningTitle.Should().NotBeNullOrEmpty();
        response.Shipment.Should().BeNull();

        _shipmentClient.Verify(
            c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Test 2b: Order in wrong state but already has labels → ineligible response WITH shipment for review
    [Fact]
    public async Task Handle_OrderNotInPackingState_WithExistingLabels_ReturnsIneligibleWithShipment_AndDoesNotMarkPacked()
    {
        var shipmentGuid = Guid.NewGuid();
        var existingLabel = new ShipmentLabel
        {
            ShipmentGuid = shipmentGuid,
            OrderCode = "0001234",
            PackageName = "P1",
            TrackingNumber = "TRK-1",
            LabelUrl = "https://example.com/label.pdf",
        };

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackingOrder { Code = "0001234", StatusId = 99, IsEligibleForPacking = false });
        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingLabel]);

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Order!.Eligibility.IsEligible.Should().BeFalse();
        response.Shipment.Should().NotBeNull();
        response.Shipment!.AlreadyExisted.Should().BeTrue();
        response.Shipment.ShipmentGuid.Should().Be(shipmentGuid);
        response.Shipment.Packages.Should().ContainSingle()
            .Which.TrackingNumber.Should().Be("TRK-1");

        _shipmentClient.Verify(
            c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _eshopOrderClient.Verify(
            c => c.MarkAsPackedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Test 3: Labels already exist on eligible order → return existing shipment without creating
    [Fact]
    public async Task Handle_LabelsExist_ReturnsExistingShipmentWithAlreadyExistedTrue()
    {
        var shipmentGuid = Guid.NewGuid();
        var existingLabel = new ShipmentLabel
        {
            ShipmentGuid = shipmentGuid,
            OrderCode = "0001234",
            PackageName = "P1",
            LabelUrl = "https://example.com/label.pdf",
            LabelZpl = "^XA...^XZ",
        };

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 400)));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingLabel]);

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Order!.Eligibility.IsEligible.Should().BeTrue();
        response.Shipment.Should().NotBeNull();
        response.Shipment!.AlreadyExisted.Should().BeTrue();
        response.Shipment.ShipmentGuid.Should().Be(shipmentGuid);
        response.Shipment.Packages.Should().HaveCount(1);
        response.Shipment.Packages[0].Name.Should().Be("P1");
        response.Shipment.Packages[0].LabelUrl.Should().Be("https://example.com/label.pdf");
        response.Shipment.Packages[0].LabelZpl.Should().Be("^XA...^XZ");

        _shipmentClient.Verify(
            c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Test 4: All items have WeightGrams = 0 → weight unavailable error
    [Fact]
    public async Task Handle_AllItemsHaveZeroWeight_ReturnsShipmentOrderWeightUnavailable()
    {
        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 2, 0), ("P002", 1, 0)));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentOrderWeightUnavailable);
    }

    // Test 5: No shipping options returned → carrier not resolved error
    [Fact]
    public async Task Handle_NoShippingOptions_ReturnsShipmentCarrierNotResolved()
    {
        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 300)));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _shipmentClient
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentCarrierNotResolved);
    }

    // Test 6: CreateShipmentAsync throws → creation failed error
    [Fact]
    public async Task Handle_CreateShipmentThrows_ReturnsShipmentCreationFailed()
    {
        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 500)));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _shipmentClient
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);

        _shipmentClient
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shipment API unavailable"));

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentCreationFailed);
    }

    // Test 7: Eligible order, no existing shipment → creates new shipment, AlreadyExisted = false
    [Fact]
    public async Task Handle_NoExistingShipment_CreatesNewShipmentWithAlreadyExistedFalse()
    {
        var shipmentGuid = Guid.NewGuid();

        var order = new PackingOrder
        {
            Code = "0001234",
            StatusId = 26,
            IsEligibleForPacking = true,
            Items = new List<PackingOrderItem>
            {
                new() { Name = "P001", Quantity = 1, WeightGrams = 400 },
            },
            ShippingStreet = "Hlavní 123",
            ShippingCity = "Praha",
            ShippingZip = "110 00",
        };

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentClient
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://carrier.example.com/new-label.pdf" }]);

        _shipmentClient
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);

        _shipmentClient
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Order!.Eligibility.IsEligible.Should().BeTrue();
        response.Shipment.Should().NotBeNull();
        response.Shipment!.AlreadyExisted.Should().BeFalse();
        response.Shipment.ShipmentGuid.Should().Be(shipmentGuid);

        _shipmentClient.Verify(
            c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()),
            Times.Once);

        response.Shipment.Packages[0].LabelUrl.Should().Be("https://carrier.example.com/new-label.pdf");
        response.Shipment.Packages[0].LabelZpl.Should().BeNull();

        response.Order!.ShippingAddress.Should().NotBeNull();
        response.Order.ShippingAddress!.Street.Should().Be("Hlavní 123");
        response.Order.ShippingAddress.City.Should().Be("Praha");
        response.Order.ShippingAddress.Zip.Should().Be("110 00");
    }

    // Shipping address: when source has no address, response.Order.ShippingAddress is null
    [Fact]
    public async Task Handle_OrderWithoutShippingAddress_ReturnsNullShippingAddress()
    {
        var shipmentGuid = Guid.NewGuid();

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 400)));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://example.com/label.pdf" }]);

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Order.Should().NotBeNull();
        response.Order!.ShippingAddress.Should().BeNull();
    }

    // MarkAsPackedAsync: called when existing shipment found and order is eligible
    [Fact]
    public async Task Handle_LabelsExist_MarksOrderAsPacked()
    {
        var shipmentGuid = Guid.NewGuid();

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 400)));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://example.com/label.pdf" }]);

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        _eshopOrderClient.Verify(
            c => c.MarkAsPackedAsync("0001234", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // MarkAsPackedAsync: called when new shipment is created on eligible order
    [Fact]
    public async Task Handle_NewShipmentCreated_MarksOrderAsPacked()
    {
        var shipmentGuid = Guid.NewGuid();

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 400)));

        _shipmentClient
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://carrier.example.com/new-label.pdf" }]);

        _shipmentClient
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);

        _shipmentClient
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        _eshopOrderClient.Verify(
            c => c.MarkAsPackedAsync("0001234", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // MarkAsPackedAsync: failure is non-fatal — scan still returns success
    [Fact]
    public async Task Handle_MarkAsPackedFails_StillReturnsSuccessfulScanResponse()
    {
        var shipmentGuid = Guid.NewGuid();

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 400)));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://example.com/label.pdf" }]);

        _eshopOrderClient
            .Setup(c => c.MarkAsPackedAsync("0001234", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet status update failed"));

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment.Should().NotBeNull();
    }

    // MarkAsPackedAsync: NOT called when order is ineligible
    [Fact]
    public async Task Handle_OrderNotInPackingState_DoesNotMarkAsPacked()
    {
        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackingOrder { Code = "0001234", StatusId = 99, IsEligibleForPacking = false });

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        _eshopOrderClient.Verify(
            c => c.MarkAsPackedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

Diff vs. previous test file:
- `DefaultOrderSettings` field removed.
- `CreateHandler` no longer takes `ShoptetOrdersSettings` and no longer wraps it in `Options.Create(...)`.
- `EligibleOrder()` sets `IsEligibleForPacking = true`.
- Each `new PackingOrder { StatusId = 99 }` mock sets `IsEligibleForPacking = false` explicitly.
- All `UpdateStatusAsync` verifications and setups are rewritten as `MarkAsPackedAsync` verifications and setups.
- Test names containing "UpdatesOrderStatusToPacked" / "StatusUpdateFails" / "DoesNotUpdateStatus" are renamed to refer to `MarkAsPacked` for readability.

- [ ] **Step 2: Run the targeted tests — expect a compile failure**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~ScanPackingOrderHandlerTests" --no-restore
```
Expected: **compile failure** — `ScanPackingOrderHandler` constructor still takes `IOptions<ShoptetOrdersSettings>`, so `CreateHandler` does not match. Drives Task 8.

Do **not** commit yet.

---

## Task 8: Simplify `ScanPackingOrderHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`

Drop `ShoptetOrdersSettings`. Consume `order.IsEligibleForPacking`. Call `MarkAsPackedAsync` instead of `UpdateStatusAsync`. Update the warning log to drop the now-unknown `StatusId` placeholder.

- [ ] **Step 1: Overwrite the handler with the simplified version**

Replace the contents of `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs` with:

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;

public class ScanPackingOrderHandler : IRequestHandler<ScanPackingOrderRequest, ScanPackingOrderResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly IPackingOrderClient _orderClient;
    private readonly IEshopOrderClient _eshopOrderClient;
    private readonly ShipmentLabelsSettings _shipmentSettings;
    private readonly ILogger<ScanPackingOrderHandler> _logger;
    private readonly IPackageRepository _packageRepository;
    private readonly ICurrentUserService _currentUserService;

    public ScanPackingOrderHandler(
        IShipmentClient shipmentClient,
        IPackingOrderClient orderClient,
        IEshopOrderClient eshopOrderClient,
        IOptions<ShipmentLabelsSettings> shipmentSettings,
        ILogger<ScanPackingOrderHandler> logger,
        IPackageRepository packageRepository,
        ICurrentUserService currentUserService)
    {
        _shipmentClient = shipmentClient;
        _orderClient = orderClient;
        _eshopOrderClient = eshopOrderClient;
        _shipmentSettings = shipmentSettings.Value;
        _logger = logger;
        _packageRepository = packageRepository;
        _currentUserService = currentUserService;
    }

    public async Task<ScanPackingOrderResponse> Handle(ScanPackingOrderRequest request, CancellationToken ct)
    {
        var order = await _orderClient.GetPackingOrderAsync(request.OrderCode, ct);
        if (order is null)
            return new ScanPackingOrderResponse(ErrorCodes.ShoptetOrderNotFound);

        var isEligible = order.IsEligibleForPacking;
        var orderData = new ScanOrderData
        {
            Code = order.Code,
            CustomerName = order.CustomerName,
            ShippingMethodName = order.ShippingMethodName,
            Cooling = order.Cooling,
            IsCooled = order.IsCooled,
            CustomerNote = order.CustomerNote,
            EshopNote = order.EshopNote,
            ShippingAddress = BuildShippingAddress(order),
            Items = order.Items,
            Eligibility = new ScanOrderEligibility
            {
                IsEligible = isEligible,
                WarningTitle = isEligible ? null : "Objednávka není ve stavu „Balí se“",
                WarningBody = isEligible ? null : "Tuto objednávku nezpracovávejte, dokud nebude ve správném stavu.",
            },
        };

        var existingLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        ScanShipmentData? existingShipment = existingLabels.Count > 0
            ? new ScanShipmentData
            {
                ShipmentGuid = existingLabels[0].ShipmentGuid,
                Packages = existingLabels
                    .Select(l => new ScanShipmentPackage
                    {
                        Name = l.PackageName,
                        TrackingNumber = l.TrackingNumber,
                        LabelUrl = l.LabelUrl,
                        LabelZpl = l.LabelZpl,
                    })
                    .ToList(),
                AlreadyExisted = true,
            }
            : null;

        if (!isEligible)
        {
            // Already-packed order rescanned for review: include shipment if it exists.
            // Don't mark-as-packed; the order has already moved past the packing state.
            return existingShipment is null
                ? new ScanPackingOrderResponse(orderData)
                : new ScanPackingOrderResponse(orderData, existingShipment);
        }

        if (existingShipment is not null)
        {
            await TryMarkAsPackedAsync(request.OrderCode, ct);
            return new ScanPackingOrderResponse(orderData, existingShipment);
        }

        var totalWeightGrams = order.Items.Sum(i => i.WeightGrams * i.Quantity);
        if (totalWeightGrams == 0)
            return new ScanPackingOrderResponse(ErrorCodes.ShipmentOrderWeightUnavailable);

        var weightGrams = Math.Max(totalWeightGrams, _shipmentSettings.MinPackageWeightGrams);

        var options = await _shipmentClient.GetShippingOptionsAsync(request.OrderCode, ct);
        if (options.Count == 0)
            return new ScanPackingOrderResponse(ErrorCodes.ShipmentCarrierNotResolved);

        var command = new CreateShipmentCommand
        {
            OrderCode = request.OrderCode,
            CarrierCode = options[0].CarrierCode,
            Package = new ShipmentPackage
            {
                WidthMm = _shipmentSettings.DefaultPackageWidthMm,
                HeightMm = _shipmentSettings.DefaultPackageHeightMm,
                DepthMm = _shipmentSettings.DefaultPackageDepthMm,
                WeightGrams = weightGrams,
            },
        };

        CreatedShipment createdShipment;
        try
        {
            createdShipment = await _shipmentClient.CreateShipmentAsync(command, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create shipment for order {OrderCode}", request.OrderCode);
            return new ScanPackingOrderResponse(ErrorCodes.ShipmentCreationFailed);
        }

        // Single fetch for package names + carrier label URLs (FE prints directly from the CDN).
        var newLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        var packages = newLabels.Count > 0
            ? newLabels.Select(l => new ScanShipmentPackage
            {
                Name = l.PackageName,
                TrackingNumber = l.TrackingNumber,
                LabelUrl = l.LabelUrl,
                LabelZpl = l.LabelZpl,
            }).ToList()
            : [new ScanShipmentPackage { Name = "PKG-1" }];

        await PersistPackagesAsync(
            request.OrderCode,
            orderData.CustomerName,
            command.CarrierCode,
            createdShipment.ShipmentGuid,
            newLabels,
            ct);

        await TryMarkAsPackedAsync(request.OrderCode, ct);
        return new ScanPackingOrderResponse(orderData, new ScanShipmentData
        {
            ShipmentGuid = createdShipment.ShipmentGuid,
            Packages = packages,
            AlreadyExisted = false,
        });
    }

    private static ShippingAddress? BuildShippingAddress(PackingOrder order)
    {
        var street = string.IsNullOrEmpty(order.ShippingStreet) ? null : order.ShippingStreet;
        var city = string.IsNullOrEmpty(order.ShippingCity) ? null : order.ShippingCity;
        var zip = string.IsNullOrEmpty(order.ShippingZip) ? null : order.ShippingZip;

        if (street is null && city is null && zip is null)
            return null;

        return new ShippingAddress
        {
            Street = street,
            City = city,
            Zip = zip,
        };
    }

    private async Task TryMarkAsPackedAsync(string orderCode, CancellationToken ct)
    {
        try
        {
            await _eshopOrderClient.MarkAsPackedAsync(orderCode, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to mark order {OrderCode} as packed", orderCode);
        }
    }

    private async Task PersistPackagesAsync(
        string orderCode,
        string customerName,
        string carrierCode,
        Guid shipmentGuid,
        IReadOnlyList<ShipmentLabel> labels,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var packedBy = _currentUserService.GetCurrentUser().Email;

        foreach (var label in labels)
        {
            try
            {
                await _packageRepository.AddAsync(new Package
                {
                    OrderCode = orderCode,
                    CustomerName = customerName,
                    PackageNumber = label.PackageName,
                    TrackingNumber = label.TrackingNumber,
                    ShippingProviderCode = carrierCode,
                    ShippingProviderName = null,
                    ShipmentGuid = shipmentGuid,
                    PackedAt = now,
                    PackedBy = packedBy,
                    CreatedAt = now,
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to persist Package row for order {OrderCode} package {PackageName}",
                    orderCode, label.PackageName);
            }
        }
    }
}
```

Diff vs. previous file:
- `_orderSettings` field, `ShoptetOrdersSettings orderSettings` ctor parameter and assignment removed.
- `var isEligible = order.StatusId == _orderSettings.PackingStateId;` → `var isEligible = order.IsEligibleForPacking;`.
- `TryMarkAsPackedAsync` calls `_eshopOrderClient.MarkAsPackedAsync(orderCode, ct)`.
- Warning log message becomes `"Failed to mark order {OrderCode} as packed"` (no `StatusId` placeholder).
- `Microsoft.Extensions.Options` using remains because `IOptions<ShipmentLabelsSettings>` is still injected.

- [ ] **Step 2: Run the targeted tests**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~ScanPackingOrderHandlerTests"
```
Expected: all 13 tests in `ScanPackingOrderHandlerTests` pass.

- [ ] **Step 3: Build the full solution**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: succeeds.

- [ ] **Step 4: Grep the Packaging module to confirm nothing references Shoptet status-id leak names**

Run:
```bash
git grep -n -e "ShoptetOrdersSettings" -e "PackingStateId" -e "PackedStateId" \
    -- "backend/src/Anela.Heblo.Application/Features/Packaging/"
```
Expected: **no output** (the grep returns nothing). If anything matches, fix that file before moving on. Per FR-3 acceptance criterion #4.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs \
       backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs
git commit -m "refactor: ScanPackingOrderHandler consumes contract IsEligibleForPacking and MarkAsPackedAsync"
```

---

## Task 9: Add an architecture test to pin the new boundary

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

The existing test class enforces module boundaries with reflection-based theory rows. We add a new `Packaging -> ShoptetOrders` rule with an allowlist that only permits the legitimate contract types (`IPackingOrderClient`, `PackingOrder`, `PackingOrderItem`, `IEshopOrderClient`, plus the supporting `ShipmentLabel` etc. that already cross via the `ShipmentLabels` module — those are not in `ShoptetOrders`, so they don't need allowlisting; only `ShoptetOrders` types do).

The rule's forbidden prefix is `Anela.Heblo.Application.Features.ShoptetOrders`. The allowlist permits the contracts the spec keeps as the legitimate seam.

- [ ] **Step 1: Add the allowlist**

In `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`, add a new field next to the other allowlist fields (e.g., just below `ExpeditionListLogisticsAllowlist`):

```csharp
    // Allowlist for Packaging -> ShoptetOrders. The Packaging module legitimately consumes
    // the IPackingOrderClient / IEshopOrderClient contracts (and their DTOs) defined in
    // Anela.Heblo.Application.Features.ShoptetOrders. Everything else — particularly
    // ShoptetOrdersSettings, PackingStateId, and PackedStateId — must not be referenced
    // from Packaging. This rule pins the 2026-06-05 decoupling in place.
    private static readonly HashSet<string> PackagingShoptetOrdersAllowlist = new(StringComparer.Ordinal)
    {
        // Constructor injections in ScanPackingOrderHandler.
        "Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder.ScanPackingOrderHandler -> Anela.Heblo.Application.Features.ShoptetOrders.IPackingOrderClient",
        "Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder.ScanPackingOrderHandler -> Anela.Heblo.Application.Features.ShoptetOrders.IEshopOrderClient",

        // PackingOrder is consumed in Handle and in the private BuildShippingAddress helper;
        // PackingOrderItem flows through ScanOrderData.Items.
        "Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder.ScanPackingOrderHandler -> Anela.Heblo.Application.Features.ShoptetOrders.PackingOrder",
        "Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder.ScanPackingOrderHandler -> Anela.Heblo.Application.Features.ShoptetOrders.PackingOrderItem",
    };
```

- [ ] **Step 2: Register the rule in the `Rules()` theory data**

In the same file, inside the `public static TheoryData<ModuleBoundaryRule> Rules()` method, append (immediately before the closing `};`) a new entry:

```csharp
        new ModuleBoundaryRule(
            Name: "Packaging -> ShoptetOrders",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Packaging",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Application.Features.ShoptetOrders",
            },
            Allowlist: PackagingShoptetOrdersAllowlist),
```

- [ ] **Step 3: Run the architecture test**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~ModuleBoundariesTests"
```
Expected: passes for `Packaging -> ShoptetOrders`. If it fails, the failure message lists every `consumerType -> referencedType` outside the allowlist. If a real consumer (`ScanPackingOrderRequest`, `ScanPackingOrderResponse`, `ScanOrderData`, etc.) legitimately references additional `ShoptetOrders` types reached via the contract surface (e.g. another `PackingOrder*` DTO), add them explicitly to `PackagingShoptetOrdersAllowlist` with a brief comment. Do **not** add `ShoptetOrdersSettings` to the allowlist — that would defeat the rule.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test: pin Packaging -> ShoptetOrders boundary in ModuleBoundariesTests"
```

---

## Task 10: Final validation

End-to-end checks before declaring the work complete. No new code changes; this step verifies the deliverable as a whole.

- [ ] **Step 1: Build the full solution**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: succeeds with zero errors.

- [ ] **Step 2: Run the full backend test suite**

Run:
```bash
dotnet test backend/Anela.Heblo.sln --no-build
```
Expected: all tests pass. Pay particular attention to:
- `Anela.Heblo.Tests.Application.Packaging.ScanPackingOrderHandlerTests` (13 tests)
- `Anela.Heblo.Tests.Application.ShoptetOrders.GetPackingOrderHandlerTests` (5 tests)
- `Anela.Heblo.Tests.Architecture.ModuleBoundariesTests` (theory; the new row must pass and all pre-existing rows must still pass)
- `Anela.Heblo.Tests.ApplicationStartupTests` (validates that DI composes — the constructor changes on `ShoptetApiPackingOrderClient` and `ShoptetOrderClient` must resolve via `IOptions<ShoptetOrdersSettings>`)

- [ ] **Step 3: Run `dotnet format` so the worktree matches repo style**

Run:
```bash
dotnet format backend/Anela.Heblo.sln
```
Expected: no diff, or a small whitespace diff. Stage any formatting changes:
```bash
git add backend/
git diff --cached --stat
```
If there are formatting-only changes, amend them into the most recent commit:
```bash
git commit --amend --no-edit
```

- [ ] **Step 4: Grep both the Packaging namespace and the source files one more time**

Confirm the FR-3 acceptance criterion and NFR-3 — knowledge of status ids is confined to the four locations the spec lists:

```bash
git grep -n -e "PackingStateId" -e "PackedStateId" -e "ShoptetOrdersSettings" -- backend/src
```
Expected output lists matches **only** in:
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersSettings.cs`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrder/BlockOrderProcessingHandler.cs` (existing, untouched)
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`

Any other match is a regression — fix it before moving on. The `ShoptetApiAdapterServiceCollectionExtensions.cs` file does not match because it never names `ShoptetOrdersSettings` directly; it lets DI resolve `IOptions<ShoptetOrdersSettings>` from the binding.

- [ ] **Step 5: Behaviour-parity smoke check (manual / optional)**

If the engineer wants to verify FR-4 manually, exercise the two endpoints against the staging configuration (or local user-secrets configuration with `PackingStateId = 26`, `PackedStateId = 52`):
- `GET /api/packing-orders/{code}` for an order in status 26 → `Eligibility.IsEligible = true`, `WarningTitle` / `WarningBody` null.
- `GET /api/packing-orders/{code}` for an order in any other status → `Eligibility.IsEligible = false`, Czech warning strings exactly as before.
- `POST /api/packaging/scan` body `{ "OrderCode": "..." }` for an eligible order → triggers exactly one `PATCH /api/orders/{code}/status` with `statusId: 52`. Same request as today.
- `POST /api/packaging/scan` for an ineligible order → no `PATCH` call to Shoptet.

Network captures should be byte-for-byte identical to pre-refactor behaviour.

---

## Spec Coverage Check

| Spec item | Covered by |
|---|---|
| **FR-1** Move packing eligibility onto `PackingOrder` contract; `ShoptetApiPackingOrderClient` is the sole producer | Tasks 1, 4 |
| **FR-1** `ScanPackingOrderHandler` consumes `order.IsEligibleForPacking` | Task 8 |
| **FR-1** `GetPackingOrderHandler` consumes `order.IsEligibleForPacking` and drops `IOptions<ShoptetOrdersSettings>` | Task 6 |
| **FR-2** `IEshopOrderClient.MarkAsPackedAsync` declared and implemented; XML doc per spec | Tasks 2, 3 |
| **FR-2** Generic `UpdateStatusAsync` left in place | Tasks 2, 3 leave it untouched; verified by Task 10 grep + tests |
| **FR-3** No `ShoptetOrdersSettings`/`PackingStateId`/`PackedStateId` references in Packaging | Task 8 step 4 grep; Task 10 step 4 grep; Task 9 architecture test enforces |
| **FR-4** Byte-for-byte behaviour parity | Tasks 5–8 keep Czech warnings + `PATCH /api/orders/{code}/status` path identical; Task 10 step 5 manual check |
| **NFR-1** No new HTTP/DB round-trips | Task 4 reuses the existing `GetOrderStatusIdAsync` call; Task 3 reuses the existing `UpdateStatusAsync` PATCH |
| **NFR-2** No security surface change | No auth/config flow touched |
| **NFR-3** Knowledge of status ids confined to 4 named locations | Task 10 step 4 grep verifies; Task 9 enforces |
| **NFR-4** No HTTP response shape change | Task 6, 8 preserve all response DTO shapes verbatim |
| **Arch-review amendment 1** Architecture enforcement test for Packaging | Task 9 |
| **Arch-review amendment 2** Tighten XML doc on `PackingOrder.StatusId` | Task 1 |
| **Out of scope** Removing `PackingOrder.StatusId`, renaming `ShoptetOrdersSettings`, changing eligibility rule | Explicitly not done; verified by Task 4 (StatusId still set) and Task 1 (StatusId still present) |
