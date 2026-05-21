using Anela.Heblo.Application.Features.Packaging.UseCases.PrepareOrderLabel;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.Contracts;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.Packaging;

public class PrepareOrderLabelHandlerTests
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

    private static readonly ShoptetOrdersSettings DefaultOrderSettings = new()
    {
        PackingStateId = 26,
    };

    private PrepareOrderLabelHandler CreateHandler(
        ShipmentLabelsSettings? labelSettings = null,
        ShoptetOrdersSettings? orderSettings = null) =>
        new(
            _shipmentClient.Object,
            _orderClient.Object,
            Options.Create(labelSettings ?? DefaultLabelSettings),
            Options.Create(orderSettings ?? DefaultOrderSettings),
            new Mock<ILogger<PrepareOrderLabelHandler>>().Object);

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

    // Test 1: Order in wrong state → eligibility rejection
    [Fact]
    public async Task Handle_OrderNotInPackingState_ReturnsError_AndNeverCallsShipmentClient()
    {
        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackingOrder { Code = "0001234", StatusId = 99 });

        var response = await CreateHandler().Handle(
            new PrepareOrderLabelRequest { OrderCode = "0001234", ForceRecreate = false },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.OrderNotInPackingState);

        _shipmentClient.Verify(
            c => c.GetLabelsByOrderCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _shipmentClient.Verify(
            c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Test 2: Labels already exist + ForceRecreate = false → return existing without creating
    [Fact]
    public async Task Handle_LabelsExist_ForceRecreateFalse_ReturnsExistingShipmentFound()
    {
        var existingLabel = new ShipmentLabel
        {
            ShipmentGuid = Guid.NewGuid(),
            OrderCode = "0001234",
            PackageName = "P1",
            LabelUrl = "https://example.com/label.pdf",
        };

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 400)));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingLabel]);

        var response = await CreateHandler().Handle(
            new PrepareOrderLabelRequest { OrderCode = "0001234", ForceRecreate = false },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.ExistingShipmentFound.Should().BeTrue();
        response.Labels.Should().HaveCount(1);

        _shipmentClient.Verify(
            c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Test 3: Labels exist + ForceRecreate = true → creates new shipment, ExistingShipmentFound = false
    [Fact]
    public async Task Handle_LabelsExist_ForceRecreateTrue_CreatesNewShipment()
    {
        var shipmentGuid = Guid.NewGuid();
        var callCount = 0;

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 400)));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return [new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1" }];
                return [new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://new.com/label.pdf" }];
            });

        _shipmentClient
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);

        _shipmentClient
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });

        var response = await CreateHandler().Handle(
            new PrepareOrderLabelRequest { OrderCode = "0001234", ForceRecreate = true },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.ExistingShipmentFound.Should().BeFalse();

        _shipmentClient.Verify(
            c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Test 4: All items have WeightGrams = 0 → weight unavailable
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
            new PrepareOrderLabelRequest { OrderCode = "0001234", ForceRecreate = false },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentOrderWeightUnavailable);
    }

    // Test 5: No shipping options returned → carrier not resolved
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
            new PrepareOrderLabelRequest { OrderCode = "0001234", ForceRecreate = false },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentCarrierNotResolved);
    }

    // Test 6: CreateShipmentAsync throws HttpRequestException → creation failed
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
            new PrepareOrderLabelRequest { OrderCode = "0001234", ForceRecreate = false },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentCreationFailed);
    }

    // Test 7: First poll returns no labels, second poll (after 3s delay) returns label with LabelUrl → LabelReady = true
    [Fact]
    public async Task Handle_FirstPollEmpty_SecondPollHasLabelUrl_ReturnsLabelReady()
    {
        var shipmentGuid = Guid.NewGuid();
        var callCount = 0;

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 400)));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount switch
                {
                    1 => Array.Empty<ShipmentLabel>(),
                    2 => [new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1" }],
                    _ => [new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://example.com/label.pdf" }],
                };
            });

        _shipmentClient
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);

        _shipmentClient
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });

        var response = await CreateHandler().Handle(
            new PrepareOrderLabelRequest { OrderCode = "0001234", ForceRecreate = false },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.LabelReady.Should().BeTrue();
        response.Labels.Should().NotBeEmpty();
        response.Labels[0].LabelUrl.Should().Be("https://example.com/label.pdf");
    }
}
