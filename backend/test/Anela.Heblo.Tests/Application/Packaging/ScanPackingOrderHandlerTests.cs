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
