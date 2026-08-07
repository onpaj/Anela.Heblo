# Packing Shipment-Creation Shared Service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix GitHub issue #3853 — `ResetOrderShipmentHandler` creates a replacement carrier shipment but never persists `Package` rows for it (unlike `ScanPackingOrderHandler`, whose near-identical code path does persist them), leaving stale/missing `Package` data after every shipment reset. Fix it by extracting the duplicated "create carrier shipment → map labels to N packages → persist Package rows" orchestration into one shared collaborator both handlers call, so behavior cannot diverge again.

**Architecture:** Introduce `IShipmentCreationService` (+ `ShipmentCreationService` impl) inside `Anela.Heblo.Application/Features/Packaging/Infrastructure/ShipmentCreation/`. It owns: weight-fallback calculation, `GetShippingOptionsAsync`, `CreateShipmentCommand` construction, `CreateShipmentAsync` try/catch, label-to-N-packages mapping, and `Package` row persistence (via the already-existing `IPackageRepository.ReplacePackagesForOrderAsync`). `ScanPackingOrderHandler` and `ResetOrderShipmentHandler` both inject it and call the same two methods (`CreateShipmentAsync`, `PersistPackagesAsync`) instead of duplicating the logic inline. `ScanPackingOrderHandler`'s *other* persistence path (`BackfillExistingShipmentPackagesAsync`, used only for reprints of an order whose shipment already exists) is untouched — it has no twin in `ResetOrderShipmentHandler` and isn't part of this bug.

**Tech Stack:** .NET 8, MediatR, xUnit, Moq, FluentAssertions.

---

## Design notes (read before starting)

**Behavioral difference that must be preserved, not unified:** `ScanPackingOrderHandler`'s existing tests (`ScanPackingOrderHandlerPackagePersistenceTests.Handle_PersistsOnePackageRowPerCreatedLabel_WithSequentialPackageNumbers`) construct labels whose `ShipmentGuid` does **not** match the mocked `CreateShipmentAsync`'s returned `ShipmentGuid`. Scan's original code never filters returned labels by the newly-created shipment's GUID. `ResetOrderShipmentHandler`, by contrast, **does** filter (tested by `Handle_EventualConsistency_SecondCallReturnsBothOldAndNew_OnlyNewPackagesInResponse` — a just-cancelled shipment's labels can still appear in the same Shoptet response as the new shipment's labels for a short window). The shared service takes a `FilterLabelsByShipmentGuid` flag on its request so each handler keeps its own tested behavior: Scan passes `false`, Reset passes `true`.

**Not moved into the shared service:** `ScanPackingOrderHandler.BackfillExistingShipmentPackagesAsync` and its private `ResolvePackerAsync` helper stay exactly where they are. They handle the "shipment already exists, reprint" path, which only Scan has and which the issue doesn't mention. `ShipmentCreationService` gets its own private `ResolvePackerAsync` (a ~6-line duplicate of the one already inside `ScanPackingOrderHandler`) rather than reaching into the handler's backfill logic — keeps the two persistence paths (new-shipment vs. reprint-backfill) independent, matching how they already behave today.

**Packer identity on Reset:** `ResetOrderShipmentRequest` has no `PackingUserId` field (and the frontend never sends one — confirmed in `PackingShipmentCreator.tsx`). Reset calls `PersistPackagesAsync(..., packingUserId: null, ...)`, which falls back to `ICurrentUserService.GetCurrentUser().Email` for `PackedBy` — the same fallback Scan already uses when no explicit packer is supplied. No DTO/frontend change needed.

**Duplication also called out in the issue but not separately extracted:** the `maxPackages = 10` / `InvalidPackageCount` guard. It's addressed cheaply by hoisting the literal into `ShipmentCreationRequest.MaxPackages` (a `public const int`) and having both handlers reference it, instead of two independent local `const int maxPackages = 10;` — avoids a second collaborator method for a one-line guard.

---

### Task 1: Add `IShipmentCreationService` interface + DTOs

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/Infrastructure/ShipmentCreation/IShipmentCreationService.cs`

- [ ] **Step 1: Write the interface and its DTOs**

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.Infrastructure.ShipmentCreation;

/// <summary>
/// Shared "create carrier shipment for an order → persist Package rows" orchestration used by
/// both ScanPackingOrderHandler (new shipment) and ResetOrderShipmentHandler (replacement
/// shipment after cancel), so the two paths cannot silently diverge again.
/// </summary>
public interface IShipmentCreationService
{
    Task<ShipmentCreationOutcome> CreateShipmentAsync(ShipmentCreationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Persists one Package row per label. Best-effort: logs and swallows repository failures
    /// instead of throwing, so a scan/reset never fails just because Package-row bookkeeping did.
    /// No-ops when <paramref name="labels"/> is empty.
    /// </summary>
    Task PersistPackagesAsync(
        string orderCode,
        string customerName,
        string carrierCode,
        string? carrierName,
        Guid shipmentGuid,
        IReadOnlyList<ShipmentLabel> labels,
        Guid? packingUserId,
        CancellationToken cancellationToken);
}

public class ShipmentCreationRequest
{
    /// <summary>Maximum number of packages a single shipment may be split into.</summary>
    public const int MaxPackages = 10;

    public string OrderCode { get; set; } = null!;
    public int NumberOfPackages { get; set; }

    /// <summary>Sum of item WeightGrams * Quantity across the order, before the zero-weight fallback is applied.</summary>
    public int TotalWeightGrams { get; set; }

    /// <summary>
    /// When true, labels returned after shipment creation are filtered to only those matching the
    /// newly created shipment's GUID before being mapped to packages. Reset needs this because a
    /// just-cancelled shipment's labels can still appear in the same response for a short window.
    /// Scan doesn't need it: no shipment existed before, so every label already belongs to the new one.
    /// </summary>
    public bool FilterLabelsByShipmentGuid { get; set; }
}

public class ShipmentCreationOutcome
{
    public bool Success { get; set; }
    public ErrorCodes? ErrorCode { get; set; }
    public Guid ShipmentGuid { get; set; }
    public string CarrierCode { get; set; } = null!;
    public string? CarrierName { get; set; }

    /// <summary>The (possibly filtered) labels used to build <see cref="Packages"/> — passed straight through to PersistPackagesAsync.</summary>
    public IReadOnlyList<ShipmentLabel> Labels { get; set; } = [];
    public List<CreatedShipmentPackage> Packages { get; set; } = [];

    public static ShipmentCreationOutcome Failure(ErrorCodes errorCode) => new() { Success = false, ErrorCode = errorCode };
}

public class CreatedShipmentPackage
{
    public string? TrackingNumber { get; set; }
    public string? LabelUrl { get; set; }
    public string? LabelZpl { get; set; }
}
```

- [ ] **Step 2: Build to confirm it compiles (nothing references it yet, so this only checks syntax)**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/Infrastructure/ShipmentCreation/IShipmentCreationService.cs
git commit -m "feat(packaging): add IShipmentCreationService contract"
```

---

### Task 2: Write failing tests for `ShipmentCreationService`

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Application/Packaging/ShipmentCreationServiceTests.cs`

- [ ] **Step 1: Write the full test file**

```csharp
using Anela.Heblo.Application.Features.Packaging.Infrastructure.ShipmentCreation;
using Anela.Heblo.Application.Features.ShipmentLabels;
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
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly Mock<IAuthorizationRepository> _authRepo = new();

    private static readonly ShipmentLabelsSettings DefaultSettings = new()
    {
        DefaultPackageWidthCm = 30,
        DefaultPackageHeightCm = 20,
        DefaultPackageDepthCm = 15,
        MinPackageWeightGrams = 100,
        FallbackPackageWeightGrams = 1000,
    };

    private ShipmentCreationService CreateSut(ShipmentLabelsSettings? settings = null)
    {
        _currentUserService.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("uid-1", "Operator", "op@example.com", IsAuthenticated: true));
        return new ShipmentCreationService(
            _shipmentClient.Object,
            _packageRepository.Object,
            _currentUserService.Object,
            _authRepo.Object,
            Options.Create(settings ?? DefaultSettings),
            new Mock<ILogger<ShipmentCreationService>>().Object);
    }

    private static ShipmentLabel MakeLabel(
        Guid shipmentGuid,
        string packageName = "P1",
        string? trackingNumber = "TRK1",
        string? labelUrl = "https://example.com/label.pdf",
        string? labelZpl = null) =>
        new()
        {
            ShipmentGuid = shipmentGuid,
            OrderCode = "0001234",
            PackageName = packageName,
            TrackingNumber = trackingNumber,
            LabelUrl = labelUrl,
            LabelZpl = labelZpl,
        };

    // --- CreateShipmentAsync ---

    [Fact]
    public async Task CreateShipmentAsync_ZeroWeight_UsesFallbackPackageWeight()
    {
        var shipmentGuid = Guid.NewGuid();
        CreateShipmentCommand? captured = null;

        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .Callback<CreateShipmentCommand, CancellationToken>((cmd, _) => captured = cmd)
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(shipmentGuid)]);

        var outcome = await CreateSut().CreateShipmentAsync(new ShipmentCreationRequest
        {
            OrderCode = "0001234",
            NumberOfPackages = 1,
            TotalWeightGrams = 0,
        }, CancellationToken.None);

        outcome.Success.Should().BeTrue();
        captured!.Package.WeightGrams.Should().Be(1000); // FallbackPackageWeightGrams
    }

    [Fact]
    public async Task CreateShipmentAsync_PerPackageWeight_FloorsAtMinimum()
    {
        var shipmentGuid = Guid.NewGuid();
        CreateShipmentCommand? captured = null;

        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .Callback<CreateShipmentCommand, CancellationToken>((cmd, _) => captured = cmd)
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(shipmentGuid), MakeLabel(shipmentGuid, "P2")]);

        var outcome = await CreateSut().CreateShipmentAsync(new ShipmentCreationRequest
        {
            OrderCode = "0001234",
            NumberOfPackages = 2,
            TotalWeightGrams = 50, // 50 / 2 = 25g, below the 100g floor
        }, CancellationToken.None);

        outcome.Success.Should().BeTrue();
        captured!.Package.WeightGrams.Should().Be(100); // MinPackageWeightGrams
    }

    [Fact]
    public async Task CreateShipmentAsync_NoShippingOptions_ReturnsShipmentCarrierNotResolved()
    {
        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var outcome = await CreateSut().CreateShipmentAsync(new ShipmentCreationRequest
        {
            OrderCode = "0001234",
            NumberOfPackages = 1,
            TotalWeightGrams = 500,
        }, CancellationToken.None);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(ErrorCodes.ShipmentCarrierNotResolved);
        _shipmentClient.Verify(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateShipmentAsync_CreateThrows_ReturnsShipmentCreationFailed()
    {
        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shipment API unavailable"));

        var outcome = await CreateSut().CreateShipmentAsync(new ShipmentCreationRequest
        {
            OrderCode = "0001234",
            NumberOfPackages = 1,
            TotalWeightGrams = 500,
        }, CancellationToken.None);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(ErrorCodes.ShipmentCreationFailed);
        _shipmentClient.Verify(c => c.GetLabelsByOrderCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateShipmentAsync_FewerLabelsThanRequested_PadsResponseToN()
    {
        var shipmentGuid = Guid.NewGuid();

        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = shipmentGuid });
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(shipmentGuid, "P1", trackingNumber: "TRK1")]); // only 1 of 3 ready

        var outcome = await CreateSut().CreateShipmentAsync(new ShipmentCreationRequest
        {
            OrderCode = "0001234",
            NumberOfPackages = 3,
            TotalWeightGrams = 900,
        }, CancellationToken.None);

        outcome.Success.Should().BeTrue();
        outcome.Packages.Should().HaveCount(3);
        outcome.Packages[0].TrackingNumber.Should().Be("TRK1");
        outcome.Packages[1].TrackingNumber.Should().BeNull();
        outcome.Packages[2].TrackingNumber.Should().BeNull();
    }

    [Fact]
    public async Task CreateShipmentAsync_FilterLabelsByShipmentGuidTrue_ExcludesOldShipmentLabels()
    {
        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();

        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = newGuid });
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeLabel(oldGuid, "OLD-P1", trackingNumber: "TRK-OLD"),
                MakeLabel(newGuid, "NEW-P1", trackingNumber: "TRK-NEW"),
            ]);

        var outcome = await CreateSut().CreateShipmentAsync(new ShipmentCreationRequest
        {
            OrderCode = "0001234",
            NumberOfPackages = 1,
            TotalWeightGrams = 500,
            FilterLabelsByShipmentGuid = true,
        }, CancellationToken.None);

        outcome.Success.Should().BeTrue();
        outcome.Packages.Should().HaveCount(1);
        outcome.Packages[0].TrackingNumber.Should().Be("TRK-NEW");
        outcome.Labels.Should().ContainSingle(l => l.TrackingNumber == "TRK-NEW");
    }

    [Fact]
    public async Task CreateShipmentAsync_FilterLabelsByShipmentGuidFalse_UsesAllReturnedLabels()
    {
        // Regression guard for Scan: labels whose ShipmentGuid doesn't match the
        // freshly-created shipment's GUID must still be used when filtering is off.
        var unrelatedGuid = Guid.NewGuid();
        var createdGuid = Guid.NewGuid();

        _shipmentClient.Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);
        _shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = createdGuid });
        _shipmentClient.Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(unrelatedGuid, "P1", trackingNumber: "TRK1")]);

        var outcome = await CreateSut().CreateShipmentAsync(new ShipmentCreationRequest
        {
            OrderCode = "0001234",
            NumberOfPackages = 1,
            TotalWeightGrams = 500,
            FilterLabelsByShipmentGuid = false,
        }, CancellationToken.None);

        outcome.Success.Should().BeTrue();
        outcome.Packages.Should().ContainSingle(p => p.TrackingNumber == "TRK1");
    }

    // --- PersistPackagesAsync ---

    [Fact]
    public async Task PersistPackagesAsync_EmptyLabels_DoesNotCallRepository()
    {
        await CreateSut().PersistPackagesAsync(
            "0001234", "Alice", "PPL", "PPL", Guid.NewGuid(), [], null, CancellationToken.None);

        _packageRepository.Verify(
            r => r.ReplacePackagesForOrderAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Package>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PersistPackagesAsync_NullPackingUserId_StampsCurrentUserEmail()
    {
        var shipmentGuid = Guid.NewGuid();
        var labels = new List<ShipmentLabel> { MakeLabel(shipmentGuid, trackingNumber: "TRK1") };

        await CreateSut().PersistPackagesAsync(
            "0001234", "Alice", "PPL", "PPL", shipmentGuid, labels, packingUserId: null, CancellationToken.None);

        _packageRepository.Verify(r => r.ReplacePackagesForOrderAsync(
            "0001234",
            It.Is<IReadOnlyCollection<Package>>(pkgs =>
                pkgs.Count == 1 &&
                pkgs.First().PackedByUserId == null &&
                pkgs.First().PackedBy == "op@example.com" &&
                pkgs.First().PackageNumber == "1" &&
                pkgs.First().TrackingNumber == "TRK1" &&
                pkgs.First().OrderCode == "0001234" &&
                pkgs.First().CustomerName == "Alice" &&
                pkgs.First().ShippingProviderCode == "PPL" &&
                pkgs.First().ShipmentGuid == shipmentGuid),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PersistPackagesAsync_WithPackingUserId_StampsResolvedUserIdAndDisplayName()
    {
        var shipmentGuid = Guid.NewGuid();
        var packerId = Guid.NewGuid();
        var labels = new List<ShipmentLabel> { MakeLabel(shipmentGuid, trackingNumber: "TRK1") };
        _authRepo.Setup(r => r.GetUserByIdAsync(packerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUser { Id = packerId, DisplayName = "Pepa Balič", Email = "pepa@x.cz", IsActive = true, CanPack = true });

        await CreateSut().PersistPackagesAsync(
            "0001234", "Alice", "PPL", "PPL", shipmentGuid, labels, packingUserId: packerId, CancellationToken.None);

        _packageRepository.Verify(r => r.ReplacePackagesForOrderAsync(
            "0001234",
            It.Is<IReadOnlyCollection<Package>>(pkgs =>
                pkgs.First().PackedByUserId == packerId &&
                pkgs.First().PackedBy == "Pepa Balič"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PersistPackagesAsync_MultipleLabelsSamePackageName_AssignsSequentialPackageNumbers()
    {
        var shipmentGuid = Guid.NewGuid();
        var labels = new List<ShipmentLabel>
        {
            MakeLabel(shipmentGuid, "Vlastní balení", trackingNumber: "TRK1"),
            MakeLabel(shipmentGuid, "Vlastní balení", trackingNumber: "TRK2"),
        };

        await CreateSut().PersistPackagesAsync(
            "0001234", "Alice", "PPL", "PPL", shipmentGuid, labels, null, CancellationToken.None);

        _packageRepository.Verify(r => r.ReplacePackagesForOrderAsync(
            "0001234",
            It.Is<IReadOnlyCollection<Package>>(pkgs =>
                pkgs.Count == 2 &&
                pkgs.Any(p => p.PackageNumber == "1" && p.TrackingNumber == "TRK1") &&
                pkgs.Any(p => p.PackageNumber == "2" && p.TrackingNumber == "TRK2")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PersistPackagesAsync_RepositoryThrows_DoesNotPropagate()
    {
        var shipmentGuid = Guid.NewGuid();
        var labels = new List<ShipmentLabel> { MakeLabel(shipmentGuid, trackingNumber: "TRK1") };
        _packageRepository
            .Setup(r => r.ReplacePackagesForOrderAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Package>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var act = async () => await CreateSut().PersistPackagesAsync(
            "0001234", "Alice", "PPL", "PPL", shipmentGuid, labels, null, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
```

- [ ] **Step 2: Run the tests to confirm they fail (the service doesn't exist yet)**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShipmentCreationServiceTests"`
Expected: Build error — `ShipmentCreationService` does not exist (`CS0246`). This confirms the test file compiles against the Task 1 interface but has no implementation to run against yet.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Application/Packaging/ShipmentCreationServiceTests.cs
git commit -m "test(packaging): add failing tests for ShipmentCreationService"
```

---

### Task 3: Implement `ShipmentCreationService`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/Infrastructure/ShipmentCreation/ShipmentCreationService.cs`

- [ ] **Step 1: Write the implementation**

```csharp
using System.Globalization;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Packaging.Infrastructure.ShipmentCreation;

public class ShipmentCreationService : IShipmentCreationService
{
    private readonly IShipmentClient _shipmentClient;
    private readonly IPackageRepository _packageRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuthorizationRepository _authRepo;
    private readonly ShipmentLabelsSettings _shipmentSettings;
    private readonly ILogger<ShipmentCreationService> _logger;

    public ShipmentCreationService(
        IShipmentClient shipmentClient,
        IPackageRepository packageRepository,
        ICurrentUserService currentUserService,
        IAuthorizationRepository authRepo,
        IOptions<ShipmentLabelsSettings> shipmentSettings,
        ILogger<ShipmentCreationService> logger)
    {
        _shipmentClient = shipmentClient;
        _packageRepository = packageRepository;
        _currentUserService = currentUserService;
        _authRepo = authRepo;
        _shipmentSettings = shipmentSettings.Value;
        _logger = logger;
    }

    public async Task<ShipmentCreationOutcome> CreateShipmentAsync(ShipmentCreationRequest request, CancellationToken cancellationToken)
    {
        var totalWeightGrams = request.TotalWeightGrams;
        if (totalWeightGrams == 0)
        {
            // Carriers reject a 0 kg package; fall back to a default package weight.
            _logger.LogWarning(
                "Order {OrderCode} has no known item weights; using fallback package weight {Fallback}g",
                request.OrderCode, _shipmentSettings.FallbackPackageWeightGrams);
            totalWeightGrams = _shipmentSettings.FallbackPackageWeightGrams;
        }

        var n = request.NumberOfPackages;
        var perPackageWeightGrams = Math.Max(totalWeightGrams / n, _shipmentSettings.MinPackageWeightGrams);

        var options = await _shipmentClient.GetShippingOptionsAsync(request.OrderCode, cancellationToken);
        if (options.Count == 0)
            return ShipmentCreationOutcome.Failure(ErrorCodes.ShipmentCarrierNotResolved);

        var command = new CreateShipmentCommand
        {
            OrderCode = request.OrderCode,
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
            createdShipment = await _shipmentClient.CreateShipmentAsync(command, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create shipment for order {OrderCode}", request.OrderCode);
            return ShipmentCreationOutcome.Failure(ErrorCodes.ShipmentCreationFailed);
        }

        // Single fetch for carrier tracking numbers + label URLs (FE prints directly from the CDN).
        // Shoptet generates labels asynchronously, so the response may contain fewer labels than
        // the requested `n`. Always produce exactly `n` entries so the FE shows the correct
        // "X/N" counter; packages with no label yet get null tracking + URLs
        // (the FE's 404 retry path handles the "carrier not ready" case).
        var allLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, cancellationToken);
        var relevantLabels = request.FilterLabelsByShipmentGuid
            ? allLabels.Where(l => l.ShipmentGuid == createdShipment.ShipmentGuid).ToList()
            : allLabels.ToList();

        var packages = Enumerable.Range(1, n)
            .Select(i =>
            {
                var label = i <= relevantLabels.Count ? relevantLabels[i - 1] : null;
                return new CreatedShipmentPackage
                {
                    TrackingNumber = label?.TrackingNumber,
                    LabelUrl = label?.LabelUrl,
                    LabelZpl = label?.LabelZpl,
                };
            })
            .ToList();

        return new ShipmentCreationOutcome
        {
            Success = true,
            ShipmentGuid = createdShipment.ShipmentGuid,
            CarrierCode = command.CarrierCode,
            CarrierName = options[0].Name,
            Labels = relevantLabels,
            Packages = packages,
        };
    }

    public async Task PersistPackagesAsync(
        string orderCode,
        string customerName,
        string carrierCode,
        string? carrierName,
        Guid shipmentGuid,
        IReadOnlyList<ShipmentLabel> labels,
        Guid? packingUserId,
        CancellationToken cancellationToken)
    {
        if (labels.Count == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        var (packedByUserId, packedBy) = await ResolvePackerAsync(packingUserId, cancellationToken);

        // Carrier package names are not unique per package (custom-packaging shipments
        // report the same "Vlastní balení" name for every package), so a 1-based index
        // within the order is used as the unique PackageNumber. The carrier's real
        // identifier is preserved in TrackingNumber.
        var packages = labels
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
            await _packageRepository.ReplacePackagesForOrderAsync(orderCode, packages, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to persist {PackageCount} Package row(s) for order {OrderCode}",
                packages.Count, orderCode);
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
}
```

- [ ] **Step 2: Run the new tests, confirm they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShipmentCreationServiceTests"`
Expected: All 12 tests pass.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/Infrastructure/ShipmentCreation/ShipmentCreationService.cs
git commit -m "feat(packaging): implement ShipmentCreationService"
```

---

### Task 4: Register the service in `PackagingModule.cs`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/PackagingModule.cs`

- [ ] **Step 1: Add the DI registration and using**

Add this using near the top (alphabetical with the existing block):

```csharp
using Anela.Heblo.Application.Features.Packaging.Infrastructure.ShipmentCreation;
```

Add this line right after the existing `services.AddScoped<IPackageRepository, PackageRepository>();` line inside `AddPackagingModule`:

```csharp
services.AddScoped<IShipmentCreationService, ShipmentCreationService>();
```

- [ ] **Step 2: Build to confirm no wiring errors**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/PackagingModule.cs
git commit -m "feat(packaging): register IShipmentCreationService in DI"
```

---

### Task 5: Refactor `ScanPackingOrderHandler` to use the shared service (no behavior change)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs:15-59` (`CreateHandler` factory only)
- Modify: `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderPackerTests.cs` (its `CreateHandler`-equivalent factory only)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Packaging/ScanPackingOrderHandlerPackagePersistenceTests.cs:17-65` (`MakeSut` factory only)

This task is a pure extraction: the goal is that every existing test in these three files passes **unchanged in its body** — only the object-construction plumbing (constructor args) changes, because `ShipmentCreationService` is now a real collaborator built from the same mocks, in between the handler and the mocked clients/repository.

- [ ] **Step 1: Rewrite `ScanPackingOrderHandler.cs`**

Replace the whole file with:

```csharp
using Anela.Heblo.Application.Features.Packaging.Infrastructure.ShipmentCreation;
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
    private readonly IShipmentCreationService _shipmentCreationService;
    private readonly ILogger<ScanPackingOrderHandler> _logger;
    private readonly IPackageRepository _packageRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuthorizationRepository _authRepo;

    public ScanPackingOrderHandler(
        IShipmentClient shipmentClient,
        IPackingOrderClient orderClient,
        IEshopOrderClient eshopOrderClient,
        IShipmentCreationService shipmentCreationService,
        ILogger<ScanPackingOrderHandler> logger,
        IPackageRepository packageRepository,
        ICurrentUserService currentUserService,
        IAuthorizationRepository authRepo)
    {
        _shipmentClient = shipmentClient;
        _orderClient = orderClient;
        _eshopOrderClient = eshopOrderClient;
        _shipmentCreationService = shipmentCreationService;
        _logger = logger;
        _packageRepository = packageRepository;
        _currentUserService = currentUserService;
        _authRepo = authRepo;
    }

    public async Task<ScanPackingOrderResponse> Handle(ScanPackingOrderRequest request, CancellationToken ct)
    {
        if (request.NumberOfPackages < 1 || request.NumberOfPackages > ShipmentCreationRequest.MaxPackages)
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

        var outcome = await _shipmentCreationService.CreateShipmentAsync(new ShipmentCreationRequest
        {
            OrderCode = request.OrderCode,
            NumberOfPackages = request.NumberOfPackages,
            TotalWeightGrams = order.Items.Sum(i => i.WeightGrams * i.Quantity),
            FilterLabelsByShipmentGuid = false,
        }, ct);

        if (!outcome.Success)
            return new ScanPackingOrderResponse(outcome.ErrorCode!.Value);

        if (request.PackingUserId is { } requestedPackerId)
        {
            var packer = await _authRepo.GetUserByIdAsync(requestedPackerId, ct);
            if (packer is null || !packer.IsActive || !packer.CanPack)
                return new ScanPackingOrderResponse(ErrorCodes.PackingUserNotEligible);
        }

        await _shipmentCreationService.PersistPackagesAsync(
            request.OrderCode,
            orderData.CustomerName,
            outcome.CarrierCode,
            outcome.CarrierName,
            outcome.ShipmentGuid,
            outcome.Labels,
            request.PackingUserId,
            ct);

        // The Shoptet "Zabaleno" (52) transition is deferred to the FE, which calls
        // .../packing/complete only after every carrier label is confirmed fetched & printed.
        // CreateShipmentAsync succeeding means Shoptet accepted the request, NOT that a usable
        // label was produced (labels generate asynchronously and can fail). Marking here would
        // move the order to "Zabaleno" even when no label exists. Single- and multi-package
        // orders share this deferred path.
        return new ScanPackingOrderResponse(orderData, new ScanShipmentData
        {
            ShipmentGuid = outcome.ShipmentGuid,
            Packages = outcome.Packages
                .Select(p => new ScanShipmentPackage
                {
                    TrackingNumber = p.TrackingNumber,
                    LabelUrl = p.LabelUrl,
                    LabelZpl = p.LabelZpl,
                })
                .ToList(),
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

- [ ] **Step 2: Update `ScanPackingOrderHandlerTests.cs`'s `CreateHandler` factory**

In `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs`, add the using:

```csharp
using Anela.Heblo.Application.Features.Packaging.Infrastructure.ShipmentCreation;
```

Replace the `CreateHandler` method (originally lines 30-42) with:

```csharp
    private ScanPackingOrderHandler CreateHandler(ShipmentLabelsSettings? labelSettings = null)
    {
        _currentUserService.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("uid-1", "Operator", "op@example.com", IsAuthenticated: true));
        var shipmentCreationService = new ShipmentCreationService(
            _shipmentClient.Object,
            _packageRepository.Object,
            _currentUserService.Object,
            _authRepo.Object,
            Options.Create(labelSettings ?? DefaultLabelSettings),
            new Mock<ILogger<ShipmentCreationService>>().Object);
        return new(
            _shipmentClient.Object,
            _orderClient.Object,
            _eshopOrderClient.Object,
            shipmentCreationService,
            new Mock<ILogger<ScanPackingOrderHandler>>().Object,
            _packageRepository.Object,
            _currentUserService.Object,
            _authRepo.Object);
    }
```

- [ ] **Step 3: Update `ScanPackingOrderPackerTests.cs`'s handler factory the same way**

Read the file first to find its exact factory method name/signature, then apply the identical transformation: build a `ShipmentCreationService` from the same mock fields (`_shipmentClient`, `_packageRepository`, `_currentUserService`, `_authRepo`, label settings), pass it as the `IShipmentCreationService` constructor argument instead of `IOptions<ShipmentLabelsSettings>`, keep every other argument in the same position it already occupies.

```bash
cat backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderPackerTests.cs
```

Apply the same `CreateHandler`-shape rewrite as Step 2 (add the `Infrastructure.ShipmentCreation` using, construct a real `ShipmentCreationService` from the existing mock fields, pass it where `IOptions<ShipmentLabelsSettings>` used to go, drop the direct `IOptions<ShipmentLabelsSettings>` argument from `ScanPackingOrderHandler`'s constructor call).

- [ ] **Step 4: Update `ScanPackingOrderHandlerPackagePersistenceTests.cs`'s `MakeSut` factory**

Replace lines 17-65 with:

```csharp
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
            DefaultPackageWidthCm = 30,
            DefaultPackageHeightCm = 20,
            DefaultPackageDepthCm = 15,
        });
        var authRepo = new Mock<IAuthorizationRepository>();
        var shipmentCreationService = new ShipmentCreationService(
            shipmentClient.Object,
            packageRepo.Object,
            currentUser.Object,
            authRepo.Object,
            shipmentSettings,
            NullLogger<ShipmentCreationService>.Instance);
        return new ScanPackingOrderHandler(
            shipmentClient.Object,
            orderClient.Object,
            eshopClient.Object,
            shipmentCreationService,
            NullLogger<ScanPackingOrderHandler>.Instance,
            packageRepo.Object,
            currentUser.Object,
            authRepo.Object);
    }
```

Add the using `Anela.Heblo.Application.Features.Packaging.Infrastructure.ShipmentCreation;` to this file's using block too.

- [ ] **Step 5: Run all three test files, confirm every existing test still passes unchanged**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ScanPackingOrderHandlerTests|FullyQualifiedName~ScanPackingOrderPackerTests|FullyQualifiedName~ScanPackingOrderHandlerPackagePersistenceTests"`
Expected: All tests pass (16 + however many are in `ScanPackingOrderPackerTests` + 5), zero failures, zero skipped.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs \
        backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderPackerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Packaging/ScanPackingOrderHandlerPackagePersistenceTests.cs
git commit -m "refactor(packaging): ScanPackingOrderHandler uses shared IShipmentCreationService"
```

---

### Task 6: Fix `ResetOrderShipmentHandler` — the actual bug

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Application/Packaging/ResetOrderShipmentHandlerTests.cs`

- [ ] **Step 1: Add new failing tests proving the bug exists, to `ResetOrderShipmentHandlerTests.cs`**

First update the class's mock fields and `CreateHandler` factory (this changes ALL tests' wiring, which is required since the handler's constructor is changing — but no *existing* test body/assertions change). Replace lines 12-30 with:

```csharp
public class ResetOrderShipmentHandlerTests
{
    private readonly Mock<IShipmentClient> _shipmentClient = new();
    private readonly Mock<IPackingOrderClient> _orderClient = new();
    private readonly Mock<IPackageRepository> _packageRepository = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly Mock<IAuthorizationRepository> _authRepo = new();

    private static readonly ShipmentLabelsSettings DefaultLabelSettings = new()
    {
        DefaultPackageWidthCm = 30,
        DefaultPackageHeightCm = 20,
        DefaultPackageDepthCm = 15,
        MinPackageWeightGrams = 100,
    };

    private ResetOrderShipmentHandler CreateHandler(ShipmentLabelsSettings? labelSettings = null)
    {
        _currentUserService.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("uid-1", "Operator", "op@example.com", IsAuthenticated: true));
        var shipmentCreationService = new ShipmentCreationService(
            _shipmentClient.Object,
            _packageRepository.Object,
            _currentUserService.Object,
            _authRepo.Object,
            Options.Create(labelSettings ?? DefaultLabelSettings),
            new Mock<ILogger<ShipmentCreationService>>().Object);
        return new(
            _shipmentClient.Object,
            _orderClient.Object,
            shipmentCreationService,
            new Mock<ILogger<ResetOrderShipmentHandler>>().Object);
    }
```

Add these usings at the top of the file:

```csharp
using Anela.Heblo.Application.Features.Packaging.Infrastructure.ShipmentCreation;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
```

Then append these new tests at the end of the class, right before the final closing `}`:

```csharp
    // Regression test for #3853: Reset must persist Package rows for the replacement
    // shipment, mirroring what ScanPackingOrderHandler already does for new shipments.
    [Fact]
    public async Task Handle_HappyPath_PersistsPackageRowForReplacementShipment()
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
            .ReturnsAsync(new PackingOrder
            {
                Code = "0001234",
                CustomerName = "Alice",
                StatusId = 26,
                Items = [new PackingOrderItem { Name = "P001", Quantity = 1, WeightGrams = 400 }],
            });

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
        _packageRepository.Verify(r => r.ReplacePackagesForOrderAsync(
            "0001234",
            It.Is<IReadOnlyCollection<Package>>(pkgs =>
                pkgs.Count == 1 &&
                pkgs.First().OrderCode == "0001234" &&
                pkgs.First().CustomerName == "Alice" &&
                pkgs.First().ShipmentGuid == newGuid &&
                pkgs.First().TrackingNumber == "TRK-NEW-1" &&
                pkgs.First().ShippingProviderCode == "PPL" &&
                pkgs.First().PackedBy == "op@example.com"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // The old shipment's stale rows aren't touched by Reset itself — ReplacePackagesForOrderAsync
    // (called with the NEW shipment's labels) atomically clears every row for the order first,
    // including ones written under the cancelled shipment, then inserts the fresh set.
    [Fact]
    public async Task Handle_MultiPackage_PersistsOnePackageRowPerCreatedPackage()
    {
        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();

        _shipmentClient
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid)])
            .ReturnsAsync(new List<ShipmentLabel>
            {
                MakeLabel(newGuid, "NEW-P1", trackingNumber: "TRK-1"),
                MakeLabel(newGuid, "NEW-P2", trackingNumber: "TRK-2"),
            });

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackingOrder
            {
                Code = "0001234",
                CustomerName = "Alice",
                StatusId = 26,
                Items = [new PackingOrderItem { Name = "P001", Quantity = 1, WeightGrams = 900 }],
            });

        _shipmentClient
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);

        _shipmentClient
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = newGuid });

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234", NumberOfPackages = 2 },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        _packageRepository.Verify(r => r.ReplacePackagesForOrderAsync(
            "0001234",
            It.Is<IReadOnlyCollection<Package>>(pkgs =>
                pkgs.Count == 2 &&
                pkgs.Any(p => p.PackageNumber == "1" && p.TrackingNumber == "TRK-1") &&
                pkgs.Any(p => p.PackageNumber == "2" && p.TrackingNumber == "TRK-2") &&
                pkgs.All(p => p.ShipmentGuid == newGuid)),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PersistenceThrows_StillReturnsSuccessfulReset()
    {
        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();

        _shipmentClient
            .SetupSequence(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeLabel(oldGuid)])
            .ReturnsAsync([MakeLabel(newGuid, "NEW-P1", trackingNumber: "TRK-NEW-1")]);

        _shipmentClient
            .Setup(c => c.CancelShipmentAsync(oldGuid, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _orderClient
            .Setup(c => c.GetPackingOrderAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackingOrder
            {
                Code = "0001234",
                CustomerName = "Alice",
                StatusId = 26,
                Items = [new PackingOrderItem { Name = "P001", Quantity = 1, WeightGrams = 400 }],
            });

        _shipmentClient
            .Setup(c => c.GetShippingOptionsAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingOption { CarrierCode = "PPL", Name = "PPL" }]);

        _shipmentClient
            .Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = newGuid });

        _packageRepository
            .Setup(r => r.ReplacePackagesForOrderAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Package>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var response = await CreateHandler().Handle(
            new ResetOrderShipmentRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
    }
```

- [ ] **Step 2: Run the new tests, confirm they fail (handler doesn't persist yet)**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ResetOrderShipmentHandlerTests"`
Expected: Build error first (constructor signature changed but handler hasn't — fix per Step 3 below), or once building, the 3 new tests fail because `ReplacePackagesForOrderAsync` is never called.

- [ ] **Step 3: Rewrite `ResetOrderShipmentHandler.cs`**

Replace the whole file with:

```csharp
using Anela.Heblo.Application.Features.Packaging.Infrastructure.ShipmentCreation;
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
        if (request.NumberOfPackages < 1 || request.NumberOfPackages > ShipmentCreationRequest.MaxPackages)
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

        var outcome = await _shipmentCreationService.CreateShipmentAsync(new ShipmentCreationRequest
        {
            OrderCode = request.OrderCode,
            NumberOfPackages = request.NumberOfPackages,
            TotalWeightGrams = order.Items.Sum(i => i.WeightGrams * i.Quantity),
            FilterLabelsByShipmentGuid = true,
        }, ct);

        if (!outcome.Success)
            return new ResetOrderShipmentResponse(outcome.ErrorCode!.Value);

        await _shipmentCreationService.PersistPackagesAsync(
            request.OrderCode,
            order.CustomerName,
            outcome.CarrierCode,
            outcome.CarrierName,
            outcome.ShipmentGuid,
            outcome.Labels,
            packingUserId: null,
            ct);

        return new ResetOrderShipmentResponse(new ResetShipmentData
        {
            ShipmentGuid = outcome.ShipmentGuid,
            Packages = outcome.Packages
                .Select(p => new ResetShipmentPackage
                {
                    TrackingNumber = p.TrackingNumber,
                    LabelUrl = p.LabelUrl,
                    LabelZpl = p.LabelZpl,
                })
                .ToList(),
            PendingCompletion = request.NumberOfPackages >= 2,
        });
    }
}
```

- [ ] **Step 4: Run the full Reset test file, confirm every test passes (old + new)**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ResetOrderShipmentHandlerTests"`
Expected: All tests pass — the 13 pre-existing tests (unchanged bodies) plus the 3 new persistence-regression tests from Step 1.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ResetOrderShipment/ResetOrderShipmentHandler.cs \
        backend/test/Anela.Heblo.Tests/Application/Packaging/ResetOrderShipmentHandlerTests.cs
git commit -m "fix(packaging): ResetOrderShipmentHandler persists Package rows for the replacement shipment (#3853)"
```

---

### Task 7: Full validation

**Files:** none (verification only)

- [ ] **Step 1: Build the whole backend**

Run: `cd backend && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: No formatting violations. If it reports violations, run `dotnet format` (without `--verify-no-changes`) and review the diff before re-running the check.

- [ ] **Step 3: Run the full Packaging test surface (not just the files touched) to catch any missed reference**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Packaging"`
Expected: All tests pass, 0 failures.

- [ ] **Step 4: Run the full backend test suite**

Run: `cd backend && dotnet test`
Expected: All tests pass, 0 failures. (Confirms no other module referenced the old `ScanPackingOrderHandler`/`ResetOrderShipmentHandler` constructors — e.g. no integration test wiring them up directly.)

- [ ] **Step 5: grep for any other direct instantiation of the two handlers, to be sure nothing outside the test files touched needs updating**

Run: `cd backend && grep -rn "new ScanPackingOrderHandler(\|new ResetOrderShipmentHandler(" --include=*.cs .`
Expected: Only the two test factory methods updated in Task 5/6 (and no other call sites — production code always resolves handlers via MediatR/DI).

- [ ] **Step 6: Commit if Step 2's `dotnet format` produced any changes**

```bash
git add -A
git commit -m "chore(packaging): dotnet format"
```

(Skip this step if `dotnet format --verify-no-changes` in Step 2 reported no violations.)
