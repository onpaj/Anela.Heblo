### task: refactor-reset-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Application/Packaging/ResetOrderShipmentHandlerTests.cs`

**Interfaces:**
- Consumes (produced by task `extract-shipment-creation-service`, already merged):
  - `Anela.Heblo.Application.Features.Packaging.Services.IShipmentCreationService.CreateAndPersistAsync(PackingOrder order, int numberOfPackages, Guid? packingUserId, CancellationToken ct)` returning `Task<ShipmentCreationResult>`.
  - `ShipmentCreationResult { bool IsSuccess; ErrorCodes? ErrorCode; Guid ShipmentGuid; string CarrierCode; string? CarrierName; IReadOnlyList<ShipmentLabel> Labels; }`.
  - `Anela.Heblo.Application.Features.Packaging.Services.ShipmentCreationService` concrete class
    constructor: `ShipmentCreationService(IShipmentClient, IPackageRepository, IAuthorizationRepository, ICurrentUserService, IOptions<ShipmentLabelsSettings>, ILogger<ShipmentCreationService>)`
    (used directly, not mocked, by the FR-3 regression test in Step 3 below).
- Produces: `ResetOrderShipmentHandler`'s constructor now takes 4 parameters (see Step 1); this is
  the last consumer to change, so after this task the feature is complete.

- [ ] **Step 1: Refactor `ResetOrderShipmentHandler.cs`**

Read `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentHandler.cs`,
then replace its entire contents with:

```csharp
using Anela.Heblo.Application.Features.Packaging.Services;
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
    private readonly IShipmentCreationService _shipmentCreationService;
    private readonly ILogger<ResetOrderShipmentHandler> _logger;

    public ResetOrderShipmentHandler(
        IShipmentClient shipmentClient,
        IPackingOrderClient orderClient,
        IShipmentCreationService shipmentCreationService,
        ILogger<ResetOrderShipmentHandler> logger)
    {
        _shipmentClient = shipmentClient;
        _orderClient = orderClient;
        _shipmentCreationService = shipmentCreationService;
        _logger = logger;
    }

    public async Task<ResetOrderShipmentResponse> Handle(ResetOrderShipmentRequest request, CancellationToken ct)
    {
        const int maxPackages = 10;
        if (request.NumberOfPackages < 1 || request.NumberOfPackages > maxPackages)
            return new ResetOrderShipmentResponse(ErrorCodes.InvalidPackageCount);

        var existingLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        if (existingLabels.Count == 0)
            return new ResetOrderShipmentResponse(ErrorCodes.NoShipmentToReset);

        var shipmentGuids = existingLabels
            .Select(l => l.ShipmentGuid)
            .Distinct()
            .ToList();

        foreach (var shipmentGuid in shipmentGuids)
        {
            try
            {
                await _shipmentClient.CancelShipmentAsync(shipmentGuid, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel shipment {ShipmentGuid} for order {OrderCode}",
                    shipmentGuid, request.OrderCode);
                return new ResetOrderShipmentResponse(ErrorCodes.ShipmentCancelFailed);
            }
        }

        var order = await _orderClient.GetPackingOrderAsync(request.OrderCode, ct);
        if (order is null)
            return new ResetOrderShipmentResponse(ErrorCodes.ShoptetOrderNotFound);

        // Reset never supplies an explicit packer today (ResetOrderShipmentRequest has no
        // PackingUserId field) — the shared service falls back to the current user's email.
        var result = await _shipmentCreationService.CreateAndPersistAsync(order, request.NumberOfPackages, null, ct);
        if (!result.IsSuccess)
            return new ResetOrderShipmentResponse(result.ErrorCode!.Value);

        var packages = result.Labels
            .Select(label => new ResetShipmentPackage
            {
                TrackingNumber = label.TrackingNumber,
                LabelUrl = label.LabelUrl,
                LabelZpl = label.LabelZpl,
            })
            .ToList();

        return new ResetOrderShipmentResponse(new ResetShipmentData
        {
            ShipmentGuid = result.ShipmentGuid,
            Packages = packages,
            PendingCompletion = request.NumberOfPackages >= 2,
        });
    }
}
```

This is the bug fix (FR-3): `CreateAndPersistAsync` always calls
`IPackageRepository.ReplacePackagesForOrderAsync` internally (see
`ShipmentCreationService.PersistPackagesAsync` in task `extract-shipment-creation-service`), so
for the first time `ResetOrderShipmentHandler` causes `Package` rows to be written — with
`ShipmentGuid` equal to the **new** shipment's GUID — and `ReplacePackagesForOrderAsync`'s
delete-then-insert-per-order-code semantics clear the stale rows left by the cancelled
shipment(s) in the same operation.

- [ ] **Step 2: Build to catch compile errors early**

```bash
dotnet build
```

Expected: `Anela.Heblo.Application` builds clean; only the test project fails at this point
(its old constructor call no longer matches) until Step 3 is done.

- [ ] **Step 3: Rewrite `ResetOrderShipmentHandlerTests.cs`**

Read `backend/test/Anela.Heblo.Tests/Application/Packaging/ResetOrderShipmentHandlerTests.cs`,
then replace its entire contents with:

```csharp
using Anela.Heblo.Application.Features.Packaging.Services;
using Anela.Heblo.Application.Features.Packaging.UseCases.ResetOrderShipment;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.Packaging;

public class ResetOrderShipmentHandlerTests
{
    private readonly Mock<IShipmentClient> _shipmentClient = new();
    private readonly Mock<IPackingOrderClient> _orderClient = new();
    private readonly Mock<IShipmentCreationService> _shipmentCreationService = new();

    private ResetOrderShipmentHandler CreateHandler(IShipmentCreationService? shipmentCreationService = null) =>
        new(
            _shipmentClient.Object,
            _orderClient.Object,
            shipmentCreationService ?? _shipmentCreationService.Object,
            new Mock<ILogger<ResetOrderShipmentHandler>>().Object);

    private static PackingOrder EligibleOrder(params (string name, int qty, int weightGrams)[] items) =>
        new()
        {
            Code = "0001234",
            CustomerName = "Alice",
            StatusId = 26,
            Items = items.Select(i => new PackingOrderItem
            {
                Name = i.name,
                Quantity = i.qty,
                WeightGrams = i.weightGrams,
            }).ToList(),
        };

    private static ShipmentLabel MakeLabel(
        Guid shipmentGuid,
        string packageName = "P1",
        string? labelUrl = "https://example.com/label.pdf",
        string? labelZpl = null,
        string? trackingNumber = null) =>
        new()
        {
            ShipmentGuid = shipmentGuid,
            OrderCode = "0001234",
            PackageName = packageName,
            LabelUrl = labelUrl,
            LabelZpl = labelZpl,
            TrackingNumber = trackingNumber,
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

    // Test 1: No existing shipment → NoShipmentToReset, CancelShipmentAsync never called
    [Fact]
    public async Task Handle_NoExistingShipment_ReturnsNoShipmentToReset_AndNeverCallsCancel()
    {
        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.NoShipmentToReset);

        _shipmentClient.Verify(
            c => c.CancelShipmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Test 2: CancelShipmentAsync throws → ShipmentCancelFailed
    [Fact]
    public async Task Handle_CancelThrows_ReturnsShipmentCancelFailed()
    {
        var shipmentGuid = Guid.NewGuid();

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(shipmentGuid)]);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(shipmentGuid, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Cancel failed"));

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentCancelFailed);
    }

    // Test 3: Happy path — cancels old shipment, delegates to the shared service, maps its result
    [Fact]
    public async Task Handle_HappyPath_CancelsOldAndDelegatesToShipmentCreationService()
    {
        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();
        var order = EligibleOrder(("P001", 1, 400));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid)]);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult(newGuid,
                MakeLabel(newGuid, "NEW-P1", "https://carrier.example.com/new-label.pdf", "^XA-NEW^XZ", "TRK-NEW-1")));

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment.Should().NotBeNull();
        response.Shipment!.ShipmentGuid.Should().Be(newGuid);
        response.Shipment.Packages.Should().HaveCount(1);
        response.Shipment.Packages[0].TrackingNumber.Should().Be("TRK-NEW-1");
        response.Shipment.Packages[0].LabelUrl.Should().Be("https://carrier.example.com/new-label.pdf");
        response.Shipment.Packages[0].LabelZpl.Should().Be("^XA-NEW^XZ");
        response.Shipment.PendingCompletion.Should().BeFalse();

        _shipmentClient.Verify(
            c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()),
            Times.Once);
        _shipmentCreationService.Verify(
            s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Test 4: Shared service reports failure after a successful cancel → error code is surfaced unchanged
    [Theory]
    [InlineData(ErrorCodes.ShipmentCarrierNotResolved)]
    [InlineData(ErrorCodes.ShipmentCreationFailed)]
    public async Task Handle_WhenShipmentCreationServiceFailsAfterSuccessfulCancel_ReturnsMappedErrorCode(ErrorCodes errorCode)
    {
        var oldGuid = Guid.NewGuid();
        var order = EligibleOrder(("P001", 1, 400));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid)]);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShipmentCreationResult { IsSuccess = false, ErrorCode = errorCode });

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(errorCode);

        _shipmentClient.Verify(
            c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Test 7: Multiple distinct shipment GUIDs → each is cancelled before the shared service is called
    [Fact]
    public async Task Handle_MultipleShipments_CancelsAllBeforeCreating()
    {
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var newGuid = Guid.NewGuid();
        var order = EligibleOrder(("P001", 1, 400));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(guid1, "P1"), MakeLabel(guid2, "P2")]);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult(newGuid, MakeLabel(newGuid, "NEW-P1")));

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();

        _shipmentClient.Verify(c => c.CancelShipmentAsync(guid1, It.IsAny<CancellationToken>()), Times.Once);
        _shipmentClient.Verify(c => c.CancelShipmentAsync(guid2, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Test 8: Two labels sharing the same shipment GUID → cancel called only once
    [Fact]
    public async Task Handle_MultipleLabelsWithSameShipmentGuid_CancelsOnlyOnce()
    {
        var sharedGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();
        var order = EligibleOrder(("P001", 1, 400));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(sharedGuid, "P1"), MakeLabel(sharedGuid, "P2")]);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(sharedGuid, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult(newGuid, MakeLabel(newGuid, "NEW-P1")));

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();

        _shipmentClient.Verify(
            c => c.CancelShipmentAsync(sharedGuid, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Test 9: Second of two cancels fails → ShipmentCancelFailed, shared service never called
    [Fact]
    public async Task Handle_SecondOfTwoCancelsFails_ReturnsShipmentCancelFailed()
    {
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(guid1, "P1"), MakeLabel(guid2, "P2")]);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(guid1, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(guid2, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Cancel failed"));

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentCancelFailed);

        _shipmentCreationService.Verify(
            s => s.CreateAndPersistAsync(It.IsAny<PackingOrder>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Test 6: Cancel returns silently (404 treated as success inside client) → handler still delegates to the shared service
    [Fact]
    public async Task Handle_CancelReturnsSilently_ProceedsToCreate()
    {
        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();
        var order = EligibleOrder(("P001", 1, 400));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid)]);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult(newGuid, MakeLabel(newGuid, "NEW-P1")));

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment!.ShipmentGuid.Should().Be(newGuid);

        _shipmentClient.Verify(
            c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()),
            Times.Once);
        _shipmentCreationService.Verify(
            s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Multi-package recreate: NumberOfPackages flows into the shared service call, and
    // PendingCompletion is true for n >= 2 (handler-owned mapping, unrelated to the service).
    [Fact]
    public async Task Handle_MultiPackage_PassesNumberOfPackagesToService_AndSetsPendingCompletion()
    {
        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();
        var order = EligibleOrder(("P001", 1, 900));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid)]);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 3, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult(newGuid,
                MakeLabel(newGuid, "NEW-P1", "https://c/1.pdf", trackingNumber: "TRK-1"),
                MakeLabel(newGuid, "NEW-P2", null),
                MakeLabel(newGuid, "NEW-P3", null)));

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234", NumberOfPackages = 3 },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment!.Packages.Should().HaveCount(3);
        response.Shipment.Packages[0].TrackingNumber.Should().Be("TRK-1");
        response.Shipment.PendingCompletion.Should().BeTrue();

        _shipmentCreationService.Verify(
            s => s.CreateAndPersistAsync(order, 3, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Multi-package recreate: out-of-range count is rejected before any cancellation work
    [Fact]
    public async Task Handle_NumberOfPackagesAboveMax_ReturnsInvalidPackageCount()
    {
        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234", NumberOfPackages = 11 },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidPackageCount);

        _shipmentClient.Verify(
            c => c.CancelShipmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // FR-3 regression guard: reset must persist Package rows for the NEW shipment (the original
    // bug was that ResetOrderShipmentHandler never called IPackageRepository at all). This test
    // wires the REAL ShipmentCreationService (not a mock) into the handler so the assertion
    // exercises the actual handler -> service -> repository call chain, not just "the handler
    // called some mock" — this is the primary guard against the bug recurring.
    [Fact]
    public async Task Handle_HappyPath_PersistsPackageRowsForNewShipment_ThroughRealShipmentCreationService()
    {
        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();

        _shipmentClient
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid)])
            .ReturnsAsync([MakeLabel(newGuid, "NEW-P1", "https://carrier.example.com/new-label.pdf", trackingNumber: "TRK-NEW-1")]);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 400)));

        _shipmentClient
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);

        _shipmentClient
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = newGuid });

        var packageRepository = new Mock<IPackageRepository>();
        IReadOnlyCollection<Package>? persistedPackages = null;
        string? persistedOrderCode = null;
        packageRepository
            .Setup(r => r.ReplacePackagesForOrderAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Package>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyCollection<Package>, CancellationToken>((orderCode, packages, _) =>
            {
                persistedOrderCode = orderCode;
                persistedPackages = packages;
            })
            .Returns(Task.CompletedTask);

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("uid-1", "Operator", "op@example.com", IsAuthenticated: true));

        var realShipmentCreationService = new ShipmentCreationService(
            _shipmentClient.Object,
            packageRepository.Object,
            new Mock<IAuthorizationRepository>().Object,
            currentUserService.Object,
            Options.Create(new ShipmentLabelsSettings
            {
                DefaultPackageWidthCm = 30,
                DefaultPackageHeightCm = 20,
                DefaultPackageDepthCm = 15,
                MinPackageWeightGrams = 100,
            }),
            new Mock<ILogger<ShipmentCreationService>>().Object);

        var response = await CreateHandler(realShipmentCreationService).Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234", NumberOfPackages = 1 },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment!.ShipmentGuid.Should().Be(newGuid);

        persistedOrderCode.Should().Be("0001234");
        persistedPackages.Should().NotBeNull();
        persistedPackages!.Count.Should().Be(1);
        persistedPackages.Should().OnlyContain(p => p.ShipmentGuid == newGuid);
        persistedPackages.First().TrackingNumber.Should().Be("TRK-NEW-1");

        packageRepository.Verify(
            r => r.ReplacePackagesForOrderAsync("0001234", It.IsAny<IReadOnlyCollection<Package>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

Coverage note: `Handle_ZeroWeightAfterCancel_UsesFallbackPackageWeight` and
`Handle_EventualConsistency_SecondCallReturnsBothOldAndNew_OnlyNewPackagesInResponse` from the
pre-refactor file are intentionally removed — their logic (weight fallback; filtering stale
labels by shipment GUID) now lives in `ShipmentCreationService`, already covered by
`CreateAndPersistAsync_AllItemsHaveZeroWeight_UsesFallbackPackageWeight` and
`CreateAndPersistAsync_FetchedLabelsIncludeStaleShipment_FiltersToNewShipmentGuidOnly` in
`ShipmentCreationServiceTests.cs` (task `extract-shipment-creation-service`, Step 6).

- [ ] **Step 4: Run Reset-related tests**

```bash
dotnet test --filter "FullyQualifiedName~ResetOrderShipmentHandlerTests" --logger "console;verbosity=normal"
```

Expected: `Failed: 0` (13 test cases: 1 `Theory` with 2 cases +  11 single-case facts).

- [ ] **Step 5: Full build, full test run, and format check**

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
```

Expected: build succeeds; `Failed: 0` across the entire suite (this is the final task — Scan,
Reset, and the new service are all wired together now); `dotnet format --verify-no-changes`
reports no formatting violations. If it reports violations, run `dotnet format` (without
`--verify-no-changes`) and re-run the verify command.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentHandler.cs \
        backend/test/Anela.Heblo.Tests/Application/Packaging/ResetOrderShipmentHandlerTests.cs
git commit -m "$(cat <<'EOF'
Fix ResetOrderShipmentHandler to persist Package rows via IShipmentCreationService

Reset now delegates to the shared IShipmentCreationService.CreateAndPersistAsync after
cancelling the prior shipment(s), instead of its own inline duplicate of the create-shipment
block. This is the bug fix: for the first time, reset causes IPackageRepository.
ReplacePackagesForOrderAsync to run for the order, with rows carrying the new shipment's GUID
and clearing the stale rows left by the cancelled shipment(s) (delete-then-insert semantics).
A new regression test constructs the real ShipmentCreationService (not mocked) to prove the
handler -> service -> repository chain actually persists on a successful reset.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (extract shared collaborator, all 7 numbered steps + Decision-4/5 amendments) →
  `extract-shipment-creation-service`, Steps 1–2 (interface/result/impl), pinned by
  `ShipmentCreationServiceTests` (Step 6: label-filter test, padded-persistence test, sequential
  PackageNumber test, invalid-count test, zero-weight test, weight-floor test, carrier-not-resolved
  test, creation-failed test, packer-resolution tests ×4, persistence-swallow test, happy-path test).
- FR-2 (Scan uses the collaborator, all other behavior unchanged) → `refactor-scan-handler`
  Step 1 (refactored `Handle`), Step 3 (rewritten tests preserving eligibility/backfill/
  deferred-mark-as-packed/DTO-shape coverage).
- FR-3 (Reset uses the collaborator, persists Package rows — the bug fix) →
  `refactor-reset-handler` Step 1 (refactored `Handle`), Step 3
  (`Handle_HappyPath_PersistsPackageRowsForNewShipment_ThroughRealShipmentCreationService` — the
  literal FR-3 regression-test acceptance criterion, using the real service, asserting order code,
  package count, and new-shipment GUID).
- FR-4 (packer attribution on reset: null-safe, no request/DTO change, eligibility gate
  conditioned on non-null `packingUserId`) → `ShipmentCreationService`'s
  `if (packingUserId is { } requestedPackerId) { ... } else { packedByUserId = null; ... }`
  branch (extract task, Step 2); `ResetOrderShipmentHandler` always passes `null` (refactor-reset
  task, Step 1); `ResetOrderShipmentRequest` is untouched (not in any task's file list).
- FR-5 (persistence failure swallowed, structured log fields, both callers) → `PersistPackagesAsync`
  in the service catches and logs `OrderCode`/`ShipmentGuid`/`PackageCount` (extract task, Step 2),
  pinned by `CreateAndPersistAsync_PersistenceThrows_StillReturnsSuccessfulResult` (Step 6).
- FR-6 (DI wiring, both handlers depend on the new service) → `PackagingModule.cs` edit (extract
  task, Step 3); both handlers' constructors (refactor tasks, Step 1).
- NFR-1 (no added external calls; service accepts already-fetched `PackingOrder`) — both handlers
  fetch `order` exactly once, before calling the service; the service never calls
  `IPackingOrderClient`.
- NFR-3 (service independently unit-testable without a MediatR handler; both handler test files
  updated to mock the service; new test file covers the extracted branches) → all three tasks.
- Module-boundary allowlist requirement (design doc + arch review Risk row) →
  `extract-shipment-creation-service` Step 4, including the two extra entries (interface +
  `PackingOrderItem` via nested-type fallback) the design doc didn't spell out but the actual
  reflection-based test requires.

**2. Placeholder scan:** All steps show complete, compilable code (full file contents for every
create/rewrite, not diffs-with-elisions except where explicitly noted as an unchanged-method
elision in `refactor-scan-handler` Step 1, which is immediately followed by the full file
contents anyway). No "TODO"/"add validation"/"similar to Task N" placeholders. Every test asserts
concrete expected values.

**3. Type consistency:** `ShipmentCreationResult`, `IShipmentCreationService.CreateAndPersistAsync`
signature, `ErrorCodes` values, and `Package`/`ShipmentLabel`/`PackingOrder` field names are
identical across all three tasks' code listings (verified by construction — the later tasks'
mocks/setups use the exact members defined in the first task).
