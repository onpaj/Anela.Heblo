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
        return new ScanPackingOrderHandler(
            shipmentClient.Object,
            orderClient.Object,
            eshopClient.Object,
            shipmentSettings,
            NullLogger<ScanPackingOrderHandler>.Instance,
            packageRepo.Object,
            currentUser.Object);
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
    public async Task Handle_PersistsOnePackageRowPerCreatedLabel_WithSequentialPackageNumbers()
    {
        // Arrange
        var shipmentGuid = Guid.NewGuid();
        // Both labels report the same carrier package name (custom-packaging shipments do
        // this); the handler must still produce distinct, unique PackageNumbers.
        var newLabels = new List<ShipmentLabel>
        {
            new() { PackageName = "Vlastní balení", ShipmentGuid = shipmentGuid, TrackingNumber = "TRK1" },
            new() { PackageName = "Vlastní balení", ShipmentGuid = shipmentGuid, TrackingNumber = "TRK2" },
        };
        var shipmentClient = new Mock<IShipmentClient>();
        var orderClient = new Mock<IPackingOrderClient>();
        var sut = MakeSut(out var repo, shipmentClient, orderClient, MakeOrder(), newLabels: newLabels);

        // Act
        var response = await sut.Handle(new ScanPackingOrderRequest { OrderCode = "ORD-1", NumberOfPackages = 2 }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        repo.Verify(r => r.ReplacePackagesForOrderAsync(
            "ORD-1",
            It.Is<IReadOnlyCollection<Package>>(packages =>
                packages.Count == 2 &&
                packages.All(p =>
                    p.OrderCode == "ORD-1" &&
                    p.CustomerName == "Alice" &&
                    p.ShippingProviderCode == "PPL" &&
                    p.PackedBy == "op@example.com") &&
                packages.Any(p => p.PackageNumber == "1" && p.TrackingNumber == "TRK1") &&
                packages.Any(p => p.PackageNumber == "2" && p.TrackingNumber == "TRK2")),
            It.IsAny<CancellationToken>()),
            Times.Once);
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
        repo.Verify(r => r.ReplacePackagesForOrderAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Package>>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
        repo.Setup(r => r.ReplacePackagesForOrderAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Package>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("duplicate key"));

        // Act
        var response = await sut.Handle(new ScanPackingOrderRequest { OrderCode = "ORD-1" }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
    }
}
