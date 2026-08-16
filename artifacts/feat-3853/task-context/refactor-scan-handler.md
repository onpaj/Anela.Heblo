### task: refactor-scan-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs`
- Delete: `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderPackerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Packaging/ScanPackingOrderHandlerPackagePersistenceTests.cs`

**Interfaces:**
- Consumes (produced by task `extract-shipment-creation-service`, already merged):
  - `Anela.Heblo.Application.Features.Packaging.Services.IShipmentCreationService.CreateAndPersistAsync(PackingOrder order, int numberOfPackages, Guid? packingUserId, CancellationToken ct)` returning `Task<ShipmentCreationResult>`.
  - `ShipmentCreationResult { bool IsSuccess; ErrorCodes? ErrorCode; Guid ShipmentGuid; string CarrierCode; string? CarrierName; IReadOnlyList<ShipmentLabel> Labels; }`.
  - DI: `IShipmentCreationService` is registered in `PackagingModule`.
- Produces: `ScanPackingOrderHandler`'s constructor now takes 8 parameters (see Step 1); no
  external caller other than MediatR and the test files in this task construct it directly.

- [ ] **Step 1: Refactor `ScanPackingOrderHandler.cs`**

Read `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`,
then replace its entire contents with:

```csharp
using Anela.Heblo.Application.Features.Packaging.Services;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;

public class ScanPackingOrderHandler : IRequestHandler<ScanPackingOrderRequest, ScanPackingOrderResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly IPackingOrderClient _orderClient;
    private readonly IEshopOrderClient _eshopOrderClient;
    private readonly ILogger<ScanPackingOrderHandler> _logger;
    private readonly IPackageRepository _packageRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuthorizationRepository _authRepo;
    private readonly IShipmentCreationService _shipmentCreationService;

    public ScanPackingOrderHandler(
        IShipmentClient shipmentClient,
        IPackingOrderClient orderClient,
        IEshopOrderClient eshopOrderClient,
        ILogger<ScanPackingOrderHandler> logger,
        IPackageRepository packageRepository,
        ICurrentUserService currentUserService,
        IAuthorizationRepository authRepo,
        IShipmentCreationService shipmentCreationService)
    {
        _shipmentClient = shipmentClient;
        _orderClient = orderClient;
        _eshopOrderClient = eshopOrderClient;
        _logger = logger;
        _packageRepository = packageRepository;
        _currentUserService = currentUserService;
        _authRepo = authRepo;
        _shipmentCreationService = shipmentCreationService;
    }

    public async Task<ScanPackingOrderResponse> Handle(ScanPackingOrderRequest request, CancellationToken ct)
    {
        const int maxPackages = 10;
        if (request.NumberOfPackages < 1 || request.NumberOfPackages > maxPackages)
            return new ScanPackingOrderResponse(ErrorCodes.InvalidPackageCount);

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
            Items = order.Items
                .Select(i => new ScanPackingOrderItemDto
                {
                    Name = i.Name,
                    Quantity = i.Quantity,
                    ImageUrl = i.ImageUrl,
                    SetName = i.SetName,
                })
                .ToList(),
            Eligibility = new ScanOrderEligibility
            {
                IsEligible = isEligible,
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
            await BackfillExistingShipmentPackagesAsync(
                request.OrderCode, orderData.CustomerName, existingLabels, request.PackingUserId, ct);
            await TryMarkAsPackedAsync(request.OrderCode, ct);
            return new ScanPackingOrderResponse(orderData, existingShipment);
        }

        var result = await _shipmentCreationService.CreateAndPersistAsync(
            order, request.NumberOfPackages, request.PackingUserId, ct);
        if (!result.IsSuccess)
            return new ScanPackingOrderResponse(result.ErrorCode!.Value);

        var packages = result.Labels
            .Select(label => new ScanShipmentPackage
            {
                TrackingNumber = label.TrackingNumber,
                LabelUrl = label.LabelUrl,
                LabelZpl = label.LabelZpl,
            })
            .ToList();

        // The Shoptet "Zabaleno" (52) transition is deferred to the FE, which calls
        // .../packing/complete only after every carrier label is confirmed fetched & printed.
        // A successful CreateAndPersistAsync means Shoptet accepted the request, NOT that a
        // usable label was produced (labels generate asynchronously and can fail). Marking
        // here would move the order to "Zabaleno" even when no label exists. Single- and
        // multi-package orders share this deferred path.
        return new ScanPackingOrderResponse(orderData, new ScanShipmentData
        {
            ShipmentGuid = result.ShipmentGuid,
            Packages = packages,
            AlreadyExisted = false,
            PendingCompletion = true,
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

    private async Task<(Guid? userId, string? name)> ResolvePackerAsync(Guid? packingUserId, CancellationToken ct)
    {
        if (packingUserId is { } id)
        {
            var user = await _authRepo.GetUserByIdAsync(id, ct);
            if (user is not null)
                return (user.Id, user.DisplayName);
        }
        return (null, _currentUserService.GetCurrentUser().Email);
    }

    /// <summary>
    /// Backfills Package rows for an order whose Shoptet shipment already exists (reprint path).
    /// Idempotent and best-effort: never throws, so a reprint always returns the existing shipment.
    /// </summary>
    private async Task BackfillExistingShipmentPackagesAsync(
        string orderCode,
        string customerName,
        IReadOnlyList<ShipmentLabel> existingLabels,
        Guid? packingUserId,
        CancellationToken cancellationToken)
    {
        if (existingLabels.Count == 0)
            return;

        try
        {
            var options = await _shipmentClient.GetShippingOptionsAsync(orderCode, cancellationToken);
            var carrierCode = options.Count > 0 ? options[0].CarrierCode : string.Empty;
            var carrierName = options.Count > 0 ? options[0].Name : null;

            var now = DateTimeOffset.UtcNow;
            var (packedByUserId, packedBy) = await ResolvePackerAsync(packingUserId, cancellationToken);

            var packages = existingLabels
                .Select(label => new Package
                {
                    OrderCode = orderCode,
                    CustomerName = customerName,
                    PackageNumber = label.PackageName,
                    TrackingNumber = label.TrackingNumber,
                    ShippingProviderCode = carrierCode,
                    ShippingProviderName = carrierName,
                    ShipmentGuid = label.ShipmentGuid,
                    PackedAt = now,
                    PackedBy = packedBy,
                    PackedByUserId = packedByUserId,
                    CreatedAt = now,
                })
                .ToList();

            await _packageRepository.AddMissingAsync(packages, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to backfill Package rows for existing shipment of order {OrderCode}", orderCode);
        }
    }
}
```

Note what changed versus the pre-refactor file: the `IOptions<ShipmentLabelsSettings>` constructor
parameter and `System.Globalization` using are gone (both were only needed by the now-removed
inline weight/carrier/creation/label/persistence block); `IShipmentCreationService` is added; the
"no existing shipment, eligible order" branch (formerly ~80 lines) is now the
`_shipmentCreationService.CreateAndPersistAsync(...)` call plus response mapping; the
`PersistPackagesAsync` private method is deleted entirely (fully absorbed into the service).
`BuildShippingAddress`, `TryMarkAsPackedAsync`, `ResolvePackerAsync`, and
`BackfillExistingShipmentPackagesAsync` are unchanged (the backfill/reprint path stays out of
scope per the spec).

- [ ] **Step 2: Build to catch compile errors early**

```bash
dotnet build
```

Expected: fails at this point only in the test projects (their old constructor calls no longer
match) — the `Anela.Heblo.Application` project itself must build clean. If
`Anela.Heblo.Application` itself fails to build, stop and fix `ScanPackingOrderHandler.cs` before
continuing.

- [ ] **Step 3: Rewrite `ScanPackingOrderHandlerTests.cs`**

Read `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs`, then
replace its entire contents with:

```csharp
using Anela.Heblo.Application.Features.Packaging.Services;
using Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.Packaging;

public class ScanPackingOrderHandlerTests
{
    private readonly Mock<IShipmentClient> _shipmentClient = new();
    private readonly Mock<IPackingOrderClient> _orderClient = new();
    private readonly Mock<IEshopOrderClient> _eshopOrderClient = new();
    private readonly Mock<IPackageRepository> _packageRepository = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly Mock<IAuthorizationRepository> _authRepo = new();
    private readonly Mock<IShipmentCreationService> _shipmentCreationService = new();

    private ScanPackingOrderHandler CreateHandler()
    {
        _currentUserService.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("uid-1", "Operator", "op@example.com", IsAuthenticated: true));
        return new(
            _shipmentClient.Object,
            _orderClient.Object,
            _eshopOrderClient.Object,
            new Mock<ILogger<ScanPackingOrderHandler>>().Object,
            _packageRepository.Object,
            _currentUserService.Object,
            _authRepo.Object,
            _shipmentCreationService.Object);
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

    private static ShipmentCreationResult SuccessResult(Guid shipmentGuid, params ShipmentLabel[] labels) =>
        new()
        {
            IsSuccess = true,
            ShipmentGuid = shipmentGuid,
            CarrierCode = "PPL",
            CarrierName = "PPL",
            Labels = labels,
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
        response.Shipment.Should().BeNull();

        _shipmentCreationService.Verify(
            s => s.CreateAndPersistAsync(It.IsAny<PackingOrder>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
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

        _shipmentCreationService.Verify(
            s => s.CreateAndPersistAsync(It.IsAny<PackingOrder>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
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
            TrackingNumber = "TRK-P1",
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
        response.Shipment.Packages[0].TrackingNumber.Should().Be("TRK-P1");
        response.Shipment.Packages[0].LabelUrl.Should().Be("https://example.com/label.pdf");
        response.Shipment.Packages[0].LabelZpl.Should().Be("^XA...^XZ");

        _shipmentCreationService.Verify(
            s => s.CreateAndPersistAsync(It.IsAny<PackingOrder>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Test 4: Eligible order, no existing shipment → delegates to IShipmentCreationService and maps its result
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
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult(shipmentGuid,
                new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://carrier.example.com/new-label.pdf" }));

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Order!.Eligibility.IsEligible.Should().BeTrue();
        response.Shipment.Should().NotBeNull();
        response.Shipment!.AlreadyExisted.Should().BeFalse();
        response.Shipment.ShipmentGuid.Should().Be(shipmentGuid);

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

    // New single-package shipment: the "Zabaleno" transition is DEFERRED to the FE
    // (PendingCompletion = true) and NOT marked at scan time — the carrier label is
    // generated asynchronously and may not exist yet.
    [Fact]
    public async Task Handle_NewSinglePackageShipment_DefersMarkAsPacked()
    {
        var shipmentGuid = Guid.NewGuid();
        var order = EligibleOrder(("P001", 1, 400));

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult(shipmentGuid,
                new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://carrier.example.com/new-label.pdf" }));

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment!.PendingCompletion.Should().BeTrue();
        _eshopOrderClient.Verify(
            c => c.MarkAsPackedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
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

    [Fact]
    public void ScanPackingOrderItemDto_HasExactlyTheFourPublicFields_AndNoWeightGrams()
    {
        var properties = typeof(ScanPackingOrderItemDto)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        properties.Should().BeEquivalentTo(new[] { "Name", "Quantity", "ImageUrl", "SetName" },
            "ScanPackingOrderItemDto must not expose internal fields such as WeightGrams to API clients.");
        typeof(ScanPackingOrderItemDto).GetProperty("WeightGrams").Should().BeNull();
    }

    [Fact]
    public void InternalPackingOrderItem_StillExposesWeightGrams_ForShipmentMath()
    {
        // Anchor the symmetric guarantee: WeightGrams must remain on the internal adapter
        // contract because ShipmentCreationService depends on it.
        typeof(PackingOrderItem).GetProperty("WeightGrams").Should().NotBeNull(
            "PackingOrderItem is the internal Application contract and ShipmentCreationService reads WeightGrams.");
    }

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

    // Multi-package: NumberOfPackages and PackingUserId are forwarded to the shared service, and
    // the response reflects however many labels the service returns; MarkAsPacked stays deferred.
    [Fact]
    public async Task Handle_MultiPackage_PassesNumberOfPackagesAndPackerToService_AndDefersMarkAsPacked()
    {
        var shipmentGuid = Guid.NewGuid();
        var packerId = Guid.NewGuid();
        var order = EligibleOrder(("P001", 1, 900));

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 3, packerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult(shipmentGuid,
                new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://c/1.pdf" },
                new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P2", LabelUrl = "https://c/2.pdf" },
                new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P3", LabelUrl = "https://c/3.pdf" }));

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234", NumberOfPackages = 3, PackingUserId = packerId },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment!.PendingCompletion.Should().BeTrue();
        response.Shipment.Packages.Should().HaveCount(3);
        response.Shipment.Packages[0].LabelUrl.Should().Be("https://c/1.pdf");

        _shipmentCreationService.Verify(
            s => s.CreateAndPersistAsync(order, 3, packerId, It.IsAny<CancellationToken>()),
            Times.Once);
        _eshopOrderClient.Verify(
            c => c.MarkAsPackedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Error mapping: whatever error code the shared service returns is surfaced unchanged.
    [Theory]
    [InlineData(ErrorCodes.ShipmentCarrierNotResolved)]
    [InlineData(ErrorCodes.ShipmentCreationFailed)]
    [InlineData(ErrorCodes.PackingUserNotEligible)]
    public async Task Handle_WhenShipmentCreationServiceFails_ReturnsMappedErrorCode(ErrorCodes errorCode)
    {
        var order = EligibleOrder(("P001", 1, 400));

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShipmentCreationResult { IsSuccess = false, ErrorCode = errorCode });

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(errorCode);
    }
}
```

Coverage note: `Handle_AllItemsHaveZeroWeight_UsesFallbackPackageWeight`,
`Handle_NoShippingOptions_ReturnsShipmentCarrierNotResolved`,
`Handle_CreateShipmentThrows_ReturnsShipmentCreationFailed`,
`Handle_MultiPackage_ShoptetReturnsFewerLabelsThanRequested_ResponseHasNPackages`, and
`Handle_MultiPackage_FloorsPerPackageWeightAtMinimum` from the pre-refactor file are intentionally
removed here — that logic now lives in `ShipmentCreationService`, and each has a direct
counterpart already added in `ShipmentCreationServiceTests.cs` (task
`extract-shipment-creation-service`, Step 6).

- [ ] **Step 4: Delete `ScanPackingOrderPackerTests.cs`**

Its three concerns — packer resolution with an explicit `packingUserId` (valid, unknown,
ineligible-inactive, ineligible-cannot-pack) and the null-`packingUserId` fallback to the current
user's email — are now `ShipmentCreationService` behavior, already covered by
`CreateAndPersistAsync_WithPackingUserId_StampsUserIdAndDisplayName`,
`CreateAndPersistAsync_WithNullPackingUserId_FallsBackToCurrentUserEmail`,
`CreateAndPersistAsync_WithUnknownPackingUserId_ReturnsPackingUserNotEligible`, and
`CreateAndPersistAsync_WithIneligiblePackingUser_ReturnsPackingUserNotEligible` in
`ShipmentCreationServiceTests.cs`.

```bash
rm backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderPackerTests.cs
```

- [ ] **Step 5: Trim `ScanPackingOrderHandlerPackagePersistenceTests.cs` to backfill-only coverage**

`BackfillExistingShipmentPackagesAsync` (the reprint path, using `IPackageRepository.AddMissingAsync`)
stays in `ScanPackingOrderHandler` — out of scope for this feature per the spec — so its three
tests stay in this file. The two tests that exercised the (now-removed) inline create-path
persistence (`Handle_PersistsOnePackageRowPerCreatedLabel_WithSequentialPackageNumbers`,
`Handle_DoesNotFailScan_WhenPersistenceThrows`) are dropped — their coverage now lives in
`ShipmentCreationServiceTests.cs`
(`CreateAndPersistAsync_LabelsShareSameCarrierPackageName_StillProducesSequentialPackageNumbers`,
`CreateAndPersistAsync_PersistenceThrows_StillReturnsSuccessfulResult`).

Read `backend/test/Anela.Heblo.Tests/Features/Packaging/ScanPackingOrderHandlerPackagePersistenceTests.cs`,
then replace its entire contents with:

```csharp
using Anela.Heblo.Application.Features.Packaging.Services;
using Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Packaging;

public class ScanPackingOrderHandlerPackagePersistenceTests
{
    private static ScanPackingOrderHandler MakeSut(
        out Mock<IPackageRepository> packageRepo,
        Mock<IShipmentClient>? shipmentClient = null,
        Mock<IPackingOrderClient>? orderClient = null,
        PackingOrder? order = null,
        IReadOnlyList<ShipmentLabel>? existingLabels = null)
    {
        packageRepo = new Mock<IPackageRepository>();
        shipmentClient ??= new Mock<IShipmentClient>();
        orderClient ??= new Mock<IPackingOrderClient>();
        var eshopClient = new Mock<IEshopOrderClient>();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("uid-1", "Operator", "op@example.com", IsAuthenticated: true));

        orderClient.Setup(c => c.GetPackingOrderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLabels ?? Array.Empty<ShipmentLabel>());

        var authRepo = new Mock<IAuthorizationRepository>();
        var shipmentCreationService = new Mock<IShipmentCreationService>();
        return new ScanPackingOrderHandler(
            shipmentClient.Object,
            orderClient.Object,
            eshopClient.Object,
            NullLogger<ScanPackingOrderHandler>.Instance,
            packageRepo.Object,
            currentUser.Object,
            authRepo.Object,
            shipmentCreationService.Object);
    }

    private static PackingOrder MakeOrder(int statusId = 26, bool isEligible = true) => new()
    {
        Code = "ORD-1",
        CustomerName = "Alice",
        ShippingMethodName = "PPL",
        StatusId = statusId,
        IsEligibleForPacking = isEligible,
        Items = new List<PackingOrderItem>
        {
            new() { WeightGrams = 500, Quantity = 1 },
        },
    };

    [Fact]
    public async Task Handle_BackfillsPackages_WhenEligibleShipmentAlreadyExisted()
    {
        // Arrange — eligible order re-scanned (reprint); shipment already exists in Shoptet
        var shipmentGuid = Guid.NewGuid();
        var existingLabels = new List<ShipmentLabel>
        {
            new() { PackageName = "PKG-1", ShipmentGuid = shipmentGuid, TrackingNumber = "TRK1" },
        };
        var sut = MakeSut(out var repo, order: MakeOrder(), existingLabels: existingLabels);

        // Act
        var response = await sut.Handle(new ScanPackingOrderRequest { OrderCode = "ORD-1" }, CancellationToken.None);

        // Assert — reprint returns the existing shipment AND backfills the missing row idempotently
        response.Success.Should().BeTrue();
        response.Shipment!.AlreadyExisted.Should().BeTrue();
        repo.Verify(r => r.AddMissingAsync(
            It.Is<IReadOnlyList<Package>>(list =>
                list.Count == 1 &&
                list[0].OrderCode == "ORD-1" &&
                list[0].PackageNumber == "PKG-1" &&
                list[0].TrackingNumber == "TRK1" &&
                list[0].ShipmentGuid == shipmentGuid &&
                list[0].ShippingProviderCode == "PPL"),
            It.IsAny<CancellationToken>()),
            Times.Once);
        repo.Verify(r => r.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DoesNotBackfill_WhenOrderNotEligible()
    {
        // Arrange — already-packed order rescanned for review (review path is intentionally untouched)
        var existingLabels = new List<ShipmentLabel>
        {
            new() { PackageName = "PKG-1", ShipmentGuid = Guid.NewGuid(), TrackingNumber = "TRK1" },
        };
        var sut = MakeSut(out var repo, order: MakeOrder(isEligible: false), existingLabels: existingLabels);

        // Act
        var response = await sut.Handle(new ScanPackingOrderRequest { OrderCode = "ORD-1" }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Shipment!.AlreadyExisted.Should().BeTrue();
        repo.Verify(r => r.ReplacePackagesForOrderAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Package>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        repo.Verify(r => r.AddMissingAsync(It.IsAny<IReadOnlyList<Package>>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DoesNotFailScan_WhenBackfillThrows()
    {
        // Arrange
        var existingLabels = new List<ShipmentLabel>
        {
            new() { PackageName = "PKG-1", ShipmentGuid = Guid.NewGuid(), TrackingNumber = "TRK1" },
        };
        var sut = MakeSut(out var repo, order: MakeOrder(), existingLabels: existingLabels);
        repo.Setup(r => r.AddMissingAsync(It.IsAny<IReadOnlyList<Package>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        // Act
        var response = await sut.Handle(new ScanPackingOrderRequest { OrderCode = "ORD-1" }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
    }
}
```

- [ ] **Step 6: Run Scan-related tests**

```bash
dotnet test --filter "FullyQualifiedName~ScanPackingOrder" --logger "console;verbosity=normal"
```

Expected: `Failed: 0` (covers `ScanPackingOrderHandlerTests` and
`ScanPackingOrderHandlerPackagePersistenceTests`; `ScanPackingOrderPackerTests` no longer exists).

- [ ] **Step 7: Full build + full test run**

```bash
dotnet build
dotnet test
```

Expected: build succeeds; `Failed: 0` across the whole suite (Reset's handler/tests are untouched
until the next task, so they remain green as before).

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs \
        backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Packaging/ScanPackingOrderHandlerPackagePersistenceTests.cs
git rm backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderPackerTests.cs
git commit -m "$(cat <<'EOF'
Refactor ScanPackingOrderHandler to delegate shipment creation to IShipmentCreationService

The "no existing shipment, eligible order" branch now calls the shared
IShipmentCreationService.CreateAndPersistAsync instead of its own inline weight/carrier/
creation/label/persistence logic. Eligibility check, existing-shipment reprint/backfill path,
and deferred TryMarkAsPackedAsync semantics are unchanged. Test coverage for the extracted
branches (zero-weight fallback, carrier resolution, label padding, packer resolution) moved to
ShipmentCreationServiceTests.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

