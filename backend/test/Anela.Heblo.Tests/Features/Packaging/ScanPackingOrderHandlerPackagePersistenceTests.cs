using Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        IReadOnlyList<ShipmentLabel>? existingLabels = null,
        IReadOnlyList<ShipmentLabel>? newLabels = null,
        IReadOnlyList<ShippingOption>? options = null)
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

        // Handler calls GetLabelsByOrderCodeAsync twice: once before (check existing) and once after (get new labels)
        shipmentClient.SetupSequence(c => c.GetLabelsByOrderCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLabels ?? Array.Empty<ShipmentLabel>())
            .ReturnsAsync(newLabels ?? Array.Empty<ShipmentLabel>());

        shipmentClient.Setup(c => c.GetShippingOptionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(options ?? new[] { new ShippingOption { CarrierCode = "PPL" } });

        shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = Guid.NewGuid() });

        var shipmentSettings = Options.Create(new ShipmentLabelsSettings
        {
            MinPackageWeightGrams = 100,
            DefaultPackageWidthMm = 300,
            DefaultPackageHeightMm = 200,
            DefaultPackageDepthMm = 150,
        });
        var ordSettings = Options.Create(new ShoptetOrdersSettings
        {
            PackingStateId = 26,
            PackedStateId = 52,
        });

        return new ScanPackingOrderHandler(
            shipmentClient.Object,
            orderClient.Object,
            eshopClient.Object,
            shipmentSettings,
            ordSettings,
            NullLogger<ScanPackingOrderHandler>.Instance,
            packageRepo.Object,
            currentUser.Object);
    }

    private static PackingOrder MakeOrder(int statusId = 26) => new()
    {
        Code = "ORD-1",
        CustomerName = "Alice",
        ShippingMethodName = "PPL",
        StatusId = statusId,
        Items = new List<PackingOrderItem>
        {
            new() { WeightGrams = 500, Quantity = 1 },
        },
    };

    [Fact]
    public async Task Handle_PersistsOnePackageRowPerCreatedLabel()
    {
        // Arrange
        var shipmentGuid = Guid.NewGuid();
        var newLabels = new List<ShipmentLabel>
        {
            new() { PackageName = "PKG-1", ShipmentGuid = shipmentGuid, TrackingNumber = "TRK1" },
            new() { PackageName = "PKG-2", ShipmentGuid = shipmentGuid, TrackingNumber = "TRK2" },
        };
        var shipmentClient = new Mock<IShipmentClient>();
        var orderClient = new Mock<IPackingOrderClient>();
        var sut = MakeSut(out var repo, shipmentClient, orderClient, MakeOrder(), newLabels: newLabels);

        // Act
        var response = await sut.Handle(new ScanPackingOrderRequest { OrderCode = "ORD-1" }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        repo.Verify(r => r.AddAsync(
            It.Is<Package>(p =>
                p.OrderCode == "ORD-1" &&
                p.CustomerName == "Alice" &&
                p.ShippingProviderCode == "PPL" &&
                p.PackedBy == "op@example.com"),
            It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_DoesNotPersist_WhenShipmentAlreadyExisted()
    {
        // Arrange
        var existingLabels = new List<ShipmentLabel>
        {
            new() { PackageName = "PKG-1", ShipmentGuid = Guid.NewGuid(), TrackingNumber = "TRK1" },
        };
        var sut = MakeSut(out var repo, order: MakeOrder(), existingLabels: existingLabels);

        // Act
        var response = await sut.Handle(new ScanPackingOrderRequest { OrderCode = "ORD-1" }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Shipment!.AlreadyExisted.Should().BeTrue();
        repo.Verify(r => r.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DoesNotFailScan_WhenPersistenceThrows()
    {
        // Arrange
        var newLabels = new List<ShipmentLabel>
        {
            new() { PackageName = "PKG-1", ShipmentGuid = Guid.NewGuid(), TrackingNumber = "TRK1" },
        };
        var sut = MakeSut(out var repo, order: MakeOrder(), newLabels: newLabels);
        repo.Setup(r => r.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("duplicate key"));

        // Act
        var response = await sut.Handle(new ScanPackingOrderRequest { OrderCode = "ORD-1" }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
    }
}
