# Decouple Manufacture from ICatalogRepository Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace every direct `ICatalogRepository` injection in the Manufacture module with a Manufacture-owned `IManufactureCatalogSource` contract implemented by a Catalog-side adapter, and enforce the boundary via `ModuleBoundariesTests`.

**Architecture:** Mirror of the existing inverse pattern (`ICatalogManufactureSource` owned by Catalog, implemented by Manufacture). Add the consumer-owned contract in `Application/Features/Manufacture/Contracts/`, the provider-owned adapter in `Application/Features/Catalog/Infrastructure/`, register the adapter in `CatalogModule`, then migrate 11 consumer files + 9 test files + add an architectural rule with an allowlist for the deliberate `CatalogAggregate` leak.

**Tech Stack:** C# 12 / .NET 8, xUnit + Moq + FluentAssertions, MediatR, reflection-based architectural tests.

---

## Background and Authoritative References

Read these before starting:

- Spec: `artifacts/feat-arch-review-manufacture-direct-use-of-ic/spec.r1.md`
- Arch review (binding amendments in §"Specification Amendments"): `artifacts/feat-arch-review-manufacture-direct-use-of-ic/arch-review.r1.md`
- Canonical contract pattern (Catalog-owned, Manufacture-implemented): `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogManufactureSource.cs` and `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapter.cs`
- Architectural rule infrastructure: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`
- Cross-module communication recipe: `docs/architecture/development_guidelines.md` section "Cross-Module Communication Example: ILeafletKnowledgeSource"
- Underlying `IReadOnlyRepository<,>` contract being delegated to: `backend/src/Anela.Heblo.Xcc/Persistance/IReadOnlyRepository.cs` (NB: `GetAllAsync` returns `Task<IEnumerable<TEntity>>`, **not** `Task<IReadOnlyList<TEntity>>` — the spec FR-1 typo is corrected by arch-review §Specification Amendments #1)

## File Structure

**New source files (2):**

```
backend/src/Anela.Heblo.Application/Features/
├── Manufacture/Contracts/
│   └── IManufactureCatalogSource.cs              # NEW — consumer-owned contract
└── Catalog/Infrastructure/
    └── CatalogManufactureCatalogSourceAdapter.cs # NEW — provider-owned adapter
```

The adapter is deliberately named `CatalogManufactureCatalogSourceAdapter` (Catalog prefix) because `ManufactureCatalogSourceAdapter` (no prefix) is already taken by the reverse-direction adapter in `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapter.cs`. Do not rename the existing adapter — out of scope.

**Modified source files (12):**

- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — add one `AddScoped<IManufactureCatalogSource, CatalogManufactureCatalogSourceAdapter>()` line in the cross-module adapter block.
- 11 Manufacture files (verified by `grep -l "ICatalogRepository" backend/src/Anela.Heblo.Application/Features/Manufacture/`):
  - `Services/BatchPlanningService.cs`
  - `Services/ResidueDistributionCalculator.cs`
  - `UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs`
  - `UseCases/GetStockAnalysis/GetManufacturingStockAnalysisHandler.cs`
  - `UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs`
  - `UseCases/CalculateBatchByIngredient/CalculateBatchByIngredientHandler.cs`
  - `UseCases/GetManufactureOutput/GetManufactureOutputHandler.cs`
  - `UseCases/CalculatedBatchSize/CalculatedBatchSizeHandler.cs`
  - `UseCases/SubmitManufactureStockTaking/SubmitManufactureStockTakingHandler.cs`
  - `UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs`
  - `UseCases/GetSemiproductRecipePdf/GetSemiproductRecipePdfHandler.cs`

For every modified Manufacture file:
- Rename constructor parameter `ICatalogRepository catalogRepository` → `IManufactureCatalogSource catalogSource`
- Rename private field `_catalogRepository` → `_catalogSource`
- Swap the `using Anela.Heblo.Domain.Features.Catalog;` directive to `using Anela.Heblo.Application.Features.Manufacture.Contracts;` (keep `using Anela.Heblo.Domain.Features.Catalog;` if the file also uses `CatalogAggregate`, `ProductType`, etc.)
- Replace `_catalogRepository.GetByIdAsync(...)` / `.GetAllAsync(...)` / `.GetByIdsAsync(...)` → `_catalogSource.GetByIdAsync(...)` / `.GetAllAsync(...)` / `.GetByIdsAsync(...)` (signatures match, no other call-site changes)

**Modified test files (10) and new test file (1):**

- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — add `ManufactureCatalogAllowlist` + new `Rules()` entry.
- 9 Manufacture test files (verified by `grep -l "Mock<ICatalogRepository>" backend/test/Anela.Heblo.Tests/Features/Manufacture/`):
  - `BatchPlanningServiceTests.cs`
  - `CalculateBatchByIngredientHandlerTests.cs`
  - `CalculateBatchBySizeHandlerTests.cs` (covers `CalculatedBatchSizeHandler`)
  - `CalculateBatchPlanHandlerTests.cs`
  - `CalculatedBatchSizeHandlerLastStockTakingTests.cs`
  - `CreateManufactureOrderHandlerTests.cs` (also `CreateManufactureOrderHandlerSinglePhaseTests.cs` shares fixture — check both)
  - `CreateManufactureOrderHandlerSinglePhaseTests.cs`
  - `GetManufactureOutputHandlerTests.cs`
  - `GetManufacturingStockAnalysisHandlerTests.cs`
  - `GetSemiproductRecipePdfHandlerTests.cs`
  - `Services/ResidueDistributionCalculatorTests.cs`
  - `SubmitManufactureStockTakingHandlerTests.cs`
  - `UpdateManufactureOrderStatusHandlerConditionsTests.cs`
  - `UpdateManufactureOrderStatusHandlerTests.cs`

  (The grep at implementation time is authoritative — use it to confirm the list. The grep performed at planning time returned 14 files including both `CalculateBatchBySizeHandlerTests.cs` and `CalculatedBatchSizeHandlerLastStockTakingTests.cs` which both touch `CalculatedBatchSizeHandler`.)

  For every modified test file:
  - Swap `Mock<ICatalogRepository>` → `Mock<IManufactureCatalogSource>`
  - Swap `new Mock<ICatalogRepository>()` → `new Mock<IManufactureCatalogSource>()`
  - Swap `using Anela.Heblo.Domain.Features.Catalog;` → keep if still used for `CatalogAggregate`, add `using Anela.Heblo.Application.Features.Manufacture.Contracts;`
  - `Setup(x => x.GetByIdAsync(...))` / `.GetAllAsync(...)` / `.GetByIdsAsync(...)` calls stay unchanged — the signatures match by design.

- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogManufactureCatalogSourceAdapterTests.cs` — NEW. Three pass-through tests mirroring `LogisticsCatalogSourceAdapterTests.cs`.

## Ordering Rationale (per arch-review §Amendments #4)

The boundary test is added with an **empty allowlist** before the consumer migration so the test fails with a complete list of `CatalogAggregate` / `ProductType` / etc. references. That list is then pasted verbatim into `ManufactureCatalogAllowlist` (grouped with explanatory comments) and the test re-run. Without this order, a handler-by-handler migration could miss compiler-generated state-machine references the reflection walker would catch.

The plan executes in this order:

1. Add contract `IManufactureCatalogSource`.
2. Add adapter `CatalogManufactureCatalogSourceAdapter` and its three pass-through tests.
3. Register adapter in `CatalogModule.AddCatalogModule()`.
4. Add `Manufacture -> Catalog` boundary rule with empty allowlist; verify test fails with one expected violation (the contract surface itself).
5. Migrate the 11 consumer files (constructor/field/call-site swap).
6. Migrate the ~10 test files (mock generic swap).
7. Re-run the boundary test, paste the residual violations into `ManufactureCatalogAllowlist` grouped by referenced type with one-line `//` comments.
8. Final `dotnet build` + `dotnet test --filter "FullyQualifiedName~Manufacture|FullyQualifiedName~ModuleBoundaries"`.

---

## Task 1: Add `IManufactureCatalogSource` contract

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/IManufactureCatalogSource.cs`

- [ ] **Step 1: Verify the target folder exists**

Run:

```bash
ls backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ 2>&1 || mkdir -p backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts
```

Expected: directory listing, or directory is created. Either outcome is fine.

- [ ] **Step 2: Create the contract file**

Write `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/IManufactureCatalogSource.cs` with this exact content:

```csharp
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

/// <summary>
/// Manufacture-owned read abstraction over Catalog products. Implemented by the Catalog
/// module via CatalogManufactureCatalogSourceAdapter. Returns CatalogAggregate as a
/// deliberate pragmatic leak — symmetric to ICatalogManufactureSource returning
/// ManufactureHistoryRecord. Allowlisted in ModuleBoundariesTests under
/// "Manufacture -> Catalog". Follow-up: introduce Manufacture-owned ProductCatalogSnapshot DTO.
/// </summary>
public interface IManufactureCatalogSource
{
    Task<CatalogAggregate?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, CatalogAggregate>> GetByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<CatalogAggregate>> GetAllAsync(CancellationToken cancellationToken = default);
}
```

Notes:
- Return types for `GetByIdAsync` and `GetAllAsync` match `IReadOnlyRepository<CatalogAggregate, string>` exactly so adapter delegation requires no transformation. `GetByIdsAsync` matches the extension/specialization declared on `ICatalogRepository` (returns `IReadOnlyDictionary<string, CatalogAggregate>`).
- The `using Anela.Heblo.Domain.Features.Catalog;` directive is intentional and will be allowlisted in Task 4 (deliberate leak per FR-1 / arch-review §Amendments #2).

- [ ] **Step 3: Verify it compiles**

Run:

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds with no errors and no new warnings.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/IManufactureCatalogSource.cs
git commit -m "feat(manufacture): add IManufactureCatalogSource consumer-owned contract"
```

---

## Task 2: Add `CatalogManufactureCatalogSourceAdapter` (test-first)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogManufactureCatalogSourceAdapterTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogManufactureCatalogSourceAdapter.cs`

- [ ] **Step 1: Write the failing adapter tests**

Write `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogManufactureCatalogSourceAdapterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogManufactureCatalogSourceAdapterTests
{
    private readonly Mock<ICatalogRepository> _repository = new();

    private IManufactureCatalogSource CreateAdapter() =>
        new CatalogManufactureCatalogSourceAdapter(_repository.Object);

    [Fact]
    public async Task GetByIdAsync_ForwardsCallAndReturnsRepositoryResult()
    {
        var ct = new CancellationTokenSource().Token;
        var expected = new CatalogAggregate { ProductCode = "ABC", ProductName = "Test" };
        _repository.Setup(r => r.GetByIdAsync("ABC", ct)).ReturnsAsync(expected);

        var result = await CreateAdapter().GetByIdAsync("ABC", ct);

        result.Should().BeSameAs(expected);
        _repository.Verify(r => r.GetByIdAsync("ABC", ct), Times.Once);
    }

    [Fact]
    public async Task GetByIdsAsync_ForwardsCallAndReturnsRepositoryResult()
    {
        var ct = new CancellationTokenSource().Token;
        var ids = new[] { "A", "B" };
        IReadOnlyDictionary<string, CatalogAggregate> expected = new Dictionary<string, CatalogAggregate>
        {
            ["A"] = new CatalogAggregate { ProductCode = "A" },
            ["B"] = new CatalogAggregate { ProductCode = "B" },
        };
        _repository.Setup(r => r.GetByIdsAsync(ids, ct)).ReturnsAsync(expected);

        var result = await CreateAdapter().GetByIdsAsync(ids, ct);

        result.Should().BeSameAs(expected);
        _repository.Verify(r => r.GetByIdsAsync(ids, ct), Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_ForwardsCallAndReturnsRepositoryResult()
    {
        var ct = new CancellationTokenSource().Token;
        IEnumerable<CatalogAggregate> expected = new[]
        {
            new CatalogAggregate { ProductCode = "A" },
            new CatalogAggregate { ProductCode = "B" },
        };
        _repository.Setup(r => r.GetAllAsync(ct)).ReturnsAsync(expected);

        var result = await CreateAdapter().GetAllAsync(ct);

        result.Should().BeSameAs(expected);
        _repository.Verify(r => r.GetAllAsync(ct), Times.Once);
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CatalogManufactureCatalogSourceAdapter"
```

Expected: build failure with errors like `error CS0246: The type or namespace name 'CatalogManufactureCatalogSourceAdapter' could not be found` — this confirms RED.

- [ ] **Step 3: Implement the adapter**

Write `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogManufactureCatalogSourceAdapter.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Catalog-side adapter that implements the Manufacture-owned IManufactureCatalogSource
/// contract by delegating to the internal ICatalogRepository. DI registration is in
/// CatalogModule.AddCatalogModule(). See ModuleBoundariesTests "Manufacture -> Catalog"
/// rule and its ManufactureCatalogAllowlist for the deliberate CatalogAggregate leak.
/// </summary>
internal sealed class CatalogManufactureCatalogSourceAdapter : IManufactureCatalogSource
{
    private readonly ICatalogRepository _repository;

    public CatalogManufactureCatalogSourceAdapter(ICatalogRepository repository)
    {
        _repository = repository;
    }

    public Task<CatalogAggregate?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        _repository.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyDictionary<string, CatalogAggregate>> GetByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default) =>
        _repository.GetByIdsAsync(ids, cancellationToken);

    public Task<IEnumerable<CatalogAggregate>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _repository.GetAllAsync(cancellationToken);
}
```

- [ ] **Step 4: Run the adapter tests and verify they pass**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CatalogManufactureCatalogSourceAdapter"
```

Expected: `Passed!  - Failed: 0, Passed: 3, Skipped: 0`.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogManufactureCatalogSourceAdapter.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogManufactureCatalogSourceAdapterTests.cs
git commit -m "feat(catalog): add CatalogManufactureCatalogSourceAdapter implementing IManufactureCatalogSource"
```

---

## Task 3: Register adapter in `CatalogModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` (cross-module adapter block around lines 48–57)

- [ ] **Step 1: Add the using directive (if not already present)**

Open `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`. Verify `using Anela.Heblo.Application.Features.Manufacture.Contracts;` is at the top of the file. If not, add it next to the other `using Anela.Heblo.Application.Features.*` directives (alphabetical ordering preserved).

- [ ] **Step 2: Add the DI registration**

Locate the cross-module adapter block:

```csharp
        services.AddScoped<IMaterialCatalogService, PurchaseMaterialCatalogAdapter>();
        services.AddScoped<IPurchasePriceRecalculationService, CatalogPurchasePriceRecalculationAdapter>();
        services.AddTransient<IAnalyticsProductSource, CatalogAnalyticsSourceAdapter>();
        services.AddTransient<ILogisticsCatalogSource, LogisticsCatalogSourceAdapter>();
        services.AddTransient<ILogisticsStockOperationService, LogisticsStockOperationAdapter>();
        // Logistics owns the query contract; Catalog (this module) provides the adapter implementation.
        services.AddTransient<ILogisticsStockOperationQueryService, LogisticsStockOperationQueryAdapter>();
        // DataQuality owns the query contracts; Catalog (this module) provides the adapter implementations.
        services.AddScoped<IStockOperationQuery, DataQualityStockOperationQueryAdapter>();
        services.AddScoped<IStockTakingQuery, DataQualityStockTakingQueryAdapter>();
```

Append immediately after the `DataQualityStockTakingQueryAdapter` line:

```csharp
        // Cross-module contract: Catalog implements Manufacture's IManufactureCatalogSource via adapter.
        // DI registration is owned by the provider (Catalog), not the consumer (Manufacture).
        services.AddScoped<IManufactureCatalogSource, CatalogManufactureCatalogSourceAdapter>();
```

Lifetime is `Scoped` to mirror the existing `ManufactureModule.cs:59` registration of `ICatalogManufactureSource`. The underlying `ICatalogRepository` is `Transient` (per `CatalogModule.cs:45`), which is safe for a `Scoped` consumer per ASP.NET Core DI rules (the transient is materialized into the scope and reused for the scope's lifetime).

- [ ] **Step 3: Build and verify**

Run:

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds with no errors and no new warnings. (No tests for DI registration itself; a smoke test for the registration is the end-to-end `dotnet test` after Task 7 — if the DI graph is broken, every Manufacture handler test that instantiates from DI will fail.)

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
git commit -m "feat(catalog): register CatalogManufactureCatalogSourceAdapter in CatalogModule"
```

---

## Task 4: Add `Manufacture -> Catalog` boundary rule with empty allowlist (expected to fail)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

- [ ] **Step 1: Add the empty allowlist constant**

Open `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`. Add this allowlist constant immediately above the `public static TheoryData<ModuleBoundaryRule> Rules()` method (around line 200, right after `DataQualityInvoicesAllowlist`):

```csharp
    // Allowlist for Manufacture -> Catalog. Populated after running the boundary test with
    // an empty allowlist and pasting the residual violations grouped by referenced type.
    // The deliberate pragmatic leak (CatalogAggregate and its property types flowing through
    // IManufactureCatalogSource) is symmetric to the CatalogManufactureAllowlist entries for
    // ManufactureHistoryRecord. Follow-up: introduce Manufacture-owned ProductCatalogSnapshot
    // DTO and map in CatalogManufactureCatalogSourceAdapter.
    private static readonly HashSet<string> ManufactureCatalogAllowlist = new(StringComparer.Ordinal);
```

- [ ] **Step 2: Add the boundary rule to `Rules()`**

Append this entry to the `TheoryData<ModuleBoundaryRule>` returned by `Rules()` (after the existing `"DataQuality -> Invoices"` entry, before the closing `};`):

```csharp
        new ModuleBoundaryRule(
            Name: "Manufacture -> Catalog",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Manufacture",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Catalog",
                "Anela.Heblo.Application.Features.Catalog",
                "Anela.Heblo.Persistence.Catalog",
            },
            Allowlist: ManufactureCatalogAllowlist),
```

- [ ] **Step 3: Run the boundary test — expect failure**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces" --logger "console;verbosity=detailed"
```

Expected: the test for the `"Manufacture -> Catalog"` rule **fails** with a long list of violations. The failure message will start with `Manufacture -> Catalog: consumer types must not reference provider-owned namespaces.` followed by lines like:

```
  Anela.Heblo.Application.Features.Manufacture.UseCases.<X>.<Y>Handler -> Anela.Heblo.Domain.Features.Catalog.ICatalogRepository (via ctor parameter ...)
  Anela.Heblo.Application.Features.Manufacture.UseCases.<X>.<Y>Handler -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate (via ...)
  Anela.Heblo.Application.Features.Manufacture.Contracts.IManufactureCatalogSource -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate (via method GetByIdAsync return)
  ...
```

Note: at this point all 11 handlers still inject `ICatalogRepository`, so the `ICatalogRepository` and the existing `CatalogAggregate`/`ProductType` references will dominate. **Save the full output of this run** — we will diff it against the post-migration output to identify the residual (allowlisted) violations in Task 7. Pipe to a file if useful:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces" --logger "console;verbosity=detailed" > /tmp/boundary-pre-migration.txt 2>&1; grep -E "Manufacture -> Catalog|via " /tmp/boundary-pre-migration.txt | head -100
```

- [ ] **Step 4: Commit (the failing-test scaffolding is intentional and will be made green by Tasks 5–7)**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test(arch): add Manufacture -> Catalog boundary rule (allowlist populated in follow-up commits)"
```

---

## Task 5: Migrate Manufacture services and handlers off `ICatalogRepository`

This task has 11 file edits, each following an identical pattern. For each file, apply: constructor parameter rename, field rename, call-site receiver rename, and `using` directive swap. Wait until the end to run the full test suite — the boundary test will still fail until Task 7 populates the allowlist, but the build must stay green throughout.

**Files (verify with a fresh `grep -l "ICatalogRepository" backend/src/Anela.Heblo.Application/Features/Manufacture/` at the start):**
- Modify all 11 files listed in the File Structure section.

### Task 5.1: `Services/BatchPlanningService.cs`

- [ ] **Step 1: Apply the edits**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/BatchPlanningService.cs`:

Add `using Anela.Heblo.Application.Features.Manufacture.Contracts;` at the top (keep `using Anela.Heblo.Domain.Features.Catalog;` because the file still uses `CatalogAggregate`).

Replace the field block:

```csharp
    private readonly ICatalogRepository _catalogRepository;
```

with:

```csharp
    private readonly IManufactureCatalogSource _catalogSource;
```

Replace the constructor parameter:

```csharp
    public BatchPlanningService(
        ICatalogRepository catalogRepository,
        IManufactureClient manufactureClient,
        IBatchDistributionCalculator batchDistributionCalculator,
        IConsumptionRateCalculator consumptionRateCalculator,
        ILogger<BatchPlanningService> logger)
    {
        _catalogRepository = catalogRepository;
```

with:

```csharp
    public BatchPlanningService(
        IManufactureCatalogSource catalogSource,
        IManufactureClient manufactureClient,
        IBatchDistributionCalculator batchDistributionCalculator,
        IConsumptionRateCalculator consumptionRateCalculator,
        ILogger<BatchPlanningService> logger)
    {
        _catalogSource = catalogSource;
```

Replace every body occurrence of `_catalogRepository.` with `_catalogSource.` (verify with grep there are no other references after the change).

- [ ] **Step 2: Build**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: green.

### Task 5.2: `Services/ResidueDistributionCalculator.cs`

- [ ] **Step 1: Apply the edits**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ResidueDistributionCalculator.cs`:

Add `using Anela.Heblo.Application.Features.Manufacture.Contracts;`. Keep `using Anela.Heblo.Domain.Features.Catalog;` (used for `ProductType`).

Replace field:

```csharp
    private readonly ICatalogRepository _catalogRepository;
```

with:

```csharp
    private readonly IManufactureCatalogSource _catalogSource;
```

Replace constructor:

```csharp
    public ResidueDistributionCalculator(IManufactureClient manufactureClient, ICatalogRepository catalogRepository)
    {
        _manufactureClient = manufactureClient;
        _catalogRepository = catalogRepository;
    }
```

with:

```csharp
    public ResidueDistributionCalculator(IManufactureClient manufactureClient, IManufactureCatalogSource catalogSource)
    {
        _manufactureClient = manufactureClient;
        _catalogSource = catalogSource;
    }
```

Replace body: `_catalogRepository.GetByIdAsync(...)` → `_catalogSource.GetByIdAsync(...)`.

- [ ] **Step 2: Build**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: green.

### Task 5.3: `UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs`

- [ ] **Step 1: Apply the edits**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs`:

Add `using Anela.Heblo.Application.Features.Manufacture.Contracts;`. Keep `using Anela.Heblo.Domain.Features.Catalog;` (used for `ProductType`).

Replace field `private readonly ICatalogRepository _catalogRepository;` with `private readonly IManufactureCatalogSource _catalogSource;`.

Replace the constructor parameter and assignment exactly as in Task 5.1.

Replace `_catalogRepository.GetByIdAsync(request.ProductCode, cancellationToken)` with `_catalogSource.GetByIdAsync(request.ProductCode, cancellationToken)`.

- [ ] **Step 2: Build**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: green.

### Task 5.4: `UseCases/GetStockAnalysis/GetManufacturingStockAnalysisHandler.cs`

- [ ] **Step 1: Apply the edits**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetStockAnalysis/GetManufacturingStockAnalysisHandler.cs`:

Add `using Anela.Heblo.Application.Features.Manufacture.Contracts;`. Keep `using Anela.Heblo.Domain.Features.Catalog;` (used for `ProductType`).

Replace field `private readonly ICatalogRepository _catalogRepository;` with `private readonly IManufactureCatalogSource _catalogSource;`.

Replace the constructor's first parameter from `ICatalogRepository catalogRepository` to `IManufactureCatalogSource catalogSource`, and the assignment `_catalogRepository = catalogRepository;` to `_catalogSource = catalogSource;`.

Replace `_catalogRepository.GetAllAsync(cancellationToken)` with `_catalogSource.GetAllAsync(cancellationToken)`.

- [ ] **Step 2: Build**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: green.

### Task 5.5: `UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs`

- [ ] **Step 1: Apply the edits**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs`:

Add `using Anela.Heblo.Application.Features.Manufacture.Contracts;`. Keep `using Anela.Heblo.Domain.Features.Catalog;` if other catalog types are referenced (review imports).

Replace field `private readonly ICatalogRepository _catalogRepository;` with `private readonly IManufactureCatalogSource _catalogSource;`.

Replace constructor parameter `ICatalogRepository catalogRepository` → `IManufactureCatalogSource catalogSource` and the matching assignment.

Replace every `_catalogRepository.` with `_catalogSource.` in the body (this file uses both `GetByIdAsync` and `GetByIdsAsync`).

- [ ] **Step 2: Build**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: green.

### Task 5.6: `UseCases/CalculateBatchByIngredient/CalculateBatchByIngredientHandler.cs`

- [ ] **Step 1: Apply the edits**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchByIngredient/CalculateBatchByIngredientHandler.cs`:

Same pattern as Task 5.3 (`GetByIdAsync` only). Add `using Anela.Heblo.Application.Features.Manufacture.Contracts;`; swap field, ctor param, and assignment; replace `_catalogRepository.` with `_catalogSource.`.

- [ ] **Step 2: Build**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: green.

### Task 5.7: `UseCases/GetManufactureOutput/GetManufactureOutputHandler.cs`

- [ ] **Step 1: Apply the edits**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOutput/GetManufactureOutputHandler.cs`:

Same pattern as Task 5.3 (`GetByIdAsync` only). Add `using Anela.Heblo.Application.Features.Manufacture.Contracts;`; swap field, ctor param, and assignment; replace `_catalogRepository.` with `_catalogSource.`.

- [ ] **Step 2: Build**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: green.

### Task 5.8: `UseCases/CalculatedBatchSize/CalculatedBatchSizeHandler.cs`

- [ ] **Step 1: Apply the edits**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculatedBatchSize/CalculatedBatchSizeHandler.cs`:

Same pattern as Task 5.3 (`GetByIdAsync` only). Swap field, ctor param, assignment, call site.

- [ ] **Step 2: Build**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: green.

### Task 5.9: `UseCases/SubmitManufactureStockTaking/SubmitManufactureStockTakingHandler.cs`

- [ ] **Step 1: Apply the edits**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufactureStockTaking/SubmitManufactureStockTakingHandler.cs`:

Same pattern as Task 5.3. Swap field, ctor param, assignment, call site (`GetByIdAsync`).

- [ ] **Step 2: Build**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: green.

### Task 5.10: `UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs`

- [ ] **Step 1: Apply the edits**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs`:

Same pattern as Task 5.3. Swap field, ctor param, assignment, call site (`GetByIdAsync`).

- [ ] **Step 2: Build**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: green.

### Task 5.11: `UseCases/GetSemiproductRecipePdf/GetSemiproductRecipePdfHandler.cs`

- [ ] **Step 1: Apply the edits**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetSemiproductRecipePdf/GetSemiproductRecipePdfHandler.cs`:

Same pattern as Task 5.3. Swap field, ctor param, assignment, call site (`GetByIdAsync`).

- [ ] **Step 2: Verify no remaining `ICatalogRepository` references in Manufacture source**

Run:

```bash
grep -rln "ICatalogRepository" backend/src/Anela.Heblo.Application/Features/Manufacture/ || echo "OK — no references remain"
```

Expected: `OK — no references remain`.

- [ ] **Step 3: Final build for source side**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: green, no new warnings.

- [ ] **Step 4: Commit Task 5 changes**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/
git commit -m "refactor(manufacture): replace ICatalogRepository injections with IManufactureCatalogSource"
```

---

## Task 6: Migrate Manufacture unit tests to mock `IManufactureCatalogSource`

This task swaps mock generic type in 9–10 test files. Pattern is identical per file; the test bodies do not change because all three method signatures match between the new contract and `ICatalogRepository`.

**Files (re-verify with `grep -l "Mock<ICatalogRepository>" backend/test/Anela.Heblo.Tests/Features/Manufacture/` at the start):**
- `BatchPlanningServiceTests.cs`
- `CalculateBatchByIngredientHandlerTests.cs`
- `CalculateBatchBySizeHandlerTests.cs`
- `CalculateBatchPlanHandlerTests.cs`
- `CalculatedBatchSizeHandlerLastStockTakingTests.cs`
- `CreateManufactureOrderHandlerTests.cs`
- `CreateManufactureOrderHandlerSinglePhaseTests.cs`
- `GetManufactureOutputHandlerTests.cs`
- `GetManufacturingStockAnalysisHandlerTests.cs`
- `GetSemiproductRecipePdfHandlerTests.cs`
- `Services/ResidueDistributionCalculatorTests.cs`
- `SubmitManufactureStockTakingHandlerTests.cs`
- `UpdateManufactureOrderStatusHandlerConditionsTests.cs`
- `UpdateManufactureOrderStatusHandlerTests.cs`

- [ ] **Step 1: Refresh the file list**

Run:

```bash
grep -lr "Mock<ICatalogRepository>" backend/test/Anela.Heblo.Tests/Features/Manufacture/
```

Expected: 9–14 files (the planning-time count was 14 but only those that mock `ICatalogRepository` need editing — there is overlap because some Manufacture tests touch `ICatalogRepository` in non-`Mock<>` contexts; treat the `Mock<ICatalogRepository>` matches as authoritative).

- [ ] **Step 2: For each file, apply the swap**

In every file matching the grep:

1. Add `using Anela.Heblo.Application.Features.Manufacture.Contracts;` near the top of the file (preserve alphabetical ordering of `using` directives where the file already follows it). Keep `using Anela.Heblo.Domain.Features.Catalog;` if `CatalogAggregate` or any other Catalog type is still referenced.
2. Replace every occurrence of `Mock<ICatalogRepository>` with `Mock<IManufactureCatalogSource>` (this covers both the field declaration and the `new Mock<ICatalogRepository>()` assignment). All other code stays unchanged — Mock setup signatures (`Setup(x => x.GetByIdAsync(...))`, `Setup(x => x.GetAllAsync(...))`, `Setup(x => x.GetByIdsAsync(...))`) match by construction.

Tip: per-file `sed` is fine and safe because the only token that changes is the generic argument:

```bash
sed -i '' 's/Mock<ICatalogRepository>/Mock<IManufactureCatalogSource>/g' <file>
```

For the `using` directive add, edit each file manually after the bulk-sed run.

- [ ] **Step 3: Verify no Manufacture test still references `ICatalogRepository`**

Run:

```bash
grep -rln "ICatalogRepository" backend/test/Anela.Heblo.Tests/Features/Manufacture/ || echo "OK"
```

Expected: `OK` (no matches). If matches remain, they are likely leftover `using` directives — remove them if the file no longer references any type from `Anela.Heblo.Domain.Features.Catalog`.

- [ ] **Step 4: Run Manufacture tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Manufacture"
```

Expected: all Manufacture tests pass. No new failures, no skips that weren't already present.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Manufacture/
git commit -m "test(manufacture): swap Mock<ICatalogRepository> to Mock<IManufactureCatalogSource>"
```

---

## Task 7: Populate `ManufactureCatalogAllowlist` with the residual deliberate-leak references

After Tasks 5–6, the boundary test still fails — but the violations are now exclusively the deliberate `CatalogAggregate` / `ProductType` / etc. leaks flowing through the new contract, which is the intended end state.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

- [ ] **Step 1: Capture the residual violation list**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces" --logger "console;verbosity=detailed" 2>&1 | tee /tmp/boundary-post-migration.txt
```

Expected output: the `Manufacture -> Catalog` rule fails with violations of the form:

```
  Anela.Heblo.Application.Features.Manufacture.Contracts.IManufactureCatalogSource -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate (via method GetByIdAsync return)
  Anela.Heblo.Application.Features.Manufacture.Services.BatchPlanningService -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate (via ...)
  Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis.GetManufacturingStockAnalysisHandler -> Anela.Heblo.Domain.Features.Catalog.ProductType (via ...)
  ...
```

Extract the unique `Consumer -> Provider` pairs (strip the `(via ...)` suffix and dedupe):

```bash
grep -E "Anela\.Heblo\.Application\.Features\.Manufacture.* -> Anela\.Heblo\." /tmp/boundary-post-migration.txt \
  | sed -E 's/ \(via .*\)//' \
  | sort -u
```

- [ ] **Step 2: Populate the allowlist**

Open `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`. Replace the empty `ManufactureCatalogAllowlist` set with a populated version. Group entries by the referenced type with a single `//` comment block per group. Use this skeleton (replace `<...>` placeholders with the actual full type names from Step 1's output):

```csharp
    // Allowlist for Manufacture -> Catalog. Each group below is a deliberate pragmatic leak
    // tracked under the same follow-up: introduce Manufacture-owned ProductCatalogSnapshot DTO
    // and map in CatalogManufactureCatalogSourceAdapter (symmetric to the CatalogManufactureAllowlist
    // ManufactureHistoryRecord block).
    private static readonly HashSet<string> ManufactureCatalogAllowlist = new(StringComparer.Ordinal)
    {
        // Contract surface: IManufactureCatalogSource returns CatalogAggregate by design.
        "Anela.Heblo.Application.Features.Manufacture.Contracts.IManufactureCatalogSource -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",

        // Consumers of CatalogAggregate reached via IManufactureCatalogSource.GetByIdAsync /
        // GetByIdsAsync / GetAllAsync. Remove these entries when ProductCatalogSnapshot DTO lands.
        "Anela.Heblo.Application.Features.Manufacture.Services.BatchPlanningService -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.Services.ResidueDistributionCalculator -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan.CalculateBatchPlanHandler -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        // ... paste remaining "<consumer> -> ...CatalogAggregate" entries here from Step 1 output ...

        // Domain enums reached via CatalogAggregate properties (ProductType, ManufactureType, etc.).
        // Same follow-up as above.
        "Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis.GetManufacturingStockAnalysisHandler -> Anela.Heblo.Domain.Features.Catalog.ProductType",
        // ... paste remaining domain-enum / value-type entries here ...
    };
```

Critical rules for the allowlist:

- **Do NOT add `ICatalogRepository`** — that is the violation this work fixes. If `ICatalogRepository` appears in Step 1's output, the migration in Task 5 is incomplete; go back and finish it.
- Paste **every** unique entry from Step 1 — do not selectively prune. The reflection walker enumerates fields, properties, constructor params, method params and returns, attribute types, and generic arguments; missing an entry will fail CI.
- Compiler-generated nested types (`+<>c__DisplayClassN_M`, `+<MethodName>d__N`) are auto-handled by the `DeclaringType` check (see `ModuleBoundariesTests.cs:385-391`), so you do not normally need entries for them — but if Step 1's output shows one, paste it (it costs nothing and survives codegen drift).
- Order entries within each group alphabetically for review-friendliness.

- [ ] **Step 3: Re-run the boundary test**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"
```

Expected: all `ModuleBoundariesTests` pass, including the `Manufacture -> Catalog` rule. If any violation remains: append it to the allowlist with the matching group's comment header, then re-run.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test(arch): populate ManufactureCatalogAllowlist with deliberate CatalogAggregate leak entries"
```

---

## Task 8: Full backend validation

- [ ] **Step 1: Solution build**

```bash
cd backend && dotnet build
```

Expected: green with no errors and no new warnings introduced by this change. (Compare warning count against `main`'s baseline if uncertain.)

- [ ] **Step 2: Solution format**

```bash
cd backend && dotnet format --verify-no-changes
```

Expected: no formatting changes required. If the command reports diffs, run `dotnet format` without `--verify-no-changes`, review the diffs, and amend Task 8 with the formatting commit. (Should not be needed for the small token changes done here.)

- [ ] **Step 3: Full Manufacture + arch test run**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Manufacture|FullyQualifiedName~ModuleBoundaries|FullyQualifiedName~CatalogManufactureCatalogSourceAdapter"
```

Expected: all tests pass.

- [ ] **Step 4: Full test suite (sanity check for unintended impact)**

```bash
cd backend && dotnet test
```

Expected: same pass/fail count as `main`. No new failures introduced by this change. Skipped/flaky tests pre-existing on `main` are tolerated.

- [ ] **Step 5: Confirm no `Anela.Heblo.Domain.Features.Catalog.ICatalogRepository` references remain in Manufacture (final guard)**

```bash
grep -rn "ICatalogRepository" backend/src/Anela.Heblo.Application/Features/Manufacture/ backend/test/Anela.Heblo.Tests/Features/Manufacture/ || echo "OK — fully decoupled"
```

Expected: `OK — fully decoupled`.

- [ ] **Step 6: (No frontend changes — skip)**

This change is backend-only with no DTO, controller, OpenAPI, or React impact. No `npm run build`, no `npm run lint`, no E2E run required. The OpenAPI client regeneration step (auto-run on backend build) is a no-op because no controller signatures changed.

---

## Self-Review Checklist (for the plan author)

- **Spec coverage:** All five functional requirements have a dedicated task:
  - FR-1 → Task 1 (with arch-review §Amendments #1 return-type correction applied).
  - FR-2 → Task 2 (adapter) + Task 3 (DI registration, with arch-review §Amendments #3 adapter tests added).
  - FR-3 → Task 5 (11 sub-tasks for 11 files).
  - FR-4 → Tasks 4 + 7 (rule added with empty allowlist first, populated after migration per arch-review §Amendments #4).
  - FR-5 → Task 6 (test mock swap) + Task 2 (new adapter tests per arch-review §Amendments #3).

- **Non-functional requirements:** NFR-1 (performance) is preserved — adapter is a literal pass-through (no `.ToList()`, no LINQ). NFR-2 (security) is unchanged — no auth, logging, or surface changes. NFR-3 (backwards compat) is preserved — `ICatalogRepository` is untouched.

- **Out of scope items honored:** No DTO extraction, no `IManufactureClient` follow-up, no `ICatalogRepository` lifetime change, no analytics method additions, no controller/DTO/frontend changes.

- **Arch-review amendment #5 (docs cross-reference):** Intentionally not included in this plan. The arch review marks it as optional and "out of scope for this PR if the spec author prefers." Keeping the diff surgical.

- **Type/method signature consistency:** All call-site receivers swap to `_catalogSource`; all method names (`GetByIdAsync`, `GetByIdsAsync`, `GetAllAsync`) are reused identically across tasks. Adapter return type for `GetAllAsync` is `Task<IEnumerable<CatalogAggregate>>` consistently in Task 1 (contract), Task 2 (adapter + test), and Task 5.4 (call site `_catalogSource.GetAllAsync(...)` is assigned to `var allCatalogItems` which then `.Where(...).ToList()` — no signature change at call site).

- **No placeholders:** Each step has either exact code, exact commands, or both. No "TBD", no "add validation", no "similar to Task N" without inlined repetition. The one exception is Task 7 Step 2's `// ... paste remaining ... entries here ...` markers — these are required because the allowlist content is generated from the failing test run in Step 1, not knowable at plan time. The list of *what to paste from* (the grep command in Step 1) and the *grouping rules* (Step 2 body) are fully specified.
