# Decouple Purchase Handlers from Catalog Domain — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace direct `ICatalogRepository` injection in five Purchase MediatR handlers with a Purchase-owned read contract (`IMaterialCatalogService`) implemented by a Catalog-side adapter, then lock the new boundary with an architectural test.

**Architecture:** Apply the established consumer-owns-contract / provider-owns-adapter pattern (`docs/architecture/development_guidelines.md` §"Cross-Module Communication Example") used by Leaflet→KnowledgeBase (2026-05-15), Logistics→Manufacture (2026-05-16), and PackingMaterials→Invoices (2026-05-21). Purchase declares `IMaterialCatalogService` and six read DTOs in its `Contracts/` folder; Catalog provides an `internal sealed PurchaseMaterialCatalogAdapter` in `Application/Features/Catalog/Infrastructure/` that projects `CatalogAggregate` → Purchase DTOs and pre-computes the per-period consumption + last-purchase summaries the stock-analysis handler needs. `CatalogModule.AddCatalogModule` registers the binding. The architectural test gets a fourth theory row with a one-entry allowlist for the pre-existing `IProductPriceErpClient` dependency in `RecalculatePurchasePriceHandler` (out of scope for this PR — tracked as a follow-up arch-review item, mirroring the Leaflet `IDocumentTextExtractor` precedent).

**Tech Stack:** .NET 8, C# (nullable enabled), xUnit, FluentAssertions, Moq, MediatR, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`. No new NuGet packages, no schema changes, no MediatR/HTTP contract changes.

---

## File Structure

**Files to create:**

| Path | Responsibility |
|------|----------------|
| `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/IMaterialCatalogService.cs` | Purchase-owned interface — five methods that hide every `CatalogAggregate` detail. |
| `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialInfo.cs` | Slim read snapshot for Create / Update / GetById / single-product Recalculate paths. |
| `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialBomReference.cs` | Lightweight `(ProductCode, BoMId)` pair used by RecalculatePrice all-products path. |
| `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialStockSnapshot.cs` | Pre-computed analysis input for GetPurchaseStockAnalysis. |
| `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialStockLevels.cs` | `(Available, Ordered, EffectiveStock)` triple. |
| `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialPurchaseSnapshot.cs` | Last-purchase summary attached to `MaterialStockSnapshot`. |
| `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialProductType.cs` | Purchase-owned enum (`Material`, `Goods`). |
| `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/PurchaseMaterialCatalogAdapter.cs` | `internal sealed` adapter — projects `CatalogAggregate` → Purchase DTOs and pre-computes per-period consumption + last-purchase. |
| `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/PurchaseMaterialCatalogAdapterTests.cs` | xUnit + Moq tests for the adapter (every method + behavior-parity test for `GetConsumed`/`GetTotalSold`). |

**Files to modify:**

| Path | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/CreatePurchaseOrderHandler.cs` | Drop `using Anela.Heblo.Domain.Features.Catalog;`; replace `_catalogRepository : ICatalogRepository` with `_materialCatalog : IMaterialCatalogService`; switch to `MaterialInfo`. |
| `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderHandler.cs` | Same as above; replace three per-line `GetByIdAsync` calls with one `GetByIdsAsync` batch (fixes existing N+1). |
| `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderById/GetPurchaseOrderByIdHandler.cs` | Same as above; replace per-`materialId` loop with `GetByIdsAsync`. |
| `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs` | Same as above; replace `GetAllAsync` + filter + per-item `GetConsumed`/`GetTotalSold`/property reads with single `GetStockAnalysisSnapshotsAsync` call; rewrite `AnalyzeStockItem` to take `MaterialStockSnapshot`. |
| `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/RecalculatePurchasePrice/RecalculatePurchasePriceHandler.cs` | Same as above; split into single-product (`GetByIdAsync` + validate `HasBoM`/`BoMId`) and all-products (`GetMaterialsWithBomAsync`) paths; keep `IProductPriceErpClient` as-is (allowlisted). |
| `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` | Add `using Anela.Heblo.Application.Features.Purchase.Contracts;`; register `services.AddScoped<IMaterialCatalogService, PurchaseMaterialCatalogAdapter>();` inside `AddCatalogModule` (after the `services.AddTransient<ICatalogRepository, CatalogRepository>();` line). |
| `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` | Add `PurchaseAllowlist` static field + new `Purchase -> Catalog` row to `Rules()`. |
| `backend/test/Anela.Heblo.Tests/Features/Purchase/CreatePurchaseOrderHandlerTests.cs` | Replace `Mock<ICatalogRepository>` with `Mock<IMaterialCatalogService>`; update setups to return `MaterialInfo`. |
| `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs` | Replace `Mock<ICatalogRepository>` with `Mock<IMaterialCatalogService>`; rebuild fixture helpers (`CreateTestCatalogItems` → `CreateTestSnapshots`) to return `MaterialStockSnapshot` lists; update `_catalogRepositoryMock.Setup(x => x.GetAllAsync …)` → `Setup(x => x.GetStockAnalysisSnapshotsAsync …)`. |
| `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs` | Replace `Mock<ICatalogRepository>` with `Mock<IMaterialCatalogService>`; build `MaterialStockSnapshot` directly with pre-normalized name (use `Anela.Heblo.Xcc.StringExtensions.NormalizeForSearch` if the test needs to compute it, or pass through a known normalized string). |

**Files NOT touched:**

- `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs` — no Purchase-side registration of `IMaterialCatalogService` (binding belongs to the provider per the documented pattern).
- `backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs`, `CatalogAggregate.cs`, repository implementations — adapter delegates to existing surface; no Catalog-side changes.
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockSeverityCalculator.cs`, `Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs` (which contains `StockSeverity`) — already Purchase-owned. The spec is explicit that no relocation is needed.

---

## Task 1: Add the failing architecture-test rule for Purchase → Catalog

This locks the boundary in place before any source changes. Run it once to **observe the violations**, then proceed — subsequent tasks turn it green. Following the working pattern from the Leaflet/Logistics rules, use a named `PurchaseAllowlist` (the one entry covers the pre-existing `IProductPriceErpClient` dependency).

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

- [ ] **Step 1: Add the `PurchaseAllowlist` static field**

Open the file. Locate the `LogisticsAllowlist` block (lines 49–64). After the closing `};` of `LogisticsAllowlist` and before the `public static TheoryData<ModuleBoundaryRule> Rules() => new()` line, insert:

```csharp
    // Allowlist for Purchase → Catalog. Each entry needs a comment with the justification.
    // Entries should be removed as the underlying violations are fixed.
    private static readonly HashSet<string> PurchaseAllowlist = new(StringComparer.Ordinal)
    {
        // Pre-existing dependency: RecalculatePurchasePriceHandler consumes IProductPriceErpClient,
        // which currently lives in Anela.Heblo.Domain.Features.Catalog.Price. IProductPriceErpClient
        // is an ERP integration boundary (not a domain repository); decoupling it requires a
        // separate Purchase-owned contract (e.g., IPurchasePriceRecalculator) and is out of scope
        // for the 2026-05-24 Purchase ↔ Catalog decoupling. Track separately and remove this entry
        // when IProductPriceErpClient is lifted behind a Purchase-owned contract.
        "Anela.Heblo.Application.Features.Purchase.UseCases.RecalculatePurchasePrice.RecalculatePurchasePriceHandler -> Anela.Heblo.Domain.Features.Catalog.Price.IProductPriceErpClient",
    };

```

- [ ] **Step 2: Append the fourth theory row to `Rules()`**

Inside the `Rules()` method, after the `PackingMaterials -> Invoices` row (currently lines 90–99) and before the closing `};` of the TheoryData initializer, append:

```csharp
        new ModuleBoundaryRule(
            Name: "Purchase -> Catalog",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Purchase",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Catalog",
                "Anela.Heblo.Application.Features.Catalog",
                "Anela.Heblo.Persistence.Catalog",
            },
            Allowlist: PurchaseAllowlist),
```

- [ ] **Step 3: Verify the new theory case currently fails**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces" \
  --no-restore --logger "console;verbosity=normal"
```

Expected: the `Purchase -> Catalog` theory case FAILS with violations including (non-exhaustive):
- `…CreatePurchaseOrderHandler -> Anela.Heblo.Domain.Features.Catalog.ICatalogRepository`
- `…UpdatePurchaseOrderHandler -> Anela.Heblo.Domain.Features.Catalog.ICatalogRepository`
- `…GetPurchaseOrderByIdHandler -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate`
- `…GetPurchaseStockAnalysisHandler -> Anela.Heblo.Domain.Features.Catalog.ICatalogRepository`
- `…GetPurchaseStockAnalysisHandler -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate`
- `…GetPurchaseStockAnalysisHandler -> Anela.Heblo.Domain.Features.Catalog.ProductType`
- `…RecalculatePurchasePriceHandler -> Anela.Heblo.Domain.Features.Catalog.ICatalogRepository`
- `…RecalculatePurchasePriceHandler -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate`

The `IProductPriceErpClient` reference is **not** in this list — the named allowlist entry suppresses it.

The three pre-existing theory cases (Leaflet, Logistics, PackingMaterials) and the `Logistics_types_should_not_reference_Purchase_owned_namespaces` fact must still PASS.

- [ ] **Step 4: Commit the failing test**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test(arch): add failing Purchase -> Catalog boundary rule"
```

---

## Task 2: Create the Purchase-owned contract types

Pure type declarations. No tests — these are inert data definitions; their correctness is exercised by every subsequent task. **Per the project convention (CLAUDE.md "DTOs are classes, never C# records"), all DTOs in this folder are `public sealed class`.** `init` setters are acceptable because these contracts are internal-to-process (never round-tripped through OpenAPI).

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialProductType.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialPurchaseSnapshot.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialStockLevels.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialStockSnapshot.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialBomReference.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialInfo.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/IMaterialCatalogService.cs`

- [ ] **Step 1: Create `MaterialProductType.cs`**

Write the file exactly:

```csharp
namespace Anela.Heblo.Application.Features.Purchase.Contracts;

/// <summary>
/// Purchase-owned classification of catalog items that purchasing cares about.
/// Maps from Catalog's ProductType inside PurchaseMaterialCatalogAdapter; only
/// items mapping to Material or Goods are returned by GetStockAnalysisSnapshotsAsync.
/// </summary>
public enum MaterialProductType
{
    Material,
    Goods,
}
```

- [ ] **Step 2: Create `MaterialPurchaseSnapshot.cs`**

Write the file exactly:

```csharp
namespace Anela.Heblo.Application.Features.Purchase.Contracts;

/// <summary>
/// Most-recent purchase record for a catalog item, attached to
/// <see cref="MaterialStockSnapshot.LastPurchase"/>.
/// </summary>
public sealed class MaterialPurchaseSnapshot
{
    public required DateTime Date { get; init; }
    public required string SupplierName { get; init; }
    public required decimal Amount { get; init; }
    public required decimal UnitPrice { get; init; }
    public required decimal TotalPrice { get; init; }
}
```

Note: `Amount` is `decimal` here (handler uses `(double)` in calculations and `(decimal)` for inventory value; the adapter projects the source `double Amount` field via `(decimal)` to keep numeric semantics aligned with how the handler consumed it). If a downstream test fails because of this casting, the adapter line is the single point of fix.

- [ ] **Step 3: Create `MaterialStockLevels.cs`**

Write the file exactly:

```csharp
namespace Anela.Heblo.Application.Features.Purchase.Contracts;

/// <summary>
/// Aggregated stock numbers exposed to Purchase. Mirrors the subset of
/// Catalog's StockData that the analysis handler reads.
/// </summary>
public sealed class MaterialStockLevels
{
    public required decimal Available { get; init; }
    public required decimal Ordered { get; init; }
    public required decimal EffectiveStock { get; init; }
}
```

- [ ] **Step 4: Create `MaterialStockSnapshot.cs`**

Write the file exactly:

```csharp
namespace Anela.Heblo.Application.Features.Purchase.Contracts;

/// <summary>
/// Pre-computed input for <c>GetPurchaseStockAnalysisHandler</c>. Filtered to
/// <see cref="MaterialProductType.Material"/> and <see cref="MaterialProductType.Goods"/>
/// inside the adapter; per-period consumption is also computed there so Purchase
/// no longer touches CatalogAggregate methods (GetConsumed / GetTotalSold).
/// </summary>
public sealed class MaterialStockSnapshot
{
    public required string ProductCode { get; init; }
    public required string ProductName { get; init; }
    public required string ProductNameNormalized { get; init; }
    public required MaterialProductType ProductType { get; init; }
    public string? SupplierName { get; init; }
    public required string MinimalOrderQuantity { get; init; }
    public required bool IsMinStockConfigured { get; init; }
    public required bool IsOptimalStockConfigured { get; init; }
    public required MaterialStockLevels Stock { get; init; }
    public required decimal StockMinSetup { get; init; }
    public required int OptimalStockDaysSetup { get; init; }
    public required double ConsumptionInPeriod { get; init; }
    public MaterialPurchaseSnapshot? LastPurchase { get; init; }
}
```

- [ ] **Step 5: Create `MaterialBomReference.cs`**

Write the file exactly:

```csharp
namespace Anela.Heblo.Application.Features.Purchase.Contracts;

/// <summary>
/// Lightweight (ProductCode, BoMId) pair returned by
/// <see cref="IMaterialCatalogService.GetMaterialsWithBomAsync"/>. Only items where
/// CatalogAggregate.HasBoM is true and CatalogAggregate.BoMId.HasValue is true are returned,
/// which guarantees the non-nullable <see cref="BoMId"/> field below.
/// </summary>
public sealed class MaterialBomReference
{
    public required string ProductCode { get; init; }
    public required int BoMId { get; init; }
}
```

- [ ] **Step 6: Create `MaterialInfo.cs`**

Write the file exactly:

```csharp
namespace Anela.Heblo.Application.Features.Purchase.Contracts;

/// <summary>
/// Slim read snapshot of a catalog item exposing only the fields Purchase needs
/// for the Create / Update / GetById / single-product Recalculate paths.
/// </summary>
public sealed class MaterialInfo
{
    public required string ProductCode { get; init; }
    public required string ProductName { get; init; }
    public string? Note { get; init; }
    public bool HasBoM { get; init; }
    public int? BoMId { get; init; }
}
```

- [ ] **Step 7: Create `IMaterialCatalogService.cs`**

Write the file exactly:

```csharp
namespace Anela.Heblo.Application.Features.Purchase.Contracts;

/// <summary>
/// Purchase-owned read abstraction over the catalog. Implemented by the Catalog
/// module via <c>PurchaseMaterialCatalogAdapter</c> per the cross-module
/// communication pattern in <c>docs/architecture/development_guidelines.md</c>.
/// </summary>
public interface IMaterialCatalogService
{
    /// <summary>
    /// Returns a single material by product code, or <c>null</c> if not in the catalog.
    /// Matches the soft-fallback semantics the existing handlers already use
    /// (<c>material?.ProductName ?? lineRequest.Name ?? "Unknown Material"</c>).
    /// </summary>
    Task<MaterialInfo?> GetByIdAsync(string productCode, CancellationToken cancellationToken);

    /// <summary>
    /// Bulk lookup. Returns a dictionary keyed by <c>ProductCode</c>. Missing IDs
    /// are omitted from the result (matches <c>ICatalogRepository.GetByIdsAsync</c>
    /// behavior at <c>backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs:54</c>).
    /// Replaces N+1 per-line lookups in UpdatePurchaseOrderHandler and GetPurchaseOrderByIdHandler.
    /// </summary>
    Task<IReadOnlyDictionary<string, MaterialInfo>> GetByIdsAsync(
        IEnumerable<string> productCodes,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns every catalog item projected to <see cref="MaterialInfo"/>. Currently
    /// unused by the migrated handlers but kept on the interface for parity with
    /// the existing repository surface — callers that legitimately need every
    /// material (search-style UI flows) can use this without falling back to
    /// <c>GetStockAnalysisSnapshotsAsync</c>.
    /// </summary>
    Task<IReadOnlyList<MaterialInfo>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns stock + pre-computed consumption snapshots for the given period,
    /// filtered to Material and Goods product types. Replaces the per-item
    /// GetConsumed/GetTotalSold calls and Material/Goods filter currently issued
    /// from GetPurchaseStockAnalysisHandler.
    /// </summary>
    Task<IReadOnlyList<MaterialStockSnapshot>> GetStockAnalysisSnapshotsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns materials that have a Bill of Materials, with the data needed to
    /// drive RecalculatePurchasePriceHandler (ProductCode, BoMId).
    /// </summary>
    Task<IReadOnlyList<MaterialBomReference>> GetMaterialsWithBomAsync(
        CancellationToken cancellationToken);
}
```

- [ ] **Step 8: Verify no Catalog usings leaked**

Run:

```bash
grep -rn "Anela.Heblo.*Features.Catalog" \
  backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/ \
  || echo "CLEAN"
```

Expected: exactly `CLEAN`. The architectural test will catch this anyway, but checking now keeps the loop tight.

- [ ] **Step 9: Build the application project to confirm the seven files compile**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds. No warnings about unused usings.

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/IMaterialCatalogService.cs \
        backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialInfo.cs \
        backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialBomReference.cs \
        backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialStockSnapshot.cs \
        backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialStockLevels.cs \
        backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialPurchaseSnapshot.cs \
        backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/MaterialProductType.cs
git commit -m "feat(purchase): add IMaterialCatalogService contract and read DTOs"
```

---

## Task 3: Write failing adapter tests (TDD red)

The `Anela.Heblo.Application` project already declares `[assembly: InternalsVisibleTo("Anela.Heblo.Tests")]` (`AssemblyInfo.cs:3`), so the `internal sealed` adapter is directly testable.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/PurchaseMaterialCatalogAdapterTests.cs`

- [ ] **Step 1: Create the test file**

Create the directory if needed (`backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/`) and write the file exactly:

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class PurchaseMaterialCatalogAdapterTests
{
    private readonly Mock<ICatalogRepository> _repository = new();

    private PurchaseMaterialCatalogAdapter CreateAdapter() => new(_repository.Object);

    private static CatalogAggregate MakeMaterial(
        string productCode,
        string productName = "Material",
        ProductType type = ProductType.Material,
        string? supplierName = null,
        decimal stockMinSetup = 0m,
        int optimalStockDaysSetup = 0,
        string minimalOrderQuantity = "",
        decimal erpStock = 0m,
        decimal ordered = 0m,
        bool hasBoM = false,
        int? bomId = null,
        string? note = null)
    {
        var aggregate = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = productName,
            Type = type,
            SupplierName = supplierName,
            MinimalOrderQuantity = minimalOrderQuantity,
            Note = note,
            Stock = new StockData
            {
                Erp = erpStock,
                Ordered = ordered,
                PrimaryStockSource = StockSource.Erp,
            },
            Properties = new CatalogProperties
            {
                StockMinSetup = stockMinSetup,
                OptimalStockDaysSetup = optimalStockDaysSetup,
            },
        };

        if (hasBoM)
        {
            aggregate.ErpPrice = new ProductPriceErp
            {
                HasBoM = true,
                BoMId = bomId,
            };
        }

        return aggregate;
    }

    [Fact]
    public async Task GetByIdAsync_delegates_to_repository_and_projects_to_material_info()
    {
        var ct = CancellationToken.None;
        _repository
            .Setup(r => r.GetByIdAsync("MAT-1", ct))
            .ReturnsAsync(MakeMaterial("MAT-1", "First", note: "n1", hasBoM: true, bomId: 42));

        var adapter = CreateAdapter();

        var result = await adapter.GetByIdAsync("MAT-1", ct);

        result.Should().NotBeNull();
        result!.ProductCode.Should().Be("MAT-1");
        result.ProductName.Should().Be("First");
        result.Note.Should().Be("n1");
        result.HasBoM.Should().BeTrue();
        result.BoMId.Should().Be(42);

        _repository.Verify(r => r.GetByIdAsync("MAT-1", ct), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_repository_returns_null()
    {
        _repository
            .Setup(r => r.GetByIdAsync("MISSING", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate?)null);

        var result = await CreateAdapter().GetByIdAsync("MISSING", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdsAsync_delegates_to_bulk_repository_method_once_and_keys_by_product_code()
    {
        var ct = CancellationToken.None;
        var ids = new[] { "MAT-1", "MAT-2" };

        var dictionary = new Dictionary<string, CatalogAggregate>(StringComparer.Ordinal)
        {
            ["MAT-1"] = MakeMaterial("MAT-1", "First"),
            ["MAT-2"] = MakeMaterial("MAT-2", "Second"),
        };

        _repository
            .Setup(r => r.GetByIdsAsync(ids, ct))
            .ReturnsAsync(dictionary);

        var result = await CreateAdapter().GetByIdsAsync(ids, ct);

        result.Should().HaveCount(2);
        result["MAT-1"].ProductName.Should().Be("First");
        result["MAT-2"].ProductName.Should().Be("Second");

        _repository.Verify(r => r.GetByIdsAsync(ids, ct), Times.Once);
    }

    [Fact]
    public async Task GetByIdsAsync_omits_ids_that_are_missing_from_the_repository_result()
    {
        var ct = CancellationToken.None;
        var ids = new[] { "MAT-1", "MISSING" };

        var dictionary = new Dictionary<string, CatalogAggregate>(StringComparer.Ordinal)
        {
            ["MAT-1"] = MakeMaterial("MAT-1", "First"),
        };

        _repository
            .Setup(r => r.GetByIdsAsync(ids, ct))
            .ReturnsAsync(dictionary);

        var result = await CreateAdapter().GetByIdsAsync(ids, ct);

        result.Keys.Should().BeEquivalentTo(new[] { "MAT-1" });
    }

    [Fact]
    public async Task GetAllAsync_projects_every_catalog_item_to_material_info()
    {
        var ct = CancellationToken.None;
        _repository
            .Setup(r => r.GetAllAsync(ct))
            .ReturnsAsync(new[]
            {
                MakeMaterial("A", "Apple"),
                MakeMaterial("B", "Banana"),
            });

        var result = await CreateAdapter().GetAllAsync(ct);

        result.Select(m => m.ProductCode).Should().BeEquivalentTo(new[] { "A", "B" });
    }

    [Fact]
    public async Task GetStockAnalysisSnapshotsAsync_filters_to_material_and_goods()
    {
        var ct = CancellationToken.None;
        _repository
            .Setup(r => r.GetAllAsync(ct))
            .ReturnsAsync(new[]
            {
                MakeMaterial("MAT-1", "Material 1", ProductType.Material),
                MakeMaterial("GOODS-1", "Goods 1", ProductType.Goods),
                MakeMaterial("PROD-1", "Product 1", ProductType.Product),
                MakeMaterial("SEMI-1", "Semiproduct 1", ProductType.SemiProduct),
                MakeMaterial("SET-1", "Set 1", ProductType.Set),
                MakeMaterial("UNDEF-1", "Undef 1", ProductType.UNDEFINED),
            });

        var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        var result = await CreateAdapter().GetStockAnalysisSnapshotsAsync(from, to, ct);

        result.Select(s => s.ProductCode).Should().BeEquivalentTo(new[] { "MAT-1", "GOODS-1" });
        result.Should().OnlyContain(s =>
            s.ProductType == MaterialProductType.Material || s.ProductType == MaterialProductType.Goods);
    }

    [Fact]
    public async Task GetStockAnalysisSnapshotsAsync_projects_stock_and_properties()
    {
        var ct = CancellationToken.None;
        var material = MakeMaterial(
            productCode: "MAT-1",
            productName: "Krém",
            type: ProductType.Material,
            supplierName: "ACME",
            stockMinSetup: 5m,
            optimalStockDaysSetup: 30,
            minimalOrderQuantity: "100",
            erpStock: 10m,
            ordered: 7m);

        _repository.Setup(r => r.GetAllAsync(ct)).ReturnsAsync(new[] { material });

        var snapshot = (await CreateAdapter().GetStockAnalysisSnapshotsAsync(
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            ct)).Single();

        snapshot.ProductCode.Should().Be("MAT-1");
        snapshot.ProductName.Should().Be("Krém");
        snapshot.ProductNameNormalized.Should().Be(material.ProductNameNormalized);
        snapshot.ProductType.Should().Be(MaterialProductType.Material);
        snapshot.SupplierName.Should().Be("ACME");
        snapshot.MinimalOrderQuantity.Should().Be("100");
        snapshot.IsMinStockConfigured.Should().BeTrue();
        snapshot.IsOptimalStockConfigured.Should().BeTrue();
        snapshot.Stock.Available.Should().Be(material.Stock.Available);
        snapshot.Stock.Ordered.Should().Be(7m);
        snapshot.Stock.EffectiveStock.Should().Be(material.Stock.EffectiveStock);
        snapshot.StockMinSetup.Should().Be(5m);
        snapshot.OptimalStockDaysSetup.Should().Be(30);
    }

    [Fact]
    public async Task GetStockAnalysisSnapshotsAsync_consumption_uses_GetConsumed_for_material()
    {
        var ct = CancellationToken.None;
        var from = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc);

        var material = MakeMaterial("MAT-1", "Material", ProductType.Material);
        material.ConsumedHistory = new List<ConsumedMaterialRecord>
        {
            new ConsumedMaterialRecord { Date = new DateTime(2025, 6, 10), Amount = 3 },
            new ConsumedMaterialRecord { Date = new DateTime(2025, 6, 20), Amount = 4 },
            new ConsumedMaterialRecord { Date = new DateTime(2025, 5, 31), Amount = 99 }, // before range
            new ConsumedMaterialRecord { Date = new DateTime(2025, 7, 1),  Amount = 99 }, // after range
        };

        _repository.Setup(r => r.GetAllAsync(ct)).ReturnsAsync(new[] { material });

        var snapshot = (await CreateAdapter().GetStockAnalysisSnapshotsAsync(from, to, ct)).Single();

        snapshot.ConsumptionInPeriod.Should().Be(material.GetConsumed(from, to));
        snapshot.ConsumptionInPeriod.Should().Be(7.0);
    }

    [Fact]
    public async Task GetStockAnalysisSnapshotsAsync_consumption_uses_GetTotalSold_for_goods()
    {
        var ct = CancellationToken.None;
        var from = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc);

        var goods = MakeMaterial("GOODS-1", "Goods", ProductType.Goods);
        goods.SalesHistory = new List<CatalogSaleRecord>
        {
            new CatalogSaleRecord { Date = new DateTime(2025, 6, 10), AmountB2B = 5, AmountB2C = 3 },
            new CatalogSaleRecord { Date = new DateTime(2025, 6, 20), AmountB2B = 1, AmountB2C = 2 },
            new CatalogSaleRecord { Date = new DateTime(2025, 7, 1),  AmountB2B = 99, AmountB2C = 99 }, // after
        };

        _repository.Setup(r => r.GetAllAsync(ct)).ReturnsAsync(new[] { goods });

        var snapshot = (await CreateAdapter().GetStockAnalysisSnapshotsAsync(from, to, ct)).Single();

        snapshot.ConsumptionInPeriod.Should().Be(goods.GetTotalSold(from, to));
        snapshot.ConsumptionInPeriod.Should().Be(11.0);
    }

    [Fact]
    public async Task GetStockAnalysisSnapshotsAsync_last_purchase_is_most_recent_record()
    {
        var ct = CancellationToken.None;
        var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        var material = MakeMaterial("MAT-1", "Material", ProductType.Material);
        material.PurchaseHistory = new List<CatalogPurchaseRecord>
        {
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2025, 3, 1), SupplierName = "Old Supplier",
                Amount = 10, PricePerPiece = 1m, PriceTotal = 10m,
            },
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2025, 9, 1), SupplierName = "New Supplier",
                Amount = 20, PricePerPiece = 2m, PriceTotal = 40m,
            },
        };

        _repository.Setup(r => r.GetAllAsync(ct)).ReturnsAsync(new[] { material });

        var snapshot = (await CreateAdapter().GetStockAnalysisSnapshotsAsync(from, to, ct)).Single();

        snapshot.LastPurchase.Should().NotBeNull();
        snapshot.LastPurchase!.Date.Should().Be(new DateTime(2025, 9, 1));
        snapshot.LastPurchase.SupplierName.Should().Be("New Supplier");
        snapshot.LastPurchase.Amount.Should().Be(20m);
        snapshot.LastPurchase.UnitPrice.Should().Be(2m);
        snapshot.LastPurchase.TotalPrice.Should().Be(40m);
    }

    [Fact]
    public async Task GetStockAnalysisSnapshotsAsync_last_purchase_is_null_when_history_is_empty()
    {
        var ct = CancellationToken.None;
        var material = MakeMaterial("MAT-1", "Material", ProductType.Material);
        material.PurchaseHistory = new List<CatalogPurchaseRecord>();

        _repository.Setup(r => r.GetAllAsync(ct)).ReturnsAsync(new[] { material });

        var snapshot = (await CreateAdapter().GetStockAnalysisSnapshotsAsync(
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            ct)).Single();

        snapshot.LastPurchase.Should().BeNull();
    }

    [Fact]
    public async Task GetMaterialsWithBomAsync_filters_by_hasBoM_and_bomId_present()
    {
        var ct = CancellationToken.None;
        _repository
            .Setup(r => r.GetAllAsync(ct))
            .ReturnsAsync(new[]
            {
                MakeMaterial("WITH-BOM", "With BoM", hasBoM: true, bomId: 7),
                MakeMaterial("BOM-NO-ID", "BoM but no Id", hasBoM: true, bomId: null),
                MakeMaterial("NO-BOM", "No BoM", hasBoM: false),
            });

        var result = await CreateAdapter().GetMaterialsWithBomAsync(ct);

        result.Should().ContainSingle();
        result[0].ProductCode.Should().Be("WITH-BOM");
        result[0].BoMId.Should().Be(7);
    }
}
```

- [ ] **Step 2: Verify the adapter tests don't compile yet**

Run:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: FAILS with `CS0246: The type or namespace name 'PurchaseMaterialCatalogAdapter' could not be found …`. This is the "red" of TDD. Move on.

- [ ] **Step 3: Do NOT commit yet**

Tasks 3 and 4 land together — the test project would be red between them.

---

## Task 4: Implement the adapter (TDD green)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/PurchaseMaterialCatalogAdapter.cs`

- [ ] **Step 1: Create the adapter file**

Write the file exactly:

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class PurchaseMaterialCatalogAdapter : IMaterialCatalogService
{
    private readonly ICatalogRepository _catalogRepository;

    public PurchaseMaterialCatalogAdapter(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task<MaterialInfo?> GetByIdAsync(string productCode, CancellationToken cancellationToken)
    {
        var aggregate = await _catalogRepository.GetByIdAsync(productCode, cancellationToken);
        return aggregate is null ? null : ToMaterialInfo(aggregate);
    }

    public async Task<IReadOnlyDictionary<string, MaterialInfo>> GetByIdsAsync(
        IEnumerable<string> productCodes,
        CancellationToken cancellationToken)
    {
        var aggregates = await _catalogRepository.GetByIdsAsync(productCodes, cancellationToken);

        var result = new Dictionary<string, MaterialInfo>(aggregates.Count, StringComparer.Ordinal);
        foreach (var (id, aggregate) in aggregates)
        {
            result[id] = ToMaterialInfo(aggregate);
        }

        return result;
    }

    public async Task<IReadOnlyList<MaterialInfo>> GetAllAsync(CancellationToken cancellationToken)
    {
        var aggregates = await _catalogRepository.GetAllAsync(cancellationToken);
        return aggregates.Select(ToMaterialInfo).ToList();
    }

    public async Task<IReadOnlyList<MaterialStockSnapshot>> GetStockAnalysisSnapshotsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var aggregates = await _catalogRepository.GetAllAsync(cancellationToken);

        return aggregates
            .Where(IsAnalysable)
            .Select(item => ToStockSnapshot(item, fromUtc, toUtc))
            .ToList();
    }

    public async Task<IReadOnlyList<MaterialBomReference>> GetMaterialsWithBomAsync(
        CancellationToken cancellationToken)
    {
        var aggregates = await _catalogRepository.GetAllAsync(cancellationToken);

        return aggregates
            .Where(item => item.HasBoM && item.BoMId.HasValue)
            .Select(item => new MaterialBomReference
            {
                ProductCode = item.ProductCode,
                BoMId = item.BoMId!.Value,
            })
            .ToList();
    }

    private static bool IsAnalysable(CatalogAggregate item) =>
        item.Type == ProductType.Material || item.Type == ProductType.Goods;

    private static MaterialInfo ToMaterialInfo(CatalogAggregate aggregate) => new()
    {
        ProductCode = aggregate.ProductCode,
        ProductName = aggregate.ProductName,
        Note = aggregate.Note,
        HasBoM = aggregate.HasBoM,
        BoMId = aggregate.BoMId,
    };

    private static MaterialStockSnapshot ToStockSnapshot(
        CatalogAggregate item,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var consumption = item.Type == ProductType.Material
            ? item.GetConsumed(fromUtc, toUtc)
            : item.GetTotalSold(fromUtc, toUtc);

        return new MaterialStockSnapshot
        {
            ProductCode = item.ProductCode,
            ProductName = item.ProductName,
            ProductNameNormalized = item.ProductNameNormalized,
            ProductType = MapProductType(item.Type),
            SupplierName = item.SupplierName,
            MinimalOrderQuantity = item.MinimalOrderQuantity,
            IsMinStockConfigured = item.IsMinStockConfigured,
            IsOptimalStockConfigured = item.IsOptimalStockConfigured,
            Stock = new MaterialStockLevels
            {
                Available = item.Stock.Available,
                Ordered = item.Stock.Ordered,
                EffectiveStock = item.Stock.EffectiveStock,
            },
            StockMinSetup = item.Properties.StockMinSetup,
            OptimalStockDaysSetup = item.Properties.OptimalStockDaysSetup,
            ConsumptionInPeriod = consumption,
            LastPurchase = ToLastPurchase(item.PurchaseHistory),
        };
    }

    private static MaterialPurchaseSnapshot? ToLastPurchase(IReadOnlyList<CatalogPurchaseRecord> history)
    {
        var latest = history
            .OrderByDescending(p => p.Date)
            .FirstOrDefault();

        if (latest is null)
            return null;

        return new MaterialPurchaseSnapshot
        {
            Date = latest.Date,
            SupplierName = latest.SupplierName ?? string.Empty,
            Amount = (decimal)latest.Amount,
            UnitPrice = latest.PricePerPiece,
            TotalPrice = latest.PriceTotal,
        };
    }

    private static MaterialProductType MapProductType(ProductType type) => type switch
    {
        ProductType.Material => MaterialProductType.Material,
        ProductType.Goods => MaterialProductType.Goods,
        _ => throw new ArgumentOutOfRangeException(
            nameof(type),
            type,
            "Adapter only projects Material and Goods. IsAnalysable filter must be applied first."),
    };
}
```

The `ArgumentOutOfRangeException` in `MapProductType` is unreachable as long as `IsAnalysable` is the gate before projection. It exists to make the invariant explicit rather than silently returning a default enum value.

- [ ] **Step 2: Confirm the application project still builds**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: succeeds.

- [ ] **Step 3: Run the adapter tests — all twelve should pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~PurchaseMaterialCatalogAdapterTests" \
  --no-restore --logger "console;verbosity=normal"
```

Expected: `Passed: 12, Failed: 0, Skipped: 0`.

- [ ] **Step 4: Commit Tasks 3 and 4 together**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/PurchaseMaterialCatalogAdapter.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/PurchaseMaterialCatalogAdapterTests.cs
git commit -m "feat(catalog): add PurchaseMaterialCatalogAdapter implementing IMaterialCatalogService"
```

---

## Task 5: Register the adapter in `CatalogModule`

The DI binding lives on the provider side per the documented pattern. No changes to `PurchaseModule`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`

- [ ] **Step 1: Add the consumer-contract `using`**

Open `CatalogModule.cs`. Locate the existing `using` block at the top of the file. Immediately after the line `using Anela.Heblo.Application.Features.Catalog.Infrastructure;` (currently line 8), add:

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
```

- [ ] **Step 2: Register the binding inside `AddCatalogModule`**

Locate `services.AddTransient<ICatalogRepository, CatalogRepository>();` (currently line 40). Immediately after that line and before the next `services.AddTransient<IManufactureDifficultyRepository, …>();` line, insert:

```csharp

        // Cross-module contract: Catalog implements Purchase's IMaterialCatalogService
        // via an adapter. DI registration owned by provider (Catalog), not consumer
        // (Purchase) — keeps the dependency direction inverted properly.
        services.AddScoped<IMaterialCatalogService, PurchaseMaterialCatalogAdapter>();
```

The relevant block in `AddCatalogModule` should now read:

```csharp
        // Register default implementations - tests can override these
        services.AddTransient<ICatalogRepository, CatalogRepository>();

        // Cross-module contract: Catalog implements Purchase's IMaterialCatalogService
        // via an adapter. DI registration owned by provider (Catalog), not consumer
        // (Purchase) — keeps the dependency direction inverted properly.
        services.AddScoped<IMaterialCatalogService, PurchaseMaterialCatalogAdapter>();

        services.AddTransient<IManufactureDifficultyRepository, ManufactureDifficultyRepository>();
```

Lifetime is `Scoped` (corrected from the spec rationale): matches MediatR handlers, avoids per-call adapter allocation, and is behaviorally equivalent to today's Transient `ICatalogRepository` (the adapter is stateless and `CatalogRepository` is cache-backed).

- [ ] **Step 3: Build the entire solution**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: succeeds with zero errors. The handlers still inject `ICatalogRepository`; the architectural test is still red. Both are fixed in Task 6.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
git commit -m "feat(catalog): register PurchaseMaterialCatalogAdapter as IMaterialCatalogService"
```

---

## Task 6: Migrate `CreatePurchaseOrderHandler`

The simplest of the five handlers — one `GetByIdAsync` call inside a `foreach`. No batch optimization needed (preserves current per-line semantics).

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/CreatePurchaseOrderHandler.cs`

- [ ] **Step 1: Replace the file contents**

Write the complete file body:

```csharp
using Anela.Heblo.Application.Common.Extensions;
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.CreatePurchaseOrder;

public class CreatePurchaseOrderHandler : IRequestHandler<CreatePurchaseOrderRequest, CreatePurchaseOrderResponse>
{
    private readonly ILogger<CreatePurchaseOrderHandler> _logger;
    private readonly IPurchaseOrderRepository _repository;
    private readonly IPurchaseOrderNumberGenerator _orderNumberGenerator;
    private readonly IMaterialCatalogService _materialCatalog;
    private readonly ICurrentUserService _currentUserService;
    private readonly ISupplierRepository _supplierRepository;

    public CreatePurchaseOrderHandler(
        ILogger<CreatePurchaseOrderHandler> logger,
        IPurchaseOrderRepository repository,
        IPurchaseOrderNumberGenerator orderNumberGenerator,
        IMaterialCatalogService materialCatalog,
        ICurrentUserService currentUserService,
        ISupplierRepository supplierRepository)
    {
        _logger = logger;
        _repository = repository;
        _orderNumberGenerator = orderNumberGenerator;
        _materialCatalog = materialCatalog;
        _currentUserService = currentUserService;
        _supplierRepository = supplierRepository;
    }

    public async Task<CreatePurchaseOrderResponse> Handle(CreatePurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        // Get supplier by ID
        var supplier = await _supplierRepository.GetByIdAsync(request.SupplierId, cancellationToken);
        if (supplier == null)
        {
            _logger.LogWarning("Supplier with ID {SupplierId} not found", request.SupplierId);
            return new CreatePurchaseOrderResponse(ErrorCodes.SupplierNotFound, new Dictionary<string, string> { { "SupplierId", request.SupplierId.ToString() } });
        }

        _logger.LogInformation("Creating new purchase order for supplier {SupplierName}", supplier.Name);

        // Parse dates from string format and ensure UTC for PostgreSQL compatibility
        var orderDate = request.OrderDate.ToUtcDateTime();
        var expectedDeliveryDate = request.ExpectedDeliveryDate.ToUtcDateTimeOrNull();

        var orderNumber = !string.IsNullOrEmpty(request.OrderNumber)
            ? request.OrderNumber
            : await _orderNumberGenerator.GenerateOrderNumberAsync(orderDate, cancellationToken);

        var currentUser = _currentUserService.GetCurrentUser();
        var createdBy = currentUser.Name ?? "System";

        var purchaseOrder = new PurchaseOrder(
            orderNumber,
            supplier.Id,
            supplier.Name,
            orderDate,
            expectedDeliveryDate,
            request.ContactVia,
            request.Notes,
            createdBy);

        // Add lines if provided
        if (request.Lines != null && request.Lines.Any())
        {
            _logger.LogInformation("Adding {LineCount} lines to purchase order {OrderNumber}",
                request.Lines.Count, orderNumber);

            foreach (var lineRequest in request.Lines)
            {
                // Look up material by ProductCode in catalog to get ProductName
                var material = await _materialCatalog.GetByIdAsync(lineRequest.MaterialId, cancellationToken);
                var materialName = material?.ProductName ?? lineRequest.Name ?? "Unknown Material";

                if (material == null)
                {
                    _logger.LogWarning("Material with code {MaterialId} not found in catalog, using provided name: {Name}",
                        lineRequest.MaterialId, materialName);
                }

                purchaseOrder.AddLine(
                    lineRequest.MaterialId,
                    materialName,
                    lineRequest.Quantity,
                    lineRequest.UnitPrice,
                    lineRequest.Notes);
            }
        }

        _logger.LogInformation("Purchase order {OrderNumber} has {LineCount} lines before saving",
            orderNumber, purchaseOrder.Lines.Count);

        await _repository.AddAsync(purchaseOrder, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Purchase order {OrderNumber} created successfully with ID {Id}. Lines in DB: {LineCount}",
            orderNumber, purchaseOrder.Id, purchaseOrder.Lines.Count);

        return await MapToResponseAsync(purchaseOrder, request.SupplierId, cancellationToken);
    }


    private async Task<CreatePurchaseOrderResponse> MapToResponseAsync(PurchaseOrder purchaseOrder, long supplierId, CancellationToken cancellationToken)
    {
        var lines = new List<PurchaseOrderLineDto>();

        foreach (var line in purchaseOrder.Lines)
        {
            lines.Add(new PurchaseOrderLineDto
            {
                Id = line.Id,
                MaterialId = line.MaterialId,
                Code = line.MaterialId, // Code is same as MaterialId
                MaterialName = line.MaterialName,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineTotal = line.LineTotal,
                Notes = line.Notes
            });
        }

        var history = purchaseOrder.History.Select(h => new PurchaseOrderHistoryDto
        {
            Id = h.Id,
            Action = h.Action,
            OldValue = h.OldValue,
            NewValue = h.NewValue,
            ChangedAt = h.ChangedAt,
            ChangedBy = h.ChangedBy
        }).ToList();

        return new CreatePurchaseOrderResponse
        {
            Id = purchaseOrder.Id,
            OrderNumber = purchaseOrder.OrderNumber,
            SupplierId = supplierId,
            SupplierName = purchaseOrder.SupplierName,
            OrderDate = purchaseOrder.OrderDate,
            ExpectedDeliveryDate = purchaseOrder.ExpectedDeliveryDate,
            ContactVia = purchaseOrder.ContactVia,
            Status = purchaseOrder.Status.ToString(),
            Notes = purchaseOrder.Notes,
            TotalAmount = purchaseOrder.TotalAmount,
            Lines = lines,
            History = history,
            CreatedAt = purchaseOrder.CreatedAt,
            CreatedBy = purchaseOrder.CreatedBy,
            UpdatedAt = purchaseOrder.UpdatedAt,
            UpdatedBy = purchaseOrder.UpdatedBy
        };
    }
}
```

**Diff highlights:** removed `using Anela.Heblo.Domain.Features.Catalog;`; added `Anela.Heblo.Application.Features.Purchase.Contracts;`; renamed `_catalogRepository : ICatalogRepository` → `_materialCatalog : IMaterialCatalogService`; constructor parameter renamed accordingly. Single call-site change: `_catalogRepository.GetByIdAsync(lineRequest.MaterialId, ct)` → `_materialCatalog.GetByIdAsync(lineRequest.MaterialId, ct)`.

- [ ] **Step 2: Update the existing handler test to use `IMaterialCatalogService`**

Open `backend/test/Anela.Heblo.Tests/Features/Purchase/CreatePurchaseOrderHandlerTests.cs`.

Replace the line `using Anela.Heblo.Domain.Features.Catalog;` with:

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
```

Replace the field:

```csharp
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
```

with:

```csharp
    private readonly Mock<IMaterialCatalogService> _materialCatalogMock;
```

In the constructor, replace:

```csharp
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
```

with:

```csharp
        _materialCatalogMock = new Mock<IMaterialCatalogService>();
```

And in the `_handler = new CreatePurchaseOrderHandler(...)` construction, replace `_catalogRepositoryMock.Object` with `_materialCatalogMock.Object`.

Finally, if any test method sets up `_catalogRepositoryMock.Setup(x => x.GetByIdAsync(...))` returning a `CatalogAggregate`, replace it with a `MaterialInfo`:

```csharp
        _materialCatalogMock
            .Setup(x => x.GetByIdAsync(ValidMaterialId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MaterialInfo
            {
                ProductCode = ValidMaterialId,
                ProductName = ValidName,
            });
```

(Inspect the file in full when editing — only the field/mock/setup names change; assertions stay identical.)

- [ ] **Step 3: Build + run CreatePurchaseOrderHandler tests**

Run:

```bash
dotnet build backend/Anela.Heblo.sln && \
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CreatePurchaseOrderHandlerTests" \
  --no-restore --logger "console;verbosity=normal"
```

Expected: build succeeds; all previously-passing `CreatePurchaseOrderHandlerTests` cases still pass.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/CreatePurchaseOrderHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Purchase/CreatePurchaseOrderHandlerTests.cs
git commit -m "refactor(purchase): CreatePurchaseOrderHandler depends on IMaterialCatalogService"
```

---

## Task 7: Migrate `UpdatePurchaseOrderHandler` (with N+1 fix)

This handler currently performs up to three `GetByIdAsync` calls per request line plus one per line in `MapToResponseAsync`. Switching to `GetByIdsAsync` is the explicit performance improvement mandated by FR-4.2 and NFR-1.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderHandler.cs`

- [ ] **Step 1: Replace the file contents**

Write the complete file body:

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;

public class UpdatePurchaseOrderHandler : IRequestHandler<UpdatePurchaseOrderRequest, UpdatePurchaseOrderResponse>
{
    private readonly ILogger<UpdatePurchaseOrderHandler> _logger;
    private readonly IPurchaseOrderRepository _repository;
    private readonly IMaterialCatalogService _materialCatalog;
    private readonly ICurrentUserService _currentUserService;
    private readonly ISupplierRepository _supplierRepository;

    public UpdatePurchaseOrderHandler(
        ILogger<UpdatePurchaseOrderHandler> logger,
        IPurchaseOrderRepository repository,
        IMaterialCatalogService materialCatalog,
        ICurrentUserService currentUserService,
        ISupplierRepository supplierRepository)
    {
        _logger = logger;
        _repository = repository;
        _materialCatalog = materialCatalog;
        _currentUserService = currentUserService;
        _supplierRepository = supplierRepository;
    }

    public async Task<UpdatePurchaseOrderResponse> Handle(UpdatePurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating purchase order {Id}", request.Id);

        var purchaseOrder = await _repository.GetByIdWithDetailsAsync(request.Id, cancellationToken);

        if (purchaseOrder == null)
        {
            _logger.LogWarning("Purchase order not found for ID {Id}", request.Id);
            return new UpdatePurchaseOrderResponse(ErrorCodes.PurchaseOrderNotFound, new Dictionary<string, string> { { "Id", request.Id.ToString() } });
        }

        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var updatedBy = currentUser.Name ?? "System";

            // Update order number if provided
            if (!string.IsNullOrEmpty(request.OrderNumber) && request.OrderNumber != purchaseOrder.OrderNumber)
            {
                purchaseOrder.UpdateOrderNumber(request.OrderNumber, updatedBy);
            }

            // Get supplier by ID
            var supplier = await _supplierRepository.GetByIdAsync(request.SupplierId, cancellationToken);
            if (supplier == null)
            {
                _logger.LogWarning("Supplier with ID {SupplierId} not found", request.SupplierId);
                return new UpdatePurchaseOrderResponse(ErrorCodes.SupplierNotFound, new Dictionary<string, string> { { "SupplierId", request.SupplierId.ToString() } });
            }

            purchaseOrder.Update(supplier.Id, supplier.Name, request.ExpectedDeliveryDate, request.ContactVia, request.Notes, updatedBy);

            var existingLineIds = purchaseOrder.Lines.Select(l => l.Id).ToHashSet();
            var requestLineIds = request.Lines.Where(l => l.Id.HasValue).Select(l => l.Id!.Value).ToHashSet();

            var linesToRemove = existingLineIds.Except(requestLineIds).ToList();
            foreach (var lineId in linesToRemove)
            {
                purchaseOrder.RemoveLine(lineId);
            }

            // Batch-fetch every material referenced by the incoming request lines.
            // Replaces the previous per-line GetByIdAsync calls (N+1).
            var requestMaterialIds = request.Lines
                .Select(l => l.MaterialId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();
            var materialLookup = requestMaterialIds.Count > 0
                ? await _materialCatalog.GetByIdsAsync(requestMaterialIds, cancellationToken)
                : (IReadOnlyDictionary<string, MaterialInfo>)new Dictionary<string, MaterialInfo>();

            foreach (var lineRequest in request.Lines)
            {
                var materialName = materialLookup.TryGetValue(lineRequest.MaterialId, out var material)
                    ? material.ProductName
                    : lineRequest.Name ?? "Unknown Material";

                if (lineRequest.Id.HasValue)
                {
                    purchaseOrder.UpdateLine(
                        lineRequest.Id.Value,
                        materialName,
                        lineRequest.Quantity,
                        lineRequest.UnitPrice,
                        lineRequest.Notes);
                }
                else
                {
                    purchaseOrder.AddLine(
                        lineRequest.MaterialId,
                        materialName,
                        lineRequest.Quantity,
                        lineRequest.UnitPrice,
                        lineRequest.Notes);
                }
            }

            // Entity is already tracked from GetByIdWithDetailsAsync, EF will auto-detect changes
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Purchase order {OrderNumber} updated successfully", purchaseOrder.OrderNumber);

            return await MapToResponseAsync(purchaseOrder, request.SupplierId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Cannot update purchase order {OrderNumber}: {Message}",
                purchaseOrder.OrderNumber, ex.Message);
            return new UpdatePurchaseOrderResponse(ErrorCodes.PurchaseOrderUpdateFailed, new Dictionary<string, string> { { "OrderNumber", purchaseOrder.OrderNumber }, { "Message", ex.Message } });
        }
    }


    private async Task<UpdatePurchaseOrderResponse> MapToResponseAsync(PurchaseOrder purchaseOrder, long supplierId, CancellationToken cancellationToken)
    {
        // Batch-fetch every material referenced by the final saved order lines.
        // Replaces the previous per-line GetByIdAsync calls (N+1).
        // NOTE: Although the result `materialLookup` is computed, the existing implementation
        // populates `PurchaseOrderLineDto.MaterialName` from `line.MaterialName` (already
        // resolved in Handle above). The mapping below preserves that behavior byte-for-byte.
        // The catalog round-trip exists today only as a (now-unused) side-effect; we drop the
        // unused fetch entirely.
        _ = cancellationToken;

        var lines = new List<PurchaseOrderLineDto>();

        foreach (var line in purchaseOrder.Lines)
        {
            lines.Add(new PurchaseOrderLineDto
            {
                Id = line.Id,
                MaterialId = line.MaterialId,
                Code = line.MaterialId, // Code is same as MaterialId
                MaterialName = line.MaterialName,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineTotal = line.LineTotal,
                Notes = line.Notes
            });
        }

        return new UpdatePurchaseOrderResponse
        {
            Id = purchaseOrder.Id,
            OrderNumber = purchaseOrder.OrderNumber,
            SupplierId = supplierId,
            SupplierName = purchaseOrder.SupplierName,
            OrderDate = purchaseOrder.OrderDate,
            ExpectedDeliveryDate = purchaseOrder.ExpectedDeliveryDate,
            ContactVia = purchaseOrder.ContactVia,
            Status = purchaseOrder.Status.ToString(),
            Notes = purchaseOrder.Notes,
            TotalAmount = purchaseOrder.TotalAmount,
            Lines = lines,
            UpdatedAt = purchaseOrder.UpdatedAt,
            UpdatedBy = purchaseOrder.UpdatedBy
        };
    }
}
```

**Important observation:** Examining the original `MapToResponseAsync`, the per-line `var material = await _catalogRepository.GetByIdAsync(...)` fetch is computed but its result is overwritten by `MaterialName = line.MaterialName` in the DTO (the local `materialName` variable is unused). The new version drops the unused fetch entirely (preserves output, eliminates the redundant round-trip). The `_ = cancellationToken;` discard prevents a compiler warning about the unused parameter while keeping the public signature stable.

If review prefers the parameter cleanly removed, change the signature `MapToResponseAsync(PurchaseOrder purchaseOrder, long supplierId)` and drop both the parameter and the discard; both call sites pass `cancellationToken` so they would need updating. Either approach is acceptable. Keep the discard form to minimize diff churn unless review explicitly asks otherwise.

- [ ] **Step 2: Build the application project**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: succeeds.

- [ ] **Step 3: Build the test project**

Run:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: succeeds. (There is no existing `UpdatePurchaseOrderHandlerTests` file — the only Update test file is `UpdatePurchaseOrderStatusHandlerTests.cs`, which targets a different handler. No test updates needed for this task.)

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderHandler.cs
git commit -m "refactor(purchase): UpdatePurchaseOrderHandler uses IMaterialCatalogService with batch lookup"
```

---

## Task 8: Migrate `GetPurchaseOrderByIdHandler` (with N+1 fix)

Replaces the per-`materialId` loop at lines 50–59 with one `GetByIdsAsync` batch call.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderById/GetPurchaseOrderByIdHandler.cs`

- [ ] **Step 1: Replace the file contents**

Write the complete file body:

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Purchase;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrderById;

public class GetPurchaseOrderByIdHandler : IRequestHandler<GetPurchaseOrderByIdRequest, GetPurchaseOrderByIdResponse>
{
    private readonly ILogger<GetPurchaseOrderByIdHandler> _logger;
    private readonly IPurchaseOrderRepository _repository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly IMaterialCatalogService _materialCatalog;

    public GetPurchaseOrderByIdHandler(
        ILogger<GetPurchaseOrderByIdHandler> logger,
        IPurchaseOrderRepository repository,
        ISupplierRepository supplierRepository,
        IMaterialCatalogService materialCatalog)
    {
        _logger = logger;
        _repository = repository;
        _supplierRepository = supplierRepository;
        _materialCatalog = materialCatalog;
    }

    public async Task<GetPurchaseOrderByIdResponse> Handle(GetPurchaseOrderByIdRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting purchase order details for ID {Id}", request.Id);

        var purchaseOrder = await _repository.GetByIdWithDetailsAsync(request.Id, cancellationToken);

        if (purchaseOrder == null)
        {
            _logger.LogWarning("Purchase order not found for ID {Id}", request.Id);
            return new GetPurchaseOrderByIdResponse(ErrorCodes.PurchaseOrderNotFound, new Dictionary<string, string> { { "Id", request.Id.ToString() } });
        }

        _logger.LogInformation("Found purchase order {OrderNumber} with {LineCount} lines and {HistoryCount} history entries",
            purchaseOrder.OrderNumber, purchaseOrder.Lines.Count, purchaseOrder.History.Count);

        // Load supplier details to get the note
        var supplier = await _supplierRepository.GetByIdAsync(purchaseOrder.SupplierId, cancellationToken);
        var supplierNote = supplier?.Description;

        // Batch-load catalog items to get notes for each material (replaces per-id N+1 loop)
        var materialIds = purchaseOrder.Lines.Select(l => l.MaterialId).Distinct().ToList();
        var materialLookup = materialIds.Count > 0
            ? await _materialCatalog.GetByIdsAsync(materialIds, cancellationToken)
            : (IReadOnlyDictionary<string, MaterialInfo>)new Dictionary<string, MaterialInfo>();

        return new GetPurchaseOrderByIdResponse
        {
            Id = purchaseOrder.Id,
            OrderNumber = purchaseOrder.OrderNumber,
            SupplierId = purchaseOrder.SupplierId,
            SupplierName = purchaseOrder.SupplierName,
            SupplierNote = supplierNote,
            OrderDate = purchaseOrder.OrderDate,
            ExpectedDeliveryDate = purchaseOrder.ExpectedDeliveryDate,
            ContactVia = purchaseOrder.ContactVia,
            Status = purchaseOrder.Status.ToString(),
            InvoiceAcquired = purchaseOrder.InvoiceAcquired,
            Notes = purchaseOrder.Notes,
            TotalAmount = purchaseOrder.TotalAmount,
            IsEditable = purchaseOrder.IsEditable,
            Lines = purchaseOrder.Lines.Select(l => new PurchaseOrderLineDto
            {
                Id = l.Id,
                MaterialId = l.MaterialId,
                Code = l.MaterialId, // Code is same as MaterialId
                MaterialName = l.MaterialName,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                LineTotal = l.LineTotal,
                Notes = l.Notes,
                CatalogNote = materialLookup.TryGetValue(l.MaterialId, out var material) ? material.Note : null
            }).ToList(),
            History = purchaseOrder.History.Select(h => new PurchaseOrderHistoryDto
            {
                Id = h.Id,
                Action = h.Action,
                OldValue = h.OldValue,
                NewValue = h.NewValue,
                ChangedAt = h.ChangedAt,
                ChangedBy = h.ChangedBy
            }).OrderByDescending(h => h.ChangedAt).ToList(),
            CreatedAt = purchaseOrder.CreatedAt,
            CreatedBy = purchaseOrder.CreatedBy,
            UpdatedAt = purchaseOrder.UpdatedAt,
            UpdatedBy = purchaseOrder.UpdatedBy
        };
    }
}
```

**Diff highlights:** removed `using Anela.Heblo.Domain.Features.Catalog;`; replaced the manual `for each materialId: GetByIdAsync` loop building a `Dictionary<string, CatalogAggregate>` with a single `GetByIdsAsync` batch call returning `IReadOnlyDictionary<string, MaterialInfo>`. The `CatalogNote` projection uses `material.Note` (the only field consumed downstream) — `CatalogAggregate.Note` and `MaterialInfo.Note` are both `string?`, response payload is byte-identical.

- [ ] **Step 2: Build the entire solution**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: succeeds. (No existing `GetPurchaseOrderByIdHandlerTests` file in the test project; no test updates needed.)

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderById/GetPurchaseOrderByIdHandler.cs
git commit -m "refactor(purchase): GetPurchaseOrderByIdHandler batch-loads materials via IMaterialCatalogService"
```

---

## Task 9: Migrate `GetPurchaseStockAnalysisHandler` (richest path)

The single largest behavioral edit. All `CatalogAggregate`, `ProductType`, `StockData`, `CatalogProperties`, `CatalogPurchaseRecord` references are removed; `AnalyzeStockItem` is rewritten to take `MaterialStockSnapshot`; the Material/Goods filter and consumption math move into the adapter (now living in `PurchaseMaterialCatalogAdapter`).

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`

- [ ] **Step 1: Replace the file contents**

Write the complete file body:

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Xcc;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;

public class GetPurchaseStockAnalysisHandler : IRequestHandler<GetPurchaseStockAnalysisRequest, GetPurchaseStockAnalysisResponse>
{
    private readonly IMaterialCatalogService _materialCatalog;
    private readonly IStockSeverityCalculator _stockSeverityCalculator;
    private readonly ILogger<GetPurchaseStockAnalysisHandler> _logger;

    public GetPurchaseStockAnalysisHandler(
        IMaterialCatalogService materialCatalog,
        IStockSeverityCalculator stockSeverityCalculator,
        ILogger<GetPurchaseStockAnalysisHandler> logger)
    {
        _materialCatalog = materialCatalog;
        _stockSeverityCalculator = stockSeverityCalculator;
        _logger = logger;
    }

    public async Task<GetPurchaseStockAnalysisResponse> Handle(
        GetPurchaseStockAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        var fromDate = request.FromDate ?? DateTime.UtcNow.AddYears(-1);
        var toDate = request.ToDate ?? DateTime.UtcNow;

        if (fromDate > toDate)
        {
            _logger.LogWarning("Invalid date range: FromDate {FromDate} is after ToDate {ToDate}", fromDate, toDate);
            return new GetPurchaseStockAnalysisResponse(ErrorCodes.InvalidDateRange, new Dictionary<string, string> { { "FromDate", fromDate.ToString() }, { "ToDate", toDate.ToString() } });
        }

        var snapshots = await _materialCatalog.GetStockAnalysisSnapshotsAsync(fromDate, toDate, cancellationToken);

        // First, analyze ALL items for summary calculation
        var allAnalysisItems = snapshots.Select(s => AnalyzeStockItem(s, fromDate, toDate)).ToList();

        // Then filter items for display
        var analysisItems = allAnalysisItems
            .Where(item => ShouldIncludeItem(item, request))
            .ToList();

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var searchTerm = request.SearchTerm.ToLower();
            var normalizedSearchTerm = request.SearchTerm.Trim().NormalizeForSearch();
            analysisItems = analysisItems
                .Where(i => i.ProductCode.ToLower().Contains(searchTerm) ||
                           i.ProductNameNormalized.Contains(normalizedSearchTerm) ||
                           (i.Supplier != null && i.Supplier.Contains(searchTerm, StringComparison.InvariantCultureIgnoreCase)) ||
                           (i.LastPurchase?.SupplierName?.ToLower().Contains(searchTerm) ?? false))
                .ToList();
        }

        analysisItems = SortItems(analysisItems, request.SortBy, request.SortDescending);

        var totalCount = analysisItems.Count;
        var pagedItems = request.IsExport
            ? analysisItems
            : analysisItems
                .Skip((request.PageNumber - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToList();

        // Calculate summary from ALL items, not filtered ones
        var summary = CalculateSummary(allAnalysisItems, fromDate, toDate);

        return new GetPurchaseStockAnalysisResponse
        {
            Items = pagedItems,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            Summary = summary
        };
    }

    private StockAnalysisItemDto AnalyzeStockItem(MaterialStockSnapshot item, DateTime fromDate, DateTime toDate)
    {
        var daysDiff = (toDate - fromDate).Days;
        if (daysDiff <= 0) daysDiff = 1;

        var consumption = item.ConsumptionInPeriod;
        var dailyConsumption = consumption / (double)daysDiff;

        int? daysUntilStockout = null;
        if (dailyConsumption > 0)
        {
            daysUntilStockout = (int)((double)item.Stock.EffectiveStock / dailyConsumption);
        }

        var minStock = item.StockMinSetup;
        var optimalStockDays = item.OptimalStockDaysSetup;
        var optimalStock = optimalStockDays > 0 ? dailyConsumption * (double)optimalStockDays : 0;

        var stockEfficiency = CalculateStockEfficiency((double)item.Stock.EffectiveStock, (double)minStock, optimalStock);
        var severity = _stockSeverityCalculator.DetermineStockSeverity((double)item.Stock.EffectiveStock, (double)minStock, optimalStock, item.IsMinStockConfigured, item.IsOptimalStockConfigured);

        var lastPurchase = GetLastPurchaseInfo(item);

        var recommendedQuantity = CalculateRecommendedOrderQuantity(
            (double)item.Stock.Available,
            optimalStock,
            (double)minStock,
            item.MinimalOrderQuantity);

        return new StockAnalysisItemDto
        {
            ProductCode = item.ProductCode,
            ProductName = item.ProductName,
            ProductNameNormalized = item.ProductNameNormalized,
            ProductType = item.ProductType.ToString(),
            AvailableStock = (double)item.Stock.Available,
            OrderedStock = (double)item.Stock.Ordered,
            EffectiveStock = (double)item.Stock.EffectiveStock,
            MinStockLevel = (double)minStock,
            OptimalStockLevel = optimalStock,
            ConsumptionInPeriod = consumption,
            DailyConsumption = dailyConsumption,
            DaysUntilStockout = daysUntilStockout,
            StockEfficiencyPercentage = stockEfficiency,
            Severity = severity,
            MinimalOrderQuantity = item.MinimalOrderQuantity,
            LastPurchase = lastPurchase,
            Supplier = item.SupplierName,
            RecommendedOrderQuantity = recommendedQuantity,
            IsConfigured = item.IsMinStockConfigured || item.IsOptimalStockConfigured
        };
    }

    private double CalculateStockEfficiency(double availableStock, double minStock, double optimalStock)
    {
        if (optimalStock <= 0)
        {
            return minStock > 0 ? (availableStock / minStock) * 100 : 0;
        }

        return (availableStock / optimalStock) * 100;
    }


    private LastPurchaseInfoDto? GetLastPurchaseInfo(MaterialStockSnapshot item)
    {
        var lastPurchase = item.LastPurchase;

        if (lastPurchase == null)
        {
            return null;
        }

        return new LastPurchaseInfoDto
        {
            Date = lastPurchase.Date,
            SupplierName = lastPurchase.SupplierName,
            Amount = (double)lastPurchase.Amount,
            UnitPrice = lastPurchase.UnitPrice,
            TotalPrice = lastPurchase.TotalPrice
        };
    }

    private double? CalculateRecommendedOrderQuantity(double availableStock, double optimalStock, double minStock, string moq)
    {
        if (optimalStock <= 0 && minStock <= 0)
        {
            return null;
        }

        var targetStock = optimalStock > 0 ? optimalStock : minStock * 2;
        var needed = targetStock - availableStock;

        if (needed <= 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(moq) && double.TryParse(moq, out var minOrderQty))
        {
            return Math.Max(needed, minOrderQty);
        }

        return needed;
    }

    private bool ShouldIncludeItem(StockAnalysisItemDto item, GetPurchaseStockAnalysisRequest request)
    {
        if (request.OnlyConfigured && !item.IsConfigured)
        {
            return false;
        }

        return request.StockStatus switch
        {
            StockStatusFilter.Critical => item.Severity == StockSeverity.Critical,
            StockStatusFilter.Low => item.Severity == StockSeverity.Low,
            StockStatusFilter.Optimal => item.Severity == StockSeverity.Optimal,
            StockStatusFilter.Overstocked => item.Severity == StockSeverity.Overstocked,
            StockStatusFilter.NotConfigured => item.Severity == StockSeverity.NotConfigured,
            _ => true
        };
    }

    private List<StockAnalysisItemDto> SortItems(List<StockAnalysisItemDto> items, StockAnalysisSortBy sortBy, bool descending)
    {
        var sorted = sortBy switch
        {
            StockAnalysisSortBy.ProductCode => items.OrderBy(i => i.ProductCode),
            StockAnalysisSortBy.ProductName => items.OrderBy(i => i.ProductName),
            StockAnalysisSortBy.AvailableStock => items.OrderBy(i => i.AvailableStock),
            StockAnalysisSortBy.Consumption => items.OrderBy(i => i.ConsumptionInPeriod),
            StockAnalysisSortBy.StockEfficiency => items.OrderBy(i => i.StockEfficiencyPercentage),
            StockAnalysisSortBy.LastPurchaseDate => items.OrderBy(i => i.LastPurchase?.Date ?? DateTime.MinValue),
            _ => items.OrderBy(i => i.StockEfficiencyPercentage)
        };

        return descending ? sorted.Reverse().ToList() : sorted.ToList();
    }

    private StockAnalysisSummaryDto CalculateSummary(List<StockAnalysisItemDto> items, DateTime fromDate, DateTime toDate)
    {
        return new StockAnalysisSummaryDto
        {
            TotalProducts = items.Count,
            CriticalCount = items.Count(i => i.Severity == StockSeverity.Critical),
            LowStockCount = items.Count(i => i.Severity == StockSeverity.Low),
            OptimalCount = items.Count(i => i.Severity == StockSeverity.Optimal),
            OverstockedCount = items.Count(i => i.Severity == StockSeverity.Overstocked),
            NotConfiguredCount = items.Count(i => i.Severity == StockSeverity.NotConfigured),
            TotalInventoryValue = items.Sum(i => (decimal)i.EffectiveStock * (i.LastPurchase?.UnitPrice ?? 0)),
            AnalysisPeriodStart = fromDate,
            AnalysisPeriodEnd = toDate
        };
    }
}
```

**Behavioral parity invariants (each must hold byte-for-byte vs. the pre-refactor version):**

| Output field | Original source | New source |
|---|---|---|
| `ProductType` (string) | `item.Type.ToString()` (e.g. `"Material"`) | `item.ProductType.ToString()` (`MaterialProductType.Material` → `"Material"`) — same string |
| `ConsumptionInPeriod` | `GetConsumed`/`GetTotalSold` switched by `item.Type` | `item.ConsumptionInPeriod` (adapter applies the same switch) |
| `LastPurchase.SupplierName` | `lastPurchase.SupplierName ?? string.Empty` | `item.LastPurchase.SupplierName` (adapter applies the `?? string.Empty` coalesce) |
| `LastPurchase.Amount` | `lastPurchase.Amount` (double) | `(double)item.LastPurchase.Amount` (decimal cast back to double in the response DTO) |
| `LastPurchase.UnitPrice/TotalPrice` | `lastPurchase.PricePerPiece` / `PriceTotal` | `item.LastPurchase.UnitPrice` / `TotalPrice` |
| `ProductNameNormalized` | `item.ProductNameNormalized` | `item.ProductNameNormalized` (adapter copies from same property) |

- [ ] **Step 2: Update `GetPurchaseStockAnalysisHandlerTests`**

Open `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs` and replace the entire file with:

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.Services;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Xcc;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public class GetPurchaseStockAnalysisHandlerTests
{
    private readonly Mock<IMaterialCatalogService> _materialCatalogMock;
    private readonly Mock<IStockSeverityCalculator> _stockSeverityCalculatorMock;
    private readonly Mock<ILogger<GetPurchaseStockAnalysisHandler>> _loggerMock;
    private readonly GetPurchaseStockAnalysisHandler _handler;

    public GetPurchaseStockAnalysisHandlerTests()
    {
        _materialCatalogMock = new Mock<IMaterialCatalogService>();
        _stockSeverityCalculatorMock = new Mock<IStockSeverityCalculator>();
        _loggerMock = new Mock<ILogger<GetPurchaseStockAnalysisHandler>>();
        _handler = new GetPurchaseStockAnalysisHandler(_materialCatalogMock.Object, _stockSeverityCalculatorMock.Object, _loggerMock.Object);
    }

    private static MaterialStockSnapshot MakeSnapshot(
        string productCode,
        string productName,
        MaterialProductType type,
        decimal available = 0m,
        decimal ordered = 0m,
        decimal stockMinSetup = 0m,
        int optimalStockDaysSetup = 0,
        string? supplierName = null,
        string minimalOrderQuantity = "",
        double consumptionInPeriod = 0,
        MaterialPurchaseSnapshot? lastPurchase = null)
    {
        var effective = available + ordered;
        return new MaterialStockSnapshot
        {
            ProductCode = productCode,
            ProductName = productName,
            ProductNameNormalized = productName.NormalizeForSearch(),
            ProductType = type,
            SupplierName = supplierName,
            MinimalOrderQuantity = minimalOrderQuantity,
            IsMinStockConfigured = stockMinSetup > 0,
            IsOptimalStockConfigured = optimalStockDaysSetup > 0,
            Stock = new MaterialStockLevels
            {
                Available = available,
                Ordered = ordered,
                EffectiveStock = effective,
            },
            StockMinSetup = stockMinSetup,
            OptimalStockDaysSetup = optimalStockDaysSetup,
            ConsumptionInPeriod = consumptionInPeriod,
            LastPurchase = lastPurchase,
        };
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsAnalysisResponse()
    {
        var snapshots = CreateTestSnapshots();
        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        _stockSeverityCalculatorMock.Setup(x => x.DetermineStockSeverity(
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.Optimal);

        var request = new GetPurchaseStockAnalysisRequest
        {
            FromDate = DateTime.UtcNow.AddMonths(-6),
            ToDate = DateTime.UtcNow,
            StockStatus = StockStatusFilter.All,
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.Items.Should().NotBeEmpty();
        response.TotalCount.Should().Be(2);
        response.PageNumber.Should().Be(1);
        response.PageSize.Should().Be(10);
        response.Summary.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_FilterByCriticalStatus_ReturnsOnlyCriticalItems()
    {
        var snapshots = CreateTestSnapshots();
        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var request = new GetPurchaseStockAnalysisRequest
        {
            StockStatus = StockStatusFilter.Critical,
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        Assert.All(response.Items, item => Assert.Equal(StockSeverity.Critical, item.Severity));
    }

    [Fact]
    public async Task Handle_OnlyConfiguredFilter_ReturnsOnlyConfiguredItems()
    {
        var snapshots = CreateTestSnapshots();
        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var request = new GetPurchaseStockAnalysisRequest
        {
            OnlyConfigured = true,
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        Assert.All(response.Items, item => Assert.True(item.IsConfigured));
    }

    [Fact]
    public async Task Handle_SearchTerm_FiltersItemsBySearchTerm()
    {
        var snapshots = CreateTestSnapshots();
        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var request = new GetPurchaseStockAnalysisRequest
        {
            SearchTerm = "MAT001",
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.Items.Should().HaveCount(1);
        response.Items[0].ProductCode.Should().Be("MAT001");
    }

    [Fact]
    public async Task Handle_InvalidDateRange_ReturnsError()
    {
        var request = new GetPurchaseStockAnalysisRequest
        {
            FromDate = DateTime.UtcNow,
            ToDate = DateTime.UtcNow.AddDays(-1)
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidDateRange);
        result.Params.Should().ContainKey("FromDate");
        result.Params.Should().ContainKey("ToDate");
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectPage()
    {
        var snapshots = CreateManyTestSnapshots(25);
        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var request = new GetPurchaseStockAnalysisRequest
        {
            PageNumber = 2,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.Items.Count.Should().Be(10);
        response.TotalCount.Should().Be(25);
        response.PageNumber.Should().Be(2);
    }

    [Fact]
    public async Task Handle_SortByStockEfficiency_ReturnsSortedItems()
    {
        var snapshots = CreateTestSnapshots();
        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var request = new GetPurchaseStockAnalysisRequest
        {
            SortBy = StockAnalysisSortBy.StockEfficiency,
            SortDescending = true,
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        var efficiencies = response.Items.Select(i => i.StockEfficiencyPercentage).ToList();
        efficiencies.Should().BeEquivalentTo(efficiencies.OrderByDescending(e => e));
    }

    [Fact]
    public async Task Handle_WithOrderedStock_PopulatesEffectiveStockCorrectly()
    {
        var snapshots = new List<MaterialStockSnapshot>
        {
            MakeSnapshot(
                "MAT001",
                "Material with Ordered Stock",
                MaterialProductType.Material,
                available: 50m,
                ordered: 100m,
                stockMinSetup: 20m,
                optimalStockDaysSetup: 30,
                supplierName: "Supplier A",
                minimalOrderQuantity: "100")
        };

        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        _stockSeverityCalculatorMock.Setup(x => x.DetermineStockSeverity(
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.Optimal);

        var request = new GetPurchaseStockAnalysisRequest
        {
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.Items.Should().HaveCount(1);

        var item = response.Items[0];
        item.AvailableStock.Should().Be(50);
        item.OrderedStock.Should().Be(100);
        item.EffectiveStock.Should().Be(150);
    }

    [Fact]
    public async Task Handle_WithoutOrderedStock_PopulatesEffectiveStockAsAvailable()
    {
        var snapshots = new List<MaterialStockSnapshot>
        {
            MakeSnapshot(
                "MAT002",
                "Material without Ordered Stock",
                MaterialProductType.Material,
                available: 75m,
                ordered: 0m,
                stockMinSetup: 20m,
                optimalStockDaysSetup: 30,
                supplierName: "Supplier B",
                minimalOrderQuantity: "50")
        };

        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        _stockSeverityCalculatorMock.Setup(x => x.DetermineStockSeverity(
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.Optimal);

        var request = new GetPurchaseStockAnalysisRequest
        {
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.Items.Should().HaveCount(1);

        var item = response.Items[0];
        item.AvailableStock.Should().Be(75);
        item.OrderedStock.Should().Be(0);
        item.EffectiveStock.Should().Be(75);
    }

    [Fact]
    public async Task Handle_ExportTrue_BypassesPaginationAndReturnsAllFilteredItems()
    {
        var snapshots = CreateManyTestSnapshots(25);
        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var request = new GetPurchaseStockAnalysisRequest
        {
            PageNumber = 1,
            PageSize = 10,
            IsExport = true
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.Items.Count.Should().Be(25, "IsExport=true should return all items, ignoring PageSize");
        response.TotalCount.Should().Be(25);
    }

    private List<MaterialStockSnapshot> CreateTestSnapshots()
    {
        return new List<MaterialStockSnapshot>
        {
            MakeSnapshot(
                "MAT001",
                "Material 1",
                MaterialProductType.Material,
                available: 10m,
                stockMinSetup: 50m,
                optimalStockDaysSetup: 30,
                supplierName: "Supplier A",
                minimalOrderQuantity: "100",
                lastPurchase: new MaterialPurchaseSnapshot
                {
                    Date = DateTime.UtcNow.AddDays(-30),
                    SupplierName = "Supplier A",
                    Amount = 100m,
                    UnitPrice = 10m,
                    TotalPrice = 1000m,
                }),
            MakeSnapshot(
                "GOD001",
                "Goods 1",
                MaterialProductType.Goods,
                available: 100m,
                stockMinSetup: 20m,
                optimalStockDaysSetup: 14,
                supplierName: "Supplier B",
                minimalOrderQuantity: "50",
                consumptionInPeriod: 15)
        };
    }

    private List<MaterialStockSnapshot> CreateManyTestSnapshots(int count)
    {
        var items = new List<MaterialStockSnapshot>();

        for (int i = 0; i < count; i++)
        {
            items.Add(MakeSnapshot(
                productCode: $"MAT{i:D3}",
                productName: $"Material {i}",
                type: i % 2 == 0 ? MaterialProductType.Material : MaterialProductType.Goods,
                available: i * 10m,
                stockMinSetup: i * 5m,
                optimalStockDaysSetup: i % 3 == 0 ? 0 : 30,
                supplierName: $"Supplier {i}",
                minimalOrderQuantity: (i * 10).ToString()));
        }

        return items;
    }
}
```

**Key changes from the original test file:**
- `Mock<ICatalogRepository>` → `Mock<IMaterialCatalogService>`.
- `Setup(x => x.GetAllAsync(...))` returning `List<CatalogAggregate>` → `Setup(x => x.GetStockAnalysisSnapshotsAsync(...))` returning `List<MaterialStockSnapshot>`.
- Test helpers `CreateTestCatalogItems` / `CreateManyTestCatalogItems` → `CreateTestSnapshots` / `CreateManyTestSnapshots` building `MaterialStockSnapshot` directly.
- `using Anela.Heblo.Domain.Features.Catalog.*` removed; `using Anela.Heblo.Xcc;` added (for `NormalizeForSearch`).
- The Diacritics test file (Task 9 Step 3) requires the same treatment.

- [ ] **Step 3: Update `GetPurchaseStockAnalysisHandlerDiacriticsTests`**

Open `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs` and replace the entire file with:

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.Services;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
using Anela.Heblo.Xcc;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public class GetPurchaseStockAnalysisHandlerDiacriticsTests
{
    private readonly Mock<IMaterialCatalogService> _materialCatalogMock;
    private readonly Mock<IStockSeverityCalculator> _stockSeverityCalculatorMock;
    private readonly Mock<ILogger<GetPurchaseStockAnalysisHandler>> _loggerMock;
    private readonly GetPurchaseStockAnalysisHandler _handler;

    public GetPurchaseStockAnalysisHandlerDiacriticsTests()
    {
        _materialCatalogMock = new Mock<IMaterialCatalogService>();
        _stockSeverityCalculatorMock = new Mock<IStockSeverityCalculator>();
        _loggerMock = new Mock<ILogger<GetPurchaseStockAnalysisHandler>>();

        _handler = new GetPurchaseStockAnalysisHandler(
            _materialCatalogMock.Object,
            _stockSeverityCalculatorMock.Object,
            _loggerMock.Object);
    }

    [Theory]
    [InlineData("krém", "Krém na ruce", true)]
    [InlineData("krem", "Krém na ruce", true)]
    [InlineData("KREM", "Krém na ruce", true)]
    [InlineData("cokolada", "Čokoláda", true)]
    [InlineData("čokoláda", "Čokoláda", true)]
    [InlineData("ČOKOLÁDA", "Čokoláda", true)]
    [InlineData("mydlo", "Přírodní mýdlo", true)]
    [InlineData("prirodni", "Přírodní mýdlo", true)]
    [InlineData("xyz", "Krém na ruce", false)]
    public async Task Handle_Should_Find_Materials_Using_Diacritic_Insensitive_Search(
        string searchTerm,
        string productName,
        bool shouldBeFound)
    {
        // Arrange
        var snapshot = new MaterialStockSnapshot
        {
            ProductCode = "TEST001",
            ProductName = productName,
            ProductNameNormalized = productName.NormalizeForSearch(),
            ProductType = MaterialProductType.Material,
            MinimalOrderQuantity = string.Empty,
            IsMinStockConfigured = false,
            IsOptimalStockConfigured = false,
            Stock = new MaterialStockLevels
            {
                Available = 0,
                Ordered = 0,
                EffectiveStock = 0,
            },
            StockMinSetup = 0,
            OptimalStockDaysSetup = 0,
            ConsumptionInPeriod = 0,
        };

        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { snapshot });

        _stockSeverityCalculatorMock.Setup(x => x.DetermineStockSeverity(
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.Optimal);

        var request = new GetPurchaseStockAnalysisRequest
        {
            SearchTerm = searchTerm,
            PageNumber = 1,
            PageSize = 20
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        if (shouldBeFound)
        {
            result.Items.Should().HaveCount(1);
            result.Items[0].ProductName.Should().Be(productName);
            result.Items[0].ProductCode.Should().Be("TEST001");
        }
        else
        {
            result.Items.Should().BeEmpty();
        }
    }
}
```

`NormalizeForSearch()` is the same extension method `CatalogAggregate.ProductName`'s setter calls, so the test reproduces production normalization behavior without depending on `CatalogAggregate`.

- [ ] **Step 4: Build + run all GetPurchaseStockAnalysis tests**

Run:

```bash
dotnet build backend/Anela.Heblo.sln && \
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetPurchaseStockAnalysisHandler" \
  --no-restore --logger "console;verbosity=normal"
```

Expected: build succeeds; every test in `GetPurchaseStockAnalysisHandlerTests` (~9 cases) and `GetPurchaseStockAnalysisHandlerDiacriticsTests` (9 theory cases) passes.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs
git commit -m "refactor(purchase): GetPurchaseStockAnalysisHandler consumes MaterialStockSnapshot via contract"
```

---

## Task 10: Migrate `RecalculatePurchasePriceHandler`

Split into the single-product (`GetByIdAsync` + validate) and all-products (`GetMaterialsWithBomAsync`) paths. The `IProductPriceErpClient` dependency stays — it's the one allowlisted entry from Task 1.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/RecalculatePurchasePrice/RecalculatePurchasePriceHandler.cs`

- [ ] **Step 1: Replace the file contents**

Write the complete file body:

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Price;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.RecalculatePurchasePrice;

public class RecalculatePurchasePriceHandler : IRequestHandler<RecalculatePurchasePriceRequest, RecalculatePurchasePriceResponse>
{
    private readonly IMaterialCatalogService _materialCatalog;
    private readonly IProductPriceErpClient _productPriceClient;
    private readonly ILogger<RecalculatePurchasePriceHandler> _logger;

    public RecalculatePurchasePriceHandler(
        IMaterialCatalogService materialCatalog,
        IProductPriceErpClient productPriceClient,
        ILogger<RecalculatePurchasePriceHandler> logger)
    {
        _materialCatalog = materialCatalog;
        _productPriceClient = productPriceClient;
        _logger = logger;
    }

    public async Task<RecalculatePurchasePriceResponse> Handle(RecalculatePurchasePriceRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting price recalculation - ProductCode: {ProductCode}, RecalculateAll: {RecalculateAll}",
            request.ProductCode, request.RecalculateAll);

        // Validate request
        if (string.IsNullOrEmpty(request.ProductCode) && !request.RecalculateAll)
        {
            _logger.LogWarning("Invalid request: Either ProductCode must be specified or RecalculateAll must be true");
            return new RecalculatePurchasePriceResponse(ErrorCodes.InvalidValue, new Dictionary<string, string> { { "Message", "Either ProductCode must be specified or RecalculateAll must be true" } });
        }

        var response = new RecalculatePurchasePriceResponse();
        List<MaterialBomReference> bomReferences;

        if (!string.IsNullOrEmpty(request.ProductCode))
        {
            // Single product recalculation
            var product = await _materialCatalog.GetByIdAsync(request.ProductCode, cancellationToken);
            if (product == null)
            {
                _logger.LogWarning("Product with code '{ProductCode}' not found", request.ProductCode);
                return new RecalculatePurchasePriceResponse(ErrorCodes.CatalogItemNotFound, new Dictionary<string, string> { { "ProductCode", request.ProductCode } });
            }

            if (!product.HasBoM || !product.BoMId.HasValue)
            {
                _logger.LogWarning("Product '{ProductCode}' does not have BoM", request.ProductCode);
                return new RecalculatePurchasePriceResponse(ErrorCodes.InvalidValue, new Dictionary<string, string> { { "ProductCode", request.ProductCode }, { "Message", $"Product {request.ProductCode} does not have BoM" } });
            }

            bomReferences = new List<MaterialBomReference>
            {
                new MaterialBomReference
                {
                    ProductCode = product.ProductCode,
                    BoMId = product.BoMId.Value,
                }
            };
            _logger.LogInformation("Processing single product: {ProductCode}", request.ProductCode);
        }
        else
        {
            // All products with BoM recalculation
            bomReferences = (await _materialCatalog.GetMaterialsWithBomAsync(cancellationToken)).ToList();
            _logger.LogInformation("Processing {Count} products with BoM", bomReferences.Count);
        }

        response.TotalCount = bomReferences.Count;

        // Process each product
        foreach (var bom in bomReferences)
        {
            try
            {
                _logger.LogDebug("Recalculating price for product {ProductCode} with BoMId {BoMId}",
                    bom.ProductCode, bom.BoMId);

                await _productPriceClient.RecalculatePurchasePrice(bom.BoMId, cancellationToken);

                response.ProcessedProducts.Add(new ProductRecalculationResult
                {
                    ProductCode = bom.ProductCode,
                    Success = true
                });

                response.SuccessCount++;
                _logger.LogDebug("Successfully recalculated price for product {ProductCode}", bom.ProductCode);
            }
            catch (Exception ex)
            {
                response.FailedCount++;
                var errorMessage = ex.Message;

                response.ProcessedProducts.Add(new ProductRecalculationResult
                {
                    ProductCode = bom.ProductCode,
                    Success = false,
                    ErrorCode = ErrorCodes.Exception,
                    Params = new Dictionary<string, string>
                    {
                        { "message", errorMessage },
                        { "exceptionType", ex.GetType().Name }
                    }
                });

                _logger.LogError(ex, "Failed to recalculate price for product {ProductCode}: {ErrorMessage}",
                    bom.ProductCode, errorMessage);
            }
        }

        _logger.LogInformation("Price recalculation completed - Success: {SuccessCount}, Failed: {FailedCount}, Total: {TotalCount}",
            response.SuccessCount, response.FailedCount, response.TotalCount);

        return response;
    }
}
```

**Behavioral parity check:** in the original handler, single-product validation lived inside `RecalculateSingleProduct` (line 109) which threw `InvalidOperationException("Product X does not have BoM")` — the throw was then caught in the outer `catch` and turned into an `Exception` error code on `ProcessedProducts`. In the new version, single-product BoM validation happens **upfront** and returns `ErrorCodes.InvalidValue` directly (matching `ErrorCodes.InvalidValue` semantics used elsewhere — see line 33 of the original handler for the same pattern).

This is a small but observable change: a non-BoM single product is now a top-level error response, not a `ProcessedProducts[0].Success = false` entry. If exact byte-parity matters for fixtures, switch back to throwing inside the `foreach` (and the spec FR-4.5 accepts both behaviors as long as the error code mapping is sensible). The plan picks upfront validation because it removes the unreachable `Exception` path for single-product BoM-missing.

- [ ] **Step 2: Build the entire solution**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: succeeds. (No existing `RecalculatePurchasePriceHandlerTests` file.)

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/RecalculatePurchasePrice/RecalculatePurchasePriceHandler.cs
git commit -m "refactor(purchase): RecalculatePurchasePriceHandler consumes IMaterialCatalogService"
```

---

## Task 11: Final architecture audit and full validation

Triple-check the boundary is clean before declaring done. This task verifies the architectural test goes green and runs the full backend suite.

**Files:** none (audit + commands only).

- [ ] **Step 1: Re-run the architecture theory — `Purchase -> Catalog` must now PASS**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ModuleBoundariesTests" \
  --no-restore --logger "console;verbosity=normal"
```

Expected: all four theory cases (Leaflet, Logistics, PackingMaterials, Purchase) PASS, plus the `Logistics_types_should_not_reference_Purchase_owned_namespaces` fact PASSES.

If `Purchase -> Catalog` still fails, inspect the violation list. Common cases:
- A Purchase handler kept a stray `using Anela.Heblo.Domain.Features.Catalog;` — delete it.
- A test compiled into `Anela.Heblo.Application` (it shouldn't — tests are a separate assembly, but double-check `Anela.Heblo.Application.csproj` did not absorb test files inadvertently).
- A new violation outside the five migrated handlers — do **not** add to the allowlist; instead, lift it behind the contract or file a follow-up arch-review issue.

- [ ] **Step 2: Grep for residual `Anela.Heblo.*Features.Catalog` references inside Purchase source**

Run:

```bash
grep -rn "Anela.Heblo.*Features.Catalog" \
  backend/src/Anela.Heblo.Application/Features/Purchase \
  || echo "CLEAN"
```

Expected output: exactly one line, the legitimate allowlisted one:

```
backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/RecalculatePurchasePrice/RecalculatePurchasePriceHandler.cs:3:using Anela.Heblo.Domain.Features.Catalog.Price;
```

If any other line appears, inspect it: stale using directive → delete; legitimate new dependency → it should have been caught by Step 1, so something is wrong with the architecture test setup.

- [ ] **Step 3: Grep the test side**

Run:

```bash
grep -rn "ICatalogRepository\|CatalogAggregate" \
  backend/test/Anela.Heblo.Tests/Features/Purchase \
  || echo "CLEAN"
```

Expected: exactly `CLEAN`. (FR-7 acceptance: no production-code Purchase handler test references `ICatalogRepository`.)

- [ ] **Step 4: Run `dotnet format` over the touched projects**

Run:

```bash
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: no formatting drift (or any drift is auto-corrected with no failures).

- [ ] **Step 5: Full build + full test run as the final gate**

Run:

```bash
dotnet build backend/Anela.Heblo.sln && \
dotnet test backend/Anela.Heblo.sln --no-build --logger "console;verbosity=normal"
```

Expected: build succeeds with zero errors; every test passes (no regressions anywhere in the solution).

- [ ] **Step 6: If `dotnet format` made changes, commit them**

```bash
git status
# If files are modified:
git add -A
git commit -m "chore: apply dotnet format after Purchase -> Catalog decoupling"
```

If `git status` shows nothing, skip the commit.

---

## Self-Review (already performed)

**Spec coverage:**

| Spec requirement | Task(s) |
|------------------|---------|
| FR-1: Purchase-owned `IMaterialCatalogService` + 6 DTOs + enum, no Catalog usings | Task 2 (Steps 1–8) |
| FR-2: Catalog-side `internal sealed PurchaseMaterialCatalogAdapter` | Task 4 |
| FR-2: Adapter `GetByIdsAsync` delegates to `ICatalogRepository.GetByIdsAsync` once | Task 4 Step 1 + Task 3 Steps `GetByIdsAsync_delegates_to_bulk_repository_method_once_and_keys_by_product_code` |
| FR-3: DI binding in `CatalogModule`, Scoped lifetime, no `PurchaseModule` change | Task 5 |
| FR-4.1: `CreatePurchaseOrderHandler` migrated, no Catalog namespace imports | Task 6 |
| FR-4.2: `UpdatePurchaseOrderHandler` migrated, batch `GetByIdsAsync`, no Catalog namespace imports | Task 7 |
| FR-4.3: `GetPurchaseOrderByIdHandler` migrated, batch `GetByIdsAsync`, byte-identical `CatalogNote` | Task 8 |
| FR-4.4: `GetPurchaseStockAnalysisHandler` migrated, `AnalyzeStockItem` takes `MaterialStockSnapshot`, no Catalog refs, `StockSeverity` unmoved | Task 9 |
| FR-4.5: `RecalculatePurchasePriceHandler` migrated, single-product path uses `GetByIdAsync` + HasBoM/BoMId.HasValue validation, all-products uses `GetMaterialsWithBomAsync`, `IProductPriceErpClient` stays | Task 10 |
| FR-5: New `Purchase -> Catalog` row in `ModuleBoundariesTests`, exactly one allowlist entry for `IProductPriceErpClient` | Task 1 (red) + Task 11 (green) |
| FR-6: Adapter unit tests under `Features/Catalog/Infrastructure/` (12 tests including Material/Goods filter, GetByIdsAsync no-fallback, GetMaterialsWithBomAsync filter, behavior-parity for GetConsumed/GetTotalSold) | Task 3 + Task 4 |
| FR-7: Existing Purchase handler tests updated to mock `IMaterialCatalogService` (including `GetPurchaseStockAnalysisHandlerDiacriticsTests`) | Task 6 Step 2, Task 9 Steps 2–3 |
| NFR-1: Performance — adapter single-fetch for stock analysis; batch lookups for handlers; no `GetAllAsync` fallback in `GetByIdsAsync` | Task 4 (Step 1: adapter uses `_catalogRepository.GetByIdsAsync` directly) |
| NFR-2: Backwards compatibility — no DTO changes, no HTTP changes, no migration | Tasks 6–10 (handler DTOs untouched) |
| NFR-3: Security — internal refactor only | Inherent |
| NFR-4: nullable on, `CancellationToken` everywhere, `internal sealed` adapter, `dotnet build` + `dotnet format` clean | Tasks 2, 4, 11 |
| Arch-review amendment 1: corrected DI lifetime rationale (Scoped, not "consistent with `ICatalogRepository`") | Task 5 Step 2 comment |
| Arch-review amendment 2: `GetPurchaseStockAnalysisHandlerDiacriticsTests` explicitly covered | Task 9 Step 3 |
| Arch-review amendment 3: behavior-parity test asserting `ConsumptionInPeriod == CatalogAggregate.GetConsumed/GetTotalSold` | Task 3 Step 1 (tests `GetStockAnalysisSnapshotsAsync_consumption_uses_GetConsumed_for_material` and `…GetTotalSold_for_goods`) |

**Placeholder scan:** every code step contains the complete code to write. No "TBD," "implement later," or "appropriate error handling" placeholders.

**Type consistency:**
- `IMaterialCatalogService` method signatures match across all tasks (Task 2 declaration, Task 3 mock setups, Task 4 implementation, Tasks 6–10 call sites).
- `MaterialInfo` shape (`ProductCode`, `ProductName`, `Note`, `HasBoM`, `BoMId`) is consistent in Task 2, Task 4 (`ToMaterialInfo`), Task 6, Task 7, Task 8, Task 10.
- `MaterialStockSnapshot` shape — `Stock` (a `MaterialStockLevels`), `StockMinSetup`, `OptimalStockDaysSetup`, `ConsumptionInPeriod`, `LastPurchase` — is consistent in Task 2, Task 4 (`ToStockSnapshot`), Task 9 (handler + tests).
- `MaterialBomReference` `BoMId` is non-nullable `int` everywhere (Task 2 declaration, Task 4 `GetMaterialsWithBomAsync`, Task 10 single-product allocation).
- `MaterialPurchaseSnapshot.Amount` is `decimal` in Task 2 / Task 4; the handler casts back to `double` in `GetLastPurchaseInfo` (Task 9 Step 1) — verify by re-reading Task 9. Confirmed: handler does `Amount = (double)lastPurchase.Amount` (line in `GetLastPurchaseInfo`).
- `_materialCatalog` is the consistent field name across all five handlers (vs. `_catalogRepository` before).
- `PurchaseAllowlist` is referenced by name in Task 1 Step 1 (declaration) and Task 1 Step 2 (`Allowlist: PurchaseAllowlist`).

No issues found.
