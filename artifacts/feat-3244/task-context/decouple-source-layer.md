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

