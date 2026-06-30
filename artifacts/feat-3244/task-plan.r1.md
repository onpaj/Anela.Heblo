# Decouple CatalogAggregate from ManufactureHistoryRecord Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `ManufactureHistoryRecord` (Manufacture domain type) inside `CatalogAggregate` and the entire Catalog application layer with a new Catalog-owned `CatalogManufactureRecord`, mapping only at the adapter boundary.

**Architecture:** Create `CatalogManufactureRecord` in `Anela.Heblo.Domain.Features.Catalog.ManufactureHistory` following the `CatalogPurchaseRecord` pattern. Update the aggregate, contract, adapter, cache, merge, refresh, and cost providers to use the new type. The `ManufactureCatalogSourceAdapter` is the single translation boundary: it receives `ManufactureHistoryRecord` from `IManufactureHistoryClient` and maps to `CatalogManufactureRecord` before returning via `ICatalogManufactureSource`.

**Tech Stack:** .NET 8, C# — no new packages required.

---

### task: create-catalog-manufacture-record

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/ManufactureHistory/CatalogManufactureRecord.cs`

- [ ] Step 1: Create directory `backend/src/Anela.Heblo.Domain/Features/Catalog/ManufactureHistory/` (it does not exist yet — check `PurchaseHistory/` as the reference: that directory contains only `CatalogPurchaseRecord.cs`).

- [ ] Step 2: Create `CatalogManufactureRecord.cs` with the exact content below. It must be a `class` (not a `record`) — the project rule forbids records on types that cross API/client generation boundaries. Mirror the field set from `ManufactureHistoryRecord` (which has `Date`, `Amount`, `PricePerPiece`, `PriceTotal`, `ProductCode`, `DocumentNumber` — no `SupplierId`/`SupplierName`):

```csharp
namespace Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;

public class CatalogManufactureRecord
{
    public DateTime Date { get; set; }
    public double Amount { get; set; }
    public decimal PricePerPiece { get; set; }
    public decimal PriceTotal { get; set; }
    public string ProductCode { get; set; }
    public string DocumentNumber { get; set; }
}
```

- [ ] Step 3: Verify the build compiles cleanly for the Domain project only:
```
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj -v quiet
```

- [ ] Step 4: Commit:
```
git add backend/src/Anela.Heblo.Domain/Features/Catalog/ManufactureHistory/CatalogManufactureRecord.cs
git commit -m "feat: add CatalogManufactureRecord domain type to Catalog.ManufactureHistory namespace"
```

---

### task: decouple-source-layer

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogManufactureSource.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapter.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogCacheStore.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/FlatManufactureCostProvider.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProvider.cs`

#### FR-2: CatalogAggregate.cs

Current state (lines 8, 92, 124–132):
```csharp
// line 8
using Anela.Heblo.Domain.Features.Manufacture;
// line 92
private IReadOnlyList<ManufactureHistoryRecord> _manufactureHistory = new List<ManufactureHistoryRecord>();
// lines 124–132
public IReadOnlyList<ManufactureHistoryRecord> ManufactureHistory
{
    get => _manufactureHistory;
    set
    {
        _manufactureHistory = value;
        // No summary update needed for manufacture history yet
    }
}
```

- [ ] Step 1: In `CatalogAggregate.cs`, replace `using Anela.Heblo.Domain.Features.Manufacture;` (line 8) with `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;`.

- [ ] Step 2: Replace the backing field on line 92:
```csharp
// old
private IReadOnlyList<ManufactureHistoryRecord> _manufactureHistory = new List<ManufactureHistoryRecord>();
// new
private IReadOnlyList<CatalogManufactureRecord> _manufactureHistory = new List<CatalogManufactureRecord>();
```

- [ ] Step 3: Replace the property declaration on lines 124–132:
```csharp
// old
public IReadOnlyList<ManufactureHistoryRecord> ManufactureHistory
{
    get => _manufactureHistory;
    set
    {
        _manufactureHistory = value;
        // No summary update needed for manufacture history yet
    }
}
// new
public IReadOnlyList<CatalogManufactureRecord> ManufactureHistory
{
    get => _manufactureHistory;
    set
    {
        _manufactureHistory = value;
        // No summary update needed for manufacture history yet
    }
}
```

#### FR-3: ICatalogManufactureSource.cs

Current state (entire file):
```csharp
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Catalog.Contracts;

/// <summary>
/// Catalog-owned read abstraction over Manufacture planned-quantities, production history,
/// and manufactured-inventory totals. Implemented by the Manufacture module via an adapter.
///
/// NOTE: Returns Domain.Features.Manufacture.ManufactureHistoryRecord — a deliberate
/// pragmatic leak because this type is already woven through Catalog's CachedManufactureHistoryData
/// and CatalogAggregate.ManufactureHistory. The leak is allowlisted in ModuleBoundariesTests.
/// Tracked follow-up: introduce a Catalog-owned CatalogManufactureHistoryRecord DTO.
/// </summary>
public interface ICatalogManufactureSource
{
    Task<Dictionary<string, decimal>> GetPlannedQuantitiesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ManufactureHistoryRecord>> GetManufactureHistoryAsync(
        DateTime dateFrom,
        DateTime dateTo,
        CancellationToken cancellationToken);

    Task<Dictionary<string, decimal>> GetManufacturedInventoryAsync(CancellationToken cancellationToken);
}
```

- [ ] Step 4: Replace the entire file content with:
```csharp
using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;

namespace Anela.Heblo.Application.Features.Catalog.Contracts;

/// <summary>
/// Catalog-owned read abstraction over Manufacture planned-quantities, production history,
/// and manufactured-inventory totals. Implemented by the Manufacture module via an adapter.
/// </summary>
public interface ICatalogManufactureSource
{
    Task<Dictionary<string, decimal>> GetPlannedQuantitiesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogManufactureRecord>> GetManufactureHistoryAsync(
        DateTime dateFrom,
        DateTime dateTo,
        CancellationToken cancellationToken);

    Task<Dictionary<string, decimal>> GetManufacturedInventoryAsync(CancellationToken cancellationToken);
}
```

#### FR-4: ManufactureCatalogSourceAdapter.cs

Current state (entire file):
```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;

namespace Anela.Heblo.Application.Features.Manufacture.Infrastructure;

internal sealed class ManufactureCatalogSourceAdapter : ICatalogManufactureSource
{
    private readonly IManufactureOrderRepository _orderRepository;
    private readonly IManufactureHistoryClient _historyClient;
    private readonly IManufacturedProductInventoryRepository _inventoryRepository;

    public ManufactureCatalogSourceAdapter(
        IManufactureOrderRepository orderRepository,
        IManufactureHistoryClient historyClient,
        IManufacturedProductInventoryRepository inventoryRepository)
    {
        _orderRepository = orderRepository;
        _historyClient = historyClient;
        _inventoryRepository = inventoryRepository;
    }

    public Task<Dictionary<string, decimal>> GetPlannedQuantitiesAsync(CancellationToken cancellationToken) =>
        _orderRepository.GetPlannedQuantitiesAsync(cancellationToken);

    public async Task<IReadOnlyList<ManufactureHistoryRecord>> GetManufactureHistoryAsync(
        DateTime dateFrom,
        DateTime dateTo,
        CancellationToken cancellationToken) =>
        await _historyClient.GetHistoryAsync(dateFrom, dateTo, productCode: null, cancellationToken);

    public Task<Dictionary<string, decimal>> GetManufacturedInventoryAsync(CancellationToken cancellationToken) =>
        _inventoryRepository.GetTotalAmountByProductCodeAsync(cancellationToken);
}
```

- [ ] Step 5: Replace the entire file with the mapping implementation. The adapter calls `_historyClient.GetHistoryAsync(...)` which returns `IReadOnlyList<ManufactureHistoryRecord>`, then maps each element to `CatalogManufactureRecord` inline. Do NOT change `IManufactureHistoryClient` or `FlexiManufactureHistoryClient`:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;

namespace Anela.Heblo.Application.Features.Manufacture.Infrastructure;

internal sealed class ManufactureCatalogSourceAdapter : ICatalogManufactureSource
{
    private readonly IManufactureOrderRepository _orderRepository;
    private readonly IManufactureHistoryClient _historyClient;
    private readonly IManufacturedProductInventoryRepository _inventoryRepository;

    public ManufactureCatalogSourceAdapter(
        IManufactureOrderRepository orderRepository,
        IManufactureHistoryClient historyClient,
        IManufacturedProductInventoryRepository inventoryRepository)
    {
        _orderRepository = orderRepository;
        _historyClient = historyClient;
        _inventoryRepository = inventoryRepository;
    }

    public Task<Dictionary<string, decimal>> GetPlannedQuantitiesAsync(CancellationToken cancellationToken) =>
        _orderRepository.GetPlannedQuantitiesAsync(cancellationToken);

    public async Task<IReadOnlyList<CatalogManufactureRecord>> GetManufactureHistoryAsync(
        DateTime dateFrom,
        DateTime dateTo,
        CancellationToken cancellationToken)
    {
        var records = await _historyClient.GetHistoryAsync(dateFrom, dateTo, productCode: null, cancellationToken);
        return records
            .Select(r => new CatalogManufactureRecord
            {
                Date = r.Date,
                Amount = r.Amount,
                PricePerPiece = r.PricePerPiece,
                PriceTotal = r.PriceTotal,
                ProductCode = r.ProductCode,
                DocumentNumber = r.DocumentNumber,
            })
            .ToList();
    }

    public Task<Dictionary<string, decimal>> GetManufacturedInventoryAsync(CancellationToken cancellationToken) =>
        _inventoryRepository.GetTotalAmountByProductCodeAsync(cancellationToken);
}
```

#### FR-5: CatalogCacheStore.cs

Affected section is lines 10, 267–275. The two methods `GetManufactureHistoryData` and `SetManufactureHistoryData` reference `ManufactureHistoryRecord`.

- [ ] Step 6: Remove `using Anela.Heblo.Domain.Features.Manufacture;` (line 10) and add `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;`.

- [ ] Step 7: Replace the two methods (lines 267–275):
```csharp
// old
public IList<ManufactureHistoryRecord> GetManufactureHistoryData() =>
    _cache.Get<List<ManufactureHistoryRecord>>(CachedManufactureHistoryDataKey) ?? new List<ManufactureHistoryRecord>();

public void SetManufactureHistoryData(IList<ManufactureHistoryRecord> value)
{
    _cache.Set(CachedManufactureHistoryDataKey, value);
    InvalidateSourceData(CachedManufactureHistoryDataKey);
    SetLoadDateInCache(CachedManufactureHistoryDataKey);
}
// new
public IList<CatalogManufactureRecord> GetManufactureHistoryData() =>
    _cache.Get<List<CatalogManufactureRecord>>(CachedManufactureHistoryDataKey) ?? new List<CatalogManufactureRecord>();

public void SetManufactureHistoryData(IList<CatalogManufactureRecord> value)
{
    _cache.Set(CachedManufactureHistoryDataKey, value);
    InvalidateSourceData(CachedManufactureHistoryDataKey);
    SetLoadDateInCache(CachedManufactureHistoryDataKey);
}
```

#### FR-6: CatalogMergeService.cs

Affected: line 10 (`using Anela.Heblo.Domain.Features.Manufacture;`) and the `MergeHistoryData` private method signature at lines 210–215 and usage at lines 95–97.

- [ ] Step 8: Remove `using Anela.Heblo.Domain.Features.Manufacture;` (line 10) and add `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;`.

- [ ] Step 9: Replace the `manufactureMap` variable initialization (lines 95–97):
```csharp
// old
var manufactureMap = _cacheStore.GetManufactureHistoryData()
    .GroupBy(p => p.ProductCode)
    .ToDictionary(k => k.Key, v => v.ToList());
// new  (identical logic, type inference handles the rest)
var manufactureMap = _cacheStore.GetManufactureHistoryData()
    .GroupBy(p => p.ProductCode)
    .ToDictionary(k => k.Key, v => v.ToList());
```
Note: no text change needed here — `GetManufactureHistoryData()` now returns `IList<CatalogManufactureRecord>` so the dictionary type becomes `Dictionary<string, List<CatalogManufactureRecord>>` automatically.

- [ ] Step 10: Replace the `MergeHistoryData` method signature (lines 210–215) and the assignment on line 229:
```csharp
// old signature
private static void MergeHistoryData(
    CatalogAggregate product,
    IDictionary<string, List<ConsumedMaterialRecord>> consumedMap,
    IDictionary<string, List<CatalogPurchaseRecord>> purchaseMap,
    IDictionary<string, List<ManufactureHistoryRecord>> manufactureMap,
    IDictionary<string, List<StockTakingRecord>> stockTakingMap)
// new signature
private static void MergeHistoryData(
    CatalogAggregate product,
    IDictionary<string, List<ConsumedMaterialRecord>> consumedMap,
    IDictionary<string, List<CatalogPurchaseRecord>> purchaseMap,
    IDictionary<string, List<CatalogManufactureRecord>> manufactureMap,
    IDictionary<string, List<StockTakingRecord>> stockTakingMap)
```

The assignment inside the method body (line 229) is `product.ManufactureHistory = manufactures.ToList();` — no text change needed because `manufactures` is already `List<CatalogManufactureRecord>` from the typed dictionary.

#### FR-7: CatalogDataRefreshService.cs

Affected: line 14 (the retained comment + using for `ManufactureHistoryRecord`).

- [ ] Step 11: Remove lines 12–14 entirely:
```csharp
// old (lines 12–14)
// Retained for ManufactureHistoryRecord return type used by ICatalogManufactureSource.GetManufactureHistoryAsync
// and CatalogAggregate.ManufactureHistory. Track follow-up: introduce Catalog-owned DTO.
using Anela.Heblo.Domain.Features.Manufacture;
```
There is no replacement needed — `CatalogDataRefreshService` references manufacture data only through `ICatalogManufactureSource` (which now returns `CatalogManufactureRecord`) and `CatalogCacheStore` (which now stores `CatalogManufactureRecord`). The only remaining usages of manufacture types are `IManufactureDifficultyRepository` and `ManufactureDifficultySetting` which are in `Anela.Heblo.Domain.Features.Manufacture` but covered by the `ManufactureCatalogAllowlist` for the difficulty settings — check: `ManufactureDifficultySetting` is NOT `ManufactureHistoryRecord`, so its using directive is fine. After removing only the `ManufactureHistoryRecord`-specific using, verify the build succeeds (the `ManufactureDifficultySetting` type still resolves via a different using that is already present in the file — specifically none, meaning `ManufactureDifficultySetting` must already be resolvable; if the build fails, add `using Anela.Heblo.Domain.Features.Manufacture;` back as a separate targeted check).

Actually: looking at the full file, `ManufactureDifficultySetting` is used at lines 201–207. Currently `using Anela.Heblo.Domain.Features.Manufacture;` on line 14 is the ONLY using that brings in `ManufactureDifficultySetting`. So the correct action is: keep the using for the difficulty settings but remove the comment that called it out as a leak for `ManufactureHistoryRecord`. The correct replacement:

```csharp
// old (lines 12–14)
// Retained for ManufactureHistoryRecord return type used by ICatalogManufactureSource.GetManufactureHistoryAsync
// and CatalogAggregate.ManufactureHistory. Track follow-up: introduce Catalog-owned DTO.
using Anela.Heblo.Domain.Features.Manufacture;

// new (replace with just the using, no comment)
using Anela.Heblo.Domain.Features.Manufacture;
```

The using itself stays but the two comment lines above it are removed.

#### FR-8: FlatManufactureCostProvider.cs

Affected: line 8 `using Anela.Heblo.Domain.Features.Manufacture;`. After the aggregate change, `product.ManufactureHistory` is now `IReadOnlyList<CatalogManufactureRecord>`, so the provider no longer references `ManufactureHistoryRecord` directly — the LINQ in `CalculateWeightedManufactureTotals` (lines 160–179) accesses `s.Date` and `s.Amount` which exist on `CatalogManufactureRecord` identically. The using is therefore unused after the aggregate change.

- [ ] Step 12: Remove `using Anela.Heblo.Domain.Features.Manufacture;` (line 8) from `FlatManufactureCostProvider.cs`. No other changes needed.

#### FR-8 (continued): ManufactureBasedMaterialCostProvider.cs

Inspecting the file: there is NO `using Anela.Heblo.Domain.Features.Manufacture;` in this file (confirmed: the file's usings at lines 1–8 do not include it). The provider accesses `product.ManufactureHistory` properties (`m.Date`, `m.PricePerPiece`, `m.Amount`) which are structural — after the aggregate change the type is `CatalogManufactureRecord` but the property names are identical. No changes needed.

- [ ] Step 13: Verify `ManufactureBasedMaterialCostProvider.cs` has no `using Anela.Heblo.Domain.Features.Manufacture;` and requires no edit. If a stale using is present, remove it.

- [ ] Step 14: Build the entire backend to confirm all compile errors are resolved:
```
dotnet build backend/src/ -v quiet
```

- [ ] Step 15: Commit all source changes together:
```
git add backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs
git add backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogManufactureSource.cs
git add backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapter.cs
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogCacheStore.cs
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs
git add backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/FlatManufactureCostProvider.cs
git commit -m "feat: decouple Catalog source layer from ManufactureHistoryRecord — use CatalogManufactureRecord throughout"
```

---

### task: fix-tests-and-allowlist

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/FlatManufactureCostProviderTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs`

#### FR-9: ModuleBoundariesTests.cs — remove 8 allowlist entries

The 8 entries to remove are in `CatalogManufactureAllowlist` (lines 137–146). The block to remove:

```csharp
        // Deliberate pragmatic leak: ManufactureHistoryRecord flows through Catalog's cache layer.
        // All entries below are tracked under the same follow-up: introduce Catalog-owned
        // CatalogManufactureHistoryRecord DTO and map in the ManufactureCatalogSourceAdapter.
        "Anela.Heblo.Application.Features.Catalog.CatalogRepository -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        "Anela.Heblo.Application.Features.Catalog.Contracts.ICatalogManufactureSource -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.CatalogCacheStore -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.CatalogDataRefreshService -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        "Anela.Heblo.Application.Features.Catalog.Infrastructure.CatalogMergeService -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        // Cost providers in Catalog.CostProviders compute costs from ManufactureHistoryRecord.
        "Anela.Heblo.Application.Features.Catalog.CostProviders.FlatManufactureCostProvider -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        "Anela.Heblo.Application.Features.Catalog.CostProviders.ManufactureBasedMaterialCostProvider -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
        // GetCatalogDetailHandler maps ManufactureHistoryRecord from CatalogAggregate into response DTOs.
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail.GetCatalogDetailHandler -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
```

- [ ] Step 1: In `ModuleBoundariesTests.cs`, remove the 8 string entries listed above from `CatalogManufactureAllowlist`, along with their associated comments. The 3 entries for `IManufactureClient` handlers (`UpdateProductCompositionOrderHandler`, `GetProductCompositionHandler`, `GetProductUsageHandler`) and the entries for `ManufactureTemplate`, `Ingredient`, `GetProductUsageResponse` must remain — do not touch them.

After the removal the `CatalogManufactureAllowlist` should retain exactly these entries:
```csharp
    private static readonly HashSet<string> CatalogManufactureAllowlist = new(StringComparer.Ordinal)
    {
        // Follow-up: migrate UpdateProductCompositionOrderHandler off IManufactureClient.
        "Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder.UpdateProductCompositionOrderHandler -> Anela.Heblo.Domain.Features.Manufacture.IManufactureClient",

        // Follow-up: migrate GetProductCompositionHandler off IManufactureClient.
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition.GetProductCompositionHandler -> Anela.Heblo.Domain.Features.Manufacture.IManufactureClient",

        // Follow-up: migrate GetProductUsageHandler off IManufactureClient.
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage.GetProductUsageHandler -> Anela.Heblo.Domain.Features.Manufacture.IManufactureClient",

        // GetProductUsageResponse holds ManufactureTemplate in its payload.
        // Follow-up: introduce Catalog-owned ManufactureTemplateDto.
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage.GetProductUsageResponse -> Anela.Heblo.Domain.Features.Manufacture.ManufactureTemplate",

        // Handlers reference ManufactureTemplate and Ingredient directly via IManufactureClient.
        // Compiler-generated types (+<>c, +d__N) are covered by the declaring-type check.
        "Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder.UpdateProductCompositionOrderHandler -> Anela.Heblo.Domain.Features.Manufacture.ManufactureTemplate",
        "Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder.UpdateProductCompositionOrderHandler -> Anela.Heblo.Domain.Features.Manufacture.Ingredient",
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage.GetProductUsageHandler -> Anela.Heblo.Domain.Features.Manufacture.ManufactureTemplate",
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition.GetProductCompositionHandler -> Anela.Heblo.Domain.Features.Manufacture.ManufactureTemplate",
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition.GetProductCompositionHandler -> Anela.Heblo.Domain.Features.Manufacture.Ingredient",
    };
```

#### FR-10a: GetCatalogDetailHandlerFullHistoryTests.cs

The test file uses `ManufactureHistoryRecord` in two places:
1. Line 9: `using Anela.Heblo.Domain.Features.Manufacture;`
2. Lines 174–194: two `new ManufactureHistoryRecord { ... }` object initializers in `Handle_Should_Exclude_PreFloor_Records_When_MonthsBack_Is_999`

- [ ] Step 2: Replace `using Anela.Heblo.Domain.Features.Manufacture;` (line 9) with `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;`.

- [ ] Step 3: Replace the two `ManufactureHistoryRecord` instantiations (lines 174–194):
```csharp
// old
        catalogItem.ManufactureHistory = new List<ManufactureHistoryRecord>
        {
            new ManufactureHistoryRecord
            {
                Date = new DateTime(2019, 12, 31),
                Amount = 5,
                PricePerPiece = 3.0M,
                PriceTotal = 15.0M,
                ProductCode = "TEST002",
                DocumentNumber = "MFG-PRE-FLOOR-001"
            },
            new ManufactureHistoryRecord
            {
                Date = new DateTime(2020, 1, 1),
                Amount = 7,
                PricePerPiece = 4.0M,
                PriceTotal = 28.0M,
                ProductCode = "TEST002",
                DocumentNumber = "MFG-AT-FLOOR-001"
            }
        };
// new
        catalogItem.ManufactureHistory = new List<CatalogManufactureRecord>
        {
            new CatalogManufactureRecord
            {
                Date = new DateTime(2019, 12, 31),
                Amount = 5,
                PricePerPiece = 3.0M,
                PriceTotal = 15.0M,
                ProductCode = "TEST002",
                DocumentNumber = "MFG-PRE-FLOOR-001"
            },
            new CatalogManufactureRecord
            {
                Date = new DateTime(2020, 1, 1),
                Amount = 7,
                PricePerPiece = 4.0M,
                PriceTotal = 28.0M,
                ProductCode = "TEST002",
                DocumentNumber = "MFG-AT-FLOOR-001"
            }
        };
```

#### FR-10b: FlatManufactureCostProviderTests.cs

The test file uses `ManufactureHistoryRecord` in multiple places:
- Line 8: `using Anela.Heblo.Domain.Features.Manufacture;`
- Lines 55–60, 122–125, 138–141, 199–200, 246–249, 304–305, 319–323: `new ManufactureHistoryRecord { ... }` inside `ManufactureHistory = new List<ManufactureHistoryRecord> { ... }` assignments

- [ ] Step 4: Replace `using Anela.Heblo.Domain.Features.Manufacture;` (line 8) with `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;`.

- [ ] Step 5: Replace every `ManufactureHistoryRecord` with `CatalogManufactureRecord` throughout the file. Use a global replace — the type name is not used for anything other than manufacture history records in this file. All occurrences are in `new List<ManufactureHistoryRecord>` and `new ManufactureHistoryRecord` patterns:

Replace all occurrences of:
- `new List<ManufactureHistoryRecord>` → `new List<CatalogManufactureRecord>`
- `new ManufactureHistoryRecord` → `new CatalogManufactureRecord`
- `List<ManufactureHistoryRecord>` → `List<CatalogManufactureRecord>`

#### FR-10c: ManufactureBasedMaterialCostProviderTests.cs

The test file uses `ManufactureHistoryRecord` in these places:
- Line 7: `using Anela.Heblo.Domain.Features.Manufacture;`
- Lines 52–63 in `BuildManufacturedProduct`: `new ManufactureHistoryRecord { ... }` inside `.Select(h => new ManufactureHistoryRecord { ... })`

- [ ] Step 6: Replace `using Anela.Heblo.Domain.Features.Manufacture;` (line 7) with `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;`.

- [ ] Step 7: Replace the `.Select(h => new ManufactureHistoryRecord { ... })` in `BuildManufacturedProduct` (lines 52–63):
```csharp
// old
        if (history != null)
        {
            agg.ManufactureHistory = history
                .Select(h => new ManufactureHistoryRecord
                {
                    Date = h.date,
                    PricePerPiece = h.pricePerPiece,
                    Amount = h.amount,
                    PriceTotal = h.pricePerPiece * (decimal)h.amount,
                    ProductCode = productCode,
                    DocumentNumber = "TEST-DOC",
                })
                .ToList();
        }
// new
        if (history != null)
        {
            agg.ManufactureHistory = history
                .Select(h => new CatalogManufactureRecord
                {
                    Date = h.date,
                    PricePerPiece = h.pricePerPiece,
                    Amount = h.amount,
                    PriceTotal = h.pricePerPiece * (decimal)h.amount,
                    ProductCode = productCode,
                    DocumentNumber = "TEST-DOC",
                })
                .ToList();
        }
```

#### FR-10d: CatalogRepositoryTests.cs

The test file uses `ManufactureHistoryRecord` in these places:
- Line 16: `using Anela.Heblo.Domain.Features.Manufacture;`
- Lines 79–80: `.ReturnsAsync(new List<ManufactureHistoryRecord>())`

- [ ] Step 8: In `CatalogRepositoryTests.cs`, replace `using Anela.Heblo.Domain.Features.Manufacture;` (line 16) with `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;`.

- [ ] Step 9: Replace the mock setup return at lines 79–80:
```csharp
// old
        _manufactureSourceMock
            .Setup(x => x.GetManufactureHistoryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureHistoryRecord>());
// new
        _manufactureSourceMock
            .Setup(x => x.GetManufactureHistoryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogManufactureRecord>());
```

Note: After this change, verify `ManufactureDifficultySetting` (used at lines 211–212 in the constructor mock setup in `CatalogRepositoryCacheOptimizationTests.cs`) still resolves — `ManufactureDifficultySetting` is in `Anela.Heblo.Domain.Features.Manufacture` but this test references it through `IManufactureDifficultyRepository` which may have its own using already. If the `using` for the difficulty setting was the only reason the `Manufacture` namespace was imported, a separate using may need to be added for just the difficulty types. Check after build.

#### FR-10e: CatalogRepositoryCacheOptimizationTests.cs

The test file uses `ManufactureHistoryRecord` in these places:
- Line 15: `using Anela.Heblo.Domain.Features.Manufacture;`
- Lines 214–215: `.ReturnsAsync(new List<ManufactureHistoryRecord>())`

- [ ] Step 10: In `CatalogRepositoryCacheOptimizationTests.cs`, replace `using Anela.Heblo.Domain.Features.Manufacture;` (line 15) with `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;`.

- [ ] Step 11: Replace the mock setup return at lines 214–215:
```csharp
// old
        _manufactureSourceMock.Setup(x => x.GetManufactureHistoryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureHistoryRecord>());
// new
        _manufactureSourceMock.Setup(x => x.GetManufactureHistoryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogManufactureRecord>());
```

Note: `ManufactureDifficultySetting` at line 211 (`_manufactureDifficultyRepositoryMock.Setup(...)`) does not itself reference `ManufactureHistoryRecord` — it is a different type. If `ManufactureDifficultySetting` needs the `Manufacture` namespace, add `using Anela.Heblo.Domain.Features.Manufacture;` back alongside the new `ManufactureHistory` using. Check after build.

#### Final verification

- [ ] Step 12: Build and run all tests:
```
dotnet build backend/ -v quiet
dotnet test backend/test/Anela.Heblo.Tests/ -v normal --filter "FullyQualifiedName~ModuleBoundariesTests|FullyQualifiedName~GetCatalogDetailHandlerFullHistoryTests|FullyQualifiedName~FlatManufactureCostProviderTests|FullyQualifiedName~ManufactureBasedMaterialCostProviderTests|FullyQualifiedName~CatalogRepositoryTests|FullyQualifiedName~CatalogRepositoryCacheOptimizationTests"
```

- [ ] Step 13: Run the full test suite to catch any transitive regressions:
```
dotnet test backend/test/Anela.Heblo.Tests/ -v quiet
```

- [ ] Step 14: Run dotnet format to ensure code style compliance:
```
dotnet format backend/ --verify-no-changes
```
If format reports changes, apply them: `dotnet format backend/`

- [ ] Step 15: Commit all test and allowlist changes:
```
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git add backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/FlatManufactureCostProviderTests.cs
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs
git add backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs
git commit -m "test: update Catalog tests and remove ManufactureHistoryRecord allowlist entries after CatalogManufactureRecord decoupling"
```
