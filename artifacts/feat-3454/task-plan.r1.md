# Implementation Plan: Remove DataQuality → Catalog Application-layer boundary violation in ProductPairingDqtComparer

**Goal:** Replace `ProductPairingDqtComparer`'s direct dependency on Catalog's Application-layer `ICatalogResilienceService` with a DataQuality-owned contract (`IDqtResilienceService`), implemented by a Catalog-side adapter, so DataQuality no longer imports Catalog Application-layer internals.

**Architecture:** Pure dependency-direction refactor, structurally identical to the existing `IStockOperationQuery`/`DataQualityStockOperationQueryAdapter` and `IStockTakingQuery`/`DataQualityStockTakingQueryAdapter` pattern in this same module pair. DataQuality defines `IDqtResilienceService` in its own `Contracts/` folder; Catalog implements `DataQualityResilienceAdapter` in its `Infrastructure/` folder that delegates 1:1 to the existing `ICatalogResilienceService` singleton; Catalog registers the adapter in its own `CatalogModule.cs` (provider registers, per the documented rule). No retry/circuit-breaker/timeout behavior changes — this is a type-substitution refactor only.

**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions

---

### task: create-dqt-resilience-contract-and-adapter

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtResilienceService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityResilienceAdapter.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityResilienceAdapterTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:60-62`

This task creates the new DataQuality-owned contract, the Catalog-owned adapter that implements it, registers the adapter in Catalog's DI module, and adds a small delegation test for the adapter (mirroring the existing sibling adapter tests in the same test folder).

- [ ] Step 1: Create the new contract file `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtResilienceService.cs` with this exact content:

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IDqtResilienceService
{
    Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default);
}
```

- [ ] Step 2: Create the new adapter file `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityResilienceAdapter.cs` with this exact content:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class DataQualityResilienceAdapter : IDqtResilienceService
{
    private readonly ICatalogResilienceService _resilienceService;

    public DataQualityResilienceAdapter(ICatalogResilienceService resilienceService)
    {
        _resilienceService = resilienceService;
    }

    public Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default) =>
        _resilienceService.ExecuteWithResilienceAsync(operation, operationName, cancellationToken);
}
```

- [ ] Step 3: Open `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`. Find these two existing lines (currently at lines 60-62):

```csharp
        // DataQuality owns the query contracts; Catalog (this module) provides the adapter implementations.
        services.AddScoped<IStockOperationQuery, DataQualityStockOperationQueryAdapter>();
        services.AddScoped<IStockTakingQuery, DataQualityStockTakingQueryAdapter>();
```

Replace them with (adding the new registration immediately after, with its own explanatory comment matching the existing style):

```csharp
        // DataQuality owns the query contracts; Catalog (this module) provides the adapter implementations.
        services.AddScoped<IStockOperationQuery, DataQualityStockOperationQueryAdapter>();
        services.AddScoped<IStockTakingQuery, DataQualityStockTakingQueryAdapter>();
        // DataQuality owns the resilience contract; Catalog (this module) provides the adapter implementation.
        services.AddScoped<IDqtResilienceService, DataQualityResilienceAdapter>();
```

Note: `CatalogModule.cs` already has `using Anela.Heblo.Application.Features.DataQuality.Contracts;` at line 21 (used by `IStockOperationQuery`/`IStockTakingQuery`), so no new `using` directive is needed — `IDqtResilienceService` resolves from the same namespace.

- [ ] Step 4: Create the adapter test file `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityResilienceAdapterTests.cs` with this exact content:

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class DataQualityResilienceAdapterTests
{
    private readonly Mock<ICatalogResilienceService> _resilienceService = new();

    private DataQualityResilienceAdapter CreateAdapter() => new(_resilienceService.Object);

    [Fact]
    public async Task ExecuteWithResilienceAsync_DelegatesToUnderlyingService_WithSameArgumentsAndReturnValue()
    {
        // Arrange
        Func<CancellationToken, Task<int>> operation = _ => Task.FromResult(42);
        const string operationName = "TestOperation";
        using var cts = new CancellationTokenSource();

        _resilienceService
            .Setup(r => r.ExecuteWithResilienceAsync(operation, operationName, cts.Token))
            .ReturnsAsync(42);

        // Act
        var result = await CreateAdapter().ExecuteWithResilienceAsync(operation, operationName, cts.Token);

        // Assert
        result.Should().Be(42);
        _resilienceService.Verify(
            r => r.ExecuteWithResilienceAsync(operation, operationName, cts.Token),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_PropagatesException_WhenUnderlyingServiceThrows()
    {
        // Arrange
        Func<CancellationToken, Task<int>> operation = _ => Task.FromResult(0);
        const string operationName = "FailingOperation";

        _resilienceService
            .Setup(r => r.ExecuteWithResilienceAsync(operation, operationName, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var act = () => CreateAdapter().ExecuteWithResilienceAsync(operation, operationName, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }
}
```

- [ ] Step 5: From the repository root (`/home/user/worktrees/feature-3454-Arch-Review-Dataquality-Productpairingdqtcomparer`), build the solution to verify the new contract, adapter, and DI registration compile:

```bash
dotnet build Anela.Heblo.sln
```

Confirm the build succeeds with no errors. `ProductPairingDqtComparer` still uses `ICatalogResilienceService` at this point (unchanged), so this step only proves the new types and DI wiring compile alongside the existing code.

- [ ] Step 6: Run the new adapter test to confirm it passes:

```bash
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~DataQualityResilienceAdapterTests"
```

Confirm both tests pass (2 total).

- [ ] Step 7: Commit:

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtResilienceService.cs backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityResilienceAdapter.cs backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityResilienceAdapterTests.cs backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
git commit -m "Add IDqtResilienceService contract and Catalog-side adapter"
```

---

### task: switch-product-pairing-comparer-to-dqt-resilience-contract

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs:1-28`
- Modify: `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs:1-15`

This task switches `ProductPairingDqtComparer`'s constructor dependency from the Catalog-owned `ICatalogResilienceService` to the new DataQuality-owned `IDqtResilienceService`, and updates the existing unit test's mock type to match. No method body or test assertion changes — this is a pure type substitution enabled by the identical interface shape.

- [ ] Step 1: Open `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs`. Replace the `using` block at the top of the file (lines 1-5):

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;
```

with:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;
```

- [ ] Step 2: In the same file, replace the field declaration and constructor (lines 9-28):

```csharp
public class ProductPairingDqtComparer : IDriftDqtComparer
{
    private readonly IEshopStockClient _eshopStockClient;
    private readonly IErpStockClient _erpStockClient;
    private readonly ICatalogResilienceService _resilienceService;
    private readonly ILogger<ProductPairingDqtComparer> _logger;

    public DqtTestType TestType => DqtTestType.ProductPairing;

    public ProductPairingDqtComparer(
        IEshopStockClient eshopStockClient,
        IErpStockClient erpStockClient,
        ICatalogResilienceService resilienceService,
        ILogger<ProductPairingDqtComparer> logger)
    {
        _eshopStockClient = eshopStockClient;
        _erpStockClient = erpStockClient;
        _resilienceService = resilienceService;
        _logger = logger;
    }
```

with:

```csharp
public class ProductPairingDqtComparer : IDriftDqtComparer
{
    private readonly IEshopStockClient _eshopStockClient;
    private readonly IErpStockClient _erpStockClient;
    private readonly IDqtResilienceService _resilienceService;
    private readonly ILogger<ProductPairingDqtComparer> _logger;

    public DqtTestType TestType => DqtTestType.ProductPairing;

    public ProductPairingDqtComparer(
        IEshopStockClient eshopStockClient,
        IErpStockClient erpStockClient,
        IDqtResilienceService resilienceService,
        ILogger<ProductPairingDqtComparer> logger)
    {
        _eshopStockClient = eshopStockClient;
        _erpStockClient = erpStockClient;
        _resilienceService = resilienceService;
        _logger = logger;
    }
```

Do not change anything else in this file — `CompareAsync` and `IsSellable` are unchanged.

- [ ] Step 3: Open `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs`. Replace the `using` block at the top of the file (lines 1-7):

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
```

with:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
```

- [ ] Step 4: In the same file, replace this line (line 15):

```csharp
    private readonly Mock<ICatalogResilienceService> _resilienceMock = new();
```

with:

```csharp
    private readonly Mock<IDqtResilienceService> _resilienceMock = new();
```

Do not change anything else in this file — the constructor call in `CreateSut()`, all 5 test methods, and all assertions are unchanged since `IDqtResilienceService.ExecuteWithResilienceAsync<T>` has the identical signature to `ICatalogResilienceService.ExecuteWithResilienceAsync<T>`.

- [ ] Step 5: From the repository root, build the solution:

```bash
dotnet build Anela.Heblo.sln
```

Confirm the build succeeds with no errors and no warnings about unused `using Anela.Heblo.Application.Features.Catalog.Infrastructure;` in either modified file (both files' Catalog-Infrastructure `using` directives were removed in steps 1 and 3).

- [ ] Step 6: Run the comparer's unit tests:

```bash
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~ProductPairingDqtComparerTests"
```

Confirm all 5 existing tests pass: `CompareAsync_ReturnsEmpty_WhenAllProductsPaired`, `CompareAsync_ReturnsMissingInErp_WhenShoptetProductNotInErp`, `CompareAsync_ReturnsMissingInErpAndPairCodeUnresolved_WhenPairCodeNotInErp`, `CompareAsync_ReturnsMissingInShoptet_OnlyForSellableErpProducts`, `CompareAsync_WrapsBothListCalls_WithResilience`.

- [ ] Step 7: Verify no file under DataQuality's `Services/` folder references the Catalog Application-layer namespace anymore (this scans the whole folder, not just the file changed in this task, to confirm the module-wide boundary claim from the spec — `IEshopStockClient`/`IErpStockClient` are Domain-layer, not Application-layer, so they are correctly excluded by this pattern):

```bash
grep -rn "using Anela.Heblo.Application.Features.Catalog" backend/src/Anela.Heblo.Application/Features/DataQuality/Services/
```

Confirm this returns no matches (empty output).

- [ ] Step 8: Commit:

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs
git commit -m "Switch ProductPairingDqtComparer to DataQuality-owned IDqtResilienceService"
```

---

### task: update-architecture-boundary-allowlist

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs:151-157`

This task removes the now-stale `ProductPairingDqtComparer -> ICatalogResilienceService` entry from the `DataQuality -> Catalog` architecture-boundary allowlist, since that dependency no longer exists after the previous task. The four remaining, genuinely out-of-scope entries (`IEshopStockClient`, `IErpStockClient`, `ErpStock`, `ProductType`, `EshopStock`) stay untouched. This file enforces module-boundary rules via a reflection-based xUnit test (`backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`) with a `"DataQuality -> Catalog"` rule driven by the `DataQualityCatalogAllowlist` set — leaving a stale entry would contradict that file's own documented convention that entries are removed once the underlying violation is fixed.

- [ ] Step 1: Open `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`. Find this block (currently at lines 149-163):

```csharp
    private static readonly HashSet<string> DataQualityCatalogAllowlist = new(StringComparer.Ordinal)
    {
        // ProductPairingDqtComparer reads eshop/erp catalog clients to compare product pairing,
        // wrapped in ICatalogResilienceService for transient-fault protection.
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.IEshopStockClient",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.IErpStockClient",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.ErpStock",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.ProductType",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Application.Features.Catalog.Infrastructure.ICatalogResilienceService",

        // Compiler-generated async state machines and lambdas for CompareAsync capture EshopStock.
        // The declaring-type check covers nested types (<CompareAsync>d__6, <<CompareAsync>b__6_1>d)
        // via this single parent entry.
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.EshopStock",
    };
```

Replace it with (the `ICatalogResilienceService` line removed, and the explanatory comment above the first four entries trimmed so it no longer references resilience wrapping that no longer applies):

```csharp
    private static readonly HashSet<string> DataQualityCatalogAllowlist = new(StringComparer.Ordinal)
    {
        // ProductPairingDqtComparer reads eshop/erp catalog clients to compare product pairing.
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.IEshopStockClient",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.IErpStockClient",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.ErpStock",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.ProductType",

        // Compiler-generated async state machines and lambdas for CompareAsync capture EshopStock.
        // The declaring-type check covers nested types (<CompareAsync>d__6, <<CompareAsync>b__6_1>d)
        // via this single parent entry.
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.EshopStock",
    };
```

Do not change the block comment above this set (currently at lines 145-148, describing the `DataQualityCatalogAllowlist` follow-up tracking) — it still accurately describes the four remaining out-of-scope entries and the tracked `IProductPairingQuery` follow-up.

- [ ] Step 2: From the repository root, build the solution:

```bash
dotnet build Anela.Heblo.sln
```

Confirm the build succeeds with no errors.

- [ ] Step 3: Run the architecture boundary tests:

```bash
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~ModuleBoundariesTests"
```

Confirm all tests in this file pass, including the `"DataQuality -> Catalog"` rule — this proves `ProductPairingDqtComparer` no longer references `Anela.Heblo.Application.Features.Catalog.Infrastructure.ICatalogResilienceService` (or any other non-allowlisted Catalog Application-layer type) via reflection, independent of the earlier manual `grep` check.

- [ ] Step 4: Confirm no remaining reference to `ICatalogResilienceService` exists anywhere in the allowlist file:

```bash
grep -n "ICatalogResilienceService" backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
```

Confirm this returns no matches (empty output).

- [ ] Step 5: Run the full test suite one final time to confirm nothing else regressed from the full set of changes across all three tasks:

```bash
dotnet test Anela.Heblo.sln
```

Confirm the run completes with 0 failures.

- [ ] Step 6: Run `dotnet format` to confirm formatting is clean across all changed files:

```bash
dotnet format Anela.Heblo.sln --verify-no-changes
```

If this reports formatting issues, run `dotnet format Anela.Heblo.sln` (without `--verify-no-changes`) to auto-fix, then re-stage the affected files before committing.

- [ ] Step 7: Commit:

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "Remove stale ICatalogResilienceService entry from DataQuality->Catalog allowlist"
```
