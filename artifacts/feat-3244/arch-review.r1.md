# Architecture Review: Decouple CatalogAggregate from ManufactureHistoryRecord

## Skip Design: true

## Architectural Fit Assessment

This feature is a pure module-boundary repair with no functional change. The spec is accurate: `CatalogAggregate` holds `IReadOnlyList<ManufactureHistoryRecord>` where `ManufactureHistoryRecord` belongs to `Anela.Heblo.Domain.Features.Manufacture`. This fuses the two domain models at compile time inside the shared `Domain` assembly.

The pattern to follow is already established in the codebase. `CatalogPurchaseRecord` (`backend/src/Anela.Heblo.Domain/Features/Catalog/PurchaseHistory/CatalogPurchaseRecord.cs`) is the direct structural analog: a Catalog-owned value object with the same shape as its source type, living in a sub-namespace of `Catalog`. The proposed `CatalogManufactureRecord` must match this precedent exactly — same folder depth, same class-not-record constraint, same property style (no `JsonPropertyName` attributes, no derived computed properties; those belong on the DTO).

The `ManufactureCatalogSourceAdapter` is the correct and only permitted translation boundary. It already implements `ICatalogManufactureSource` from the Manufacture module's Application layer, making it the one place in the codebase that may simultaneously reference both `ManufactureHistoryRecord` and `CatalogManufactureRecord`.

The `ModuleBoundariesTests` architecture test — verified by reading the full test file — currently maintains an explicit allowlist (`CatalogManufactureAllowlist`) of eight `ManufactureHistoryRecord` entries to suppress violations. Removal of those eight entries is the definitive automated proof of completion.

## Proposed Architecture

### Component Overview

```
Domain layer
─────────────────────────────────────────────────────────────────────
Manufacture.Domain                     Catalog.Domain
  ManufactureHistoryRecord               CatalogAggregate
  (unchanged)                              ManufactureHistory: IReadOnlyList<CatalogManufactureRecord>
                                         ManufactureHistory/
                                           CatalogManufactureRecord  ← NEW

Application layer
─────────────────────────────────────────────────────────────────────
Manufacture.Application.Infrastructure   Catalog.Application.Contracts
  ManufactureCatalogSourceAdapter          ICatalogManufactureSource
    implements ICatalogManufactureSource     GetManufactureHistoryAsync()
    fetches: ManufactureHistoryRecord          → Task<IReadOnlyList<CatalogManufactureRecord>>
    maps → CatalogManufactureRecord          (updated)
    returns: IReadOnlyList<CatalogManufactureRecord>

Catalog.Application.Infrastructure
  CatalogCacheStore
    GetManufactureHistoryData() → IList<CatalogManufactureRecord>
    SetManufactureHistoryData(IList<CatalogManufactureRecord>)
  CatalogMergeService
    MergeHistoryData(..., IDictionary<string, List<CatalogManufactureRecord>> manufactureMap, ...)
  CatalogDataRefreshService
    RefreshManufactureHistoryData — type flows naturally; no explicit ManufactureHistoryRecord reference remains

Catalog.Application.CostProviders
  FlatManufactureCostProvider
  ManufactureBasedMaterialCostProvider
    both access product.ManufactureHistory (now IReadOnlyList<CatalogManufactureRecord>)
    — no explicit ManufactureHistoryRecord usage in either; violations come from the
      ManufactureHistoryRecord type appearing as a generic argument in the compiled type graph.
      Removing the import resolves this after FR-2 is complete.

Tests
─────────────────────────────────────────────────────────────────────
Architecture/ModuleBoundariesTests.cs
  CatalogManufactureAllowlist: remove 8 ManufactureHistoryRecord entries
Features/Catalog/* test files: replace ManufactureHistoryRecord construction with CatalogManufactureRecord
```

### Key Design Decisions

#### Decision 1: Class, not record
**Options considered:** C# `record` (concise) vs `class` (verbose but consistent).
**Chosen approach:** `class`, matching `CatalogPurchaseRecord` and the project-wide DTOs-are-classes rule.
**Rationale:** The OpenAPI generator and serialisation pipeline cannot handle record parameter-order semantics. Domain value objects that flow into the OpenAPI pipeline must be classes. `CatalogManufactureRecord` will eventually be serialised via the existing `CatalogManufactureRecordDto` mapping path, so the same constraint applies.

#### Decision 2: Sub-namespace location
**Options considered:** Place `CatalogManufactureRecord` directly in `Anela.Heblo.Domain.Features.Catalog` vs in a sub-namespace `Catalog.ManufactureHistory`.
**Chosen approach:** Sub-namespace `Anela.Heblo.Domain.Features.Catalog.ManufactureHistory`, matching the existing `Catalog.PurchaseHistory` precedent.
**Rationale:** All other Catalog history value objects live in their own sub-namespaces (`PurchaseHistory`, `Sales`, `ConsumedMaterials`). Consistency with the existing directory convention requires the new type to follow the same pattern.

#### Decision 3: Mapping location
**Options considered:** Map in `CatalogDataRefreshService`, map in `CatalogCacheStore`, map in `ManufactureCatalogSourceAdapter`.
**Chosen approach:** Map exclusively in `ManufactureCatalogSourceAdapter.GetManufactureHistoryAsync`.
**Rationale:** `ManufactureCatalogSourceAdapter` is the single file in the codebase that is permitted to reference both `ManufactureHistoryRecord` (as a Manufacture-domain type) and `CatalogManufactureRecord` (as the Catalog-owned type). Mapping here means every consumer of `ICatalogManufactureSource` receives the Catalog-owned type. No mapping helper or extension method is needed — the six-field projection is trivial inline.

#### Decision 4: No changes to GetCatalogDetailHandler
**Options considered:** Touch handler because it appears in the allowlist.
**Chosen approach:** Do not modify `GetCatalogDetailHandler`.
**Rationale:** Reading the handler source confirms it imports no Manufacture namespace and uses `ManufactureHistory` only through the aggregate property and `CatalogManufactureRecordDto` (already Catalog-owned). The allowlist entry `GetCatalogDetailHandler -> ManufactureHistoryRecord` is caused by the type appearing in the compiled type graph via the aggregate's property type. Once FR-2 changes `CatalogAggregate.ManufactureHistory` to `IReadOnlyList<CatalogManufactureRecord>`, the handler's allowlist entry disappears without any direct change to the handler.

#### Decision 5: No change to IManufactureHistoryClient or FlexiManufactureHistoryClient
**Options considered:** Update these to return `CatalogManufactureRecord` directly.
**Chosen approach:** Leave both untouched.
**Rationale:** `IManufactureHistoryClient` is Manufacture-owned; changing it to return a Catalog type would create the inverse violation. The adapter is the boundary.

## Implementation Guidance

### Directory / Module Structure

One new file to create:

```
backend/src/Anela.Heblo.Domain/Features/Catalog/ManufactureHistory/
  CatalogManufactureRecord.cs           ← FR-1
```

Files to update (in dependency order):

```
1. backend/src/Anela.Heblo.Domain/Features/Catalog/ManufactureHistory/CatalogManufactureRecord.cs   (create)
2. backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs                              (FR-2)
3. backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogManufactureSource.cs      (FR-3)
4. backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapter.cs  (FR-4)
5. backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogCacheStore.cs         (FR-5)
6. backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs       (FR-6)
7. backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs (FR-7)
8. backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/FlatManufactureCostProvider.cs (FR-8)
9. backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProvider.cs (FR-8)
10. backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs                             (FR-9)
11. backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs       (FR-10)
12. backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/FlatManufactureCostProviderTests.cs (FR-10)
13. backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs (FR-10)
14. backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs                         (FR-10)
15. backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs      (FR-10)
```

### Interfaces and Contracts

**`CatalogManufactureRecord`** — new Catalog Domain class:
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
No `JsonPropertyName` attributes (those live on `CatalogManufactureRecordDto`). No computed properties. Class, not record.

**`ICatalogManufactureSource`** — updated return type:
```csharp
Task<IReadOnlyList<CatalogManufactureRecord>> GetManufactureHistoryAsync(
    DateTime dateFrom,
    DateTime dateTo,
    CancellationToken cancellationToken);
```

**`ManufactureCatalogSourceAdapter`** — mapping inline:
```csharp
public async Task<IReadOnlyList<CatalogManufactureRecord>> GetManufactureHistoryAsync(
    DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken)
{
    var records = await _historyClient.GetHistoryAsync(dateFrom, dateTo, productCode: null, cancellationToken);
    return records.Select(r => new CatalogManufactureRecord
    {
        Date = r.Date,
        Amount = r.Amount,
        PricePerPiece = r.PricePerPiece,
        PriceTotal = r.PriceTotal,
        ProductCode = r.ProductCode,
        DocumentNumber = r.DocumentNumber
    }).ToList();
}
```

### Data Flow

**Before (violated):**
```
FlexiManufactureHistoryClient
  → IManufactureHistoryClient.GetHistoryAsync() : List<ManufactureHistoryRecord>
  → ManufactureCatalogSourceAdapter.GetManufactureHistoryAsync() : IReadOnlyList<ManufactureHistoryRecord>
  → ICatalogManufactureSource (Manufacture type leaks into Catalog namespace)
  → CatalogDataRefreshService.RefreshManufactureHistoryData()
  → CatalogCacheStore.SetManufactureHistoryData(IList<ManufactureHistoryRecord>)
  → CatalogMergeService.MergeHistoryData(..., IDictionary<string, List<ManufactureHistoryRecord>> ...)
  → CatalogAggregate.ManufactureHistory : IReadOnlyList<ManufactureHistoryRecord>
  → FlatManufactureCostProvider, ManufactureBasedMaterialCostProvider, GetCatalogDetailHandler
```

**After (correct):**
```
FlexiManufactureHistoryClient
  → IManufactureHistoryClient.GetHistoryAsync() : List<ManufactureHistoryRecord>  [Manufacture-owned, unchanged]
  → ManufactureCatalogSourceAdapter.GetManufactureHistoryAsync()
      maps ManufactureHistoryRecord → CatalogManufactureRecord  [boundary]
      returns IReadOnlyList<CatalogManufactureRecord>
  → ICatalogManufactureSource (Catalog-owned type, no leak)
  → CatalogDataRefreshService.RefreshManufactureHistoryData()
  → CatalogCacheStore.SetManufactureHistoryData(IList<CatalogManufactureRecord>)
  → CatalogMergeService.MergeHistoryData(..., IDictionary<string, List<CatalogManufactureRecord>> ...)
  → CatalogAggregate.ManufactureHistory : IReadOnlyList<CatalogManufactureRecord>
  → FlatManufactureCostProvider, ManufactureBasedMaterialCostProvider, GetCatalogDetailHandler
      (access same properties: Date, Amount, PricePerPiece, PriceTotal, ProductCode, DocumentNumber)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `CatalogRepository` allowlist entry (`CatalogRepository -> ManufactureHistoryRecord`) may not disappear automatically | Low | `CatalogRepository` has no direct import of `ManufactureHistoryRecord` (verified by source read). The entry is caused by the type appearing in the compiled property graph transitively via `CatalogCacheStore`. Once FR-5 updates `CatalogCacheStore`, the transitive reference disappears. Run `ModuleBoundariesTests` after each step to confirm. |
| In-memory cache contains serialised `ManufactureHistoryRecord` objects at time of deployment | Low | Both types have identical field names and types. The cache is in-process `IMemoryCache` holding live CLR objects — there is no serialisation boundary. After the code change, the cache key `CachedManufactureHistoryData` will simply hold `List<CatalogManufactureRecord>` on the next refresh. The first cache miss post-deploy triggers a refresh through `CatalogMergeService`, which is the normal path. No data loss. |
| `FlatManufactureCostProvider` imports `using Anela.Heblo.Domain.Features.Manufacture;` explicitly (line 8, verified) and will generate a compiler warning/error once the reference is no longer needed | Low | Remove the `using` directive from the cost provider as part of FR-8. The file uses no other type from that namespace. |
| Test files that construct `ManufactureHistoryRecord` for mock setup in Catalog tests (five files confirmed) will fail to compile | Medium | These are mechanical substitutions (type name only). All six properties are identical. Do not change Manufacture-module test files — `GetManufactureOutputHandlerTests.cs`, `ProductionActivityAnalyzerTests.cs`, `GetManufacturingStockAnalysisHandlerTests.cs` legitimately construct `ManufactureHistoryRecord`. |
| The eight allowlist entries are removed before all source changes compile | High | Remove allowlist entries last (after all source and test changes pass `dotnet build`). The architecture test only runs compiled types — removing entries before the build is fixed will cause the test to fail on the remaining violations, which is the intended guard. Sequence: fix source → fix tests → remove allowlist → verify `ModuleBoundariesTests` passes. |

## Specification Amendments

No amendments required. The spec is accurate and complete. One clarification worth noting for the implementer:

The `GetCatalogDetailHandler` allowlist entry (`GetCatalogDetailHandler -> ManufactureHistoryRecord`) is resolved by FR-2 alone, with no change to the handler file itself. The handler does not import the Manufacture namespace (verified); the allowlist entry is generated by the reflection-based test detecting `ManufactureHistoryRecord` as a generic type argument in the compiled type graph of the handler's methods that interact with `CatalogAggregate.ManufactureHistory`. Once FR-2 changes the aggregate property type, this entry no longer fires.

## Prerequisites

None. All files exist; no migrations, configuration, infrastructure, or new dependencies are required. The change is entirely in-process type substitution with a field-for-field mapping at one adapter boundary.

Implementation can begin immediately on the feature branch without any pre-work.
