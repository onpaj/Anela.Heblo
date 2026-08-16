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
