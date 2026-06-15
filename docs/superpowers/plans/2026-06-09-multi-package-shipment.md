# Multi-Package Shipment in Packaging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a packing operator ship an order in multiple parcels — a button next to the scan box opens a modal to pick how many packages (2–10, default 2), confirmed by scanning the order; N labels then print one-by-one, and the order is marked `Zabaleno` only after the last label is acknowledged.

**Architecture:** The frontend `PackingLabelPrinter` already prints an array of labels one-by-one and shows a done view — it needs almost no change. The work is: (1) backend creates N identical packages in one Shoptet shipment (weight split evenly) and **defers** mark-as-packed for N≥2, signalling this via a new `pendingCompletion` flag; (2) a new completion endpoint marks the order packed; (3) the frontend adds the multi-package button + modal and fires the completion call when the printer finishes. Single-package (N=1) behaviour is unchanged — it still marks packed at scan time.

**Tech Stack:** .NET 8, MediatR, MVC controllers, Moq + xUnit + FluentAssertions (backend); React 18, TanStack Query, Jest + React Testing Library, Tailwind, Lucide icons (frontend).

---

## Decisions baked into this plan (confirmed with the user)

1. **Deferred state change for multi-package only.** Scan with N≥2 does **not** call `MarkAsPackedAsync`; the new `POST …/packing/complete` endpoint does, fired by the frontend after the last label. N=1 keeps marking at scan time (proven path, byte-for-byte unchanged). A `pendingCompletion` flag on the scan response drives the frontend so it doesn't have to remember N.
2. **Weight split evenly:** each package weight = `Max(totalWeightGrams / N, MinPackageWeightGrams)`.
3. **Range 2–10** in the modal; the `-` button stops at 2, `+` at 10.
4. **`pendingCompletion` is an optional field** on the `ScanShipment` TS interface (absent ⇒ treated as `false`). This avoids touching unrelated test fixtures that build `ScanShipment`.

## File structure

**Backend (create):**
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/CompletePackingOrder/CompletePackingOrderRequest.cs`
- `…/CompletePackingOrder/CompletePackingOrderResponse.cs`
- `…/CompletePackingOrder/CompletePackingOrderHandler.cs`
- `backend/test/Anela.Heblo.Tests/Application/Packaging/CompletePackingOrderHandlerTests.cs`

**Backend (modify):**
- `…/Application/Shared/ErrorCodes.cs` — two new codes
- `…/Application/Features/ShipmentLabels/ShipmentCreation.cs` — `CreateShipmentCommand.PackageCount`
- `…/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs` — build N packages
- `…/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderRequest.cs` — `NumberOfPackages`
- `…/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderResponse.cs` — `ScanShipmentData.PendingCompletion`
- `…/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs` — N packages, split weight, defer mark-as-packed
- `…/API/Controllers/PackagingController.cs` — scan query param + completion endpoint
- `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs` — new cases

**Frontend (create):**
- `frontend/src/components/baleni/MultiPackageModal.tsx`
- `frontend/src/components/baleni/__tests__/MultiPackageModal.test.tsx`
- `frontend/src/api/hooks/useCompletePackingOrder.ts`
- `frontend/src/api/hooks/__tests__/useCompletePackingOrder.test.ts`

**Frontend (modify):**
- `frontend/src/api/hooks/useScanPackingOrder.ts` — object variables + `pendingCompletion`
- `frontend/src/components/baleni/BaleniPacking.tsx` — button + modal wiring
- `frontend/src/components/baleni/PackingLabelPrinter.tsx` — fire completion on done
- `frontend/src/components/baleni/__tests__/BaleniPacking.test.tsx` — update `mutate` assertions, new modal test
- `frontend/src/components/baleni/__tests__/PackingLabelPrinter.test.tsx` — mock completion hook, new case

**Docs (modify):**
- `docs/integrations/shoptet-api.md` — document multi-package shipment creation

---

## Task 1: Add the two new error codes

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:373`

- [ ] **Step 1: Add the codes**

After the `PackageNotFound = 3006,` line (currently line 373), insert:

```csharp
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidPackageCount = 3007,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    PackingCompletionFailed = 3008,
```

- [ ] **Step 2: Build to verify the enum compiles**

Run: `cd backend && dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
git commit -m "feat(packaging): add InvalidPackageCount and PackingCompletionFailed error codes"
```

---

## Task 2: `CreateShipmentCommand.PackageCount` + Shoptet client replicates packages

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentCreation.cs:9-14`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs:115-135`

This is an adapter behaviour change asserted at the handler level in Task 3 (the codebase has no `ShoptetShipmentClient` unit test harness). Keep it backward compatible: default `PackageCount = 1`.

- [ ] **Step 1: Add `PackageCount` to the command**

In `ShipmentCreation.cs`, change `CreateShipmentCommand` to:

```csharp
public class CreateShipmentCommand
{
    public string OrderCode { get; set; } = null!;
    public string CarrierCode { get; set; } = null!;
    public int PackageCount { get; set; } = 1;
    public ShipmentPackage Package { get; set; } = null!;
}
```

- [ ] **Step 2: Build N identical packages in the client**

In `ShoptetShipmentClient.CreateShipmentAsync`, replace the `var envelope = …` block (currently lines 118-135) with:

```csharp
        var packageCount = command.PackageCount < 1 ? 1 : command.PackageCount;
        var packages = Enumerable.Range(0, packageCount)
            .Select(_ => new ShoptetCreatePackageDto
            {
                Width = command.Package.WidthMm,
                Height = command.Package.HeightMm,
                Depth = command.Package.DepthMm,
                Weight = weightKg,
            })
            .ToList();

        var envelope = new ShoptetCreateShipmentRequestEnvelope
        {
            Data = new ShoptetCreateShipmentRequestData
            {
                OrderCode = command.OrderCode,
                ShippingId = shippingId,
                Packages = packages,
            }
        };
```

(`weightKg` is already computed just above. `System.Linq` is available via global usings — confirm build.)

- [ ] **Step 3: Build**

Run: `cd backend && dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentCreation.cs backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs
git commit -m "feat(packaging): support N packages in CreateShipmentCommand and Shoptet client"
```

---

## Task 3: Scan handler — N packages, even weight split, deferred mark-as-packed

**Files:**
- Modify: `…/ScanPackingOrder/ScanPackingOrderRequest.cs`
- Modify: `…/ScanPackingOrder/ScanPackingOrderResponse.cs:53-58`
- Modify: `…/ScanPackingOrder/ScanPackingOrderHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

Append these tests inside the `ScanPackingOrderHandlerTests` class (before the closing brace), after the existing `Handle_OrderNotInPackingState_DoesNotMarkAsPacked` test:

```csharp
    // Multi-package: out-of-range count is rejected before any work
    [Fact]
    public async Task Handle_NumberOfPackagesAboveMax_ReturnsInvalidPackageCount()
    {
        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234", NumberOfPackages = 11 },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidPackageCount);
        _orderClient.Verify(
            c => c.GetPackingOrderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Multi-package: creates N packages, splits weight evenly, does NOT mark packed
    [Fact]
    public async Task Handle_MultiPackage_CreatesNPackages_SplitsWeight_AndDefersMarkAsPacked()
    {
        var shipmentGuid = Guid.NewGuid();
        CreateShipmentCommand? captured = null;

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 900)));

        _shipmentClient
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync(new List<ShipmentLabel>
            {
                new() { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://c/1.pdf" },
                new() { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P2", LabelUrl = "https://c/2.pdf" },
                new() { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P3", LabelUrl = "https://c/3.pdf" },
            });

        _shipmentClient
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "1", Name = "PPL" }]);

        _shipmentClient
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .Callback<CreateShipmentCommand, CancellationToken>((cmd, _) => captured = cmd)
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234", NumberOfPackages = 3 },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.PackageCount.Should().Be(3);
        captured.Package.WeightGrams.Should().Be(300); // 900 / 3
        response.Shipment!.PendingCompletion.Should().BeTrue();
        response.Shipment.Packages.Should().HaveCount(3);

        _eshopOrderClient.Verify(
            c => c.MarkAsPackedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Multi-package: per-package weight is floored at MinPackageWeightGrams
    [Fact]
    public async Task Handle_MultiPackage_FloorsPerPackageWeightAtMinimum()
    {
        var shipmentGuid = Guid.NewGuid();
        CreateShipmentCommand? captured = null;

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 120))); // 120 / 3 = 40 < min 100

        _shipmentClient
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync(new List<ShipmentLabel>
            {
                new() { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1" },
            });

        _shipmentClient
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "1", Name = "PPL" }]);

        _shipmentClient
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .Callback<CreateShipmentCommand, CancellationToken>((cmd, _) => captured = cmd)
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });

        await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234", NumberOfPackages = 3 },
            CancellationToken.None);

        captured!.Package.WeightGrams.Should().Be(100);
    }

    // Single package (default): still marks packed, PendingCompletion = false
    [Fact]
    public async Task Handle_SinglePackage_MarksPacked_AndPendingCompletionFalse()
    {
        var shipmentGuid = Guid.NewGuid();

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 400)));

        _shipmentClient
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync(new List<ShipmentLabel>
            {
                new() { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://c/1.pdf" },
            });

        _shipmentClient
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "1", Name = "PPL" }]);

        _shipmentClient
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234", NumberOfPackages = 1 },
            CancellationToken.None);

        response.Shipment!.PendingCompletion.Should().BeFalse();
        _eshopOrderClient.Verify(
            c => c.MarkAsPackedAsync("0001234", It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run the new tests — verify they fail to compile / fail**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~ScanPackingOrderHandlerTests"`
Expected: FAIL — `NumberOfPackages`, `PackageCount`, and `PendingCompletion` do not exist yet.

- [ ] **Step 3: Add `NumberOfPackages` to the request**

Replace `ScanPackingOrderRequest.cs` body with:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;

public class ScanPackingOrderRequest : IRequest<ScanPackingOrderResponse>
{
    public string OrderCode { get; set; } = null!;
    public int NumberOfPackages { get; set; } = 1;
}
```

- [ ] **Step 4: Add `PendingCompletion` to `ScanShipmentData`**

In `ScanPackingOrderResponse.cs`, change `ScanShipmentData` to:

```csharp
public class ScanShipmentData
{
    public Guid ShipmentGuid { get; set; }
    public List<ScanShipmentPackage> Packages { get; set; } = [];
    public bool AlreadyExisted { get; set; }
    public bool PendingCompletion { get; set; }
}
```

- [ ] **Step 5: Implement the handler changes**

In `ScanPackingOrderHandler.cs`:

(a) Add the guard as the **first** statement of `Handle` (before `var order = …`):

```csharp
        const int maxPackages = 10;
        if (request.NumberOfPackages < 1 || request.NumberOfPackages > maxPackages)
            return new ScanPackingOrderResponse(ErrorCodes.InvalidPackageCount);
```

(b) Replace the weight + command block (currently lines 111-128) with:

```csharp
        var n = request.NumberOfPackages;
        var perPackageWeightGrams = Math.Max(totalWeightGrams / n, _shipmentSettings.MinPackageWeightGrams);

        var options = await _shipmentClient.GetShippingOptionsAsync(request.OrderCode, ct);
        if (options.Count == 0)
            return new ScanPackingOrderResponse(ErrorCodes.ShipmentCarrierNotResolved);

        var command = new CreateShipmentCommand
        {
            OrderCode = request.OrderCode,
            CarrierCode = options[0].CarrierCode,
            PackageCount = n,
            Package = new ShipmentPackage
            {
                WidthMm = _shipmentSettings.DefaultPackageWidthMm,
                HeightMm = _shipmentSettings.DefaultPackageHeightMm,
                DepthMm = _shipmentSettings.DefaultPackageDepthMm,
                WeightGrams = perPackageWeightGrams,
            },
        };
```

(c) Replace the final block (currently lines 161-167, `await TryMarkAsPackedAsync(…)` through the `return`) with:

```csharp
        var pendingCompletion = n >= 2;
        if (!pendingCompletion)
        {
            await TryMarkAsPackedAsync(request.OrderCode, ct);
        }

        return new ScanPackingOrderResponse(orderData, new ScanShipmentData
        {
            ShipmentGuid = createdShipment.ShipmentGuid,
            Packages = packages,
            AlreadyExisted = false,
            PendingCompletion = pendingCompletion,
        });
```

(Leave the existing-shipment and ineligible branches untouched — they keep `PendingCompletion = false` by default and their existing mark-as-packed behaviour.)

- [ ] **Step 6: Run the handler tests — verify all pass**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~ScanPackingOrderHandlerTests"`
Expected: PASS (all old + 4 new tests).

- [ ] **Step 7: Format + commit**

```bash
cd backend && dotnet format
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs
git commit -m "feat(packaging): create N packages and defer mark-as-packed for multi-package scans"
```

---

## Task 4: Scan controller accepts `numberOfPackages`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs:29-37`

- [ ] **Step 1: Add the query parameter**

Replace the `ScanOrder` action with:

```csharp
    [HttpPost("orders/{orderCode}/scan")]
    [Authorize(Roles = AccessRoles.PackagingWrite)]
    public async Task<ActionResult<ScanPackingOrderResponse>> ScanOrder(
        [FromRoute] string orderCode,
        [FromQuery] int numberOfPackages = 1,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new ScanPackingOrderRequest { OrderCode = orderCode, NumberOfPackages = numberOfPackages },
            cancellationToken);
        return HandleResponse(response);
    }
```

- [ ] **Step 2: Build**

Run: `cd backend && dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/PackagingController.cs
git commit -m "feat(packaging): accept numberOfPackages query param on scan endpoint"
```

---

## Task 5: `CompletePackingOrder` use case

**Files:**
- Create: `…/Packaging/UseCases/CompletePackingOrder/CompletePackingOrderRequest.cs`
- Create: `…/CompletePackingOrder/CompletePackingOrderResponse.cs`
- Create: `…/CompletePackingOrder/CompletePackingOrderHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Application/Packaging/CompletePackingOrderHandlerTests.cs`

> Note: `CompletePackingOrderResponse` MUST inherit `BaseResponse` — a reflection contract test fails in CI otherwise. MediatR auto-registers the handler via assembly scan (`ApplicationModule.cs` `RegisterServicesFromAssembly`), so no DI wiring is needed.

- [ ] **Step 1: Write the failing tests**

Create `CompletePackingOrderHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Packaging.UseCases.CompletePackingOrder;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.Packaging;

public class CompletePackingOrderHandlerTests
{
    private readonly Mock<IEshopOrderClient> _eshopOrderClient = new();

    private CompletePackingOrderHandler CreateHandler() =>
        new(_eshopOrderClient.Object, new Mock<ILogger<CompletePackingOrderHandler>>().Object);

    [Fact]
    public async Task Handle_MarksOrderAsPacked_AndReturnsCompleted()
    {
        var response = await CreateHandler().Handle(
            new CompletePackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Completed.Should().BeTrue();
        _eshopOrderClient.Verify(
            c => c.MarkAsPackedAsync("0001234", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenMarkAsPackedThrows_ReturnsPackingCompletionFailed()
    {
        _eshopOrderClient
            .Setup(c => c.MarkAsPackedAsync("0001234", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet down"));

        var response = await CreateHandler().Handle(
            new CompletePackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PackingCompletionFailed);
    }
}
```

- [ ] **Step 2: Run — verify it fails to compile**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~CompletePackingOrderHandlerTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Create the request**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.CompletePackingOrder;

public class CompletePackingOrderRequest : IRequest<CompletePackingOrderResponse>
{
    public string OrderCode { get; set; } = null!;
}
```

- [ ] **Step 4: Create the response**

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.CompletePackingOrder;

public class CompletePackingOrderResponse : BaseResponse
{
    public bool Completed { get; set; }

    public CompletePackingOrderResponse(bool completed)
    {
        Completed = completed;
    }

    public CompletePackingOrderResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

- [ ] **Step 5: Create the handler**

```csharp
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.CompletePackingOrder;

public class CompletePackingOrderHandler
    : IRequestHandler<CompletePackingOrderRequest, CompletePackingOrderResponse>
{
    private readonly IEshopOrderClient _eshopOrderClient;
    private readonly ILogger<CompletePackingOrderHandler> _logger;

    public CompletePackingOrderHandler(
        IEshopOrderClient eshopOrderClient,
        ILogger<CompletePackingOrderHandler> logger)
    {
        _eshopOrderClient = eshopOrderClient;
        _logger = logger;
    }

    public async Task<CompletePackingOrderResponse> Handle(
        CompletePackingOrderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _eshopOrderClient.MarkAsPackedAsync(request.OrderCode, cancellationToken);
            return new CompletePackingOrderResponse(completed: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to mark order {OrderCode} as packed during packing completion",
                request.OrderCode);
            return new CompletePackingOrderResponse(ErrorCodes.PackingCompletionFailed);
        }
    }
}
```

> Verify the `IEshopOrderClient` namespace matches `ScanPackingOrderHandler.cs` (it imports `Anela.Heblo.Application.Features.ShoptetOrders`). If your tree differs, adjust the `using` accordingly.

- [ ] **Step 6: Run — verify pass**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~CompletePackingOrderHandlerTests"`
Expected: PASS.

- [ ] **Step 7: Format + commit**

```bash
cd backend && dotnet format
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/CompletePackingOrder backend/test/Anela.Heblo.Tests/Application/Packaging/CompletePackingOrderHandlerTests.cs
git commit -m "feat(packaging): add CompletePackingOrder use case to mark order packed"
```

---

## Task 6: Completion endpoint on the controller

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs` (using + new action)

- [ ] **Step 1: Add the using**

Add near the other `using Anela.Heblo.Application.Features.Packaging.UseCases.*;` lines:

```csharp
using Anela.Heblo.Application.Features.Packaging.UseCases.CompletePackingOrder;
```

- [ ] **Step 2: Add the endpoint**

Insert before the closing brace of the controller (e.g. after `DeletePackage`):

```csharp
    /// <summary>
    /// Marks the order as packed after all multi-package labels have been printed.
    /// </summary>
    [HttpPost("orders/{orderCode}/packing/complete")]
    [Authorize(Roles = AccessRoles.PackagingWrite)]
    public async Task<ActionResult<CompletePackingOrderResponse>> CompletePacking(
        [FromRoute] string orderCode,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new CompletePackingOrderRequest { OrderCode = orderCode },
            cancellationToken);
        return HandleResponse(response);
    }
```

- [ ] **Step 3: Build + run the whole packaging suite**

Run: `cd backend && dotnet build && dotnet test --filter "FullyQualifiedName~Packaging"`
Expected: Build succeeded; all packaging tests PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/PackagingController.cs
git commit -m "feat(packaging): add packing/complete endpoint"
```

---

## Task 7: Document the multi-package Shoptet usage

**Files:**
- Modify: `docs/integrations/shoptet-api.md`

> Project rule: Shoptet API findings must be documented before the live store is relied upon (no sandbox).

- [ ] **Step 1: Append a section**

Add to `docs/integrations/shoptet-api.md`:

```markdown
## Multi-package shipments (`POST /api/shipments`)

The packaging scan can request **N parcels** for one order. We send the `data.packages`
array with N entries (same width/height/depth; weight = order weight ÷ N, floored at
`MinPackageWeightGrams`). Shoptet returns one shipment GUID with N distinct package names;
`GET /api/shipments?orderCode={code}` then lists all N packages, each with its own label
URL / tracking number once ready.

**To verify on staging before relying on it:** create a multi-package shipment for a test
order, confirm Shoptet returns N distinct package names and N printable labels, and that the
order can still be marked packed (status 52) afterwards.
```

- [ ] **Step 2: Commit**

```bash
git add docs/integrations/shoptet-api.md
git commit -m "docs(shoptet): document multi-package shipment creation"
```

---

## Task 8: Scan hook takes an object + exposes `pendingCompletion`

**Files:**
- Modify: `frontend/src/api/hooks/useScanPackingOrder.ts`
- Modify: `frontend/src/components/baleni/__tests__/BaleniPacking.test.tsx:179,194`

> First confirm `BaleniPacking` is the only `mutate` caller:
> `grep -rn "useScanPackingOrder" frontend/src` — expect the hook file + `BaleniPacking.tsx` + its test only.

- [ ] **Step 1: Update the existing BaleniPacking mutate assertions (RED)**

In `BaleniPacking.test.tsx`, change the assertion on line 179 from:

```typescript
    expect(mutate).toHaveBeenLastCalledWith('250001');
```

to:

```typescript
    expect(mutate).toHaveBeenLastCalledWith({ orderCode: '250001', numberOfPackages: 1 });
```

(The "scanned twice" test on line ~194 only checks call count — leave it.)

- [ ] **Step 2: Run — verify it fails**

Run: `cd frontend && npx jest src/components/baleni/__tests__/BaleniPacking.test.tsx -t "scan input submits"`
Expected: FAIL (still called with the bare string).

- [ ] **Step 3: Update the hook**

In `useScanPackingOrder.ts`:

(a) Add `pendingCompletion` to the `ScanShipment` interface:

```typescript
export interface ScanShipment {
  shipmentGuid: string;
  packages: ScanShipmentPackage[];
  alreadyExisted: boolean;
  pendingCompletion?: boolean;
}
```

(b) Replace the `scanPackingOrder` function signature/URL and the mutation type:

```typescript
export type ScanPackingOrderVariables = {
  orderCode: string;
  numberOfPackages?: number;
};

const scanPackingOrder = async ({
  orderCode,
  numberOfPackages = 1,
}: ScanPackingOrderVariables): Promise<ScanPackingOrderResult> => {
  const apiClient = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
  const response = await apiClient.http.fetch(
    `${apiClient.baseUrl}/api/packaging/orders/${encodeURIComponent(orderCode)}/scan?numberOfPackages=${numberOfPackages}`,
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
  useMutation<ScanPackingOrderResult, Error, ScanPackingOrderVariables>({
    mutationFn: scanPackingOrder,
  });
```

(`pendingCompletion` flows through the `as ScanShipment` cast automatically — backend serialises it camelCase.)

- [ ] **Step 4: Run — verify pass**

Run: `cd frontend && npx jest src/components/baleni/__tests__/BaleniPacking.test.tsx`
Expected: PASS (after Task 11 the modal tests are added; for now the existing tests pass — `BaleniPacking.tsx` still calls `mutate({ orderCode, numberOfPackages: 1 })` once Task 11 lands. To keep this task self-contained and green, also do Step 5).

- [ ] **Step 5: Keep BaleniPacking green now**

In `BaleniPacking.tsx`, change `handleScan` so the existing test passes immediately:

```typescript
  const handleScan = (value: string, numberOfPackages = 1) => {
    scanMutation.mutate({ orderCode: value, numberOfPackages });
  };
```

(The top `ScanInput`'s `onScan={handleScan}` already passes just the string, defaulting count to 1.)

- [ ] **Step 6: Lint + commit**

```bash
cd frontend && npm run lint
git add frontend/src/api/hooks/useScanPackingOrder.ts frontend/src/components/baleni/BaleniPacking.tsx frontend/src/components/baleni/__tests__/BaleniPacking.test.tsx
git commit -m "feat(packaging): scan hook accepts numberOfPackages and exposes pendingCompletion"
```

---

## Task 9: `useCompletePackingOrder` hook

**Files:**
- Create: `frontend/src/api/hooks/useCompletePackingOrder.ts`
- Test: `frontend/src/api/hooks/__tests__/useCompletePackingOrder.test.ts`

- [ ] **Step 1: Write the failing test**

```typescript
import { completePackingOrder } from '../useCompletePackingOrder';
import { getAuthenticatedApiClient } from '../../client';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const mockFetch = jest.fn();

beforeEach(() => {
  jest.clearAllMocks();
  (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
    baseUrl: 'http://api',
    http: { fetch: mockFetch },
  });
});

describe('completePackingOrder', () => {
  it('POSTs to the packing/complete endpoint with the encoded order code', async () => {
    mockFetch.mockResolvedValue({ json: async () => ({ success: true }) });

    await completePackingOrder('25/0001');

    expect(mockFetch).toHaveBeenCalledWith(
      'http://api/api/packaging/orders/25%2F0001/packing/complete',
      { method: 'POST' }
    );
  });

  it('throws a friendly message when the server reports failure', async () => {
    mockFetch.mockResolvedValue({
      json: async () => ({ success: false, errorCode: 'PackingCompletionFailed' }),
    });

    await expect(completePackingOrder('250001')).rejects.toThrow(
      'Nepodařilo se dokončit balení objednávky.'
    );
  });
});
```

- [ ] **Step 2: Run — verify it fails**

Run: `cd frontend && npx jest src/api/hooks/__tests__/useCompletePackingOrder.test.ts`
Expected: FAIL — module does not exist.

- [ ] **Step 3: Create the hook**

```typescript
import { useMutation } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

const COMPLETE_ERROR_MESSAGES: Partial<Record<string, string>> = {
  PackingCompletionFailed: 'Nepodařilo se dokončit balení objednávky.',
};

const GENERIC_COMPLETE_ERROR = 'Chyba při dokončení balení.';

export const completePackingOrder = async (orderCode: string): Promise<void> => {
  const apiClient = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
  const response = await apiClient.http.fetch(
    `${apiClient.baseUrl}/api/packaging/orders/${encodeURIComponent(orderCode)}/packing/complete`,
    { method: 'POST' }
  );
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const data = (await response.json()) as any;

  if (!data.success) {
    const message = (data.errorCode && COMPLETE_ERROR_MESSAGES[data.errorCode as string]) ?? GENERIC_COMPLETE_ERROR;
    throw new Error(message);
  }
};

export const useCompletePackingOrder = () =>
  useMutation<void, Error, string>({
    mutationFn: completePackingOrder,
  });
```

- [ ] **Step 4: Run — verify pass**

Run: `cd frontend && npx jest src/api/hooks/__tests__/useCompletePackingOrder.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/hooks/useCompletePackingOrder.ts frontend/src/api/hooks/__tests__/useCompletePackingOrder.test.ts
git commit -m "feat(packaging): add useCompletePackingOrder hook"
```

---

## Task 10: `MultiPackageModal` component

**Files:**
- Create: `frontend/src/components/baleni/MultiPackageModal.tsx`
- Test: `frontend/src/components/baleni/__tests__/MultiPackageModal.test.tsx`

- [ ] **Step 1: Write the failing test**

```typescript
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import MultiPackageModal from '../MultiPackageModal';

const noop = () => {};

describe('MultiPackageModal', () => {
  it('defaults to 2 packages', () => {
    render(<MultiPackageModal onConfirm={noop} onClose={noop} />);
    expect(screen.getByTestId('multi-package-count')).toHaveTextContent('2');
  });

  it('increments up to 10 then disables the plus button', () => {
    render(<MultiPackageModal onConfirm={noop} onClose={noop} />);
    const plus = screen.getByTestId('multi-package-increment');
    for (let i = 0; i < 20; i++) fireEvent.click(plus);
    expect(screen.getByTestId('multi-package-count')).toHaveTextContent('10');
    expect(plus).toBeDisabled();
  });

  it('does not decrement below 2', () => {
    render(<MultiPackageModal onConfirm={noop} onClose={noop} />);
    const minus = screen.getByTestId('multi-package-decrement');
    fireEvent.click(minus);
    expect(screen.getByTestId('multi-package-count')).toHaveTextContent('2');
    expect(minus).toBeDisabled();
  });

  it('confirms with the scanned order code and current count', () => {
    const onConfirm = jest.fn();
    render(<MultiPackageModal onConfirm={onConfirm} onClose={noop} />);
    fireEvent.click(screen.getByTestId('multi-package-increment')); // 3
    const input = screen.getByRole('textbox') as HTMLInputElement;
    fireEvent.change(input, { target: { value: '250001' } });
    fireEvent.submit(input.closest('form')!);
    expect(onConfirm).toHaveBeenCalledWith('250001', 3);
  });

  it('calls onClose when the close button is clicked', () => {
    const onClose = jest.fn();
    render(<MultiPackageModal onConfirm={noop} onClose={onClose} />);
    fireEvent.click(screen.getByTestId('multi-package-close'));
    expect(onClose).toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run — verify it fails**

Run: `cd frontend && npx jest src/components/baleni/__tests__/MultiPackageModal.test.tsx`
Expected: FAIL — module does not exist.

- [ ] **Step 3: Create the component**

```tsx
import { useState } from 'react';
import { Minus, Plus, X } from 'lucide-react';
import ScanInput from '../terminal/ScanInput';

interface MultiPackageModalProps {
  onConfirm: (orderCode: string, packageCount: number) => void;
  onClose: () => void;
}

const MIN_PACKAGES = 2;
const MAX_PACKAGES = 10;
const DEFAULT_PACKAGES = 2;

function MultiPackageModal({ onConfirm, onClose }: MultiPackageModalProps) {
  const [count, setCount] = useState(DEFAULT_PACKAGES);

  return (
    <div
      data-testid="multi-package-modal"
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-6"
    >
      <div className="flex w-full max-w-sm flex-col gap-6 rounded-2xl bg-white p-8 shadow-2xl">
        <div className="flex items-center justify-between">
          <h2 className="text-2xl font-bold text-neutral-slate">Více balíků</h2>
          <button
            type="button"
            aria-label="Zavřít"
            data-testid="multi-package-close"
            onClick={onClose}
            className="rounded-lg p-2 text-neutral-gray hover:bg-neutral-100"
          >
            <X className="h-6 w-6" />
          </button>
        </div>

        <div className="flex items-center justify-center gap-6">
          <button
            type="button"
            aria-label="Méně balíků"
            data-testid="multi-package-decrement"
            disabled={count <= MIN_PACKAGES}
            onClick={() => setCount((c) => Math.max(MIN_PACKAGES, c - 1))}
            className="flex h-16 w-16 items-center justify-center rounded-2xl border-2 border-neutral-300 bg-white text-neutral-slate shadow active:scale-95 disabled:opacity-40"
          >
            <Minus className="h-8 w-8" />
          </button>
          <span
            data-testid="multi-package-count"
            className="w-16 text-center text-5xl font-bold text-neutral-slate"
          >
            {count}
          </span>
          <button
            type="button"
            aria-label="Více balíků"
            data-testid="multi-package-increment"
            disabled={count >= MAX_PACKAGES}
            onClick={() => setCount((c) => Math.min(MAX_PACKAGES, c + 1))}
            className="flex h-16 w-16 items-center justify-center rounded-2xl bg-primary-blue text-white shadow active:scale-95 disabled:opacity-40"
          >
            <Plus className="h-8 w-8" />
          </button>
        </div>

        <ScanInput
          label="Potvrďte naskenováním objednávky"
          placeholder="Naskenujte objednávku…"
          onScan={(orderCode) => onConfirm(orderCode, count)}
          autoFocusOnMount
          refocusOnBlur
        />
      </div>
    </div>
  );
}

export default MultiPackageModal;
```

- [ ] **Step 4: Run — verify pass**

Run: `cd frontend && npx jest src/components/baleni/__tests__/MultiPackageModal.test.tsx`
Expected: PASS.

- [ ] **Step 5: Lint + commit**

```bash
cd frontend && npm run lint
git add frontend/src/components/baleni/MultiPackageModal.tsx frontend/src/components/baleni/__tests__/MultiPackageModal.test.tsx
git commit -m "feat(packaging): add MultiPackageModal with +/- selector and scan confirm"
```

---

## Task 11: Wire the multi-package button + modal into `BaleniPacking`

**Files:**
- Modify: `frontend/src/components/baleni/BaleniPacking.tsx`
- Modify: `frontend/src/components/baleni/__tests__/BaleniPacking.test.tsx`

- [ ] **Step 1: Write the failing test**

Append to `BaleniPacking.test.tsx` (inside `describe('BaleniPacking', …)`). Note: this file's mock of `PackingShipmentCreator` already prevents deep rendering. Add a mock for `MultiPackageModal` at the top with the other `jest.mock` calls:

```typescript
jest.mock('../MultiPackageModal', () => ({
  __esModule: true,
  default: ({ onConfirm }: { onConfirm: (code: string, count: number) => void }) => (
    <button data-testid="mock-modal-confirm" onClick={() => onConfirm('250001', 3)}>
      confirm
    </button>
  ),
}));
```

Then the test:

```typescript
  it('opens the multi-package modal and scans with the chosen count', () => {
    const mutate = jest.fn();
    mockHook.mockReturnValue({ ...idleMutation, mutate });

    render(<BaleniPacking />);
    expect(screen.queryByTestId('mock-modal-confirm')).not.toBeInTheDocument();

    fireEvent.click(screen.getByTestId('multi-package-button'));
    fireEvent.click(screen.getByTestId('mock-modal-confirm'));

    expect(mutate).toHaveBeenLastCalledWith({ orderCode: '250001', numberOfPackages: 3 });
  });
```

- [ ] **Step 2: Run — verify it fails**

Run: `cd frontend && npx jest src/components/baleni/__tests__/BaleniPacking.test.tsx -t "multi-package modal"`
Expected: FAIL — no `multi-package-button`.

- [ ] **Step 3: Implement the wiring**

In `BaleniPacking.tsx`:

(a) Update imports:

```typescript
import { useState, type ReactNode } from 'react';
import { ScanLine, Loader2, PackagePlus } from 'lucide-react';
import MultiPackageModal from './MultiPackageModal';
```

(b) Inside `BaleniPacking`, add modal state (next to `isShowingDoneView`):

```typescript
  const [isMultiModalOpen, setIsMultiModalOpen] = useState(false);
```

(c) `handleScan` already accepts `(value, numberOfPackages = 1)` from Task 8. Add a confirm handler:

```typescript
  const handleMultiConfirm = (orderCode: string, numberOfPackages: number) => {
    setIsMultiModalOpen(false);
    handleScan(orderCode, numberOfPackages);
  };
```

(d) Replace the top scan row (currently lines 117-129) with a button + the scan input, and suppress the top input's blur-refocus while the modal is open:

```tsx
      <div className="flex items-end justify-end gap-2">
        <button
          type="button"
          data-testid="multi-package-button"
          aria-label="Více balíků"
          onClick={() => setIsMultiModalOpen(true)}
          className="flex h-14 items-center gap-2 rounded-xl border-2 border-neutral-300 bg-white px-4 text-base font-semibold text-neutral-slate shadow active:scale-95"
        >
          <PackagePlus className="h-5 w-5" />
          Více balíků
        </button>
        <div className="w-72 shrink-0">
          <ScanInput
            label="Sken čísla objednávky"
            placeholder="Naskenujte objednávku…"
            onScan={handleScan}
            loading={scanMutation.isPending}
            autoFocusOnMount
            refocusOnBlur={!isMultiModalOpen}
            allowKeyboardToggle
          />
        </div>
      </div>
      {isMultiModalOpen && (
        <MultiPackageModal
          onConfirm={handleMultiConfirm}
          onClose={() => setIsMultiModalOpen(false)}
        />
      )}
```

- [ ] **Step 4: Run the full BaleniPacking suite — verify pass**

Run: `cd frontend && npx jest src/components/baleni/__tests__/BaleniPacking.test.tsx`
Expected: PASS.

- [ ] **Step 5: Lint + commit**

```bash
cd frontend && npm run lint
git add frontend/src/components/baleni/BaleniPacking.tsx frontend/src/components/baleni/__tests__/BaleniPacking.test.tsx
git commit -m "feat(packaging): add multi-package button and modal to packing screen"
```

---

## Task 12: Fire completion after the last label

**Files:**
- Modify: `frontend/src/components/baleni/PackingLabelPrinter.tsx`
- Modify: `frontend/src/components/baleni/__tests__/PackingLabelPrinter.test.tsx`

- [ ] **Step 1: Add the completion-hook mock + failing test**

In `PackingLabelPrinter.test.tsx`, add this mock next to the existing `jest.mock` calls (top of file):

```typescript
const mockComplete = jest.fn();
jest.mock('../../../api/hooks/useCompletePackingOrder', () => ({
  useCompletePackingOrder: () => ({ mutate: mockComplete }),
}));
```

Add `mockComplete.mockClear();` inside the existing `beforeEach`. Then add two tests:

```typescript
  it('fires completion once when done and shipment is pendingCompletion', () => {
    const shipment = { ...makeShipment([pkg1, pkg2]), pendingCompletion: true };
    render(<PackingLabelPrinter order={makeOrder('250001')} shipment={shipment} />);

    fireAck(0); // first label acknowledged
    fireEvent.click(screen.getByTestId('print-next-label-button'));
    fireAck(1); // last label acknowledged → done

    expect(mockComplete).toHaveBeenCalledTimes(1);
    expect(mockComplete).toHaveBeenCalledWith('250001');
  });

  it('does NOT fire completion for a single-package (pendingCompletion absent) shipment', () => {
    render(<PackingLabelPrinter order={makeOrder('250001')} shipment={makeShipment([pkg1])} />);
    fireAck(0);
    expect(screen.getByTestId('done-view')).toBeInTheDocument();
    expect(mockComplete).not.toHaveBeenCalled();
  });
```

- [ ] **Step 2: Run — verify the new tests fail**

Run: `cd frontend && npx jest src/components/baleni/__tests__/PackingLabelPrinter.test.tsx -t "completion"`
Expected: FAIL — `mockComplete` never called.

- [ ] **Step 3: Implement**

In `PackingLabelPrinter.tsx`:

(a) Update imports:

```typescript
import { useEffect, useMemo, useRef, useState } from 'react';
import { useCompletePackingOrder } from '../../api/hooks/useCompletePackingOrder';
```

(b) Inside the component, after the existing `useState` lines, add:

```typescript
  const completeMutation = useCompletePackingOrder();
  const completedRef = useRef(false);
```

(c) In the existing reset effect keyed on `order.code`, also reset the guard:

```typescript
  useEffect(() => {
    setPrintedCount(0);
    setAcknowledgedCount(0);
    completedRef.current = false;
  }, [order.code]);
```

(d) Add a completion effect after the `onDoneStateChange` effect:

```typescript
  useEffect(() => {
    if (isDone && shipment.pendingCompletion && !completedRef.current) {
      completedRef.current = true;
      completeMutation.mutate(order.code);
    }
    // completeMutation identity is not stable across renders; the ref guards double-fire.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isDone, shipment.pendingCompletion, order.code]);
```

- [ ] **Step 4: Run the full printer suite — verify pass**

Run: `cd frontend && npx jest src/components/baleni/__tests__/PackingLabelPrinter.test.tsx`
Expected: PASS (all old + 2 new).

- [ ] **Step 5: Lint + commit**

```bash
cd frontend && npm run lint
git add frontend/src/components/baleni/PackingLabelPrinter.tsx frontend/src/components/baleni/__tests__/PackingLabelPrinter.test.tsx
git commit -m "feat(packaging): mark order packed after the last multi-package label prints"
```

---

## Task 13: Full validation

- [ ] **Step 1: Backend build + format + tests**

Run: `cd backend && dotnet build && dotnet format --verify-no-changes && dotnet test`
Expected: Build succeeded; no format changes; all tests PASS.

- [ ] **Step 2: Frontend build + lint + tests**

Run: `cd frontend && npm run build && npm run lint && npx jest src/components/baleni src/api/hooks/__tests__/useCompletePackingOrder.test.ts`
Expected: Build succeeds; lint clean; tests PASS.

- [ ] **Step 3: Manual / E2E on staging** (`./scripts/run-playwright-tests.sh`)

Verify end-to-end:
1. **Single (unchanged):** scan an eligible order via the top box → one label prints → order becomes `Zabaleno`.
2. **Multi:** click **Více balíků** → modal opens (count 2) → set to 3 with `+` → scan the order → first label auto-prints, order detail + "Vytisknout štítek 2/3" shown → click through labels 2 and 3 → after the **last** label, the order flips to `Zabaleno` (check Shoptet status 52) and 3 `Package` rows persist with evenly-split weights.
3. **Reset:** the next scan via the top box defaults to a single package again.

---

## Self-review notes

- **Spec coverage:** button next to input (Task 11) ✓; modal with big +/- default 2 (Task 10) ✓; confirm by scanning order (Task 10 ScanInput + Task 11 wiring) ✓; classic one-by-one label workflow (existing `PackingLabelPrinter`, unchanged) ✓; state change upon last label (Tasks 3, 5, 6, 12) ✓; next order reverts to single (modal state not sticky; top box defaults to 1 — Tasks 8/11) ✓.
- **Type consistency:** `PackageCount` (command), `NumberOfPackages` (request/query), `PendingCompletion`/`pendingCompletion` (response/TS), `onConfirm(orderCode, packageCount)` are used identically across all tasks.
- **Edge cases:** out-of-range count → `InvalidPackageCount` (Task 3); per-package weight floored at minimum (Task 3); completion fires exactly once via `completedRef` (Task 12); existing-shipment / ineligible branches keep `pendingCompletion=false` and their current behaviour.
