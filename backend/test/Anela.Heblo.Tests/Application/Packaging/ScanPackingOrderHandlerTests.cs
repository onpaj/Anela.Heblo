using Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;
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

public class ScanPackingOrderHandlerTests
{
    private readonly Mock<IShipmentClient> _shipmentClient = new();
    private readonly Mock<IPackingOrderClient> _orderClient = new();
    private readonly Mock<IEshopOrderClient> _eshopOrderClient = new();
    private readonly Mock<IPackageRepository> _packageRepository = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly Mock<IAuthorizationRepository> _authRepo = new();

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
            _currentUserService.Object,
            _authRepo.Object);
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
        // contract because ScanPackingOrderHandler and ResetOrderShipmentHandler depend on it.
        typeof(PackingOrderItem).GetProperty("WeightGrams").Should().NotBeNull(
            "PackingOrderItem is the internal Application contract and ScanPackingOrderHandler.cs:102 reads WeightGrams.");
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

    // Multi-package: Shoptet returns fewer labels than requested → response still has N packages
    [Fact]
    public async Task Handle_MultiPackage_ShoptetReturnsFewerLabelsThanRequested_ResponseHasNPackages()
    {
        var shipmentGuid = Guid.NewGuid();

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 900)));

        _shipmentClient
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync(new List<ShipmentLabel>
            {
                new() { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://c/1.pdf" },
            }); // only 1 label ready, even though 3 were requested

        _shipmentClient
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "1", Name = "PPL" }]);

        _shipmentClient
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234", NumberOfPackages = 3 },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment!.Packages.Should().HaveCount(3);
        response.Shipment.Packages[0].LabelUrl.Should().Be("https://c/1.pdf");
        response.Shipment.Packages[1].LabelUrl.Should().BeNull();
        response.Shipment.Packages[2].LabelUrl.Should().BeNull();
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
}
