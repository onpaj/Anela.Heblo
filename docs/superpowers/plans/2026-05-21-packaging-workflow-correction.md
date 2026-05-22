# Packaging Workflow Correction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the scan→button→label flow with scan-creates-shipment-immediately: the `POST /api/packaging/orders/{code}/scan` endpoint loads the order, checks eligibility, and creates a Shoptet shipment in one round trip, returning an `alreadyExisted` flag so the FE can auto-print (new shipment) or show a dialog (existing shipment) without any button press.

**Architecture:** Rename `PrepareOrderLabel` handler to `ScanPackingOrder`, strip label-URL polling, embed order info + eligibility + shipment into the response. Add `ResetOrderShipment` handler for the invalidate-and-recreate path. FE deletes `usePackingOrder` + `usePrepareOrderLabel`, adds `useScanPackingOrder` + `useResetOrderShipment`, rewrites `PackingShipmentCreator` as a passive state-machine controller driven entirely by props.

**Tech Stack:** .NET 8 / MediatR / MVC (backend), React 18 / TanStack Query v5 / TypeScript (frontend), xUnit + Moq + FluentAssertions (BE tests), Jest + React Testing Library (FE tests).

---

## Prerequisite: Document Shoptet DELETE shipment endpoint (human task — do before Task 2)

The Shoptet shipment DELETE endpoint is **not yet documented** in `docs/integrations/shoptet-api.md`. Before implementing `DeleteShipmentAsync` in Task 2, a human must:

1. Exercise `DELETE /api/shipments/{shipmentGuid}` on staging (assumed URL; no sandbox per CLAUDE.md — every call hits the live store).
2. Record: exact URL, required headers, success response, error responses, and any state restrictions (e.g., shipment must not yet be dispatched).
3. Add a subsection under §11 (Delivery API) in `docs/integrations/shoptet-api.md`.

Task 2 implements with the assumed URL `DELETE /api/shipments/{shipmentGuid}`; update the adapter if the real endpoint differs.

---

## File Map

**Backend — create:**
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentHandler.cs`
- `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/Packaging/ResetOrderShipmentHandlerTests.cs`

**Backend — delete:**
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/PrepareOrderLabel/PrepareOrderLabelRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/PrepareOrderLabel/PrepareOrderLabelResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/PrepareOrderLabel/PrepareOrderLabelHandler.cs`
- `backend/test/Anela.Heblo.Tests/Application/Packaging/PrepareOrderLabelHandlerTests.cs`

**Backend — modify:**
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/IShipmentClient.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs`
- `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs`

**Frontend — create:**
- `frontend/src/api/hooks/useScanPackingOrder.ts`
- `frontend/src/api/hooks/useResetOrderShipment.ts`

**Frontend — delete:**
- `frontend/src/api/hooks/usePackingOrder.ts`
- `frontend/src/api/hooks/usePrepareOrderLabel.ts`

**Frontend — rewrite:**
- `frontend/src/components/baleni/BaleniPacking.tsx`
- `frontend/src/components/baleni/PackingShipmentCreator.tsx`
- `frontend/src/components/baleni/__tests__/PackingShipmentCreator.test.tsx`

**Frontend — import-update only (one-line change each):**
- `frontend/src/components/baleni/PackingStateWarning.tsx`
- `frontend/src/components/baleni/PackingOrderMeta.tsx`
- `frontend/src/components/baleni/PackingCoolingIndicator.tsx`

**Docs — create:**
- `docs/features/packaging.md`

---

## Task 1: Backend — ScanPackingOrder use case + controller rename

**Context:** This replaces `PrepareOrderLabel` with `ScanPackingOrder`. The handler reuses eligibility-check + shipment-exists-check + create logic from `PrepareOrderLabelHandler.cs`, but strips the label-URL polling tail (lines 96–106). Ineligible orders now return a `success: true` response with `eligibility.isEligible: false` instead of an error code. Two new error codes are added for the Reset use case (implemented in Task 2).

**Files:**
- Add to: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/PrepareOrderLabel/` (all 3 files)

- [ ] **Step 1: Add error codes to ErrorCodes.cs**

In `ErrorCodes.cs`, the `// Packaging module errors (30XX)` section currently has only `OrderNotInPackingState = 3001`. Add two new codes after it:

```csharp
    // Packaging module errors (30XX)
    [HttpStatusCode(HttpStatusCode.Conflict)]
    OrderNotInPackingState = 3001,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    ShipmentDeleteFailed = 3002,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    NoShipmentToReset = 3003,
```

- [ ] **Step 2: Create ScanPackingOrderRequest.cs**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;

public class ScanPackingOrderRequest : IRequest<ScanPackingOrderResponse>
{
    public string OrderCode { get; set; } = null!;
}
```

- [ ] **Step 3: Create ScanPackingOrderResponse.cs**

The ineligibility case returns a *success* response (order found but wrong state) rather than an error code, because the FE needs to display the warning and the order details rather than an error banner.

```csharp
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;

public class ScanPackingOrderResponse : BaseResponse
{
    public ScanOrderData? Order { get; set; }
    public ScanShipmentData? Shipment { get; set; }

    // Ineligible: order found, wrong state, no Shoptet write
    public ScanPackingOrderResponse(ScanOrderData order)
    {
        Order = order;
    }

    // Eligible: order found, shipment created or existing
    public ScanPackingOrderResponse(ScanOrderData order, ScanShipmentData shipment)
    {
        Order = order;
        Shipment = shipment;
    }

    // System error (order not found, weight missing, carrier not found, creation failed)
    public ScanPackingOrderResponse(ErrorCodes errorCode) : base(errorCode) { }
}

// All nested response types co-located here to avoid file proliferation.

public class ScanOrderData
{
    public string Code { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string ShippingMethodName { get; set; } = null!;
    public Cooling Cooling { get; set; }
    public bool IsCooled { get; set; }
    public string? CustomerNote { get; set; }
    public string? EshopNote { get; set; }
    public ScanOrderEligibility Eligibility { get; set; } = null!;
    public List<PackingOrderItem> Items { get; set; } = [];
}

public class ScanOrderEligibility
{
    public bool IsEligible { get; set; }
    public string? WarningTitle { get; set; }
    public string? WarningBody { get; set; }
}

public class ScanShipmentData
{
    public Guid ShipmentGuid { get; set; }
    public List<ScanShipmentPackage> Packages { get; set; } = [];
    public bool AlreadyExisted { get; set; }
}

public class ScanShipmentPackage
{
    public string Name { get; set; } = null!;
}
```

- [ ] **Step 4: Create ScanPackingOrderHandler.cs**

Read `PrepareOrderLabelHandler.cs` before creating this file — it is the source of truth for the create-shipment block. Strip lines 96–106 (polling loop) and change the return type. Note: `ShoptetOrdersSettings` is injected directly (not via `IOptions<>`), matching the existing pattern in `PrepareOrderLabelHandler`.

Czech eligibility strings are copied verbatim from `GetPackingOrderHandler.cs` (`WarningTitle` and `WarningBody`).

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;

public class ScanPackingOrderHandler : IRequestHandler<ScanPackingOrderRequest, ScanPackingOrderResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly IPackingOrderClient _orderClient;
    private readonly ShipmentLabelsSettings _shipmentSettings;
    private readonly ShoptetOrdersSettings _orderSettings;
    private readonly ILogger<ScanPackingOrderHandler> _logger;

    public ScanPackingOrderHandler(
        IShipmentClient shipmentClient,
        IPackingOrderClient orderClient,
        ShipmentLabelsSettings shipmentSettings,
        ShoptetOrdersSettings orderSettings,
        ILogger<ScanPackingOrderHandler> logger)
    {
        _shipmentClient = shipmentClient;
        _orderClient = orderClient;
        _shipmentSettings = shipmentSettings;
        _orderSettings = orderSettings;
        _logger = logger;
    }

    public async Task<ScanPackingOrderResponse> Handle(ScanPackingOrderRequest request, CancellationToken ct)
    {
        var order = await _orderClient.GetPackingOrderAsync(request.OrderCode, ct);
        if (order is null)
            return new ScanPackingOrderResponse(ErrorCodes.ShoptetOrderNotFound);

        var isEligible = order.StatusId == _orderSettings.PackingStateId;
        var orderData = new ScanOrderData
        {
            Code = order.Code,
            CustomerName = order.CustomerName,
            ShippingMethodName = order.ShippingMethodName,
            Cooling = order.Cooling,
            IsCooled = order.IsCooled,
            CustomerNote = order.CustomerNote,
            EshopNote = order.EshopNote,
            Items = order.Items,
            Eligibility = new ScanOrderEligibility
            {
                IsEligible = isEligible,
                WarningTitle = isEligible ? null : "Objednávka není ve stavu „Balí se"",
                WarningBody = isEligible ? null : "Tuto objednávku nezpracovávejte, dokud nebude ve správném stavu.",
            },
        };

        if (!isEligible)
            return new ScanPackingOrderResponse(orderData);

        var existingLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        if (existingLabels.Count > 0)
        {
            var existingShipment = new ScanShipmentData
            {
                ShipmentGuid = existingLabels[0].ShipmentGuid,
                Packages = existingLabels
                    .Select(l => new ScanShipmentPackage { Name = l.PackageName })
                    .ToList(),
                AlreadyExisted = true,
            };
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

        // Single fetch for package names — no label-URL polling (FE fetches PDF via /label/pdf proxy)
        var newLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        var packages = newLabels.Count > 0
            ? newLabels.Select(l => new ScanShipmentPackage { Name = l.PackageName }).ToList()
            : [new ScanShipmentPackage { Name = "PKG-1" }];

        return new ScanPackingOrderResponse(orderData, new ScanShipmentData
        {
            ShipmentGuid = createdShipment.ShipmentGuid,
            Packages = packages,
            AlreadyExisted = false,
        });
    }
}
```

- [ ] **Step 5: Update PackagingController.cs**

Read the current file first. Replace the `PrepareLabel` action (route `orders/{orderCode}/label`) with a new `ScanOrder` action (route `orders/{orderCode}/scan`). The scan endpoint has no request body. The `GetLabelPdf` action is unchanged. Remove the `PrepareOrderLabelBody` inner class and its using/import for `PrepareOrderLabel`.

The new controller should look like this (preserving all existing using directives that remain relevant):

```csharp
using Anela.Heblo.API.Controllers.Base;
using Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/packaging")]
public class PackagingController : BaseApiController
{
    [HttpPost("orders/{orderCode}/scan")]
    public async Task<ActionResult<ScanPackingOrderResponse>> ScanOrder(
        string orderCode,
        CancellationToken ct)
    {
        var response = await Mediator.Send(new ScanPackingOrderRequest { OrderCode = orderCode }, ct);
        return HandleResponse(response);
    }

    // GetLabelPdf action stays exactly as-is — copy it verbatim from the current file
}
```

Note: also add the `shipment/reset` route in Task 2 (it's a separate use case).

- [ ] **Step 6: Delete the PrepareOrderLabel folder**

```bash
rm -rf backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/PrepareOrderLabel/
```

- [ ] **Step 7: Verify build compiles**

```bash
cd /path/to/repo && dotnet build backend/Anela.Heblo.sln
```

Expected: 0 errors. If there are errors referencing `PrepareOrderLabel*` types, search the codebase for remaining usages and remove them.

```bash
grep -r "PrepareOrderLabel" backend/src/ --include="*.cs"
```

Expected: no output.

- [ ] **Step 8: Run dotnet format**

```bash
dotnet format backend/Anela.Heblo.sln
```

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/ \
        backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs \
        backend/src/Anela.Heblo.API/Controllers/PackagingController.cs
git commit -m "feat(packaging): rename PrepareOrderLabel to ScanPackingOrder, embed order+eligibility in response"
```

---

## Task 2: Backend — DeleteShipmentAsync + ResetOrderShipment use case

**Context:** Implements the "invalidate + recreate" path. Adds `DeleteShipmentAsync` to the existing `IShipmentClient` interface, implements it in `ShoptetShipmentClient`, and creates the `ResetOrderShipment` use case + controller route. The handler: deletes the existing shipment, then runs the same weight→carrier→create block as `ScanPackingOrderHandler`.

**Prerequisite:** Human must have documented the Shoptet DELETE endpoint (see top of this plan).

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/IShipmentClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs`

- [ ] **Step 1: Add DeleteShipmentAsync to IShipmentClient.cs**

Read the current file first. Add the method to the interface:

```csharp
Task DeleteShipmentAsync(Guid shipmentGuid, CancellationToken ct = default);
```

- [ ] **Step 2: Implement DeleteShipmentAsync in ShoptetShipmentClient.cs**

Read the current file first to understand the HTTP client pattern (`_http`, base URL, error handling). Add the method. If the real DELETE URL differs from `DELETE /api/shipments/{guid}`, update it here.

```csharp
public async Task DeleteShipmentAsync(Guid shipmentGuid, CancellationToken ct)
{
    var response = await _http.DeleteAsync($"/api/shipments/{shipmentGuid}", ct);
    if (!response.IsSuccessStatusCode)
    {
        var content = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"Shoptet DELETE /api/shipments/{shipmentGuid} failed ({response.StatusCode}): {content}");
    }
}
```

- [ ] **Step 3: Create ResetOrderShipmentRequest.cs**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ResetOrderShipment;

public class ResetOrderShipmentRequest : IRequest<ResetOrderShipmentResponse>
{
    public string OrderCode { get; set; } = null!;
}
```

- [ ] **Step 4: Create ResetOrderShipmentResponse.cs**

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ResetOrderShipment;

public class ResetOrderShipmentResponse : BaseResponse
{
    public ResetShipmentData? Shipment { get; set; }

    public ResetOrderShipmentResponse(ResetShipmentData shipment)
    {
        Shipment = shipment;
    }

    public ResetOrderShipmentResponse(ErrorCodes errorCode) : base(errorCode) { }
}

public class ResetShipmentData
{
    public Guid ShipmentGuid { get; set; }
    public List<ResetShipmentPackage> Packages { get; set; } = [];
}

public class ResetShipmentPackage
{
    public string Name { get; set; } = null!;
}
```

- [ ] **Step 5: Create ResetOrderShipmentHandler.cs**

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ResetOrderShipment;

public class ResetOrderShipmentHandler : IRequestHandler<ResetOrderShipmentRequest, ResetOrderShipmentResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly IPackingOrderClient _orderClient;
    private readonly ShipmentLabelsSettings _shipmentSettings;
    private readonly ILogger<ResetOrderShipmentHandler> _logger;

    public ResetOrderShipmentHandler(
        IShipmentClient shipmentClient,
        IPackingOrderClient orderClient,
        ShipmentLabelsSettings shipmentSettings,
        ILogger<ResetOrderShipmentHandler> logger)
    {
        _shipmentClient = shipmentClient;
        _orderClient = orderClient;
        _shipmentSettings = shipmentSettings;
        _logger = logger;
    }

    public async Task<ResetOrderShipmentResponse> Handle(ResetOrderShipmentRequest request, CancellationToken ct)
    {
        var existingLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        if (existingLabels.Count == 0)
            return new ResetOrderShipmentResponse(ErrorCodes.NoShipmentToReset);

        var shipmentGuid = existingLabels[0].ShipmentGuid;

        try
        {
            await _shipmentClient.DeleteShipmentAsync(shipmentGuid, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete shipment {ShipmentGuid} for order {OrderCode}",
                shipmentGuid, request.OrderCode);
            return new ResetOrderShipmentResponse(ErrorCodes.ShipmentDeleteFailed);
        }

        var order = await _orderClient.GetPackingOrderAsync(request.OrderCode, ct);
        if (order is null)
            return new ResetOrderShipmentResponse(ErrorCodes.ShoptetOrderNotFound);

        var totalWeightGrams = order.Items.Sum(i => i.WeightGrams * i.Quantity);
        if (totalWeightGrams == 0)
            return new ResetOrderShipmentResponse(ErrorCodes.ShipmentOrderWeightUnavailable);

        var weightGrams = Math.Max(totalWeightGrams, _shipmentSettings.MinPackageWeightGrams);

        var options = await _shipmentClient.GetShippingOptionsAsync(request.OrderCode, ct);
        if (options.Count == 0)
            return new ResetOrderShipmentResponse(ErrorCodes.ShipmentCarrierNotResolved);

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
            _logger.LogError(ex, "Failed to create replacement shipment for order {OrderCode}", request.OrderCode);
            return new ResetOrderShipmentResponse(ErrorCodes.ShipmentCreationFailed);
        }

        var newLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        var packages = newLabels.Count > 0
            ? newLabels.Select(l => new ResetShipmentPackage { Name = l.PackageName }).ToList()
            : [new ResetShipmentPackage { Name = "PKG-1" }];

        return new ResetOrderShipmentResponse(new ResetShipmentData
        {
            ShipmentGuid = createdShipment.ShipmentGuid,
            Packages = packages,
        });
    }
}
```

- [ ] **Step 6: Add shipment/reset route to PackagingController.cs**

Read the current controller (already has `scan` from Task 1). Add the reset action inside the class:

```csharp
using Anela.Heblo.Application.Features.Packaging.UseCases.ResetOrderShipment;

// Inside PackagingController class:

[HttpPost("orders/{orderCode}/shipment/reset")]
public async Task<ActionResult<ResetOrderShipmentResponse>> ResetShipment(
    string orderCode,
    CancellationToken ct)
{
    var response = await Mediator.Send(new ResetOrderShipmentRequest { OrderCode = orderCode }, ct);
    return HandleResponse(response);
}
```

- [ ] **Step 7: Verify build and format**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln
```

Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShipmentLabels/IShipmentClient.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs \
        backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ \
        backend/src/Anela.Heblo.API/Controllers/PackagingController.cs
git commit -m "feat(packaging): add DeleteShipmentAsync and ResetOrderShipment use case"
```

---

## Task 3: Backend — tests

**Context:** Delete `PrepareOrderLabelHandlerTests.cs`, replace with `ScanPackingOrderHandlerTests.cs` (7 cases covering the new handler), and add `ResetOrderShipmentHandlerTests.cs` (6 cases). Use same test framework as existing tests: xUnit + Moq + FluentAssertions.

**Files:**
- Delete: `backend/test/Anela.Heblo.Tests/Application/Packaging/PrepareOrderLabelHandlerTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Application/Packaging/ResetOrderShipmentHandlerTests.cs`

- [ ] **Step 1: Read existing PrepareOrderLabelHandlerTests.cs**

Read the file to understand the mock setup pattern, fixture structure, and how `ShipmentLabelsSettings` and `ShoptetOrdersSettings` are configured in tests. Note the default values: `PackingStateId=26`, `DefaultPackageWidthMm=300`, `DefaultPackageHeightMm=200`, `DefaultPackageDepthMm=150`, `MinPackageWeightGrams=100`.

- [ ] **Step 2: Write the failing tests for ScanPackingOrderHandler**

Create `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Application.Packaging;

public class ScanPackingOrderHandlerTests
{
    private readonly Mock<IShipmentClient> _shipmentClient = new();
    private readonly Mock<IPackingOrderClient> _orderClient = new();

    private readonly ShipmentLabelsSettings _shipmentSettings = new()
    {
        DefaultPackageWidthMm = 300,
        DefaultPackageHeightMm = 200,
        DefaultPackageDepthMm = 150,
        MinPackageWeightGrams = 100,
    };

    private readonly ShoptetOrdersSettings _orderSettings = new() { PackingStateId = 26 };

    private ScanPackingOrderHandler CreateHandler() => new(
        _shipmentClient.Object,
        _orderClient.Object,
        _shipmentSettings,
        _orderSettings,
        NullLogger<ScanPackingOrderHandler>.Instance);

    private static PackingOrder MakeEligibleOrder(int weightGrams = 500) => new()
    {
        Code = "ORD001",
        CustomerName = "Test Customer",
        ShippingMethodName = "PPL",
        StatusId = 26,
        Cooling = Cooling.None,
        IsCooled = false,
        Items = [new PackingOrderItem { Name = "Item", Quantity = 1, WeightGrams = weightGrams }],
    };

    [Fact]
    public async Task Handle_OrderNotFound_ReturnsShoptetOrderNotFoundError()
    {
        _orderClient.Setup(c => c.GetPackingOrderAsync("ORD001", default)).ReturnsAsync((PackingOrder?)null);

        var result = await CreateHandler().Handle(new ScanPackingOrderRequest { OrderCode = "ORD001" }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ShoptetOrderNotFound.ToString());
    }

    [Fact]
    public async Task Handle_OrderIneligible_ReturnsSuccessWithWarning_AndNoShoptetWrite()
    {
        var ineligibleOrder = MakeEligibleOrder();
        ineligibleOrder.StatusId = 99; // not PackingStateId (26)
        _orderClient.Setup(c => c.GetPackingOrderAsync("ORD001", default)).ReturnsAsync(ineligibleOrder);

        var result = await CreateHandler().Handle(new ScanPackingOrderRequest { OrderCode = "ORD001" }, default);

        result.Success.Should().BeTrue();
        result.Order.Should().NotBeNull();
        result.Order!.Eligibility.IsEligible.Should().BeFalse();
        result.Order.Eligibility.WarningTitle.Should().NotBeNullOrEmpty();
        result.Shipment.Should().BeNull();
        _shipmentClient.Verify(c => c.GetLabelsByOrderCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _shipmentClient.Verify(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ExistingShipment_ReturnsAlreadyExistedTrue_WithoutCreating()
    {
        var order = MakeEligibleOrder();
        var shipmentGuid = Guid.NewGuid();
        _orderClient.Setup(c => c.GetPackingOrderAsync("ORD001", default)).ReturnsAsync(order);
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("ORD001", default))
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "ORD001", PackageName = "PKG-1" }]);

        var result = await CreateHandler().Handle(new ScanPackingOrderRequest { OrderCode = "ORD001" }, default);

        result.Success.Should().BeTrue();
        result.Shipment.Should().NotBeNull();
        result.Shipment!.AlreadyExisted.Should().BeTrue();
        result.Shipment.ShipmentGuid.Should().Be(shipmentGuid);
        _shipmentClient.Verify(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoExistingShipment_CreatesNewShipment_ReturnsAlreadyExistedFalse()
    {
        var order = MakeEligibleOrder();
        var newGuid = Guid.NewGuid();
        _orderClient.Setup(c => c.GetPackingOrderAsync("ORD001", default)).ReturnsAsync(order);
        _shipmentClient.SetupSequence(c => c.GetLabelsByOrderCodeAsync("ORD001", default))
            .ReturnsAsync([]) // first call: no existing
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = newGuid, OrderCode = "ORD001", PackageName = "PKG-1" }]); // second call: post-create
        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("ORD001", default))
            .ReturnsAsync([new ShippingOption { CarrierCode = "1", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), default))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = newGuid });

        var result = await CreateHandler().Handle(new ScanPackingOrderRequest { OrderCode = "ORD001" }, default);

        result.Success.Should().BeTrue();
        result.Shipment!.AlreadyExisted.Should().BeFalse();
        result.Shipment.ShipmentGuid.Should().Be(newGuid);
        result.Shipment.Packages.Should().HaveCount(1);
        result.Shipment.Packages[0].Name.Should().Be("PKG-1");
    }

    [Fact]
    public async Task Handle_ZeroItemWeight_ReturnsShipmentOrderWeightUnavailable()
    {
        var order = MakeEligibleOrder(weightGrams: 0);
        _orderClient.Setup(c => c.GetPackingOrderAsync("ORD001", default)).ReturnsAsync(order);
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("ORD001", default)).ReturnsAsync([]);

        var result = await CreateHandler().Handle(new ScanPackingOrderRequest { OrderCode = "ORD001" }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ShipmentOrderWeightUnavailable.ToString());
    }

    [Fact]
    public async Task Handle_NoCarrierOptions_ReturnsShipmentCarrierNotResolved()
    {
        var order = MakeEligibleOrder();
        _orderClient.Setup(c => c.GetPackingOrderAsync("ORD001", default)).ReturnsAsync(order);
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("ORD001", default)).ReturnsAsync([]);
        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("ORD001", default)).ReturnsAsync([]);

        var result = await CreateHandler().Handle(new ScanPackingOrderRequest { OrderCode = "ORD001" }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ShipmentCarrierNotResolved.ToString());
    }

    [Fact]
    public async Task Handle_CreateShipmentThrows_ReturnsShipmentCreationFailed()
    {
        var order = MakeEligibleOrder();
        _orderClient.Setup(c => c.GetPackingOrderAsync("ORD001", default)).ReturnsAsync(order);
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("ORD001", default)).ReturnsAsync([]);
        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("ORD001", default))
            .ReturnsAsync([new ShippingOption { CarrierCode = "1", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), default))
            .ThrowsAsync(new HttpRequestException("Shoptet error"));

        var result = await CreateHandler().Handle(new ScanPackingOrderRequest { OrderCode = "ORD001" }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ShipmentCreationFailed.ToString());
    }
}
```

- [ ] **Step 3: Run the tests — they should FAIL (handler not registered or compile issues)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ScanPackingOrderHandlerTests" \
  --no-build
```

If there are compile errors, fix them before proceeding. If tests run and pass (unexpected), verify the test setup is actually exercising the handler.

- [ ] **Step 4: Write ResetOrderShipmentHandlerTests.cs**

```csharp
using Anela.Heblo.Application.Features.Packaging.UseCases.ResetOrderShipment;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Application.Packaging;

public class ResetOrderShipmentHandlerTests
{
    private readonly Mock<IShipmentClient> _shipmentClient = new();
    private readonly Mock<IPackingOrderClient> _orderClient = new();

    private readonly ShipmentLabelsSettings _shipmentSettings = new()
    {
        DefaultPackageWidthMm = 300,
        DefaultPackageHeightMm = 200,
        DefaultPackageDepthMm = 150,
        MinPackageWeightGrams = 100,
    };

    private ResetOrderShipmentHandler CreateHandler() => new(
        _shipmentClient.Object,
        _orderClient.Object,
        _shipmentSettings,
        NullLogger<ResetOrderShipmentHandler>.Instance);

    private static ShipmentLabel MakeLabel(Guid guid) =>
        new() { ShipmentGuid = guid, OrderCode = "ORD001", PackageName = "PKG-1" };

    private static PackingOrder MakeOrder() => new()
    {
        Code = "ORD001",
        CustomerName = "Test",
        ShippingMethodName = "PPL",
        StatusId = 26,
        Cooling = Cooling.None,
        IsCooled = false,
        Items = [new PackingOrderItem { Name = "Item", Quantity = 1, WeightGrams = 500 }],
    };

    [Fact]
    public async Task Handle_NoExistingShipment_ReturnsNoShipmentToReset()
    {
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("ORD001", default)).ReturnsAsync([]);

        var result = await CreateHandler().Handle(new ResetOrderShipmentRequest { OrderCode = "ORD001" }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NoShipmentToReset.ToString());
        _shipmentClient.Verify(c => c.DeleteShipmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DeleteFails_ReturnsShipmentDeleteFailed()
    {
        var guid = Guid.NewGuid();
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("ORD001", default)).ReturnsAsync([MakeLabel(guid)]);
        _shipmentClient.Setup(c => c.DeleteShipmentAsync(guid, default))
            .ThrowsAsync(new HttpRequestException("Shoptet error"));

        var result = await CreateHandler().Handle(new ResetOrderShipmentRequest { OrderCode = "ORD001" }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ShipmentDeleteFailed.ToString());
    }

    [Fact]
    public async Task Handle_Success_DeletesOldAndCreatesNewShipment()
    {
        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();
        _shipmentClient.SetupSequence(c => c.GetLabelsByOrderCodeAsync("ORD001", default))
            .ReturnsAsync([MakeLabel(oldGuid)]) // first: existing label
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = newGuid, OrderCode = "ORD001", PackageName = "PKG-1" }]); // second: after creation
        _shipmentClient.Setup(c => c.DeleteShipmentAsync(oldGuid, default)).Returns(Task.CompletedTask);
        _orderClient.Setup(c => c.GetPackingOrderAsync("ORD001", default)).ReturnsAsync(MakeOrder());
        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("ORD001", default))
            .ReturnsAsync([new ShippingOption { CarrierCode = "1", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), default))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = newGuid });

        var result = await CreateHandler().Handle(new ResetOrderShipmentRequest { OrderCode = "ORD001" }, default);

        result.Success.Should().BeTrue();
        result.Shipment!.ShipmentGuid.Should().Be(newGuid);
        _shipmentClient.Verify(c => c.DeleteShipmentAsync(oldGuid, default), Times.Once);
    }

    [Fact]
    public async Task Handle_ZeroWeightAfterDelete_ReturnsWeightUnavailable()
    {
        var guid = Guid.NewGuid();
        var zeroWeightOrder = MakeOrder();
        zeroWeightOrder.Items = [new PackingOrderItem { Name = "Item", Quantity = 1, WeightGrams = 0 }];
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("ORD001", default)).ReturnsAsync([MakeLabel(guid)]);
        _shipmentClient.Setup(c => c.DeleteShipmentAsync(guid, default)).Returns(Task.CompletedTask);
        _orderClient.Setup(c => c.GetPackingOrderAsync("ORD001", default)).ReturnsAsync(zeroWeightOrder);

        var result = await CreateHandler().Handle(new ResetOrderShipmentRequest { OrderCode = "ORD001" }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ShipmentOrderWeightUnavailable.ToString());
    }

    [Fact]
    public async Task Handle_CreateFailsAfterDelete_ReturnsCreationFailed()
    {
        var guid = Guid.NewGuid();
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("ORD001", default)).ReturnsAsync([MakeLabel(guid)]);
        _shipmentClient.Setup(c => c.DeleteShipmentAsync(guid, default)).Returns(Task.CompletedTask);
        _orderClient.Setup(c => c.GetPackingOrderAsync("ORD001", default)).ReturnsAsync(MakeOrder());
        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("ORD001", default))
            .ReturnsAsync([new ShippingOption { CarrierCode = "1", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), default))
            .ThrowsAsync(new HttpRequestException("Shoptet error"));

        var result = await CreateHandler().Handle(new ResetOrderShipmentRequest { OrderCode = "ORD001" }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ShipmentCreationFailed.ToString());
    }
}
```

- [ ] **Step 5: Delete PrepareOrderLabelHandlerTests.cs**

```bash
rm backend/test/Anela.Heblo.Tests/Application/Packaging/PrepareOrderLabelHandlerTests.cs
```

- [ ] **Step 6: Run all packaging tests — all should pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Packaging"
```

Expected: 13 tests pass (7 scan + 6 reset).

- [ ] **Step 7: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Application/Packaging/
git commit -m "test(packaging): add ScanPackingOrder and ResetOrderShipment handler tests"
```

---

## Task 4: Frontend — new hooks + delete old hooks + update sub-component imports

**Context:** Deletes `usePackingOrder.ts` and `usePrepareOrderLabel.ts`. Creates `useScanPackingOrder.ts` (which also exports `PackingOrder`, `PackingEligibility`, `PackingOrderItem`, `Cooling` types so sub-components keep working). Creates `useResetOrderShipment.ts`. Updates the three sub-components that import `PackingOrder` from `usePackingOrder`.

The API client pattern (taken verbatim from `usePrepareOrderLabel.ts`):
```typescript
const apiClient = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
await apiClient.http.fetch(`${apiClient.baseUrl}/api/...`, { method: 'POST' });
```

**Files:**
- Create: `frontend/src/api/hooks/useScanPackingOrder.ts`
- Create: `frontend/src/api/hooks/useResetOrderShipment.ts`
- Delete: `frontend/src/api/hooks/usePackingOrder.ts`
- Delete: `frontend/src/api/hooks/usePrepareOrderLabel.ts`
- Modify (import only): `frontend/src/components/baleni/PackingStateWarning.tsx`
- Modify (import only): `frontend/src/components/baleni/PackingOrderMeta.tsx`
- Modify (import only): `frontend/src/components/baleni/PackingCoolingIndicator.tsx`

- [ ] **Step 1: Create useScanPackingOrder.ts**

Note: this file re-exports `PackingOrder`, `PackingEligibility`, `PackingOrderItem`, `Cooling` so that sub-components importing those types from `usePackingOrder` can update their import path to this file instead.

```typescript
import { useMutation } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

export type Cooling = 'None' | 'L1' | 'L2';

export interface PackingOrderItem {
  name: string;
  quantity: number;
  imageUrl: string | null;
  setName: string | null;
}

export interface PackingEligibility {
  isEligible: boolean;
  warningTitle: string | null;
  warningBody: string | null;
}

// PackingOrder is intentionally shaped to match the scan response so existing
// sub-components (PackingStateWarning, PackingOrderMeta, PackingCoolingIndicator)
// continue to work without prop-type changes.
export interface PackingOrder {
  code: string;
  customerName: string;
  shippingMethodName: string;
  cooling: Cooling;
  isCooled: boolean;
  customerNote: string | null;
  eshopNote: string | null;
  eligibility: PackingEligibility;
  items: PackingOrderItem[];
}

export interface ScanShipmentPackage {
  name: string;
}

export interface ScanShipment {
  shipmentGuid: string;
  packages: ScanShipmentPackage[];
  alreadyExisted: boolean;
}

export interface ScanPackingOrderResult {
  order: PackingOrder;
  shipment: ScanShipment | null;
}

const SCAN_ERROR_MESSAGES: Partial<Record<string, string>> = {
  ShoptetOrderNotFound: 'Objednávka nebyla nalezena.',
  ShipmentCarrierNotResolved: 'Dopravce se nepodařilo určit pro tuto objednávku.',
  ShipmentCreationFailed: 'Shoptet nemohl vytvořit zásilku — zkuste znovu.',
  ShipmentOrderWeightUnavailable: 'Nelze zjistit hmotnost objednávky.',
};

const GENERIC_SCAN_ERROR = 'Chyba při skenování objednávky.';

const scanPackingOrder = async (orderCode: string): Promise<ScanPackingOrderResult> => {
  const apiClient = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
  const response = await apiClient.http.fetch(
    `${apiClient.baseUrl}/api/packaging/orders/${encodeURIComponent(orderCode)}/scan`,
    { method: 'POST', headers: { 'Content-Type': 'application/json' } }
  );
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const data = (await response.json()) as any;

  if (!data.success) {
    const message = (data.errorCode && SCAN_ERROR_MESSAGES[data.errorCode as string]) ?? GENERIC_SCAN_ERROR;
    throw new Error(message);
  }

  return {
    order: data.order as PackingOrder,
    shipment: (data.shipment as ScanShipment) ?? null,
  };
};

export const useScanPackingOrder = () =>
  useMutation<ScanPackingOrderResult, Error, string>({
    mutationFn: scanPackingOrder,
  });
```

- [ ] **Step 2: Create useResetOrderShipment.ts**

```typescript
import { useMutation } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import type { ScanShipment } from './useScanPackingOrder';

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

const RESET_ERROR_MESSAGES: Partial<Record<string, string>> = {
  NoShipmentToReset: 'Žádná zásilka k invalidaci.',
  ShipmentDeleteFailed: 'Shoptet nemohl smazat zásilku.',
  ShipmentCreationFailed: 'Shoptet nemohl vytvořit novou zásilku.',
  ShipmentCarrierNotResolved: 'Dopravce se nepodařilo určit.',
  ShipmentOrderWeightUnavailable: 'Nelze zjistit hmotnost objednávky.',
};

const GENERIC_RESET_ERROR = 'Chyba při invalidaci zásilky.';

const resetOrderShipment = async (orderCode: string): Promise<ScanShipment> => {
  const apiClient = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
  const response = await apiClient.http.fetch(
    `${apiClient.baseUrl}/api/packaging/orders/${encodeURIComponent(orderCode)}/shipment/reset`,
    { method: 'POST' }
  );
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const data = (await response.json()) as any;

  if (!data.success) {
    const message = (data.errorCode && RESET_ERROR_MESSAGES[data.errorCode as string]) ?? GENERIC_RESET_ERROR;
    throw new Error(message);
  }

  return data.shipment as ScanShipment;
};

export const useResetOrderShipment = () =>
  useMutation<ScanShipment, Error, string>({
    mutationFn: resetOrderShipment,
  });
```

- [ ] **Step 3: Update import in PackingStateWarning.tsx, PackingOrderMeta.tsx, PackingCoolingIndicator.tsx**

In each file, change:
```typescript
// BEFORE (exact string will vary — grep for it)
import { ... } from '../hooks/usePackingOrder';
// or
import { ... } from '@/api/hooks/usePackingOrder';

// AFTER (same prefix, different file name)
import { ... } from '../hooks/useScanPackingOrder';
// or
import { ... } from '@/api/hooks/useScanPackingOrder';
```

Run to find all occurrences:
```bash
grep -r "usePackingOrder" frontend/src/components/ --include="*.tsx" -l
```

- [ ] **Step 4: Delete old hooks**

```bash
rm frontend/src/api/hooks/usePackingOrder.ts
rm frontend/src/api/hooks/usePrepareOrderLabel.ts
```

- [ ] **Step 5: Verify TypeScript build passes**

```bash
cd frontend && npm run build
```

Expected: no type errors. If sub-components fail to find `PackingOrder` type, verify the import path update in Step 3 is correct.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/hooks/ \
        frontend/src/components/baleni/PackingStateWarning.tsx \
        frontend/src/components/baleni/PackingOrderMeta.tsx \
        frontend/src/components/baleni/PackingCoolingIndicator.tsx
git commit -m "feat(packaging): replace usePackingOrder+usePrepareOrderLabel with useScanPackingOrder+useResetOrderShipment"
```

---

## Task 5: Frontend — rewrite BaleniPacking.tsx

**Context:** `BaleniPacking.tsx` (101 lines) currently calls `usePackingOrder(scannedCode)` (a query hook). The rewrite replaces it with `useScanPackingOrder()` (a mutation hook). The component now calls `scanMutation.mutate(value)` on scan, and passes the scan result directly into `PackingShipmentCreator`.

`PackingShipmentCreator` should only render when the order is eligible. The existing sub-components (`PackingStateWarning`, `PackingOrderMeta`, `PackingCoolingIndicator`, `PackingOrderNotes`, `PackingItems`) keep their current props — they all accept fields from `PackingOrder`, and `ScanPackingOrderResult.order` has the same shape (ensured in Task 4).

**Before writing code:** Read the current `BaleniPacking.tsx` to identify:
- The exact import paths for shared UI components (loading spinner, error display, scan prompt)
- The wrapper JSX structure and CSS classes
- How `ScanInput` is used (prop name for the callback)

**Files:**
- Rewrite: `frontend/src/components/baleni/BaleniPacking.tsx`

- [ ] **Step 1: Write the failing test (there are no dedicated BaleniPacking tests — skip RED step, go straight to implementation)**

`BaleniPacking` integration is verified by the FE build + manual smoke test in Phase 5. Proceed to implementation.

- [ ] **Step 2: Rewrite BaleniPacking.tsx**

Read the current file first. Preserve: all existing imports for sub-components, wrapper JSX structure, CSS class names, and how `ScanInput`'s callback is named. The key changes are:
1. Remove `useState<string | null>` for `scannedCode`
2. Remove `usePackingOrder` import + hook
3. Add `useScanPackingOrder` mutation
4. `handleScan` calls `scanMutation.mutate(value)` (no state needed for code)
5. `renderBody()` reads from `scanMutation` instead of query hook result
6. Pass `scanShipment={order.eligibility.isEligible ? shipment : null}` (or omit `PackingShipmentCreator` entirely when ineligible)

```tsx
import { useScanPackingOrder } from '@/api/hooks/useScanPackingOrder';
// ... keep all other existing imports

function BaleniPacking() {
  const scanMutation = useScanPackingOrder();

  function handleScan(value: string) {
    scanMutation.mutate(value);
  }

  function renderBody() {
    if (scanMutation.isPending) {
      // Use same loading indicator as current implementation — check current file
      return <LoadingSpinner message="Načítám objednávku…" />;
    }

    if (scanMutation.isError) {
      // Use same error display as current implementation — check current file
      return <ErrorDisplay message={scanMutation.error.message} />;
    }

    if (!scanMutation.data) {
      // Use same scan-prompt as current implementation — check current file
      return <ScanPrompt />;
    }

    const { order, shipment } = scanMutation.data;

    return (
      <>
        <PackingStateWarning order={order} />
        <PackingOrderMeta order={order} />
        <PackingCoolingIndicator order={order} />
        <PackingOrderNotes customerNote={order.customerNote} eshopNote={order.eshopNote} />
        {order.eligibility.isEligible && (
          <PackingShipmentCreator orderCode={order.code} scanShipment={shipment} />
        )}
        <PackingItems items={order.items} />
      </>
    );
  }

  return (
    // Preserve existing wrapper element/className from current file
    <>
      <ScanInput onScan={handleScan} />
      {renderBody()}
    </>
  );
}

export default BaleniPacking;
```

The placeholder names (`LoadingSpinner`, `ErrorDisplay`, `ScanPrompt`) must be replaced with whatever the current `BaleniPacking.tsx` actually uses. Read the file before writing.

- [ ] **Step 3: Verify TypeScript build**

```bash
cd frontend && npm run build
```

- [ ] **Step 4: Run lint**

```bash
cd frontend && npm run lint
```

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/baleni/BaleniPacking.tsx
git commit -m "feat(packaging): rewrite BaleniPacking to use scan mutation instead of order query"
```

---

## Task 6: Frontend — rewrite PackingShipmentCreator.tsx + update its tests

**Context:** `PackingShipmentCreator.tsx` (110 lines) currently owns shipment creation via `usePrepareOrderLabel`. In the new design it is a **passive controller**: it receives `scanShipment: ScanShipment | null` as a prop and decides what to do based on `alreadyExisted`. No button. Auto-print when `alreadyExisted = false`, dialog when `alreadyExisted = true`.

`PackingLabelPrinter` still takes `labels: ShipmentLabelDto[]`. To create `ShipmentLabelDto[]` from `ScanShipment`, cast a minimal object — `printLabelPdf` only reads `shipmentGuid` and `packageName` from it (verified in the codebase exploration).

**Files:**
- Rewrite: `frontend/src/components/baleni/PackingShipmentCreator.tsx`
- Rewrite: `frontend/src/components/baleni/__tests__/PackingShipmentCreator.test.tsx`

- [ ] **Step 1: Read the current PackingShipmentCreator.tsx and its test file**

Note: exact component imports (spinner, error display), CSS class names, the `PackingLabelPrinter` import path, and what `ShipmentLabelDto` import looks like.

- [ ] **Step 2: Write the failing tests first**

The new tests mock `useScanPackingOrder` (indirectly via props — no hook needed in tests), `useResetOrderShipment`, and `PackingLabelPrinter`. The component no longer has a "create" button; tests focus on the dialog and auto-print paths.

Create `frontend/src/components/baleni/__tests__/PackingShipmentCreator.test.tsx`:

```tsx
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import PackingShipmentCreator from '../PackingShipmentCreator';
import type { ScanShipment } from '@/api/hooks/useScanPackingOrder';

// Mock PackingLabelPrinter
vi.mock('../PackingLabelPrinter', () => ({
  default: ({ labels }: { labels: unknown[] }) => (
    <div data-testid="label-printer">{labels.length} labels</div>
  ),
}));

// Mock useResetOrderShipment
const mockResetMutate = vi.fn();
vi.mock('@/api/hooks/useResetOrderShipment', () => ({
  useResetOrderShipment: () => ({
    mutate: mockResetMutate,
    isPending: false,
    isError: false,
    error: null,
  }),
}));

const newShipment: ScanShipment = {
  shipmentGuid: 'guid-123',
  packages: [{ name: 'PKG-1' }],
  alreadyExisted: false,
};

const existingShipment: ScanShipment = {
  ...newShipment,
  alreadyExisted: true,
};

describe('PackingShipmentCreator', () => {
  beforeEach(() => {
    mockResetMutate.mockClear();
  });

  it('renders nothing when scanShipment is null', () => {
    const { container } = render(
      <PackingShipmentCreator orderCode="ORD001" scanShipment={null} />
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('auto-shows PackingLabelPrinter when shipment is new (alreadyExisted=false)', () => {
    render(<PackingShipmentCreator orderCode="ORD001" scanShipment={newShipment} />);
    expect(screen.getByTestId('label-printer')).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('shows dialog when shipment already existed', () => {
    render(<PackingShipmentCreator orderCode="ORD001" scanShipment={existingShipment} />);
    expect(screen.queryByTestId('label-printer')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /použít existující|reprint/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /novou zásilku|invalidovat/i })).toBeInTheDocument();
  });

  it('clicking reprint closes dialog and shows PackingLabelPrinter', () => {
    render(<PackingShipmentCreator orderCode="ORD001" scanShipment={existingShipment} />);
    fireEvent.click(screen.getByRole('button', { name: /použít existující|reprint/i }));
    expect(screen.getByTestId('label-printer')).toBeInTheDocument();
  });

  it('clicking invalidate & new calls useResetOrderShipment with orderCode', () => {
    render(<PackingShipmentCreator orderCode="ORD001" scanShipment={existingShipment} />);
    fireEvent.click(screen.getByRole('button', { name: /novou zásilku|invalidovat/i }));
    expect(mockResetMutate).toHaveBeenCalledWith('ORD001', expect.any(Object));
  });

  it('shows loading spinner while reset is pending', () => {
    vi.mocked(require('@/api/hooks/useResetOrderShipment').useResetOrderShipment).mockReturnValueOnce({
      mutate: mockResetMutate,
      isPending: true,
      isError: false,
      error: null,
    });
    render(<PackingShipmentCreator orderCode="ORD001" scanShipment={existingShipment} />);
    fireEvent.click(screen.getByRole('button', { name: /novou zásilku|invalidovat/i }));
    // Loading state should show — check for spinner or loading text
    expect(screen.queryByTestId('label-printer')).not.toBeInTheDocument();
  });

  it('shows error banner when reset fails', () => {
    vi.mocked(require('@/api/hooks/useResetOrderShipment').useResetOrderShipment).mockReturnValueOnce({
      mutate: mockResetMutate,
      isPending: false,
      isError: true,
      error: new Error('Shoptet nemohl smazat zásilku.'),
    });
    render(<PackingShipmentCreator orderCode="ORD001" scanShipment={existingShipment} />);
    fireEvent.click(screen.getByRole('button', { name: /novou zásilku|invalidovat/i }));
    // Error state should render error message somewhere
  });
});
```

Note: The button labels in the regex (`/použít existující|reprint/i` etc.) should be updated to match the exact Czech labels you write in the component.

- [ ] **Step 3: Run tests — they should FAIL**

```bash
cd frontend && npm test -- --run components/baleni/__tests__/PackingShipmentCreator
```

Expected: multiple failures (component doesn't match new design yet).

- [ ] **Step 4: Rewrite PackingShipmentCreator.tsx**

Read the current file to understand the existing import paths (spinner, error display, `PackingLabelPrinter`, `ShipmentLabelDto`).

```tsx
import { useEffect, useState } from 'react';
import { useResetOrderShipment } from '@/api/hooks/useResetOrderShipment';
import type { ScanShipment } from '@/api/hooks/useScanPackingOrder';
import { ShipmentLabelDto } from '@/api/generated/api-client'; // verify exact import path from old file
import PackingLabelPrinter from './PackingLabelPrinter';
// Keep other imports for spinner/error components from the current file

interface PackingShipmentCreatorProps {
  orderCode: string;
  scanShipment: ScanShipment | null;
}

// Converts ScanShipment into the shape PackingLabelPrinter/printLabelPdf expects.
// printLabelPdf only reads shipmentGuid and packageName — other fields are unused.
function toLabels(shipment: ScanShipment): ShipmentLabelDto[] {
  return shipment.packages.map(
    (pkg) =>
      ({
        shipmentGuid: shipment.shipmentGuid,
        packageName: pkg.name,
        labelUrl: null,
        labelZpl: null,
        hasPdf: false,
        hasZpl: false,
        trackingNumber: null,
        trackingUrl: null,
      }) as ShipmentLabelDto
  );
}

function PackingShipmentCreator({ orderCode, scanShipment }: PackingShipmentCreatorProps) {
  const [showDialog, setShowDialog] = useState(false);
  const [labelsForPrint, setLabelsForPrint] = useState<ShipmentLabelDto[] | null>(null);
  const resetMutation = useResetOrderShipment();

  useEffect(() => {
    if (!scanShipment) return;
    setShowDialog(false);
    setLabelsForPrint(null);
    if (scanShipment.alreadyExisted) {
      setShowDialog(true);
    } else {
      setLabelsForPrint(toLabels(scanShipment));
    }
  }, [scanShipment]);

  function handleReprint() {
    setShowDialog(false);
    setLabelsForPrint(toLabels(scanShipment!));
  }

  function handleInvalidateAndNew() {
    setShowDialog(false);
    resetMutation.mutate(orderCode, {
      onSuccess: (newShipment) => {
        setLabelsForPrint(toLabels(newShipment));
      },
    });
  }

  if (labelsForPrint) {
    return <PackingLabelPrinter orderCode={orderCode} labels={labelsForPrint} />;
  }

  if (resetMutation.isPending) {
    // Use same spinner component as current file
    return <LoadingSpinner message="Vytvářím novou zásilku…" />;
  }

  if (resetMutation.isError) {
    // Use same error component as current file
    return <ErrorDisplay message={resetMutation.error.message} />;
  }

  if (showDialog) {
    return (
      <div>
        <p>Zásilka pro tuto objednávku již existuje.</p>
        <button onClick={handleReprint}>Použít existující zásilku</button>
        <button onClick={handleInvalidateAndNew}>Vytvořit novou zásilku</button>
      </div>
    );
  }

  return null;
}

export default PackingShipmentCreator;
```

Replace `LoadingSpinner` and `ErrorDisplay` with the real component names from the current file.

- [ ] **Step 5: Run tests — all should pass**

```bash
cd frontend && npm test -- --run components/baleni/__tests__/PackingShipmentCreator
```

Fix any test mismatches (e.g., button label text not matching the regex patterns in tests).

- [ ] **Step 6: Verify full build**

```bash
cd frontend && npm run build && npm run lint
```

- [ ] **Step 7: Commit**

```bash
git add frontend/src/components/baleni/PackingShipmentCreator.tsx \
        frontend/src/components/baleni/__tests__/PackingShipmentCreator.test.tsx
git commit -m "feat(packaging): rewrite PackingShipmentCreator as passive dialog+print controller"
```

---

## Task 7: Docs — create docs/features/packaging.md

**Context:** Durable feature spec so future packaging changes have a clear contract. No code — documentation only.

**Files:**
- Create: `docs/features/packaging.md`

- [ ] **Step 1: Create docs/features/packaging.md**

```markdown
# Packaging (Balení) Feature Spec

## 1. Purpose

The Balení screen is a kiosk-style terminal where warehouse staff scan order barcodes, verify packing eligibility, and print shipping labels. It is designed for a single dedicated device with a barcode scanner and a label printer. Staff do not navigate — they scan, confirm visually, and move to the next order.

## 2. Actors & Devices

| Actor | Device | Notes |
|---|---|---|
| Packer | Kiosk terminal (touchscreen or keyboard) | Runs a browser in kiosk mode (auto-confirms print dialogs) |
| Barcode scanner | USB or Bluetooth HID | Emits order code as keystrokes, triggers scan event |
| Label printer | Network-connected label printer | Driven by the browser's default printer via `iframe.contentWindow.print()` |

## 3. End-to-End Workflow

```
[idle] ──scan──▶ POST /api/packaging/orders/{code}/scan
         │
         ├── order not found                  → error banner
         ├── order ineligible                 → PackingStateWarning; stop
         ├── eligible & shipment is NEW       → fetch label PDF → iframe.print()
         └── eligible & shipment EXISTED      → dialog
                  ├── "Použít existující"     → fetch label PDF → iframe.print()
                  └── "Vytvořit novou"        → POST /api/packaging/orders/{code}/shipment/reset
                                              → fetch label PDF → iframe.print()
```

**Scan** always triggers a POST `/scan`. There is no separate "create shipment" button.

**Label fetch** is always `GET /api/packaging/orders/{code}/label/pdf?shipmentGuid=...&packageName=...`, proxied by the BE from Balíkobot/carrier.

## 4. States & Guards

### Eligibility

An order is eligible for packing iff `order.statusId == ShoptetOrdersSettings.PackingStateId` (default: 26, "Balí se"). Ineligible orders render a Czech warning (`warningTitle`, `warningBody` from the scan response) and block all Shoptet writes.

### Already-Shipped Signal

"Shipment already exists" = `GET /api/shipments?orderCode={code}` returns at least one item. Derived at scan time by `ScanPackingOrderHandler`. The FE receives `shipment.alreadyExisted: true`.

## 5. API Contract

### POST /api/packaging/orders/{orderCode}/scan

No request body required.

**Success response (order found):**
```json
{
  "success": true,
  "order": {
    "code": "ORD001",
    "customerName": "Jana Nováková",
    "shippingMethodName": "PPL",
    "cooling": "None",
    "isCooled": false,
    "customerNote": null,
    "eshopNote": null,
    "eligibility": {
      "isEligible": true,
      "warningTitle": null,
      "warningBody": null
    },
    "items": [{ "name": "...", "quantity": 1, "imageUrl": null, "setName": null }]
  },
  "shipment": {
    "shipmentGuid": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "packages": [{ "name": "PKG-1" }],
    "alreadyExisted": false
  }
}
```

`shipment` is `null` when `eligibility.isEligible` is `false`.

**Error response:**
```json
{ "success": false, "errorCode": "ShoptetOrderNotFound" }
```

Error codes: `ShoptetOrderNotFound`, `ShipmentOrderWeightUnavailable`, `ShipmentCarrierNotResolved`, `ShipmentCreationFailed`.

---

### POST /api/packaging/orders/{orderCode}/shipment/reset

No request body required. Deletes the existing Shoptet shipment, then creates a new one.

**Success response:**
```json
{
  "success": true,
  "shipment": {
    "shipmentGuid": "new-guid",
    "packages": [{ "name": "PKG-1" }]
  }
}
```

Error codes: `NoShipmentToReset` (HTTP 409), `ShipmentDeleteFailed` (HTTP 503), `ShipmentCreationFailed`, `ShipmentCarrierNotResolved`, `ShipmentOrderWeightUnavailable`, `ShoptetOrderNotFound`.

---

### GET /api/packaging/orders/{orderCode}/label/pdf?shipmentGuid=...&packageName=...

Unchanged PDF proxy. Returns `application/pdf`. Used by `printLabelPdf.ts` via iframe print.

## 6. Shoptet Integration

See `docs/integrations/shoptet-api.md` §11 for shipment endpoints. Constraints:
- **No sandbox** — every API call hits the live store.
- Endpoints used: `GET /api/shipments?orderCode=...`, `GET /api/shipments/order/{code}/shipping-options`, `POST /api/shipments`, `DELETE /api/shipments/{shipmentGuid}`.
- Carrier code is resolved via shipping-options (`shippingId` cast to string).
- Label URL latency: Balíkobot may take a few seconds. The scan endpoint does NOT wait for label URL readiness; `printLabelPdf` fetches on demand.

## 7. Printing Model

- FE creates an invisible `<iframe>` with a Blob URL of the PDF.
- On iframe load: `iframe.contentWindow.print()` opens the browser print dialog.
- In kiosk mode the print dialog is auto-confirmed by the OS.
- Failure modes: PDF proxy returns 404 (label not yet ready, carrier latency) or fetch error (network). `printLabelPdf.ts` currently silently swallows errors; a future improvement would surface them.

## 8. Failure Modes & Recovery

| Failure | BE returns | FE displays |
|---|---|---|
| Shoptet order not found | `ShoptetOrderNotFound` (404) | Error banner |
| Order in wrong state | `success: true`, `eligibility.isEligible: false` | PackingStateWarning |
| Weight data missing | `ShipmentOrderWeightUnavailable` (422) | Error banner |
| Carrier not resolved | `ShipmentCarrierNotResolved` (422) | Error banner |
| Shipment creation fails | `ShipmentCreationFailed` (503) | Error banner |
| Shipment delete fails | `ShipmentDeleteFailed` (503) | Error banner (reset path) |
| No shipment to reset | `NoShipmentToReset` (409) | Error banner (reset path) |
| Label PDF not ready | HTTP 404 from label/pdf proxy | printLabelPdf silently fails (kiosk retry) |

## 9. Configuration

| Key | Location | Default | Purpose |
|---|---|---|---|
| `ShoptetOrdersSettings:PackingStateId` | `appsettings.json` | 26 | Shoptet status ID for "Balí se" |
| `ShipmentLabels:MinPackageWeightGrams` | `appsettings.json` | 100 | Floor for package weight sent to carrier |
| `ShipmentLabels:DefaultPackage*Mm` | `appsettings.json` | 300×200×150 | Default package dimensions |
| Kiosk mode (print auto-confirm) | Browser/OS setting | — | Set at device level; not app-configurable |

## 10. Future Improvements (out of scope here)

- DB-backed print history for audit log
- Per-package "printed" toggle in UI
- Multi-package workflow (currently creates one package per shipment)
- Server-side printing via CUPS print sink (`docs/superpowers/plans/2026-03-25-cups-print-sink.md`)
- Label-reprint audit log
- Surface `printLabelPdf` errors in the UI instead of silent failure

## 11. Glossary

| Term | Meaning |
|---|---|
| Balení / Balí se | Czech for "packing" / "is being packed" (the order status that gates packing) |
| Zásilka | Shipment (Shoptet entity created via Balíkobot) |
| Štítek | Label (the PDF output printed on the label printer) |
| Expedice | Dispatch / expedition (a related but separate workflow) |
```

- [ ] **Step 2: Commit**

```bash
git add docs/features/packaging.md
git commit -m "docs(packaging): add packaging feature spec"
```

---

## Verification

### Build / tests gate

```bash
# Backend
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Packaging"

# Frontend
cd frontend && npm run build && npm run lint
cd frontend && npm test -- --run components/baleni
```

### Functional smoke test (manual — see Phase 5 in the spec)

1. Scan an order in "Balí se" with no existing shipment → label prints automatically.
2. Scan the same order again → dialog appears; "Použít existující zásilku" reprints.
3. From dialog, click "Vytvořit novou zásilku" → old shipment deleted, new created, label prints.
4. Scan an order NOT in "Balí se" → warning renders; no Shoptet write.

### Doc sanity

- `docs/integrations/shoptet-api.md` has the DELETE shipment endpoint documented (prerequisite).
- `docs/features/packaging.md` exists and renders correctly.
