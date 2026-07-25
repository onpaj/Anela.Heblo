# Implementation Plan: Invert `IShipmentClient` dependency in `CompleteDeliveredOrdersJob`

## Goal
Replace `CompleteDeliveredOrdersJob`'s direct dependency on `ShipmentLabels`' six-method `IShipmentClient` with a narrow, `ShoptetOrders`-owned contract (`IShipmentDeliveryChecker`), implemented by a `ShipmentLabels`-owned adapter — matching the existing `IPackingCarrierCoolingSource` / `IPackingProductSource` pattern in the same module. Pure refactor: no behavior change.

## Architecture summary
- **New contract** (consumer-owned): `Anela.Heblo.Application.Features.ShoptetOrders.Contracts.IShipmentDeliveryChecker`, one method: `Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default)`.
- **New adapter** (provider-owned): `Anela.Heblo.Application.Features.ShipmentLabels.Infrastructure.ShipmentLabelsShipmentDeliveryCheckerAdapter`, `internal sealed`, delegates to the existing `IShipmentClient`.
- **DI**: registered inside `ShipmentLabelsModule.AddShipmentLabelsModule` (provider owns the registration), `AddTransient`.
- **Consumer swap**: `CompleteDeliveredOrdersJob` retypes its field/ctor param from `IShipmentClient` to `IShipmentDeliveryChecker`; call site at line 99 is unchanged (identical method signature).
- **Regression guard**: a new `ModuleBoundariesTests` rule (`"ShoptetOrders -> ShipmentLabels"`) pins the boundary so `ShoptetOrders` can never again reference `ShipmentLabels` types directly.
- Nothing else changes: `IShipmentClient`, `ShoptetShipmentClient`, its `AddHttpClient<>` registration, `ShoptetOrdersModule.cs`, and the `Packaging` module's own `IShipmentClient` consumers are untouched.

All commands below are run from the repo root: `/home/user/worktrees/feature-3720-Arch-Review-Shoptetorders-Completedeliveredordersj`.

---

### task: add-shipment-delivery-checker-contract-and-adapter

Add the new consumer-owned contract and the provider-owned adapter that implements it, with a unit test for the adapter's delegation behavior.

**Step 1 — write the failing adapter test first.**

Create `backend/test/Anela.Heblo.Tests/Features/ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapterTests.cs` (new file, new folder — mirrors `backend/test/Anela.Heblo.Tests/Features/CarrierCooling/Infrastructure/CarrierCoolingPackingCarrierCoolingAdapterTests.cs`):

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.Infrastructure;
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.ShipmentLabels.Infrastructure;

public class ShipmentLabelsShipmentDeliveryCheckerAdapterTests
{
    [Fact]
    public async Task HasDeliveredShipmentAsync_DelegatesToShipmentClient_WithSameArgumentsAndResult()
    {
        var orderCode = "ORD-123";
        using var cts = new CancellationTokenSource();
        var shipmentClient = new Mock<IShipmentClient>();
        shipmentClient
            .Setup(c => c.HasDeliveredShipmentAsync(orderCode, cts.Token))
            .ReturnsAsync(true);
        var sut = new ShipmentLabelsShipmentDeliveryCheckerAdapter(shipmentClient.Object);

        var result = await sut.HasDeliveredShipmentAsync(orderCode, cts.Token);

        result.Should().BeTrue();
        shipmentClient.Verify(c => c.HasDeliveredShipmentAsync(orderCode, cts.Token), Times.Once);
    }

    [Fact]
    public async Task HasDeliveredShipmentAsync_ReturnsFalse_WhenShipmentClientReturnsFalse()
    {
        var shipmentClient = new Mock<IShipmentClient>();
        shipmentClient
            .Setup(c => c.HasDeliveredShipmentAsync("ORD-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sut = new ShipmentLabelsShipmentDeliveryCheckerAdapter(shipmentClient.Object);

        var result = await sut.HasDeliveredShipmentAsync("ORD-456");

        result.Should().BeFalse();
    }
}
```

Run it and confirm it fails to compile (neither `IShipmentDeliveryChecker` nor `ShipmentLabelsShipmentDeliveryCheckerAdapter` exist yet):

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShipmentLabelsShipmentDeliveryCheckerAdapterTests"
```

Expect a build error referencing the two missing types.

**Step 2 — create the consumer-owned contract.**

Create `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IShipmentDeliveryChecker.cs` (new file):

```csharp
namespace Anela.Heblo.Application.Features.ShoptetOrders.Contracts;

public interface IShipmentDeliveryChecker
{
    Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default);
}
```

**Step 3 — create the provider-owned adapter.**

Create `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapter.cs` (new file, new `Infrastructure/` folder under `ShipmentLabels` — mirrors `CarrierCooling/Infrastructure/` and `Catalog/Infrastructure/`):

```csharp
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;

namespace Anela.Heblo.Application.Features.ShipmentLabels.Infrastructure;

internal sealed class ShipmentLabelsShipmentDeliveryCheckerAdapter : IShipmentDeliveryChecker
{
    private readonly IShipmentClient _shipmentClient;

    public ShipmentLabelsShipmentDeliveryCheckerAdapter(IShipmentClient shipmentClient)
    {
        _shipmentClient = shipmentClient;
    }

    public Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default)
        => _shipmentClient.HasDeliveredShipmentAsync(orderCode, ct);
}
```

**Step 4 — run the test again and confirm it passes.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShipmentLabelsShipmentDeliveryCheckerAdapterTests"
```

Both `[Fact]`s must pass.

**Step 5 — build the whole solution to confirm nothing else broke.**

```bash
dotnet build Anela.Heblo.sln
```

**Step 6 — commit.**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IShipmentDeliveryChecker.cs \
        backend/src/Anela.Heblo.Application/Features/ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapter.cs \
        backend/test/Anela.Heblo.Tests/Features/ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapterTests.cs
git commit -m "Add IShipmentDeliveryChecker contract and ShipmentLabels adapter"
```

---

### task: register-shipment-delivery-checker-in-shipmentlabels-module

Wire `IShipmentDeliveryChecker` → `ShipmentLabelsShipmentDeliveryCheckerAdapter` into DI, inside `ShipmentLabelsModule` (the provider), following the same comment convention as `CarrierCoolingModule.cs`.

**Step 1 — write a failing DI-wiring test first.**

Create `backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/ShipmentLabelsModuleTests.cs` (new file; folder already exists — it holds `GetOrderShipmentLabelsHandlerTests.cs` etc.):

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.Infrastructure;
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Anela.Heblo.Tests.Application.ShipmentLabels;

public class ShipmentLabelsModuleTests
{
    [Fact]
    public void AddShipmentLabelsModule_RegistersIShipmentDeliveryChecker_AsShipmentLabelsShipmentDeliveryCheckerAdapter()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IShipmentClient>());
        var configuration = new ConfigurationBuilder().Build();

        services.AddShipmentLabelsModule(configuration);
        var serviceProvider = services.BuildServiceProvider();

        var checker = serviceProvider.GetRequiredService<IShipmentDeliveryChecker>();
        checker.Should().BeOfType<ShipmentLabelsShipmentDeliveryCheckerAdapter>();
    }
}
```

Run it and confirm it fails (either a compile error if the previous task wasn't merged, or — since the previous task is already done — an `InvalidOperationException: Unable to resolve service for type 'IShipmentDeliveryChecker'` at `GetRequiredService`, because the registration doesn't exist yet):

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShipmentLabelsModuleTests"
```

**Step 2 — add the DI registration.**

Read the current file first: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsModule.cs`. Add two `using` statements and the registration line.

Change the top of the file from:
```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackageLabelPdf;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.Validators;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
```
to:
```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackageLabelPdf;
using Anela.Heblo.Application.Features.ShipmentLabels.Infrastructure;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.Validators;
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
```

Change the body from:
```csharp
        // Named HttpClient used by GetPackageLabelPdfHandler to stream carrier-CDN PDFs
        // through our own origin so the SPA can silent-print without CORS errors.
        services.AddHttpClient(GetPackageLabelPdfHandler.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
```
to:
```csharp
        // Named HttpClient used by GetPackageLabelPdfHandler to stream carrier-CDN PDFs
        // through our own origin so the SPA can silent-print without CORS errors.
        services.AddHttpClient(GetPackageLabelPdfHandler.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Cross-module contract: ShipmentLabels implements ShoptetOrders' IShipmentDeliveryChecker via
        // adapter. DI registration is owned by the provider (ShipmentLabels), not the consumer (ShoptetOrders).
        services.AddTransient<IShipmentDeliveryChecker, ShipmentLabelsShipmentDeliveryCheckerAdapter>();

        return services;
```

**Step 3 — run the test again and confirm it passes.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShipmentLabelsModuleTests"
```

**Step 4 — build the whole solution.**

```bash
dotnet build Anela.Heblo.sln
```

**Step 5 — commit.**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsModule.cs \
        backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/ShipmentLabelsModuleTests.cs
git commit -m "Register IShipmentDeliveryChecker in ShipmentLabelsModule"
```

---

### task: swap-complete-delivered-orders-job-to-new-contract

Retype `CompleteDeliveredOrdersJob`'s dependency from `IShipmentClient` to `IShipmentDeliveryChecker`, and update its existing unit tests to mock the new interface. No change to `ExecuteAsync` control flow, logging, or job metadata.

**Step 1 — update the test file's mock type first (red step).**

File: `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/CompleteDeliveredOrdersJobTests.cs`.

Change the `using` block at the top from:
```csharp
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure.Jobs;
```
to:
```csharp
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure.Jobs;
```

Change the `MakeSut` return-tuple type and body from:
```csharp
    private static (
        CompleteDeliveredOrdersJob Sut,
        Mock<IEshopOrderClient> Orders,
        Mock<IShipmentClient> Shipments,
        Mock<IRecurringJobStatusChecker> StatusChecker)
        MakeSut(bool jobEnabled = true, bool applyChanges = true, bool useTestSource = false)
    {
        var orders = new Mock<IEshopOrderClient>();
        var shipments = new Mock<IShipmentClient>();
```
to:
```csharp
    private static (
        CompleteDeliveredOrdersJob Sut,
        Mock<IEshopOrderClient> Orders,
        Mock<IShipmentDeliveryChecker> Shipments,
        Mock<IRecurringJobStatusChecker> StatusChecker)
        MakeSut(bool jobEnabled = true, bool applyChanges = true, bool useTestSource = false)
    {
        var orders = new Mock<IEshopOrderClient>();
        var shipments = new Mock<IShipmentDeliveryChecker>();
```

No other lines in this file change — every `shipments.Setup(...)` / `shipments.Verify(...)` call site references `HasDeliveredShipmentAsync`, whose signature is identical on `IShipmentDeliveryChecker`.

Confirm this alone breaks the build (the job's constructor still expects `IShipmentClient`, so passing `shipments.Object` of type `IShipmentDeliveryChecker` into `new CompleteDeliveredOrdersJob(...)` at line 44-46 fails to compile):

```bash
dotnet build Anela.Heblo.sln
```

Expect a compile error in `CompleteDeliveredOrdersJobTests.cs` about the constructor argument type.

**Step 2 — update the job itself (green step).**

File: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob.cs`.

Change line 2 from:
```csharp
using Anela.Heblo.Application.Features.ShipmentLabels;
```
to:
```csharp
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
```

Change line 14 from:
```csharp
    private readonly IShipmentClient _shipmentClient;
```
to:
```csharp
    private readonly IShipmentDeliveryChecker _shipmentClient;
```

Change line 31 (constructor parameter) from:
```csharp
        IShipmentClient shipmentClient,
```
to:
```csharp
        IShipmentDeliveryChecker shipmentClient,
```

No other line changes — the field assignment (`_shipmentClient = shipmentClient;`), the call site at line 99 (`_shipmentClient.HasDeliveredShipmentAsync(order.Code, cancellationToken)`), and `Metadata` are untouched.

**Step 3 — run the job's test suite and confirm all 9 tests pass.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CompleteDeliveredOrdersJobTests"
```

Expect 9 passing tests: `ExecuteAsync_SkipsWork_WhenJobDisabled`, `ExecuteAsync_CompletesOrder_WhenShipmentDelivered`, `ExecuteAsync_AppendsNote_PreservingExistingRemark`, `ExecuteAsync_DoesNotComplete_WhenNoShipmentDelivered`, `ExecuteAsync_ProcessesBothSourceStates`, `ExecuteAsync_DryRun_DetectsButDoesNotMutate_WhenFeatureFlagDisabled`, `ExecuteAsync_UsesTestSourceState_WhenTestSourceFlagEnabled`, `ExecuteAsync_TestSourceRespectsDryRun_WhenCompletionFlagDisabled`, `ExecuteAsync_ContinuesProcessing_WhenOneOrderThrows`.

**Step 4 — build and run the full test suite.**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

**Step 5 — commit.**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob.cs \
        backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/CompleteDeliveredOrdersJobTests.cs
git commit -m "Swap CompleteDeliveredOrdersJob to consumer-owned IShipmentDeliveryChecker"
```

---

### task: add-module-boundary-rule-for-shoptetorders-shipmentlabels

Pin the `ShoptetOrders -> ShipmentLabels` boundary in `ModuleBoundariesTests.cs` so a future contributor cannot reintroduce a direct `IShipmentClient` (or any other `ShipmentLabels` type) reference into `ShoptetOrders`. This is the regression guard for the whole fix.

File: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`.

**Step 1 — add a new empty allowlist field.**

Insert a new `private static readonly HashSet<string>` field immediately after the existing `PackagingShoptetOrdersAllowlist` field (which currently ends at line 339 with `};`) and before the `public static TheoryData<ModuleBoundaryRule> Rules() => new()` line (line 341). Find this exact text:

```csharp
        // PackingStatsTile is a dashboard tile that mirrors GetPackingDashboardHandler's logic;
        // it consumes only IPackingOrderClient (returns int?) — no ShoptetOrders DTOs cross the boundary.
        "Anela.Heblo.Application.Features.Packaging.DashboardTiles.PackingStatsTile -> Anela.Heblo.Application.Features.ShoptetOrders.IPackingOrderClient",
    };

    public static TheoryData<ModuleBoundaryRule> Rules() => new()
```

Replace it with:

```csharp
        // PackingStatsTile is a dashboard tile that mirrors GetPackingDashboardHandler's logic;
        // it consumes only IPackingOrderClient (returns int?) — no ShoptetOrders DTOs cross the boundary.
        "Anela.Heblo.Application.Features.Packaging.DashboardTiles.PackingStatsTile -> Anela.Heblo.Application.Features.ShoptetOrders.IPackingOrderClient",
    };

    // Allowlist for ShoptetOrders -> ShipmentLabels. Empty — CompleteDeliveredOrdersJob now consumes
    // the ShoptetOrders-owned IShipmentDeliveryChecker contract; the ShipmentLabels adapter
    // (ShipmentLabelsShipmentDeliveryCheckerAdapter) lives in ShipmentLabels.Infrastructure and
    // implements it there, so no ShoptetOrders type needs to reference ShipmentLabels directly.
    private static readonly HashSet<string> ShoptetOrdersShipmentLabelsAllowlist = new(StringComparer.Ordinal);

    public static TheoryData<ModuleBoundaryRule> Rules() => new()
```

**Step 2 — add the rule entry.**

Insert a new `ModuleBoundaryRule` as the last entry in the `Rules()` `TheoryData`. Find this exact text (the current last entry, ending the `TheoryData` initializer):

```csharp
        new ModuleBoundaryRule(
            Name: "FinancialOverview -> Catalog",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.FinancialOverview",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Catalog",
                "Anela.Heblo.Application.Features.Catalog",
                "Anela.Heblo.Persistence.Catalog",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal)),
    };
```

Replace it with:

```csharp
        new ModuleBoundaryRule(
            Name: "FinancialOverview -> Catalog",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.FinancialOverview",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Catalog",
                "Anela.Heblo.Application.Features.Catalog",
                "Anela.Heblo.Persistence.Catalog",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal)),

        new ModuleBoundaryRule(
            Name: "ShoptetOrders -> ShipmentLabels",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ShoptetOrders",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Application.Features.ShipmentLabels",
            },
            Allowlist: ShoptetOrdersShipmentLabelsAllowlist),
    };
```

**Step 3 — run the architecture test suite and confirm the new rule passes with zero violations.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"
```

This must pass for all rules in `Rules()`, including the new `"ShoptetOrders -> ShipmentLabels"` entry — confirming the three prior tasks fully removed `ShoptetOrders`' compile-time dependency on `ShipmentLabels`.

If it fails, check for a leftover reference: search for any remaining `Anela.Heblo.Application.Features.ShipmentLabels` usage under `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/` — everything under that path (including `IShipmentDeliveryChecker.cs`) must be free of it, since the adapter and its `IShipmentClient` dependency live in `ShipmentLabels.Infrastructure`, not `ShoptetOrders`.

**Step 4 — run the full backend test suite and build one last time.**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet format Anela.Heblo.sln --verify-no-changes
```

**Step 5 — commit.**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "Add ModuleBoundariesTests rule pinning ShoptetOrders -> ShipmentLabels"
```
