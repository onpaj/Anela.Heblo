using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.ShipmentLabels;

public class CreateOrderShipmentHandlerTests
{
    private readonly Mock<IShipmentClient> _shipmentClientMock = new();
    private readonly Mock<IPackingOrderClient> _orderClientMock = new();

    private static readonly ShipmentLabelsSettings DefaultSettings = new()
    {
        DefaultPackageWidthMm = 300,
        DefaultPackageHeightMm = 200,
        DefaultPackageDepthMm = 150,
        MinPackageWeightGrams = 100,
    };

    private CreateOrderShipmentHandler CreateHandler(ShipmentLabelsSettings? settings = null) =>
        new(
            _shipmentClientMock.Object,
            _orderClientMock.Object,
            Options.Create(settings ?? DefaultSettings),
            NullLogger<CreateOrderShipmentHandler>.Instance);

    private static PackingOrder PackingOrderWith(params (string code, int qty, int weightGrams)[] items) =>
        new()
        {
            Code = "0001234",
            Items = items.Select(i => new PackingOrderItem
            {
                Name = i.code,
                Quantity = i.qty,
                WeightGrams = i.weightGrams,
            }).ToList(),
        };

    [Fact]
    public async Task Handle_HappyPath_CreatesShipmentAndReturnsReadyLabel()
    {
        var shipmentGuid = Guid.NewGuid();
        var label = new ShipmentLabel
        {
            ShipmentGuid = shipmentGuid,
            OrderCode = "0001234",
            PackageName = "P1",
            LabelUrl = "https://example.com/label.pdf",
        };

        _shipmentClientMock
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([label]);

        _orderClientMock
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PackingOrderWith(("P001", 2, 300)));

        _shipmentClientMock
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "123", Name = "PPL" }]);

        _shipmentClientMock
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid, Status = null });

        var response = await CreateHandler().Handle(
            new CreateOrderShipmentRequest { OrderCode = "0001234", ForceCreate = false },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.LabelReady.Should().BeTrue();
        response.Labels.Should().HaveCount(1);
        response.Labels[0].LabelUrl.Should().Be("https://example.com/label.pdf");
        response.ExistingShipmentFound.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_LabelNotReadyAfterCreate_RetryAndStillNotReady_ReturnsLabelReadyFalse()
    {
        var shipmentGuid = Guid.NewGuid();
        var labelNotReady = new ShipmentLabel
        {
            ShipmentGuid = shipmentGuid,
            OrderCode = "0001234",
            PackageName = "P1",
            LabelUrl = null,
            LabelZpl = null,
        };

        _shipmentClientMock
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([labelNotReady])
            .ReturnsAsync([labelNotReady]);

        _orderClientMock
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PackingOrderWith(("P001", 1, 500)));

        _shipmentClientMock
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "123", Name = "PPL" }]);

        _shipmentClientMock
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });

        var response = await CreateHandler().Handle(
            new CreateOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.LabelReady.Should().BeFalse();
        response.Labels.Should().HaveCount(1);

        _shipmentClientMock.Verify(
            c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task Handle_ExistingShipmentWithoutForceCreate_ReturnsAlreadyExistsWithLabels()
    {
        var existingLabel = new ShipmentLabel
        {
            ShipmentGuid = Guid.NewGuid(),
            OrderCode = "0001234",
            PackageName = "P1",
            LabelUrl = "https://existing.com/label.pdf",
        };

        _shipmentClientMock
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingLabel]);

        var response = await CreateHandler().Handle(
            new CreateOrderShipmentRequest { OrderCode = "0001234", ForceCreate = false },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentAlreadyExists);
        response.ExistingShipmentFound.Should().BeTrue();
        response.Labels.Should().HaveCount(1);
        response.Labels[0].LabelUrl.Should().Be("https://existing.com/label.pdf");

        _shipmentClientMock.Verify(
            c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ExistingShipmentWithForceCreate_ProceedsToCreate()
    {
        var shipmentGuid = Guid.NewGuid();
        _shipmentClientMock
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1" }])
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://new.com/label.pdf" }]);

        _orderClientMock
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PackingOrderWith(("P001", 1, 400)));

        _shipmentClientMock
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "123", Name = "PPL" }]);

        _shipmentClientMock
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });

        var response = await CreateHandler().Handle(
            new CreateOrderShipmentRequest { OrderCode = "0001234", ForceCreate = true },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        _shipmentClientMock.Verify(
            c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NoCarrierOptions_ReturnsCarrierNotResolved()
    {
        _shipmentClientMock
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _orderClientMock
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PackingOrderWith(("P001", 1, 400)));

        _shipmentClientMock
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await CreateHandler().Handle(
            new CreateOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentCarrierNotResolved);
    }

    [Fact]
    public async Task Handle_EmptyOrder_ReturnsWeightUnavailable()
    {
        _shipmentClientMock
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _orderClientMock
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackingOrder { Code = "0001234", Items = [] });

        var response = await CreateHandler().Handle(
            new CreateOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentOrderWeightUnavailable);
    }

    [Fact]
    public async Task Handle_WeightAppliesMinPackageFloor()
    {
        var shipmentGuid = Guid.NewGuid();
        _shipmentClientMock
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://x.com" }]);

        _orderClientMock
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PackingOrderWith(("P001", 1, 10)));

        _shipmentClientMock
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "123", Name = "PPL" }]);

        CreateShipmentCommand? capturedCommand = null;
        _shipmentClientMock
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .Callback<CreateShipmentCommand, CancellationToken>((cmd, _) => capturedCommand = cmd)
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });

        await CreateHandler().Handle(
            new CreateOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        capturedCommand!.Package.WeightGrams.Should().Be(100);
    }

    [Fact]
    public async Task Handle_CreateShipmentThrows_ReturnsShipmentCreationFailed()
    {
        _shipmentClientMock
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _orderClientMock
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PackingOrderWith(("P001", 1, 500)));

        _shipmentClientMock
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "123", Name = "PPL" }]);

        _shipmentClientMock
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet API unavailable"));

        var response = await CreateHandler().Handle(
            new CreateOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentCreationFailed);
    }
}
