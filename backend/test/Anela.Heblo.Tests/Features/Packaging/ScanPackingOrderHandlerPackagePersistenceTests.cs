using Anela.Heblo.Application.Features.Packaging.Services;
using Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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
        IReadOnlyList<ShipmentLabel>? existingLabels = null)
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

        shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLabels ?? Array.Empty<ShipmentLabel>());

        // BackfillExistingShipmentPackagesAsync (the reprint path exercised by these tests)
        // still calls GetShippingOptionsAsync directly -- unlike shipment creation, that method
        // wasn't moved into IShipmentCreationService. Leaving this unmocked makes Moq return a
        // null IReadOnlyList<ShippingOption>, which NREs inside the backfill's own try/catch and
        // silently swallows the AddMissingAsync call the tests below verify.
        shipmentClient.Setup(c => c.GetShippingOptionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ShippingOption { CarrierCode = "PPL" } });

        var authRepo = new Mock<IAuthorizationRepository>();
        var shipmentCreationService = new Mock<IShipmentCreationService>();
        return new ScanPackingOrderHandler(
            shipmentClient.Object,
            orderClient.Object,
            eshopClient.Object,
            NullLogger<ScanPackingOrderHandler>.Instance,
            packageRepo.Object,
            currentUser.Object,
            authRepo.Object,
            shipmentCreationService.Object);
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
    public async Task Handle_BackfillsPackages_WhenEligibleShipmentAlreadyExisted()
    {
        // Arrange — eligible order re-scanned (reprint); shipment already exists in Shoptet
        var shipmentGuid = Guid.NewGuid();
        var existingLabels = new List<ShipmentLabel>
        {
            new() { PackageName = "PKG-1", ShipmentGuid = shipmentGuid, TrackingNumber = "TRK1" },
        };
        var sut = MakeSut(out var repo, order: MakeOrder(), existingLabels: existingLabels);

        // Act
        var response = await sut.Handle(new ScanPackingOrderRequest { OrderCode = "ORD-1" }, CancellationToken.None);

        // Assert — reprint returns the existing shipment AND backfills the missing row idempotently
        response.Success.Should().BeTrue();
        response.Shipment!.AlreadyExisted.Should().BeTrue();
        repo.Verify(r => r.AddMissingAsync(
            It.Is<IReadOnlyList<Package>>(list =>
                list.Count == 1 &&
                list[0].OrderCode == "ORD-1" &&
                list[0].PackageNumber == "PKG-1" &&
                list[0].TrackingNumber == "TRK1" &&
                list[0].ShipmentGuid == shipmentGuid &&
                list[0].ShippingProviderCode == "PPL"),
            It.IsAny<CancellationToken>()),
            Times.Once);
        repo.Verify(r => r.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DoesNotBackfill_WhenOrderNotEligible()
    {
        // Arrange — already-packed order rescanned for review (review path is intentionally untouched)
        var existingLabels = new List<ShipmentLabel>
        {
            new() { PackageName = "PKG-1", ShipmentGuid = Guid.NewGuid(), TrackingNumber = "TRK1" },
        };
        var sut = MakeSut(out var repo, order: MakeOrder(isEligible: false), existingLabels: existingLabels);

        // Act
        var response = await sut.Handle(new ScanPackingOrderRequest { OrderCode = "ORD-1" }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Shipment!.AlreadyExisted.Should().BeTrue();
        repo.Verify(r => r.ReplacePackagesForOrderAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Package>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        repo.Verify(r => r.AddMissingAsync(It.IsAny<IReadOnlyList<Package>>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DoesNotFailScan_WhenBackfillThrows()
    {
        // Arrange
        var existingLabels = new List<ShipmentLabel>
        {
            new() { PackageName = "PKG-1", ShipmentGuid = Guid.NewGuid(), TrackingNumber = "TRK1" },
        };
        var sut = MakeSut(out var repo, order: MakeOrder(), existingLabels: existingLabels);
        repo.Setup(r => r.AddMissingAsync(It.IsAny<IReadOnlyList<Package>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        // Act
        var response = await sut.Handle(new ScanPackingOrderRequest { OrderCode = "ORD-1" }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
    }
}
