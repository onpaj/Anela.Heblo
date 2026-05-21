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
        string? labelZpl = null) =>
        new()
        {
            ShipmentGuid = shipmentGuid,
            OrderCode = "0001234",
            PackageName = packageName,
            LabelUrl = labelUrl,
            LabelZpl = labelZpl,
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
            .ReturnsAsync([MakeLabel(newGuid, "NEW-P1", "https://carrier.example.com/new-label.pdf", "^XA-NEW^XZ")]);

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment.Should().NotBeNull();
        response.Shipment!.ShipmentGuid.Should().Be(newGuid);
        response.Shipment.Packages.Should().HaveCount(1);
        response.Shipment.Packages[0].Name.Should().Be("NEW-P1");

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
}
