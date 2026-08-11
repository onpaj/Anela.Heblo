# Implementation Plan: Extract shared shipment-creation logic (ScanPackingOrder / ResetOrderShipment)

Source docs: `artifacts/feat-3853/spec.r1.md`, `artifacts/feat-3853/arch-review.r1.md`, `artifacts/feat-3853/design.r1.md`.

## Overview

`ScanPackingOrderHandler` and `ResetOrderShipmentHandler` (both in
`backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/`) each hand-duplicate the
"resolve weight → resolve carrier → create shipment → fetch/filter/pad labels → resolve packer →
persist `Package` rows" block. `ScanPackingOrderHandler` persists `Package` rows for the shipment
it creates; `ResetOrderShipmentHandler` never does (an unrecoverable-until-fixed bug: on every
reset, `Package` rows are left stale for the cancelled shipment and no rows exist for the
replacement one).

This plan extracts that block into a new collaborator, `IShipmentCreationService` /
`ShipmentCreationService`, in a new `Packaging/Services/` folder, and repoints both handlers at
it. Two additional correctness fixes ride along, both required by the arch review and both
inside the extracted block (not new scope):

1. **Label filtering** — the collaborator must filter `GetLabelsByOrderCodeAsync`'s result to
   `label.ShipmentGuid == createdShipment.ShipmentGuid` before padding. `ScanPackingOrderHandler`
   today skips this filter (safe only by accident, since it only runs on the "no existing
   shipment" branch); `ResetOrderShipmentHandler` already does this filter today (since a reset
   just cancelled a prior shipment, and stale labels can still come back from the fetch). The
   collaborator must always filter — adopting Reset's version, not Scan's.
2. **Padded persistence** — the collaborator must persist one `Package` row per index in the
   **padded** `n`-length label list, not per raw fetched-label count. Today
   `ScanPackingOrderHandler.PersistPackagesAsync` persists only `newLabels.Count` rows (dropping
   rows for packages whose Shoptet label hasn't generated yet), which is a second, previously
   undocumented instance of the bug class this feature fixes.

Three tasks, each independently buildable/testable:

1. `extract-shipment-creation-service` — new interface + implementation + DI + module-boundary
   allowlist + its own full unit test suite.
2. `refactor-scan-handler` — `ScanPackingOrderHandler` delegates its create-path to the service;
   its three test files are updated/trimmed/removed accordingly.
3. `refactor-reset-handler` — `ResetOrderShipmentHandler` delegates to the service (this is the
   bug fix — for the first time, Reset calls `IPackageRepository`); its test file is updated with
   a regression test proving the fix.

All commands below assume the working directory is the repository root
(`/Users/rem/orca/workspaces/Anela.Heblo/worktrees/feature-3853-Arch-Review-Packaging-Shipment-Creation-Logic-Is-C`),
which contains `Anela.Heblo.sln`.

---

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

### task: refactor-scan-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs`
- Delete: `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderPackerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Packaging/ScanPackingOrderHandlerPackagePersistenceTests.cs`

**Interfaces:**
- Consumes (produced by task `extract-shipment-creation-service`, already merged):
  - `Anela.Heblo.Application.Features.Packaging.Services.IShipmentCreationService.CreateAndPersistAsync(PackingOrder order, int numberOfPackages, Guid? packingUserId, CancellationToken ct)` returning `Task<ShipmentCreationResult>`.
  - `ShipmentCreationResult { bool IsSuccess; ErrorCodes? ErrorCode; Guid ShipmentGuid; string CarrierCode; string? CarrierName; IReadOnlyList<ShipmentLabel> Labels; }`.
  - DI: `IShipmentCreationService` is registered in `PackagingModule`.
- Produces: `ScanPackingOrderHandler`'s constructor now takes 8 parameters (see Step 1); no
  external caller other than MediatR and the test files in this task construct it directly.

- [ ] **Step 1: Refactor `ScanPackingOrderHandler.cs`**

Read `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`,
then replace its entire contents with:

```csharp
using Anela.Heblo.Application.Features.Packaging.Services;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;

public class ScanPackingOrderHandler : IRequestHandler<ScanPackingOrderRequest, ScanPackingOrderResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly IPackingOrderClient _orderClient;
    private readonly IEshopOrderClient _eshopOrderClient;
    private readonly ILogger<ScanPackingOrderHandler> _logger;
    private readonly IPackageRepository _packageRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuthorizationRepository _authRepo;
    private readonly IShipmentCreationService _shipmentCreationService;

    public ScanPackingOrderHandler(
        IShipmentClient shipmentClient,
        IPackingOrderClient orderClient,
        IEshopOrderClient eshopOrderClient,
        ILogger<ScanPackingOrderHandler> logger,
        IPackageRepository packageRepository,
        ICurrentUserService currentUserService,
        IAuthorizationRepository authRepo,
        IShipmentCreationService shipmentCreationService)
    {
        _shipmentClient = shipmentClient;
        _orderClient = orderClient;
        _eshopOrderClient = eshopOrderClient;
        _logger = logger;
        _packageRepository = packageRepository;
        _currentUserService = currentUserService;
        _authRepo = authRepo;
        _shipmentCreationService = shipmentCreationService;
    }

    public async Task<ScanPackingOrderResponse> Handle(ScanPackingOrderRequest request, CancellationToken ct)
    {
        const int maxPackages = 10;
        if (request.NumberOfPackages < 1 || request.NumberOfPackages > maxPackages)
            return new ScanPackingOrderResponse(ErrorCodes.InvalidPackageCount);

        var order = await _orderClient.GetPackingOrderAsync(request.OrderCode, ct);
        if (order is null)
            return new ScanPackingOrderResponse(ErrorCodes.ShoptetOrderNotFound);

        var isEligible = order.IsEligibleForPacking;
        var orderData = new ScanOrderData
        {
            Code = order.Code,
            CustomerName = order.CustomerName,
            ShippingMethodName = order.ShippingMethodName,
            Cooling = order.Cooling,
            IsCooled = order.IsCooled,
            CustomerNote = order.CustomerNote,
            EshopNote = order.EshopNote,
            ShippingAddress = BuildShippingAddress(order),
            Items = order.Items
                .Select(i => new ScanPackingOrderItemDto
                {
                    Name = i.Name,
                    Quantity = i.Quantity,
                    ImageUrl = i.ImageUrl,
                    SetName = i.SetName,
                })
                .ToList(),
            Eligibility = new ScanOrderEligibility
            {
                IsEligible = isEligible,
            },
        };

        var existingLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        ScanShipmentData? existingShipment = existingLabels.Count > 0
            ? new ScanShipmentData
            {
                ShipmentGuid = existingLabels[0].ShipmentGuid,
                Packages = existingLabels
                    .Select(l => new ScanShipmentPackage
                    {
                        TrackingNumber = l.TrackingNumber,
                        LabelUrl = l.LabelUrl,
                        LabelZpl = l.LabelZpl,
                    })
                    .ToList(),
                AlreadyExisted = true,
            }
            : null;

        if (!isEligible)
        {
            // Already-packed order rescanned for review: include shipment if it exists.
            // Don't mark-as-packed; the order has already moved past the packing state.
            return existingShipment is null
                ? new ScanPackingOrderResponse(orderData)
                : new ScanPackingOrderResponse(orderData, existingShipment);
        }

        if (existingShipment is not null)
        {
            await BackfillExistingShipmentPackagesAsync(
                request.OrderCode, orderData.CustomerName, existingLabels, request.PackingUserId, ct);
            await TryMarkAsPackedAsync(request.OrderCode, ct);
            return new ScanPackingOrderResponse(orderData, existingShipment);
        }

        var result = await _shipmentCreationService.CreateAndPersistAsync(
            order, request.NumberOfPackages, request.PackingUserId, ct);
        if (!result.IsSuccess)
            return new ScanPackingOrderResponse(result.ErrorCode!.Value);

        var packages = result.Labels
            .Select(label => new ScanShipmentPackage
            {
                TrackingNumber = label.TrackingNumber,
                LabelUrl = label.LabelUrl,
                LabelZpl = label.LabelZpl,
            })
            .ToList();

        // The Shoptet "Zabaleno" (52) transition is deferred to the FE, which calls
        // .../packing/complete only after every carrier label is confirmed fetched & printed.
        // A successful CreateAndPersistAsync means Shoptet accepted the request, NOT that a
        // usable label was produced (labels generate asynchronously and can fail). Marking
        // here would move the order to "Zabaleno" even when no label exists. Single- and
        // multi-package orders share this deferred path.
        return new ScanPackingOrderResponse(orderData, new ScanShipmentData
        {
            ShipmentGuid = result.ShipmentGuid,
            Packages = packages,
            AlreadyExisted = false,
            PendingCompletion = true,
        });
    }

    private static ShippingAddress? BuildShippingAddress(PackingOrder order)
    {
        var street = string.IsNullOrEmpty(order.ShippingStreet) ? null : order.ShippingStreet;
        var city = string.IsNullOrEmpty(order.ShippingCity) ? null : order.ShippingCity;
        var zip = string.IsNullOrEmpty(order.ShippingZip) ? null : order.ShippingZip;

        if (street is null && city is null && zip is null)
            return null;

        return new ShippingAddress
        {
            Street = street,
            City = city,
            Zip = zip,
        };
    }

    private async Task TryMarkAsPackedAsync(string orderCode, CancellationToken ct)
    {
        try
        {
            await _eshopOrderClient.MarkAsPackedAsync(orderCode, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to mark order {OrderCode} as packed", orderCode);
        }
    }

    private async Task<(Guid? userId, string? name)> ResolvePackerAsync(Guid? packingUserId, CancellationToken ct)
    {
        if (packingUserId is { } id)
        {
            var user = await _authRepo.GetUserByIdAsync(id, ct);
            if (user is not null)
                return (user.Id, user.DisplayName);
        }
        return (null, _currentUserService.GetCurrentUser().Email);
    }

    /// <summary>
    /// Backfills Package rows for an order whose Shoptet shipment already exists (reprint path).
    /// Idempotent and best-effort: never throws, so a reprint always returns the existing shipment.
    /// </summary>
    private async Task BackfillExistingShipmentPackagesAsync(
        string orderCode,
        string customerName,
        IReadOnlyList<ShipmentLabel> existingLabels,
        Guid? packingUserId,
        CancellationToken cancellationToken)
    {
        if (existingLabels.Count == 0)
            return;

        try
        {
            var options = await _shipmentClient.GetShippingOptionsAsync(orderCode, cancellationToken);
            var carrierCode = options.Count > 0 ? options[0].CarrierCode : string.Empty;
            var carrierName = options.Count > 0 ? options[0].Name : null;

            var now = DateTimeOffset.UtcNow;
            var (packedByUserId, packedBy) = await ResolvePackerAsync(packingUserId, cancellationToken);

            var packages = existingLabels
                .Select(label => new Package
                {
                    OrderCode = orderCode,
                    CustomerName = customerName,
                    PackageNumber = label.PackageName,
                    TrackingNumber = label.TrackingNumber,
                    ShippingProviderCode = carrierCode,
                    ShippingProviderName = carrierName,
                    ShipmentGuid = label.ShipmentGuid,
                    PackedAt = now,
                    PackedBy = packedBy,
                    PackedByUserId = packedByUserId,
                    CreatedAt = now,
                })
                .ToList();

            await _packageRepository.AddMissingAsync(packages, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to backfill Package rows for existing shipment of order {OrderCode}", orderCode);
        }
    }
}
```

Note what changed versus the pre-refactor file: the `IOptions<ShipmentLabelsSettings>` constructor
parameter and `System.Globalization` using are gone (both were only needed by the now-removed
inline weight/carrier/creation/label/persistence block); `IShipmentCreationService` is added; the
"no existing shipment, eligible order" branch (formerly ~80 lines) is now the
`_shipmentCreationService.CreateAndPersistAsync(...)` call plus response mapping; the
`PersistPackagesAsync` private method is deleted entirely (fully absorbed into the service).
`BuildShippingAddress`, `TryMarkAsPackedAsync`, `ResolvePackerAsync`, and
`BackfillExistingShipmentPackagesAsync` are unchanged (the backfill/reprint path stays out of
scope per the spec).

- [ ] **Step 2: Build to catch compile errors early**

```bash
dotnet build
```

Expected: fails at this point only in the test projects (their old constructor calls no longer
match) — the `Anela.Heblo.Application` project itself must build clean. If
`Anela.Heblo.Application` itself fails to build, stop and fix `ScanPackingOrderHandler.cs` before
continuing.

- [ ] **Step 3: Rewrite `ScanPackingOrderHandlerTests.cs`**

Read `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs`, then
replace its entire contents with:

```csharp
using Anela.Heblo.Application.Features.Packaging.Services;
using Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.Packaging;

public class ScanPackingOrderHandlerTests
{
    private readonly Mock<IShipmentClient> _shipmentClient = new();
    private readonly Mock<IPackingOrderClient> _orderClient = new();
    private readonly Mock<IEshopOrderClient> _eshopOrderClient = new();
    private readonly Mock<IPackageRepository> _packageRepository = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly Mock<IAuthorizationRepository> _authRepo = new();
    private readonly Mock<IShipmentCreationService> _shipmentCreationService = new();

    private ScanPackingOrderHandler CreateHandler()
    {
        _currentUserService.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("uid-1", "Operator", "op@example.com", IsAuthenticated: true));
        return new(
            _shipmentClient.Object,
            _orderClient.Object,
            _eshopOrderClient.Object,
            new Mock<ILogger<ScanPackingOrderHandler>>().Object,
            _packageRepository.Object,
            _currentUserService.Object,
            _authRepo.Object,
            _shipmentCreationService.Object);
    }

    private static PackingOrder EligibleOrder(params (string name, int qty, int weightGrams)[] items) =>
        new()
        {
            Code = "0001234",
            StatusId = 26,
            IsEligibleForPacking = true,
            Items = items.Select(i => new PackingOrderItem
            {
                Name = i.name,
                Quantity = i.qty,
                WeightGrams = i.weightGrams,
            }).ToList(),
        };

    private static ShipmentCreationResult SuccessResult(Guid shipmentGuid, params ShipmentLabel[] labels) =>
        new()
        {
            IsSuccess = true,
            ShipmentGuid = shipmentGuid,
            CarrierCode = "PPL",
            CarrierName = "PPL",
            Labels = labels,
        };

    // Test 1: Order not found → ErrorCodes.ShoptetOrderNotFound
    [Fact]
    public async Task Handle_OrderNotFound_ReturnsShoptetOrderNotFound()
    {
        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackingOrder?)null);

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShoptetOrderNotFound);
    }

    // Test 2: Order in wrong state, no existing labels → ineligible response with no shipment
    [Fact]
    public async Task Handle_OrderNotInPackingState_WithoutExistingLabels_ReturnsIneligibleWithNoShipment()
    {
        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackingOrder { Code = "0001234", StatusId = 99, IsEligibleForPacking = false });
        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Order.Should().NotBeNull();
        response.Order!.Eligibility.IsEligible.Should().BeFalse();
        response.Shipment.Should().BeNull();

        _shipmentCreationService.Verify(
            s => s.CreateAndPersistAsync(It.IsAny<PackingOrder>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Test 2b: Order in wrong state but already has labels → ineligible response WITH shipment for review
    [Fact]
    public async Task Handle_OrderNotInPackingState_WithExistingLabels_ReturnsIneligibleWithShipment_AndDoesNotMarkPacked()
    {
        var shipmentGuid = Guid.NewGuid();
        var existingLabel = new ShipmentLabel
        {
            ShipmentGuid = shipmentGuid,
            OrderCode = "0001234",
            PackageName = "P1",
            TrackingNumber = "TRK-1",
            LabelUrl = "https://example.com/label.pdf",
        };

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackingOrder { Code = "0001234", StatusId = 99, IsEligibleForPacking = false });
        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingLabel]);

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Order!.Eligibility.IsEligible.Should().BeFalse();
        response.Shipment.Should().NotBeNull();
        response.Shipment!.AlreadyExisted.Should().BeTrue();
        response.Shipment.ShipmentGuid.Should().Be(shipmentGuid);
        response.Shipment.Packages.Should().ContainSingle()
            .Which.TrackingNumber.Should().Be("TRK-1");

        _shipmentCreationService.Verify(
            s => s.CreateAndPersistAsync(It.IsAny<PackingOrder>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _eshopOrderClient.Verify(
            c => c.MarkAsPackedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Test 3: Labels already exist on eligible order → return existing shipment without creating
    [Fact]
    public async Task Handle_LabelsExist_ReturnsExistingShipmentWithAlreadyExistedTrue()
    {
        var shipmentGuid = Guid.NewGuid();
        var existingLabel = new ShipmentLabel
        {
            ShipmentGuid = shipmentGuid,
            OrderCode = "0001234",
            PackageName = "P1",
            TrackingNumber = "TRK-P1",
            LabelUrl = "https://example.com/label.pdf",
            LabelZpl = "^XA...^XZ",
        };

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 400)));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingLabel]);

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Order!.Eligibility.IsEligible.Should().BeTrue();
        response.Shipment.Should().NotBeNull();
        response.Shipment!.AlreadyExisted.Should().BeTrue();
        response.Shipment.ShipmentGuid.Should().Be(shipmentGuid);
        response.Shipment.Packages.Should().HaveCount(1);
        response.Shipment.Packages[0].TrackingNumber.Should().Be("TRK-P1");
        response.Shipment.Packages[0].LabelUrl.Should().Be("https://example.com/label.pdf");
        response.Shipment.Packages[0].LabelZpl.Should().Be("^XA...^XZ");

        _shipmentCreationService.Verify(
            s => s.CreateAndPersistAsync(It.IsAny<PackingOrder>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Test 4: Eligible order, no existing shipment → delegates to IShipmentCreationService and maps its result
    [Fact]
    public async Task Handle_NoExistingShipment_CreatesNewShipmentWithAlreadyExistedFalse()
    {
        var shipmentGuid = Guid.NewGuid();

        var order = new PackingOrder
        {
            Code = "0001234",
            StatusId = 26,
            IsEligibleForPacking = true,
            Items = new List<PackingOrderItem>
            {
                new() { Name = "P001", Quantity = 1, WeightGrams = 400 },
            },
            ShippingStreet = "Hlavní 123",
            ShippingCity = "Praha",
            ShippingZip = "110 00",
        };

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult(shipmentGuid,
                new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://carrier.example.com/new-label.pdf" }));

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Order!.Eligibility.IsEligible.Should().BeTrue();
        response.Shipment.Should().NotBeNull();
        response.Shipment!.AlreadyExisted.Should().BeFalse();
        response.Shipment.ShipmentGuid.Should().Be(shipmentGuid);

        response.Shipment.Packages[0].LabelUrl.Should().Be("https://carrier.example.com/new-label.pdf");
        response.Shipment.Packages[0].LabelZpl.Should().BeNull();

        response.Order!.ShippingAddress.Should().NotBeNull();
        response.Order.ShippingAddress!.Street.Should().Be("Hlavní 123");
        response.Order.ShippingAddress.City.Should().Be("Praha");
        response.Order.ShippingAddress.Zip.Should().Be("110 00");
    }

    // Shipping address: when source has no address, response.Order.ShippingAddress is null
    [Fact]
    public async Task Handle_OrderWithoutShippingAddress_ReturnsNullShippingAddress()
    {
        var shipmentGuid = Guid.NewGuid();

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 400)));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://example.com/label.pdf" }]);

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Order.Should().NotBeNull();
        response.Order!.ShippingAddress.Should().BeNull();
    }

    // MarkAsPackedAsync: called when existing shipment found and order is eligible
    [Fact]
    public async Task Handle_LabelsExist_MarksOrderAsPacked()
    {
        var shipmentGuid = Guid.NewGuid();

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 400)));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://example.com/label.pdf" }]);

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        _eshopOrderClient.Verify(
            c => c.MarkAsPackedAsync("0001234", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // New single-package shipment: the "Zabaleno" transition is DEFERRED to the FE
    // (PendingCompletion = true) and NOT marked at scan time — the carrier label is
    // generated asynchronously and may not exist yet.
    [Fact]
    public async Task Handle_NewSinglePackageShipment_DefersMarkAsPacked()
    {
        var shipmentGuid = Guid.NewGuid();
        var order = EligibleOrder(("P001", 1, 400));

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult(shipmentGuid,
                new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://carrier.example.com/new-label.pdf" }));

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment!.PendingCompletion.Should().BeTrue();
        _eshopOrderClient.Verify(
            c => c.MarkAsPackedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // MarkAsPackedAsync: failure is non-fatal — scan still returns success
    [Fact]
    public async Task Handle_MarkAsPackedFails_StillReturnsSuccessfulScanResponse()
    {
        var shipmentGuid = Guid.NewGuid();

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibleOrder(("P001", 1, 400)));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://example.com/label.pdf" }]);

        _eshopOrderClient
            .Setup(c => c.MarkAsPackedAsync("0001234", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet status update failed"));

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment.Should().NotBeNull();
    }

    // MarkAsPackedAsync: NOT called when order is ineligible
    [Fact]
    public async Task Handle_OrderNotInPackingState_DoesNotMarkAsPacked()
    {
        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackingOrder { Code = "0001234", StatusId = 99, IsEligibleForPacking = false });

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        _eshopOrderClient.Verify(
            c => c.MarkAsPackedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void ScanPackingOrderItemDto_HasExactlyTheFourPublicFields_AndNoWeightGrams()
    {
        var properties = typeof(ScanPackingOrderItemDto)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        properties.Should().BeEquivalentTo(new[] { "Name", "Quantity", "ImageUrl", "SetName" },
            "ScanPackingOrderItemDto must not expose internal fields such as WeightGrams to API clients.");
        typeof(ScanPackingOrderItemDto).GetProperty("WeightGrams").Should().BeNull();
    }

    [Fact]
    public void InternalPackingOrderItem_StillExposesWeightGrams_ForShipmentMath()
    {
        // Anchor the symmetric guarantee: WeightGrams must remain on the internal adapter
        // contract because ShipmentCreationService depends on it.
        typeof(PackingOrderItem).GetProperty("WeightGrams").Should().NotBeNull(
            "PackingOrderItem is the internal Application contract and ShipmentCreationService reads WeightGrams.");
    }

    // Multi-package: out-of-range count is rejected before any work
    [Fact]
    public async Task Handle_NumberOfPackagesAboveMax_ReturnsInvalidPackageCount()
    {
        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234", NumberOfPackages = 11 },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidPackageCount);
        _orderClient.Verify(
            c => c.GetPackingOrderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Multi-package: NumberOfPackages and PackingUserId are forwarded to the shared service, and
    // the response reflects however many labels the service returns; MarkAsPacked stays deferred.
    [Fact]
    public async Task Handle_MultiPackage_PassesNumberOfPackagesAndPackerToService_AndDefersMarkAsPacked()
    {
        var shipmentGuid = Guid.NewGuid();
        var packerId = Guid.NewGuid();
        var order = EligibleOrder(("P001", 1, 900));

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 3, packerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult(shipmentGuid,
                new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P1", LabelUrl = "https://c/1.pdf" },
                new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P2", LabelUrl = "https://c/2.pdf" },
                new ShipmentLabel { ShipmentGuid = shipmentGuid, OrderCode = "0001234", PackageName = "P3", LabelUrl = "https://c/3.pdf" }));

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234", NumberOfPackages = 3, PackingUserId = packerId },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment!.PendingCompletion.Should().BeTrue();
        response.Shipment.Packages.Should().HaveCount(3);
        response.Shipment.Packages[0].LabelUrl.Should().Be("https://c/1.pdf");

        _shipmentCreationService.Verify(
            s => s.CreateAndPersistAsync(order, 3, packerId, It.IsAny<CancellationToken>()),
            Times.Once);
        _eshopOrderClient.Verify(
            c => c.MarkAsPackedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Error mapping: whatever error code the shared service returns is surfaced unchanged.
    [Theory]
    [InlineData(ErrorCodes.ShipmentCarrierNotResolved)]
    [InlineData(ErrorCodes.ShipmentCreationFailed)]
    [InlineData(ErrorCodes.PackingUserNotEligible)]
    public async Task Handle_WhenShipmentCreationServiceFails_ReturnsMappedErrorCode(ErrorCodes errorCode)
    {
        var order = EligibleOrder(("P001", 1, 400));

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShipmentCreationResult { IsSuccess = false, ErrorCode = errorCode });

        var response = await CreateHandler().Handle(
            new ScanPackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(errorCode);
    }
}
```

Coverage note: `Handle_AllItemsHaveZeroWeight_UsesFallbackPackageWeight`,
`Handle_NoShippingOptions_ReturnsShipmentCarrierNotResolved`,
`Handle_CreateShipmentThrows_ReturnsShipmentCreationFailed`,
`Handle_MultiPackage_ShoptetReturnsFewerLabelsThanRequested_ResponseHasNPackages`, and
`Handle_MultiPackage_FloorsPerPackageWeightAtMinimum` from the pre-refactor file are intentionally
removed here — that logic now lives in `ShipmentCreationService`, and each has a direct
counterpart already added in `ShipmentCreationServiceTests.cs` (task
`extract-shipment-creation-service`, Step 6).

- [ ] **Step 4: Delete `ScanPackingOrderPackerTests.cs`**

Its three concerns — packer resolution with an explicit `packingUserId` (valid, unknown,
ineligible-inactive, ineligible-cannot-pack) and the null-`packingUserId` fallback to the current
user's email — are now `ShipmentCreationService` behavior, already covered by
`CreateAndPersistAsync_WithPackingUserId_StampsUserIdAndDisplayName`,
`CreateAndPersistAsync_WithNullPackingUserId_FallsBackToCurrentUserEmail`,
`CreateAndPersistAsync_WithUnknownPackingUserId_ReturnsPackingUserNotEligible`, and
`CreateAndPersistAsync_WithIneligiblePackingUser_ReturnsPackingUserNotEligible` in
`ShipmentCreationServiceTests.cs`.

```bash
rm backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderPackerTests.cs
```

- [ ] **Step 5: Trim `ScanPackingOrderHandlerPackagePersistenceTests.cs` to backfill-only coverage**

`BackfillExistingShipmentPackagesAsync` (the reprint path, using `IPackageRepository.AddMissingAsync`)
stays in `ScanPackingOrderHandler` — out of scope for this feature per the spec — so its three
tests stay in this file. The two tests that exercised the (now-removed) inline create-path
persistence (`Handle_PersistsOnePackageRowPerCreatedLabel_WithSequentialPackageNumbers`,
`Handle_DoesNotFailScan_WhenPersistenceThrows`) are dropped — their coverage now lives in
`ShipmentCreationServiceTests.cs`
(`CreateAndPersistAsync_LabelsShareSameCarrierPackageName_StillProducesSequentialPackageNumbers`,
`CreateAndPersistAsync_PersistenceThrows_StillReturnsSuccessfulResult`).

Read `backend/test/Anela.Heblo.Tests/Features/Packaging/ScanPackingOrderHandlerPackagePersistenceTests.cs`,
then replace its entire contents with:

```csharp
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
```

- [ ] **Step 6: Run Scan-related tests**

```bash
dotnet test --filter "FullyQualifiedName~ScanPackingOrder" --logger "console;verbosity=normal"
```

Expected: `Failed: 0` (covers `ScanPackingOrderHandlerTests` and
`ScanPackingOrderHandlerPackagePersistenceTests`; `ScanPackingOrderPackerTests` no longer exists).

- [ ] **Step 7: Full build + full test run**

```bash
dotnet build
dotnet test
```

Expected: build succeeds; `Failed: 0` across the whole suite (Reset's handler/tests are untouched
until the next task, so they remain green as before).

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs \
        backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Packaging/ScanPackingOrderHandlerPackagePersistenceTests.cs
git rm backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderPackerTests.cs
git commit -m "$(cat <<'EOF'
Refactor ScanPackingOrderHandler to delegate shipment creation to IShipmentCreationService

The "no existing shipment, eligible order" branch now calls the shared
IShipmentCreationService.CreateAndPersistAsync instead of its own inline weight/carrier/
creation/label/persistence logic. Eligibility check, existing-shipment reprint/backfill path,
and deferred TryMarkAsPackedAsync semantics are unchanged. Test coverage for the extracted
branches (zero-weight fallback, carrier resolution, label padding, packer resolution) moved to
ShipmentCreationServiceTests.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### task: refactor-reset-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Application/Packaging/ResetOrderShipmentHandlerTests.cs`

**Interfaces:**
- Consumes (produced by task `extract-shipment-creation-service`, already merged):
  - `Anela.Heblo.Application.Features.Packaging.Services.IShipmentCreationService.CreateAndPersistAsync(PackingOrder order, int numberOfPackages, Guid? packingUserId, CancellationToken ct)` returning `Task<ShipmentCreationResult>`.
  - `ShipmentCreationResult { bool IsSuccess; ErrorCodes? ErrorCode; Guid ShipmentGuid; string CarrierCode; string? CarrierName; IReadOnlyList<ShipmentLabel> Labels; }`.
  - `Anela.Heblo.Application.Features.Packaging.Services.ShipmentCreationService` concrete class
    constructor: `ShipmentCreationService(IShipmentClient, IPackageRepository, IAuthorizationRepository, ICurrentUserService, IOptions<ShipmentLabelsSettings>, ILogger<ShipmentCreationService>)`
    (used directly, not mocked, by the FR-3 regression test in Step 3 below).
- Produces: `ResetOrderShipmentHandler`'s constructor now takes 4 parameters (see Step 1); this is
  the last consumer to change, so after this task the feature is complete.

- [ ] **Step 1: Refactor `ResetOrderShipmentHandler.cs`**

Read `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentHandler.cs`,
then replace its entire contents with:

```csharp
using Anela.Heblo.Application.Features.Packaging.Services;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ResetOrderShipment;

public class ResetOrderShipmentHandler : IRequestHandler<ResetOrderShipmentRequest, ResetOrderShipmentResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly IPackingOrderClient _orderClient;
    private readonly IShipmentCreationService _shipmentCreationService;
    private readonly ILogger<ResetOrderShipmentHandler> _logger;

    public ResetOrderShipmentHandler(
        IShipmentClient shipmentClient,
        IPackingOrderClient orderClient,
        IShipmentCreationService shipmentCreationService,
        ILogger<ResetOrderShipmentHandler> logger)
    {
        _shipmentClient = shipmentClient;
        _orderClient = orderClient;
        _shipmentCreationService = shipmentCreationService;
        _logger = logger;
    }

    public async Task<ResetOrderShipmentResponse> Handle(ResetOrderShipmentRequest request, CancellationToken ct)
    {
        const int maxPackages = 10;
        if (request.NumberOfPackages < 1 || request.NumberOfPackages > maxPackages)
            return new ResetOrderShipmentResponse(ErrorCodes.InvalidPackageCount);

        var existingLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, ct);
        if (existingLabels.Count == 0)
            return new ResetOrderShipmentResponse(ErrorCodes.NoShipmentToReset);

        var shipmentGuids = existingLabels
            .Select(l => l.ShipmentGuid)
            .Distinct()
            .ToList();

        foreach (var shipmentGuid in shipmentGuids)
        {
            try
            {
                await _shipmentClient.CancelShipmentAsync(shipmentGuid, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel shipment {ShipmentGuid} for order {OrderCode}",
                    shipmentGuid, request.OrderCode);
                return new ResetOrderShipmentResponse(ErrorCodes.ShipmentCancelFailed);
            }
        }

        var order = await _orderClient.GetPackingOrderAsync(request.OrderCode, ct);
        if (order is null)
            return new ResetOrderShipmentResponse(ErrorCodes.ShoptetOrderNotFound);

        // Reset never supplies an explicit packer today (ResetOrderShipmentRequest has no
        // PackingUserId field) — the shared service falls back to the current user's email.
        var result = await _shipmentCreationService.CreateAndPersistAsync(order, request.NumberOfPackages, null, ct);
        if (!result.IsSuccess)
            return new ResetOrderShipmentResponse(result.ErrorCode!.Value);

        var packages = result.Labels
            .Select(label => new ResetShipmentPackage
            {
                TrackingNumber = label.TrackingNumber,
                LabelUrl = label.LabelUrl,
                LabelZpl = label.LabelZpl,
            })
            .ToList();

        return new ResetOrderShipmentResponse(new ResetShipmentData
        {
            ShipmentGuid = result.ShipmentGuid,
            Packages = packages,
            PendingCompletion = request.NumberOfPackages >= 2,
        });
    }
}
```

This is the bug fix (FR-3): `CreateAndPersistAsync` always calls
`IPackageRepository.ReplacePackagesForOrderAsync` internally (see
`ShipmentCreationService.PersistPackagesAsync` in task `extract-shipment-creation-service`), so
for the first time `ResetOrderShipmentHandler` causes `Package` rows to be written — with
`ShipmentGuid` equal to the **new** shipment's GUID — and `ReplacePackagesForOrderAsync`'s
delete-then-insert-per-order-code semantics clear the stale rows left by the cancelled
shipment(s) in the same operation.

- [ ] **Step 2: Build to catch compile errors early**

```bash
dotnet build
```

Expected: `Anela.Heblo.Application` builds clean; only the test project fails at this point
(its old constructor call no longer matches) until Step 3 is done.

- [ ] **Step 3: Rewrite `ResetOrderShipmentHandlerTests.cs`**

Read `backend/test/Anela.Heblo.Tests/Application/Packaging/ResetOrderShipmentHandlerTests.cs`,
then replace its entire contents with:

```csharp
using Anela.Heblo.Application.Features.Packaging.Services;
using Anela.Heblo.Application.Features.Packaging.UseCases.ResetOrderShipment;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.Packaging;

public class ResetOrderShipmentHandlerTests
{
    private readonly Mock<IShipmentClient> _shipmentClient = new();
    private readonly Mock<IPackingOrderClient> _orderClient = new();
    private readonly Mock<IShipmentCreationService> _shipmentCreationService = new();

    private ResetOrderShipmentHandler CreateHandler(IShipmentCreationService? shipmentCreationService = null) =>
        new(
            _shipmentClient.Object,
            _orderClient.Object,
            shipmentCreationService ?? _shipmentCreationService.Object,
            new Mock<ILogger<ResetOrderShipmentHandler>>().Object);

    private static PackingOrder EligibleOrder(params (string name, int qty, int weightGrams)[] items) =>
        new()
        {
            Code = "0001234",
            CustomerName = "Alice",
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

    private static ShipmentCreationResult SuccessResult(Guid shipmentGuid, params ShipmentLabel[] labels) =>
        new()
        {
            IsSuccess = true,
            ShipmentGuid = shipmentGuid,
            CarrierCode = "PPL",
            CarrierName = "PPL",
            Labels = labels,
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

    // Test 3: Happy path — cancels old shipment, delegates to the shared service, maps its result
    [Fact]
    public async Task Handle_HappyPath_CancelsOldAndDelegatesToShipmentCreationService()
    {
        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();
        var order = EligibleOrder(("P001", 1, 400));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid)]);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult(newGuid,
                MakeLabel(newGuid, "NEW-P1", "https://carrier.example.com/new-label.pdf", "^XA-NEW^XZ", "TRK-NEW-1")));

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment.Should().NotBeNull();
        response.Shipment!.ShipmentGuid.Should().Be(newGuid);
        response.Shipment.Packages.Should().HaveCount(1);
        response.Shipment.Packages[0].TrackingNumber.Should().Be("TRK-NEW-1");
        response.Shipment.Packages[0].LabelUrl.Should().Be("https://carrier.example.com/new-label.pdf");
        response.Shipment.Packages[0].LabelZpl.Should().Be("^XA-NEW^XZ");
        response.Shipment.PendingCompletion.Should().BeFalse();

        _shipmentClient.Verify(
            c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()),
            Times.Once);
        _shipmentCreationService.Verify(
            s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Test 4: Shared service reports failure after a successful cancel → error code is surfaced unchanged
    [Theory]
    [InlineData(ErrorCodes.ShipmentCarrierNotResolved)]
    [InlineData(ErrorCodes.ShipmentCreationFailed)]
    public async Task Handle_WhenShipmentCreationServiceFailsAfterSuccessfulCancel_ReturnsMappedErrorCode(ErrorCodes errorCode)
    {
        var oldGuid = Guid.NewGuid();
        var order = EligibleOrder(("P001", 1, 400));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid)]);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShipmentCreationResult { IsSuccess = false, ErrorCode = errorCode });

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(errorCode);

        _shipmentClient.Verify(
            c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Test 7: Multiple distinct shipment GUIDs → each is cancelled before the shared service is called
    [Fact]
    public async Task Handle_MultipleShipments_CancelsAllBeforeCreating()
    {
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var newGuid = Guid.NewGuid();
        var order = EligibleOrder(("P001", 1, 400));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(guid1, "P1"), MakeLabel(guid2, "P2")]);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult(newGuid, MakeLabel(newGuid, "NEW-P1")));

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
        var order = EligibleOrder(("P001", 1, 400));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(sharedGuid, "P1"), MakeLabel(sharedGuid, "P2")]);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(sharedGuid, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult(newGuid, MakeLabel(newGuid, "NEW-P1")));

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();

        _shipmentClient.Verify(
            c => c.CancelShipmentAsync(sharedGuid, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Test 9: Second of two cancels fails → ShipmentCancelFailed, shared service never called
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

        _shipmentCreationService.Verify(
            s => s.CreateAndPersistAsync(It.IsAny<PackingOrder>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Test 6: Cancel returns silently (404 treated as success inside client) → handler still delegates to the shared service
    [Fact]
    public async Task Handle_CancelReturnsSilently_ProceedsToCreate()
    {
        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();
        var order = EligibleOrder(("P001", 1, 400));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid)]);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult(newGuid, MakeLabel(newGuid, "NEW-P1")));

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment!.ShipmentGuid.Should().Be(newGuid);

        _shipmentClient.Verify(
            c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()),
            Times.Once);
        _shipmentCreationService.Verify(
            s => s.CreateAndPersistAsync(order, 1, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Multi-package recreate: NumberOfPackages flows into the shared service call, and
    // PendingCompletion is true for n >= 2 (handler-owned mapping, unrelated to the service).
    [Fact]
    public async Task Handle_MultiPackage_PassesNumberOfPackagesToService_AndSetsPendingCompletion()
    {
        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();
        var order = EligibleOrder(("P001", 1, 900));

        _shipmentClient
            .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid)]);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _shipmentCreationService
            .Setup(s => s.CreateAndPersistAsync(order, 3, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult(newGuid,
                MakeLabel(newGuid, "NEW-P1", "https://c/1.pdf", trackingNumber: "TRK-1"),
                MakeLabel(newGuid, "NEW-P2", null),
                MakeLabel(newGuid, "NEW-P3", null)));

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234", NumberOfPackages = 3 },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment!.Packages.Should().HaveCount(3);
        response.Shipment.Packages[0].TrackingNumber.Should().Be("TRK-1");
        response.Shipment.PendingCompletion.Should().BeTrue();

        _shipmentCreationService.Verify(
            s => s.CreateAndPersistAsync(order, 3, null, It.IsAny<CancellationToken>()),
            Times.Once);
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

    // FR-3 regression guard: reset must persist Package rows for the NEW shipment (the original
    // bug was that ResetOrderShipmentHandler never called IPackageRepository at all). This test
    // wires the REAL ShipmentCreationService (not a mock) into the handler so the assertion
    // exercises the actual handler -> service -> repository call chain, not just "the handler
    // called some mock" — this is the primary guard against the bug recurring.
    [Fact]
    public async Task Handle_HappyPath_PersistsPackageRowsForNewShipment_ThroughRealShipmentCreationService()
    {
        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();

        _shipmentClient
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid)])
            .ReturnsAsync([MakeLabel(newGuid, "NEW-P1", "https://carrier.example.com/new-label.pdf", trackingNumber: "TRK-NEW-1")]);

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

        var packageRepository = new Mock<IPackageRepository>();
        IReadOnlyCollection<Package>? persistedPackages = null;
        string? persistedOrderCode = null;
        packageRepository
            .Setup(r => r.ReplacePackagesForOrderAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Package>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyCollection<Package>, CancellationToken>((orderCode, packages, _) =>
            {
                persistedOrderCode = orderCode;
                persistedPackages = packages;
            })
            .Returns(Task.CompletedTask);

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("uid-1", "Operator", "op@example.com", IsAuthenticated: true));

        var realShipmentCreationService = new ShipmentCreationService(
            _shipmentClient.Object,
            packageRepository.Object,
            new Mock<IAuthorizationRepository>().Object,
            currentUserService.Object,
            Options.Create(new ShipmentLabelsSettings
            {
                DefaultPackageWidthCm = 30,
                DefaultPackageHeightCm = 20,
                DefaultPackageDepthCm = 15,
                MinPackageWeightGrams = 100,
            }),
            new Mock<ILogger<ShipmentCreationService>>().Object);

        var response = await CreateHandler(realShipmentCreationService).Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234", NumberOfPackages = 1 },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment!.ShipmentGuid.Should().Be(newGuid);

        persistedOrderCode.Should().Be("0001234");
        persistedPackages.Should().NotBeNull();
        persistedPackages!.Count.Should().Be(1);
        persistedPackages.Should().OnlyContain(p => p.ShipmentGuid == newGuid);
        persistedPackages.First().TrackingNumber.Should().Be("TRK-NEW-1");

        packageRepository.Verify(
            r => r.ReplacePackagesForOrderAsync("0001234", It.IsAny<IReadOnlyCollection<Package>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

Coverage note: `Handle_ZeroWeightAfterCancel_UsesFallbackPackageWeight` and
`Handle_EventualConsistency_SecondCallReturnsBothOldAndNew_OnlyNewPackagesInResponse` from the
pre-refactor file are intentionally removed — their logic (weight fallback; filtering stale
labels by shipment GUID) now lives in `ShipmentCreationService`, already covered by
`CreateAndPersistAsync_AllItemsHaveZeroWeight_UsesFallbackPackageWeight` and
`CreateAndPersistAsync_FetchedLabelsIncludeStaleShipment_FiltersToNewShipmentGuidOnly` in
`ShipmentCreationServiceTests.cs` (task `extract-shipment-creation-service`, Step 6).

- [ ] **Step 4: Run Reset-related tests**

```bash
dotnet test --filter "FullyQualifiedName~ResetOrderShipmentHandlerTests" --logger "console;verbosity=normal"
```

Expected: `Failed: 0` (13 test cases: 1 `Theory` with 2 cases +  11 single-case facts).

- [ ] **Step 5: Full build, full test run, and format check**

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
```

Expected: build succeeds; `Failed: 0` across the entire suite (this is the final task — Scan,
Reset, and the new service are all wired together now); `dotnet format --verify-no-changes`
reports no formatting violations. If it reports violations, run `dotnet format` (without
`--verify-no-changes`) and re-run the verify command.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentHandler.cs \
        backend/test/Anela.Heblo.Tests/Application/Packaging/ResetOrderShipmentHandlerTests.cs
git commit -m "$(cat <<'EOF'
Fix ResetOrderShipmentHandler to persist Package rows via IShipmentCreationService

Reset now delegates to the shared IShipmentCreationService.CreateAndPersistAsync after
cancelling the prior shipment(s), instead of its own inline duplicate of the create-shipment
block. This is the bug fix: for the first time, reset causes IPackageRepository.
ReplacePackagesForOrderAsync to run for the order, with rows carrying the new shipment's GUID
and clearing the stale rows left by the cancelled shipment(s) (delete-then-insert semantics).
A new regression test constructs the real ShipmentCreationService (not mocked) to prove the
handler -> service -> repository chain actually persists on a successful reset.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (extract shared collaborator, all 7 numbered steps + Decision-4/5 amendments) →
  `extract-shipment-creation-service`, Steps 1–2 (interface/result/impl), pinned by
  `ShipmentCreationServiceTests` (Step 6: label-filter test, padded-persistence test, sequential
  PackageNumber test, invalid-count test, zero-weight test, weight-floor test, carrier-not-resolved
  test, creation-failed test, packer-resolution tests ×4, persistence-swallow test, happy-path test).
- FR-2 (Scan uses the collaborator, all other behavior unchanged) → `refactor-scan-handler`
  Step 1 (refactored `Handle`), Step 3 (rewritten tests preserving eligibility/backfill/
  deferred-mark-as-packed/DTO-shape coverage).
- FR-3 (Reset uses the collaborator, persists Package rows — the bug fix) →
  `refactor-reset-handler` Step 1 (refactored `Handle`), Step 3
  (`Handle_HappyPath_PersistsPackageRowsForNewShipment_ThroughRealShipmentCreationService` — the
  literal FR-3 regression-test acceptance criterion, using the real service, asserting order code,
  package count, and new-shipment GUID).
- FR-4 (packer attribution on reset: null-safe, no request/DTO change, eligibility gate
  conditioned on non-null `packingUserId`) → `ShipmentCreationService`'s
  `if (packingUserId is { } requestedPackerId) { ... } else { packedByUserId = null; ... }`
  branch (extract task, Step 2); `ResetOrderShipmentHandler` always passes `null` (refactor-reset
  task, Step 1); `ResetOrderShipmentRequest` is untouched (not in any task's file list).
- FR-5 (persistence failure swallowed, structured log fields, both callers) → `PersistPackagesAsync`
  in the service catches and logs `OrderCode`/`ShipmentGuid`/`PackageCount` (extract task, Step 2),
  pinned by `CreateAndPersistAsync_PersistenceThrows_StillReturnsSuccessfulResult` (Step 6).
- FR-6 (DI wiring, both handlers depend on the new service) → `PackagingModule.cs` edit (extract
  task, Step 3); both handlers' constructors (refactor tasks, Step 1).
- NFR-1 (no added external calls; service accepts already-fetched `PackingOrder`) — both handlers
  fetch `order` exactly once, before calling the service; the service never calls
  `IPackingOrderClient`.
- NFR-3 (service independently unit-testable without a MediatR handler; both handler test files
  updated to mock the service; new test file covers the extracted branches) → all three tasks.
- Module-boundary allowlist requirement (design doc + arch review Risk row) →
  `extract-shipment-creation-service` Step 4, including the two extra entries (interface +
  `PackingOrderItem` via nested-type fallback) the design doc didn't spell out but the actual
  reflection-based test requires.

**2. Placeholder scan:** All steps show complete, compilable code (full file contents for every
create/rewrite, not diffs-with-elisions except where explicitly noted as an unchanged-method
elision in `refactor-scan-handler` Step 1, which is immediately followed by the full file
contents anyway). No "TODO"/"add validation"/"similar to Task N" placeholders. Every test asserts
concrete expected values.

**3. Type consistency:** `ShipmentCreationResult`, `IShipmentCreationService.CreateAndPersistAsync`
signature, `ErrorCodes` values, and `Package`/`ShipmentLabel`/`PackingOrder` field names are
identical across all three tasks' code listings (verified by construction — the later tasks'
mocks/setups use the exact members defined in the first task).
