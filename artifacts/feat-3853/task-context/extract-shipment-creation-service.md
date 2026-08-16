### task: extract-shipment-creation-service

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/Services/IShipmentCreationService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationResult.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/PackagingModule.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Application/Packaging/ShipmentCreationServiceTests.cs`

**Interfaces:**
- Consumes (all pre-existing, unchanged):
  - `Anela.Heblo.Application.Features.ShipmentLabels.IShipmentClient` — `GetShippingOptionsAsync(string orderCode, CancellationToken ct = default)`, `CreateShipmentAsync(CreateShipmentCommand command, CancellationToken ct = default)`, `GetLabelsByOrderCodeAsync(string orderCode, CancellationToken ct = default)`.
  - `Anela.Heblo.Application.Features.ShipmentLabels.ShippingOption { string CarrierCode; string Name; }`, `CreateShipmentCommand { string OrderCode; string CarrierCode; int PackageCount; ShipmentPackage Package; }`, `ShipmentPackage { int WidthCm; int HeightCm; int DepthCm; int WeightGrams; }`, `CreatedShipment { Guid ShipmentGuid; string? Status; }`, `ShipmentLabel { Guid ShipmentGuid; string OrderCode; string PackageName; string? LabelUrl; string? LabelZpl; string? TrackingNumber; string? TrackingUrl; }`.
  - `Anela.Heblo.Application.Features.ShipmentLabels.ShipmentLabelsSettings { int DefaultPackageWidthCm; int DefaultPackageHeightCm; int DefaultPackageDepthCm; int MinPackageWeightGrams; int FallbackPackageWeightGrams; }`.
  - `Anela.Heblo.Domain.Features.Packaging.IPackageRepository.ReplacePackagesForOrderAsync(string orderCode, IReadOnlyCollection<Package> packages, CancellationToken cancellationToken = default)`.
  - `Anela.Heblo.Domain.Features.Packaging.Package { int Id; string OrderCode; string CustomerName; string PackageNumber; string? TrackingNumber; string ShippingProviderCode; string? ShippingProviderName; Guid ShipmentGuid; DateTimeOffset PackedAt; string? PackedBy; Guid? PackedByUserId; DateTimeOffset CreatedAt; }`.
  - `Anela.Heblo.Domain.Features.Authorization.IAuthorizationRepository.GetUserByIdAsync(Guid id, CancellationToken ct = default)` returning `Anela.Heblo.Domain.Features.Authorization.Entities.AppUser? { Guid Id; string Email; string DisplayName; bool IsActive; bool CanPack; ... }`.
  - `Anela.Heblo.Domain.Features.Users.ICurrentUserService.GetCurrentUser()` returning `Anela.Heblo.Domain.Features.Users.CurrentUser(string? Id, string? Name, string? Email, bool IsAuthenticated)`.
  - `Anela.Heblo.Application.Features.ShoptetOrders.PackingOrder { string Code; string CustomerName; ...; List<PackingOrderItem> Items; }`, `PackingOrderItem { string Name; int Quantity; string? ImageUrl; string? SetName; int WeightGrams; }`.
  - `Anela.Heblo.Application.Shared.ErrorCodes` — uses `InvalidPackageCount`, `ShipmentCarrierNotResolved`, `ShipmentCreationFailed`, `PackingUserNotEligible`.
- Produces (consumed by tasks `refactor-scan-handler` and `refactor-reset-handler`):
  - `Anela.Heblo.Application.Features.Packaging.Services.IShipmentCreationService.CreateAndPersistAsync(PackingOrder order, int numberOfPackages, Guid? packingUserId, CancellationToken ct)` returning `Task<ShipmentCreationResult>`.
  - `Anela.Heblo.Application.Features.Packaging.Services.ShipmentCreationResult { bool IsSuccess; ErrorCodes? ErrorCode; Guid ShipmentGuid; string CarrierCode; string? CarrierName; IReadOnlyList<ShipmentLabel> Labels; }`.
  - DI registration: `IShipmentCreationService` resolvable via `AddPackagingModule()`.

- [ ] **Step 1: Create the interface and result type**

Write `backend/src/Anela.Heblo.Application/Features/Packaging/Services/IShipmentCreationService.cs`:

```csharp
using Anela.Heblo.Application.Features.ShoptetOrders;

namespace Anela.Heblo.Application.Features.Packaging.Services;

/// <summary>
/// Owns the shared "resolve weight → resolve carrier → create shipment → fetch/filter/pad
/// labels → resolve packer → persist Package rows" sequence used by both
/// ScanPackingOrderHandler (create path) and ResetOrderShipmentHandler.
/// </summary>
public interface IShipmentCreationService
{
    /// <summary>
    /// Creates a carrier shipment for <paramref name="order"/> and persists the resulting
    /// Package rows. The caller must have already fetched <paramref name="order"/> — this
    /// method never calls IPackingOrderClient itself. <paramref name="packingUserId"/> is
    /// null when no specific packer is being attributed (e.g. always for Reset today).
    /// </summary>
    Task<ShipmentCreationResult> CreateAndPersistAsync(
        PackingOrder order,
        int numberOfPackages,
        Guid? packingUserId,
        CancellationToken ct);
}
```

Write `backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationResult.cs`:

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.Services;

public class ShipmentCreationResult
{
    public bool IsSuccess { get; init; }

    /// <summary>Set when IsSuccess == false.</summary>
    public ErrorCodes? ErrorCode { get; init; }

    public Guid ShipmentGuid { get; init; }

    public string CarrierCode { get; init; } = null!;

    public string? CarrierName { get; init; }

    /// <summary>
    /// Exactly `numberOfPackages` entries: filtered to this shipment's GUID, padded with
    /// null-fields entries where Shoptet hasn't generated a label yet.
    /// </summary>
    public IReadOnlyList<ShipmentLabel> Labels { get; init; } = [];
}
```

- [ ] **Step 2: Create the implementation**

Write `backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationService.cs`:

```csharp
using System.Globalization;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Packaging.Services;

public class ShipmentCreationService : IShipmentCreationService
{
    private const int MaxPackages = 10;

    private readonly IShipmentClient _shipmentClient;
    private readonly IPackageRepository _packageRepository;
    private readonly IAuthorizationRepository _authRepo;
    private readonly ICurrentUserService _currentUserService;
    private readonly ShipmentLabelsSettings _shipmentSettings;
    private readonly ILogger<ShipmentCreationService> _logger;

    public ShipmentCreationService(
        IShipmentClient shipmentClient,
        IPackageRepository packageRepository,
        IAuthorizationRepository authRepo,
        ICurrentUserService currentUserService,
        IOptions<ShipmentLabelsSettings> shipmentSettings,
        ILogger<ShipmentCreationService> logger)
    {
        _shipmentClient = shipmentClient;
        _packageRepository = packageRepository;
        _authRepo = authRepo;
        _currentUserService = currentUserService;
        _shipmentSettings = shipmentSettings.Value;
        _logger = logger;
    }

    public async Task<ShipmentCreationResult> CreateAndPersistAsync(
        PackingOrder order,
        int numberOfPackages,
        Guid? packingUserId,
        CancellationToken ct)
    {
        if (numberOfPackages < 1 || numberOfPackages > MaxPackages)
            return new ShipmentCreationResult { IsSuccess = false, ErrorCode = ErrorCodes.InvalidPackageCount };

        var totalWeightGrams = order.Items.Sum(i => i.WeightGrams * i.Quantity);
        if (totalWeightGrams == 0)
        {
            // Carriers reject a 0 kg package; fall back to a default package weight.
            _logger.LogWarning(
                "Order {OrderCode} has no known item weights; using fallback package weight {Fallback}g",
                order.Code, _shipmentSettings.FallbackPackageWeightGrams);
            totalWeightGrams = _shipmentSettings.FallbackPackageWeightGrams;
        }

        var n = numberOfPackages;
        var perPackageWeightGrams = Math.Max(totalWeightGrams / n, _shipmentSettings.MinPackageWeightGrams);

        var options = await _shipmentClient.GetShippingOptionsAsync(order.Code, ct);
        if (options.Count == 0)
            return new ShipmentCreationResult { IsSuccess = false, ErrorCode = ErrorCodes.ShipmentCarrierNotResolved };

        var command = new CreateShipmentCommand
        {
            OrderCode = order.Code,
            CarrierCode = options[0].CarrierCode,
            PackageCount = n,
            Package = new ShipmentPackage
            {
                WidthCm = _shipmentSettings.DefaultPackageWidthCm,
                HeightCm = _shipmentSettings.DefaultPackageHeightCm,
                DepthCm = _shipmentSettings.DefaultPackageDepthCm,
                WeightGrams = perPackageWeightGrams,
            },
        };

        CreatedShipment createdShipment;
        try
        {
            createdShipment = await _shipmentClient.CreateShipmentAsync(command, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create shipment for order {OrderCode}", order.Code);
            return new ShipmentCreationResult { IsSuccess = false, ErrorCode = ErrorCodes.ShipmentCreationFailed };
        }

        // Single fetch for carrier tracking numbers + label URLs (FE prints directly from the CDN).
        // Shoptet generates labels asynchronously, so the response may contain fewer labels than
        // the requested `n`, and — since a prior shipment for this order may have just been
        // cancelled (Reset) — the fetch can also still return stale labels for the cancelled
        // shipment(s). Filter to this shipment's GUID before padding to exactly `n` entries so
        // the FE shows the correct "X/N" counter without ever mixing in a cancelled shipment's
        // label; packages with no label yet get a null-fields entry (the FE's 404 retry path
        // handles the "carrier not ready" case).
        var fetchedLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(order.Code, ct);
        var matchingLabels = fetchedLabels
            .Where(l => l.ShipmentGuid == createdShipment.ShipmentGuid)
            .ToList();

        var paddedLabels = Enumerable.Range(1, n)
            .Select(i => i <= matchingLabels.Count
                ? matchingLabels[i - 1]
                : new ShipmentLabel
                {
                    ShipmentGuid = createdShipment.ShipmentGuid,
                    OrderCode = order.Code,
                    PackageName = string.Empty,
                })
            .ToList();

        Guid? packedByUserId;
        string? packedBy;
        if (packingUserId is { } requestedPackerId)
        {
            var packer = await _authRepo.GetUserByIdAsync(requestedPackerId, ct);
            if (packer is null || !packer.IsActive || !packer.CanPack)
                return new ShipmentCreationResult { IsSuccess = false, ErrorCode = ErrorCodes.PackingUserNotEligible };

            packedByUserId = packer.Id;
            packedBy = packer.DisplayName;
        }
        else
        {
            packedByUserId = null;
            packedBy = _currentUserService.GetCurrentUser().Email;
        }

        await PersistPackagesAsync(
            order.Code,
            order.CustomerName,
            command.CarrierCode,
            options[0].Name,
            createdShipment.ShipmentGuid,
            paddedLabels,
            packedByUserId,
            packedBy,
            ct);

        return new ShipmentCreationResult
        {
            IsSuccess = true,
            ShipmentGuid = createdShipment.ShipmentGuid,
            CarrierCode = command.CarrierCode,
            CarrierName = options[0].Name,
            Labels = paddedLabels,
        };
    }

    private async Task PersistPackagesAsync(
        string orderCode,
        string customerName,
        string carrierCode,
        string? carrierName,
        Guid shipmentGuid,
        IReadOnlyList<ShipmentLabel> paddedLabels,
        Guid? packedByUserId,
        string? packedBy,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Carrier package names are not unique per package (custom-packaging shipments
        // report the same "Vlastní balení" name for every package), so a 1-based index
        // within the order is used as the unique PackageNumber. The carrier's real
        // identifier is preserved in TrackingNumber. Rows are built from the padded
        // (n-length) list, not the raw fetched-label count, so a package whose label
        // Shoptet hasn't generated yet still gets a row (TrackingNumber = null) that
        // FillTrackingNumbersJob can later backfill.
        var packages = paddedLabels
            .Select((label, index) => new Package
            {
                OrderCode = orderCode,
                CustomerName = customerName,
                PackageNumber = (index + 1).ToString(CultureInfo.InvariantCulture),
                TrackingNumber = label.TrackingNumber,
                ShippingProviderCode = carrierCode,
                ShippingProviderName = carrierName,
                ShipmentGuid = shipmentGuid,
                PackedAt = now,
                PackedBy = packedBy,
                PackedByUserId = packedByUserId,
                CreatedAt = now,
            })
            .ToList();

        try
        {
            await _packageRepository.ReplacePackagesForOrderAsync(orderCode, packages, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to persist {PackageCount} Package row(s) for order {OrderCode} (shipment {ShipmentGuid})",
                packages.Count, orderCode, shipmentGuid);
        }
    }
}
```

- [ ] **Step 3: Register the service in DI**

Read `backend/src/Anela.Heblo.Application/Features/Packaging/PackagingModule.cs`, then edit it:

Change the `using` block at the top from:
```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Packaging.DashboardTiles;
using Anela.Heblo.Application.Features.Packaging.UseCases.GetOrderTrackingNumber;
using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;
using Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;
using Anela.Heblo.Application.Features.Packaging.Validators;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Persistence.Repositories.Packaging;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
```
to:
```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Packaging.DashboardTiles;
using Anela.Heblo.Application.Features.Packaging.Services;
using Anela.Heblo.Application.Features.Packaging.UseCases.GetOrderTrackingNumber;
using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;
using Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;
using Anela.Heblo.Application.Features.Packaging.Validators;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Persistence.Repositories.Packaging;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
```

Change:
```csharp
        // Repository (implementation lives in the Persistence layer)
        services.AddScoped<IPackageRepository, PackageRepository>();
```
to:
```csharp
        // Repository (implementation lives in the Persistence layer)
        services.AddScoped<IPackageRepository, PackageRepository>();

        services.AddScoped<IShipmentCreationService, ShipmentCreationService>();
```

- [ ] **Step 4: Add the module-boundary allowlist entries**

`ShipmentCreationService`'s `CreateAndPersistAsync` takes a `PackingOrder` parameter (owned by
`Anela.Heblo.Application.Features.ShoptetOrders`), and its body iterates `order.Items` (of type
`PackingOrderItem`, same module) inside a lambda (`order.Items.Sum(i => i.WeightGrams * i.Quantity)`).
`backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`'s
`Consumer_types_should_not_reference_provider_owned_namespaces` test enumerates every type whose
namespace starts with `Anela.Heblo.Application.Features.Packaging` — this includes the new
`IShipmentCreationService` interface, the `ShipmentCreationService` class, and any
compiler-generated nested type (e.g. `ShipmentCreationService+<>c`) the C# compiler emits for that
lambda. Without allowlist entries this fails the build's test suite.

Read `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`, then edit the
`PackagingShoptetOrdersAllowlist` initializer. Change:
```csharp
        "Anela.Heblo.Application.Features.Packaging.UseCases.CompletePackingOrder.CompletePackingOrderHandler -> Anela.Heblo.Application.Features.ShoptetOrders.IEshopOrderClient",
    };
```
to:
```csharp
        "Anela.Heblo.Application.Features.Packaging.UseCases.CompletePackingOrder.CompletePackingOrderHandler -> Anela.Heblo.Application.Features.ShoptetOrders.IEshopOrderClient",

        // ShipmentCreationService.CreateAndPersistAsync(PackingOrder order, ...) — the interface
        // method parameter and the class's own implementation both reference PackingOrder.
        "Anela.Heblo.Application.Features.Packaging.Services.IShipmentCreationService -> Anela.Heblo.Application.Features.ShoptetOrders.PackingOrder",
        "Anela.Heblo.Application.Features.Packaging.Services.ShipmentCreationService -> Anela.Heblo.Application.Features.ShoptetOrders.PackingOrder",

        // order.Items.Sum(i => i.WeightGrams * i.Quantity) inside CreateAndPersistAsync compiles
        // to a lambda parameter of type PackingOrderItem, captured in a compiler-generated nested
        // type under ShipmentCreationService; covered via the DeclaringType fallback check below.
        "Anela.Heblo.Application.Features.Packaging.Services.ShipmentCreationService -> Anela.Heblo.Application.Features.ShoptetOrders.PackingOrderItem",
    };
```

- [ ] **Step 5: Build and run the module-boundary test**

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~ModuleBoundariesTests" --logger "console;verbosity=normal"
```

Expected: build succeeds with no errors, and the test run reports `Failed: 0`. If
`Consumer_types_should_not_reference_provider_owned_namespaces` still fails, the failure message
lists the exact missing `"ConsumerType -> ProviderType (via ...)"` strings (e.g. a differently
named compiler-generated type) — add each one verbatim as a new entry in
`PackagingShoptetOrdersAllowlist` with a one-line comment explaining where it comes from, then
re-run this command until it passes.

- [ ] **Step 6: Write `ShipmentCreationServiceTests.cs`**

Write `backend/test/Anela.Heblo.Tests/Application/Packaging/ShipmentCreationServiceTests.cs`:

```csharp
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
```

- [ ] **Step 7: Run the new tests**

```bash
dotnet test --filter "FullyQualifiedName~ShipmentCreationServiceTests" --logger "console;verbosity=normal"
```

Expected: `Failed: 0` (16 test cases: 2 from the `InvalidPackageCount` theory + 2 from the
`IneligiblePackingUser` theory + 12 single-case facts).

- [ ] **Step 8: Full build + full test run**

```bash
dotnet build
dotnet test
```

Expected: build succeeds; the full suite passes (the two existing handlers have not been touched
yet in this task, so their existing tests are unaffected).

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/Services/ \
        backend/src/Anela.Heblo.Application/Features/Packaging/PackagingModule.cs \
        backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs \
        backend/test/Anela.Heblo.Tests/Application/Packaging/ShipmentCreationServiceTests.cs
git commit -m "$(cat <<'EOF'
Extract IShipmentCreationService from ScanPackingOrderHandler/ResetOrderShipmentHandler

New Packaging/Services/ShipmentCreationService owns the shared weight/carrier/shipment-creation/
label-filter-and-pad/packer-resolution/persistence sequence, ready to be consumed by both
handlers. Registered in PackagingModule DI; module-boundary allowlist updated for the new
PackingOrder/PackingOrderItem references.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

