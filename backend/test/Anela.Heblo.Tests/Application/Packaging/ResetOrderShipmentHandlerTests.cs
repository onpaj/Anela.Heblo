using Anela.Heblo.Application.Features.Packaging.UseCases.ResetOrderShipment;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.Packaging;

public class ResetOrderShipmentHandlerTests
{
    private readonly Mock<IShipmentClient> _shipmentClient = new();
    private readonly Mock<IPackingOrderClient> _orderClient = new();

    private static readonly ShipmentLabelsSettings DefaultLabelSettings = new()
    {
        DefaultPackageWidthMm = 300,
        DefaultPackageHeightMm = 200,
        DefaultPackageDepthMm = 150,
        MinPackageWeightGrams = 100,
    };

    private ResetOrderShipmentHandler CreateHandler(ShipmentLabelsSettings? labelSettings = null) =>
        new(
            _shipmentClient.Object,
            _orderClient.Object,
            Options.Create(labelSettings ?? DefaultLabelSettings),
            new Mock<ILogger<ResetOrderShipmentHandler>>().Object);

    private static PackingOrder EligibleOrder(params (string name, int qty, int weightGrams)[] items) =>
        new()
        {
            Code = "0001234",
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

    // Test 3: Happy path — cancels old shipment, creates new one, returns new shipment data
    [Fact]
    public async Task Handle_HappyPath_CancelsOldAndCreatesNewShipment()
    {
        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid)]);

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

        _shipmentClient
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid)])
            .ReturnsAsync([MakeLabel(newGuid, "NEW-P1", "https://carrier.example.com/new-label.pdf", "^XA-NEW^XZ", "TRK-NEW-1")]);

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment.Should().NotBeNull();
        response.Shipment!.ShipmentGuid.Should().Be(newGuid);
        response.Shipment.Packages.Should().HaveCount(1);
        response.Shipment.Packages[0].TrackingNumber.Should().Be("TRK-NEW-1");
        response.Shipment.PendingCompletion.Should().BeFalse();

        _shipmentClient.Verify(
            c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()),
            Times.Once);
        _shipmentClient.Verify(
            c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()),
            Times.Once);

        response.Shipment.Packages[0].LabelUrl.Should().Be("https://carrier.example.com/new-label.pdf");
        response.Shipment.Packages[0].LabelZpl.Should().Be("^XA-NEW^XZ");
    }

    // Test 4: CreateShipmentAsync throws after successful cancel → ShipmentCreationFailed
    [Fact]
    public async Task Handle_CreateThrowsAfterSuccessfulCancel_ReturnsShipmentCreationFailed()
    {
        var oldGuid = Guid.NewGuid();

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid)]);

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
            .ThrowsAsync(new HttpRequestException("Shipment API unavailable"));

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentCreationFailed);

        _shipmentClient.Verify(
            c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Test 5: Zero item weight after cancel → ShipmentOrderWeightUnavailable
    [Fact]
    public async Task Handle_ZeroWeightAfterCancel_ReturnsShipmentOrderWeightUnavailable()
    {
        var oldGuid = Guid.NewGuid();

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid)]);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 2, 0), ("P002", 1, 0)));

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentOrderWeightUnavailable);

        _shipmentClient.Verify(
            c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Test 7: Multiple distinct shipment GUIDs → each is cancelled before creating replacement
    [Fact]
    public async Task Handle_MultipleShipments_CancelsAllBeforeCreating()
    {
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var newGuid = Guid.NewGuid();

        _shipmentClient
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(guid1, "P1"), MakeLabel(guid2, "P2")])
            .ReturnsAsync([MakeLabel(newGuid, "NEW-P1")]);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
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

        _shipmentClient
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(sharedGuid, "P1"), MakeLabel(sharedGuid, "P2")])
            .ReturnsAsync([MakeLabel(newGuid, "NEW-P1")]);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(sharedGuid, It.IsAny<CancellationToken>()))
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

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();

        _shipmentClient.Verify(
            c => c.CancelShipmentAsync(sharedGuid, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Test 9: Second of two cancels fails → ShipmentCancelFailed, no replacement created
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

        _shipmentClient.Verify(
            c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Test: eventual consistency — second GetLabelsByOrderCodeAsync returns both old and new labels;
    // handler must filter by new shipment GUID so only the new package appears in the response
    [Fact]
    public async Task Handle_EventualConsistency_SecondCallReturnsBothOldAndNew_OnlyNewPackagesInResponse()
    {
        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();

        _shipmentClient
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid, "OLD-P1")])
            .ReturnsAsync([MakeLabel(oldGuid, "OLD-P1", trackingNumber: "TRK-OLD"), MakeLabel(newGuid, "NEW-P1", "https://carrier.example.com/new.pdf", trackingNumber: "TRK-NEW")]);

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

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment!.Packages.Should().HaveCount(1);
        response.Shipment.Packages[0].TrackingNumber.Should().Be("TRK-NEW");
        response.Shipment.Packages[0].LabelUrl.Should().Be("https://carrier.example.com/new.pdf");
    }

    // Test 6: Cancel returns silently (404 treated as success inside client) → handler still creates replacement
    [Fact]
    public async Task Handle_CancelReturnsSilently_ProceedsToCreate()
    {
        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();

        _shipmentClient
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid)])
            .ReturnsAsync([MakeLabel(newGuid, "NEW-P1")]);

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

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment!.ShipmentGuid.Should().Be(newGuid);

        _shipmentClient.Verify(
            c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()),
            Times.Once);
        _shipmentClient.Verify(
            c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Multi-package recreate: NumberOfPackages flows into PackageCount, response has N packages,
    // per-package weight is divided, and PendingCompletion is true for n >= 2.
    [Fact]
    public async Task Handle_MultiPackage_SetsPackageCountWeightAndPendingCompletion()
    {
        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();
        CreateShipmentCommand? captured = null;

        _shipmentClient
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid)])
            .ReturnsAsync(new List<ShipmentLabel>
            {
                MakeLabel(newGuid, "NEW-P1", "https://c/1.pdf", trackingNumber: "TRK-1"),
            }); // only 1 of 3 labels ready

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 900)));

        _shipmentClient
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);

        _shipmentClient
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .Callback<CreateShipmentCommand, CancellationToken>((cmd, _) => captured = cmd)
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = newGuid });

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234", NumberOfPackages = 3 },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        captured!.PackageCount.Should().Be(3);
        captured.Package.WeightGrams.Should().Be(300); // 900 / 3
        response.Shipment!.Packages.Should().HaveCount(3);
        response.Shipment.Packages[0].TrackingNumber.Should().Be("TRK-1");
        response.Shipment.Packages[1].TrackingNumber.Should().BeNull();
        response.Shipment.Packages[2].TrackingNumber.Should().BeNull();
        response.Shipment.PendingCompletion.Should().BeTrue();
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
}
