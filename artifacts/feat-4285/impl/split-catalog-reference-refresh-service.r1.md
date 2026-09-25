# Implementation: split-catalog-reference-refresh-service

## What was implemented

Extracted the internal reference-data refresh operations out of `CatalogDataRefreshService`
into a new, narrower `CatalogReferenceRefreshService` class, per the task context: stock-taking
data refresh, manufacture-difficulty-settings refresh (both the all-products and single-product
copy-then-set paths), and the manufacture-cost cross-reference pass over the cached catalog
aggregate. The new service uses the exact same method bodies and cache-store interaction as the
originals. `CatalogDataRefreshService.cs` itself was intentionally left untouched at this step
(its removal is a separate task, `remove-old-refresh-service-and-verify`, per the task-context
set for this feature), so the classes currently coexist with duplicate method bodies — same
pattern as the prior `split-catalog-history-refresh-service`, `split-catalog-stock-refresh-service`,
and `split-catalog-meta-refresh-service` tasks.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogReferenceRefreshService.cs`
  — new sealed class with a 5-parameter constructor (`IStockTakingRepository`,
  `IManufactureDifficultyRepository`, `TimeProvider`, `CatalogCacheStore`,
  `ILogger<CatalogReferenceRefreshService>`) and three public methods:
  `RefreshStockTakingData`, `RefreshManufactureDifficultySettingsData`,
  `RefreshManufactureCostData`. Copied verbatim from the task context's Step 1 code block,
  matching the corresponding methods in `CatalogDataRefreshService.cs` byte-for-byte.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogReferenceRefreshServiceTests.cs`
  — new test class moving the three relevant test cases verbatim from
  `CatalogDataRefreshServiceTests.cs`:
  `RefreshManufactureDifficultySettingsData_SingleProduct_DoesNotMutateSharedDictionaryOrAggregate`,
  `RefreshManufactureDifficultySettingsData_SingleProduct_NoCurrentSnapshot_UpdatesDictionaryWithoutThrowing`,
  `RefreshManufactureCostData_DoesNotMutateLiveCatalogAggregates`, plus a `CreateService` helper
  scoped to this class's 5 constructor parameters.

## Tests

- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogReferenceRefreshServiceTests.cs`
  covers: single-product manufacture-difficulty-settings refresh not mutating the shared
  dictionary or live aggregate reference (isolation contract), the same refresh completing
  without throwing when no current cache snapshot exists yet, and manufacture-cost data refresh
  not mutating already-referenced live catalog aggregates.

## How to verify

```bash
cd backend
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CatalogReferenceRefreshServiceTests"
```

Ran both: build succeeded (0 errors, only pre-existing unrelated warnings), and
`Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3`.

## Notes

- `CatalogDataRefreshService.cs` was not modified or removed — that is explicitly a later task
  (`remove-old-refresh-service-and-verify`) in this feature's task-context set, and
  `CatalogRepository`/`CatalogModule` wiring is likewise untouched here (also a later task,
  `wire-catalog-repository-and-module`). This step only adds the new standalone service.
- No deviation from the task-context's given code was needed — the given file compiled and its
  three moved tests passed as written.

## PR Summary

Extracted stock-taking refresh, manufacture-difficulty-settings refresh, and the
manufacture-cost cross-reference pass from `CatalogDataRefreshService` into a new standalone
`CatalogReferenceRefreshService`, along with its own test file covering the three relevant test
cases moved verbatim from the original test suite.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogReferenceRefreshService.cs` — new service
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogReferenceRefreshServiceTests.cs` — new test file

## Status
DONE
