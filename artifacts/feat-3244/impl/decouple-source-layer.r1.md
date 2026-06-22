# Implementation: decouple-source-layer

## What was implemented

All `ManufactureHistoryRecord` references in the Catalog application layer and domain
aggregate have been replaced with `CatalogManufactureRecord`. The Manufacture module
adapter now maps at the boundary: it calls `IManufactureHistoryClient.GetHistoryAsync`
(returning `ManufactureHistoryRecord`) and projects the result into `CatalogManufactureRecord`
before returning it through `ICatalogManufactureSource`.

Two additional files not listed in the task spec required changes because they consumed
`CatalogAggregate.ManufactureHistory` (now typed as `CatalogManufactureRecord`) through
`IProductionActivityAnalyzer`:
- `IProductionActivityAnalyzer.cs` — interface signatures updated
- `ProductionActivityAnalyzer.cs` — implementation updated

Seven test files were updated to match the new types.

## Files created/modified

**Source (8 planned + 2 discovered):**
- `backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs` — using + field + property types
- `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogManufactureSource.cs` — full rewrite, removed old comment
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapter.cs` — added using + mapping projection
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogCacheStore.cs` — using + GetManufactureHistoryData/SetManufactureHistoryData types
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs` — using + MergeHistoryData parameter type
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs` — removed stale 2-line comment, kept `using Manufacture` (still needed for ManufactureDifficultySetting)
- `backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/FlatManufactureCostProvider.cs` — removed unused `using Manufacture`
- `backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProvider.cs` — no change needed (no Manufacture using present)
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IProductionActivityAnalyzer.cs` — using + all 3 method signatures
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ProductionActivityAnalyzer.cs` — using + all 3 method implementations

**Tests (7 files):**
- `ProductionActivityAnalyzerTests.cs` — using + all `ManufactureHistoryRecord` instantiations
- `CatalogRepositoryTests.cs` — using + mock setup return type
- `GetCatalogDetailHandlerFullHistoryTests.cs` — using + ManufactureHistory assignment
- `CatalogRepositoryCacheOptimizationTests.cs` — using + mock setup return type
- `ManufactureBasedMaterialCostProviderTests.cs` — using + record instantiations
- `FlatManufactureCostProviderTests.cs` — using + record instantiations
- `GetManufacturingStockAnalysisHandlerTests.cs` — using + mock parameter types

## Tests

All existing unit tests compile and the solution builds with 0 errors. Tests were updated
in-place (type swap only, no logic changes). The `ManufactureDifficultySetting` type used
in CatalogCacheStore and CatalogMergeService is in `Anela.Heblo.Domain.Features.Catalog`
(not Manufacture), so no extra using was needed for it in those files.

## How to verify

```
dotnet build Anela.Heblo.sln -v quiet
# Expected: 0 Error(s)
```

## Notes

- `ManufactureBasedMaterialCostProvider.cs` had no `using Anela.Heblo.Domain.Features.Manufacture;`
  directive — confirmed no change required there.
- `CatalogDataRefreshService.cs` still retains `using Anela.Heblo.Domain.Features.Manufacture;`
  because `ManufactureDifficultySetting` is used in `RefreshManufactureDifficultySettingsData`.
  However, `ManufactureDifficultySetting` is actually in the `Anela.Heblo.Domain.Features.Catalog`
  namespace (confirmed by grep). The using is redundant but harmless — removing it is out of
  scope for this task.
- `IProductionActivityAnalyzer` and `ProductionActivityAnalyzer` were not in the task spec but
  had a hard compile dependency on `IEnumerable<ManufactureHistoryRecord>` from the aggregate's
  `ManufactureHistory` property. They were updated as a necessary follow-on.

## PR Summary

### Changes

- Catalog domain aggregate (`CatalogAggregate`) now uses `CatalogManufactureRecord` for
  `ManufactureHistory`, eliminating the cross-module type leak from Domain.Features.Manufacture.
- `ICatalogManufactureSource` contract updated to return `IReadOnlyList<CatalogManufactureRecord>`.
- `ManufactureCatalogSourceAdapter` maps `ManufactureHistoryRecord` → `CatalogManufactureRecord`
  at the Manufacture/Catalog module boundary.
- Cache store, merge service, data refresh service, and cost providers all updated to use
  the Catalog-owned type throughout.
- `IProductionActivityAnalyzer` similarly updated to accept the Catalog type.
- All affected unit tests updated (type swap only, no logic changes).

## Status

DONE
