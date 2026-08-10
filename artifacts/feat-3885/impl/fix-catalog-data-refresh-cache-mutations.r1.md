# Implementation: fix-catalog-data-refresh-cache-mutations

## What was implemented
Fixed both cache-isolation violations described in the spec:

1. `RefreshManufactureDifficultySettingsData(string? product, ct)` — single-product branch now copies the existing dictionary into a new `Dictionary<string, List<ManufactureDifficultySetting>>` with the target key replaced, and calls `SetManufactureDifficultySettingsData(newDict)` instead of mutating the dictionary returned by `GetManufactureDifficultySettingsData()` in place. It then updates the live snapshot, if one exists and contains the target product, by building a new `List<CatalogAggregate>` where only the matching product is a `Clone()` with `ManufactureDifficultySettings.Assign(...)` called on the clone; all other products pass through unchanged by reference. The new list is installed via `ReplaceCacheAtomicallyAsync`.
2. `RefreshManufactureCostData(ct)` — no longer loops over `_cacheStore.GetCatalogData()` and mutates `product.ManufactureHistory` in place. It now builds a new `List<CatalogAggregate>` (clone-with-new-history for products present in `manufactureMap`, pass-through for the rest) and installs it via `ReplaceCacheAtomicallyAsync`. Removed the now-inaccurate `// Pre-existing behavior: mutates live aggregate in-place...` comment.

Both methods now guard the "no current snapshot" case (`TryGetCurrent() == null`) by skipping the snapshot-update step without throwing.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs` — the two method bodies described above (lines ~197-259 after the change).
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogDataRefreshServiceTests.cs` — replaced `RefreshManufactureDifficultySettingsData_SingleProduct_UpdatesLiveAggregate` (named after the bug being fixed) with `RefreshManufactureDifficultySettingsData_SingleProduct_DoesNotMutateSharedDictionaryOrAggregate` and added `RefreshManufactureDifficultySettingsData_SingleProduct_NoCurrentSnapshot_UpdatesDictionaryWithoutThrowing` and `RefreshManufactureCostData_DoesNotMutateLiveCatalogAggregates`. Added a `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;` for `CatalogManufactureRecord`.

## Tests
- `RefreshManufactureDifficultySettingsData_SingleProduct_DoesNotMutateSharedDictionaryOrAggregate` — asserts that dictionary and aggregate references captured *before* the call are untouched after it, and that fresh reads reflect the update (including `GetLoadDateFromCache` proving `Set*Data` plumbing ran).
- `RefreshManufactureDifficultySettingsData_SingleProduct_NoCurrentSnapshot_UpdatesDictionaryWithoutThrowing` — regression guard for the `TryGetCurrent() == null` branch.
- `RefreshManufactureCostData_DoesNotMutateLiveCatalogAggregates` — asserts the pre-call product reference is untouched, a fresh snapshot has the new `ManufactureHistory`, and an untouched product passes through unchanged.
- All pre-existing tests in `CatalogDataRefreshServiceTests`, `CatalogCacheStoreTests`, and `CatalogMergeServiceTests` pass unmodified.

## How to verify
```bash
cd backend
dotnet build Anela.Heblo.sln          # or: dotnet build backend/Anela.Heblo.sln from repo root — 0 errors
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Catalog.Infrastructure" --no-build
# → 126/126 passed
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Catalog" --no-build
# → 813/817 passed; the 4 failures are GetStockUpOperationsSummaryIntegrationTests,
#   pre-existing Testcontainers/Docker-unavailable failures unrelated to this change.
```

## Notes
- `RefreshManufactureCostData` remains unregistered in `CatalogModule` (dead code) — wiring it up is explicitly out of scope per `spec.r1.md`.
- No public interface signatures changed (`ICatalogRepository`, `CatalogCacheStore`).
- `ct` parameter on `RefreshManufactureCostData` remains unused, matching the pre-existing pattern in this class (not introduced by this change).
- The Docker-dependent integration test failures observed locally (`GetStockUpOperationsSummaryIntegrationTests`) are an environment limitation (no Docker daemon in this sandbox), not a regression from this change — confirmed unrelated by file/namespace (`Features/Catalog/GetStockUpOperationsSummaryIntegrationTests.cs`, uses `PostgresSharedContainerFixture`/Testcontainers, no relationship to `CatalogDataRefreshService`/`CatalogCacheStore`).

## PR Summary
Fixed two cache-isolation violations in `CatalogDataRefreshService` that mutated live, shared `CatalogAggregate`/dictionary objects in place instead of following the clone-before-mutate discipline `CatalogMergeService.Merge()` already uses (fixed under #3827 for the identical class of bug). `RefreshManufactureDifficultySettingsData`'s single-product branch — reachable in production from the create/update/delete manufacture-difficulty handlers — now copies the settings dictionary and swaps in a cloned aggregate via the existing `Set*Data`/`ReplaceCacheAtomicallyAsync` plumbing instead of writing through live references, so concurrent readers can no longer observe a partially-updated snapshot and `InvalidateSourceData`/`SetLoadDateInCache` run as they do for every other refresh path. `RefreshManufactureCostData` (currently dead code, not wired into any refresh task) received the same fix so that reviving it later doesn't silently reproduce the bug.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs` — clone-before-mutate for both methods
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogDataRefreshServiceTests.cs` — isolation-focused regression tests for both methods

## Status
DONE
