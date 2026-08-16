using Anela.Heblo.Application.Features.Packaging.Services;
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

namespace Anela.Heblo.Tests.Application.Packaging;

public class ShipmentCreationServiceTests
{
    private readonly Mock<IShipmentClient> _shipmentClient = new();
    private readonly Mock<IPackageRepository> _packageRepository = new();
    private readonly Mock<IAuthorizationRepository> _authRepo = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();

    private static readonly ShipmentLabelsSettings DefaultLabelSettings = new()
    {
        DefaultPackageWidthCm = 30,
        DefaultPackageHeightCm = 20,
        DefaultPackageDepthCm = 15,
        MinPackageWeightGrams = 100,
        FallbackPackageWeightGrams = 1000,
    };

    private ShipmentCreationService CreateService(ShipmentLabelsSettings? labelSettings = null)
    {
        _currentUserService.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("uid-1", "Operator", "op@example.com", IsAuthenticated: true));
        return new ShipmentCreationService(
            _shipmentClient.Object,
            _packageRepository.Object,
            _authRepo.Object,
            _currentUserService.Object,
            Options.Create(labelSettings ?? DefaultLabelSettings),
            new Mock<ILogger<ShipmentCreationService>>().Object);
    }

    private static PackingOrder EligibleOrder(params (string name, int qty, int weightGrams)[] items) =>
        new()
        {
            Code = "0001234",
            CustomerName = "Alice",
            StatusId = 26,
            IsEligibleForPacking = true,
            Items = items.Select(i => new PackingOrderItem
            {
                Name = i.name,
                Quantity = i.qty,
                WeightGrams = i.weightGrams,
            }).ToList(),
        };

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task CreateAndPersistAsync_InvalidPackageCount_ReturnsInvalidPackageCount_AndMakesNoExternalCalls(int numberOfPackages)
    {
        var order = EligibleOrder(("P001", 1, 400));

        var result = await CreateService().CreateAndPersistAsync(order, numberOfPackages, null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidPackageCount);
        _shipmentClient.Verify(c => c.GetShippingOptionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _shipmentClient.Verify(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAndPersistAsync_AllItemsHaveZeroWeight_UsesFallbackPackageWeight()
    {
        var order = EligibleOrder(("P001", 2, 0), ("P002", 1, 0));
        var shipmentGuid = Guid.NewGuid();
        CreateShipmentCommand? captured = null;

        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "1", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .Callback<CreateShipmentCommand, CancellationToken>((cmd, _) => captured = cmd)
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1" }]);

        var result = await CreateService().CreateAndPersistAsync(order, 1, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured!.Package.WeightGrams.Should().Be(1000);
    }

    [Fact]
    public async Task CreateAndPersistAsync_MultiPackage_FloorsPerPackageWeightAtMinimum()
    {
        var order = EligibleOrder(("P001", 1, 120)); // 120/3=40 < min 100
        var shipmentGuid = Guid.NewGuid();
        CreateShipmentCommand? captured = null;

        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "1", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .Callback<CreateShipmentCommand, CancellationToken>((cmd, _) => captured = cmd)
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1" }]);

        await CreateService().CreateAndPersistAsync(order, 3, null, CancellationToken.None);

        captured!.Package.WeightGrams.Should().Be(100);
    }

    [Fact]
    public async Task CreateAndPersistAsync_NoShippingOptions_ReturnsShipmentCarrierNotResolved()
    {
        var order = EligibleOrder(("P001", 1, 300));
        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateService().CreateAndPersistAsync(order, 1, null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ShipmentCarrierNotResolved);
    }

    [Fact]
    public async Task CreateAndPersistAsync_CreateShipmentThrows_ReturnsShipmentCreationFailed()
    {
        var order = EligibleOrder(("P001", 1, 500));
        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shipment API unavailable"));

        var result = await CreateService().CreateAndPersistAsync(order, 1, null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ShipmentCreationFailed);
    }

    // Arch review Decision 4: fetched labels can still include a just-cancelled shipment's
    // stale labels (Reset's scenario) — the service must filter to the new shipment's GUID.
    [Fact]
    public async Task CreateAndPersistAsync_FetchedLabelsIncludeStaleShipment_FiltersToNewShipmentGuidOnly()
    {
        var order = EligibleOrder(("P001", 1, 400));
        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();

        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = newGuid });
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShipmentLabel>
            {
                new() { ShipmentGuid = oldGuid, OrderCode = "0001234", PackageName = "OLD-P1", TrackingNumber = "TRK-OLD" },
                new() { ShipmentGuid = newGuid, OrderCode = "0001234", PackageName = "NEW-P1", TrackingNumber = "TRK-NEW", LabelUrl = "https://carrier.example.com/new.pdf" },
            });

        var result = await CreateService().CreateAndPersistAsync(order, 1, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Labels.Should().HaveCount(1);
        result.Labels[0].TrackingNumber.Should().Be("TRK-NEW");
        result.Labels[0].LabelUrl.Should().Be("https://carrier.example.com/new.pdf");
    }

    // Arch review Decision 5: persistence must use the padded n-length list, not the raw
    // fetched-label count.
    [Fact]
    public async Task CreateAndPersistAsync_FewerLabelsThanRequested_PadsResultAndPersistsExactlyNRows()
    {
        var order = EligibleOrder(("P001", 1, 900));
        var shipmentGuid = Guid.NewGuid();
        IReadOnlyCollection<Package>? persisted = null;

        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShipmentLabel>
            {
                new() { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", TrackingNumber = "TRK-1", LabelUrl = "https://c/1.pdf" },
            }); // only 1 of 3 ready

        _packageRepository.Setup(r => r.ReplacePackagesForOrderAsync(
                "0001234", It.IsAny<IReadOnlyCollection<Package>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyCollection<Package>, CancellationToken>((_, packages, _) => persisted = packages)
            .Returns(Task.CompletedTask);

        var result = await CreateService().CreateAndPersistAsync(order, 3, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Labels.Should().HaveCount(3);
        result.Labels[0].TrackingNumber.Should().Be("TRK-1");
        result.Labels[1].TrackingNumber.Should().BeNull();
        result.Labels[2].TrackingNumber.Should().BeNull();

        persisted.Should().NotBeNull();
        persisted!.Count.Should().Be(3); // padded count, not the raw fetched-label count of 1
        persisted.Select(p => p.PackageNumber).Should().BeEquivalentTo(new[] { "1", "2", "3" });
        persisted.First(p => p.PackageNumber == "1").TrackingNumber.Should().Be("TRK-1");
        persisted.First(p => p.PackageNumber == "2").TrackingNumber.Should().BeNull();
        persisted.First(p => p.PackageNumber == "3").TrackingNumber.Should().BeNull();
        persisted.Should().OnlyContain(p => p.ShipmentGuid == shipmentGuid);
    }

    [Fact]
    public async Task CreateAndPersistAsync_LabelsShareSameCarrierPackageName_StillProducesSequentialPackageNumbers()
    {
        var order = EligibleOrder(("P001", 1, 500));
        var shipmentGuid = Guid.NewGuid();
        IReadOnlyCollection<Package>? persisted = null;

        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShipmentLabel>
            {
                new() { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "Vlastní balení", TrackingNumber = "TRK1" },
                new() { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "Vlastní balení", TrackingNumber = "TRK2" },
            });

        _packageRepository.Setup(r => r.ReplacePackagesForOrderAsync(
                "0001234", It.IsAny<IReadOnlyCollection<Package>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyCollection<Package>, CancellationToken>((_, packages, _) => persisted = packages)
            .Returns(Task.CompletedTask);

        var result = await CreateService().CreateAndPersistAsync(order, 2, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        persisted.Should().NotBeNull();
        persisted!.Any(p => p.PackageNumber == "1" && p.TrackingNumber == "TRK1").Should().BeTrue();
        persisted.Any(p => p.PackageNumber == "2" && p.TrackingNumber == "TRK2").Should().BeTrue();
    }

    [Fact]
    public async Task CreateAndPersistAsync_WithPackingUserId_StampsUserIdAndDisplayName()
    {
        var order = EligibleOrder(("P001", 1, 500));
        var packerId = Guid.NewGuid();
        var shipmentGuid = Guid.NewGuid();
        var packer = new AppUser
        {
            Id = packerId,
            DisplayName = "Pepa Balič",
            Email = "pepa@x.cz",
            IsActive = true,
            CanPack = true,
            Source = AppUserSource.Local,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1" }]);
        _authRepo.Setup(r => r.GetUserByIdAsync(packerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(packer);

        Package? persisted = null;
        _packageRepository.Setup(r => r.ReplacePackagesForOrderAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Package>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyCollection<Package>, CancellationToken>((_, packages, _) => persisted = packages.FirstOrDefault())
            .Returns(Task.CompletedTask);

        await CreateService().CreateAndPersistAsync(order, 1, packerId, CancellationToken.None);

        persisted.Should().NotBeNull();
        persisted!.PackedByUserId.Should().Be(packerId);
        persisted.PackedBy.Should().Be("Pepa Balič");
        _authRepo.Verify(r => r.GetUserByIdAsync(packerId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAndPersistAsync_WithNullPackingUserId_FallsBackToCurrentUserEmail()
    {
        var order = EligibleOrder(("P001", 1, 500));
        var shipmentGuid = Guid.NewGuid();

        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1" }]);

        Package? persisted = null;
        _packageRepository.Setup(r => r.ReplacePackagesForOrderAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Package>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyCollection<Package>, CancellationToken>((_, packages, _) => persisted = packages.FirstOrDefault())
            .Returns(Task.CompletedTask);

        await CreateService().CreateAndPersistAsync(order, 1, null, CancellationToken.None);

        persisted.Should().NotBeNull();
        persisted!.PackedByUserId.Should().BeNull();
        persisted.PackedBy.Should().Be("op@example.com");
    }

    [Fact]
    public async Task CreateAndPersistAsync_WithUnknownPackingUserId_ReturnsPackingUserNotEligible()
    {
        var order = EligibleOrder(("P001", 1, 500));
        var unknownId = Guid.NewGuid();

        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = Guid.NewGuid() });
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _authRepo.Setup(r => r.GetUserByIdAsync(unknownId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AppUser?)null);

        var result = await CreateService().CreateAndPersistAsync(order, 1, unknownId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.PackingUserNotEligible);
        _packageRepository.Verify(r => r.ReplacePackagesForOrderAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Package>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task CreateAndPersistAsync_WithIneligiblePackingUser_ReturnsPackingUserNotEligible(bool isActive, bool canPack)
    {
        var order = EligibleOrder(("P001", 1, 500));
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

        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = Guid.NewGuid() });
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _authRepo.Setup(r => r.GetUserByIdAsync(packerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ineligible);

        var result = await CreateService().CreateAndPersistAsync(order, 1, packerId, CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.PackingUserNotEligible);
        _packageRepository.Verify(r => r.ReplacePackagesForOrderAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Package>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAndPersistAsync_PersistenceThrows_StillReturnsSuccessfulResult()
    {
        var order = EligibleOrder(("P001", 1, 500));
        var shipmentGuid = Guid.NewGuid();

        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", TrackingNumber = "TRK1" }]);
        _packageRepository.Setup(r => r.ReplacePackagesForOrderAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Package>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("duplicate key"));

        var result = await CreateService().CreateAndPersistAsync(order, 1, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.ShipmentGuid.Should().Be(shipmentGuid);
        result.Labels.Should().HaveCount(1);
        result.Labels[0].TrackingNumber.Should().Be("TRK1");
    }

    [Fact]
    public async Task CreateAndPersistAsync_HappyPath_ReturnsSuccessfulResultWithCarrierAndShipmentInfo()
    {
        var order = EligibleOrder(("P001", 1, 400));
        var shipmentGuid = Guid.NewGuid();

        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "Balíková přeprava PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", TrackingNumber = "TRK1", LabelUrl = "https://c/1.pdf", LabelZpl = "^XA^XZ" }]);

        var result = await CreateService().CreateAndPersistAsync(order, 1, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.ShipmentGuid.Should().Be(shipmentGuid);
        result.CarrierCode.Should().Be("PPL");
        result.CarrierName.Should().Be("Balíková přeprava PPL");
        result.Labels.Should().HaveCount(1);
        result.Labels[0].TrackingNumber.Should().Be("TRK1");
        result.Labels[0].LabelUrl.Should().Be("https://c/1.pdf");
        result.Labels[0].LabelZpl.Should().Be("^XA^XZ");
    }
}
