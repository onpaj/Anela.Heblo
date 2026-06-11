using Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.Packaging;

public class ScanPackingOrderPackerTests
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

    private ScanPackingOrderHandler CreateHandler()
    {
        _currentUserService.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("uid-1", "Operator", "op@example.com", IsAuthenticated: true));
        return new(
            _shipmentClient.Object,
            _orderClient.Object,
            _eshopOrderClient.Object,
            Options.Create(DefaultLabelSettings),
            new Mock<ILogger<ScanPackingOrderHandler>>().Object,
            _packageRepository.Object,
            _currentUserService.Object,
            _authRepo.Object);
    }

    private static PackingOrder EligibleOrder() =>
        new()
        {
            Code = "0001234",
            StatusId = 26,
            IsEligibleForPacking = true,
            Items = [new PackingOrderItem { Name = "Item", Quantity = 1, WeightGrams = 500 }],
        };

    [Fact]
    public async Task Handle_WithPackingUserId_StampsUserIdAndDisplayName()
    {
        var packerId = Guid.NewGuid();
        var shipmentGuid = Guid.NewGuid();
        var packer = new AppUser
        {
            Id = packerId,
            DisplayName = "Pepa Balič",
            Email = "pepa@x.cz",
            Source = AppUserSource.Local,
            CreatedAt = DateTimeOffset.UtcNow,
            CanPack = true,
        };

        _orderClient.Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder());
        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });
        _shipmentClient.SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "PKG-1" }]);
        _authRepo.Setup(r => r.GetUserByIdAsync(packerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(packer);

        Package? persisted = null;
        _packageRepository.Setup(r => r.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()))
            .Callback<Package, CancellationToken>((p, _) => persisted = p)
            .Returns(Task.CompletedTask);

        await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234", PackingUserId = packerId },
            CancellationToken.None);

        persisted.Should().NotBeNull();
        persisted!.PackedByUserId.Should().Be(packerId);
        persisted.PackedBy.Should().Be("Pepa Balič");
    }

    [Fact]
    public async Task Handle_WithNullPackingUserId_FallsBackToCurrentUser()
    {
        var shipmentGuid = Guid.NewGuid();

        _orderClient.Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder());
        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });
        _shipmentClient.SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "PKG-1" }]);

        Package? persisted = null;
        _packageRepository.Setup(r => r.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()))
            .Callback<Package, CancellationToken>((p, _) => persisted = p)
            .Returns(Task.CompletedTask);

        await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234", PackingUserId = null },
            CancellationToken.None);

        persisted.Should().NotBeNull();
        persisted!.PackedByUserId.Should().BeNull();
        persisted.PackedBy.Should().Be("op@example.com");
    }

    [Fact]
    public async Task Handle_WithUnknownPackingUserId_ReturnsPackingUserNotEligible()
    {
        var unknownId = Guid.NewGuid();

        _orderClient.Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder());
        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = Guid.NewGuid() });
        _shipmentClient.SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([]);
        _authRepo.Setup(r => r.GetUserByIdAsync(unknownId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AppUser?)null);

        var result = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234", PackingUserId = unknownId },
            CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.PackingUserNotEligible);
        _packageRepository.Verify(r => r.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Handle_WithIneligiblePackingUser_ReturnsPackingUserNotEligible(bool isActive, bool canPack)
    {
        var packerId = Guid.NewGuid();
        var ineligible = new AppUser
        {
            Id = packerId,
            DisplayName = "Ineligible",
            Email = "x@x.cz",
            IsActive = isActive,
            CanPack = canPack,
            Source = AppUserSource.Local,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _orderClient.Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder());
        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = Guid.NewGuid() });
        _shipmentClient.SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([]);
        _authRepo.Setup(r => r.GetUserByIdAsync(packerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ineligible);

        var result = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234", PackingUserId = packerId },
            CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.PackingUserNotEligible);
        _packageRepository.Verify(r => r.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
