All fixes committed. Here's the final state:

---

# Implementation: Decompose CatalogRepository

## What was implemented
Full decomposition of the 962-line `CatalogRepository` (25 constructor parameters) into three focused types plus a delegating facade, with an `IHostedService` to wire the merge callback at startup.

## Files created/modified
- `Infrastructure/CatalogCacheStore.cs` — singleton, sole owner of `IMemoryCache` access for catalog data; 19 typed per-source get/set pairs, dual-key (current/stale) aggregate cache, semaphore-guarded atomic replace (398 LOC)
- `Infrastructure/CatalogMergeService.cs` — singleton, owns `Merge()` mapping, `ExecuteBackgroundMergeAsync`, `ExecutePriorityMergeAsync` (289 LOC)
- `Infrastructure/CatalogDataRefreshService.cs` — transient, owns all 19 `Refresh*Data` methods and cross-module stock helpers (288 LOC)
- `Infrastructure/CatalogMergeCallbackWiring.cs` — `IHostedService`, wires `SetMergeCallback` exactly once at startup
- `CatalogRepository.cs` — slimmed to a delegating facade (170 LOC, was 962)
- `ICatalogRepository.cs` — `ManufactureCostLoadDate` removed (FR-5)
- `MockCatalogRepository.cs` — updated to match trimmed interface
- `CatalogModule.cs` — registers the 3 new types + hosted service with correct lifetimes
- Test files: `CatalogCacheStoreTests`, `CatalogMergeServiceTests`, `CatalogDataRefreshServiceTests`, `CatalogMergeCallbackWiringTests` — new test suites; existing test files updated for new constructors

## Tests
- 449/453 passing; 4 failures are `GetStockUpOperationsSummaryIntegrationTests` requiring Docker (pre-existing environment issue)
- All catalog unit tests pass including all new infrastructure test suites

## How to verify
```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Catalog"
```

## Notes
- `IMemoryCache` references in `Cache/*.cs` (DirectManufactureCostCache etc.) are cost-provider caches unrelated to this refactor — the catalog data path is clean
- All cache key strings preserved verbatim to maintain warm-cache continuity on deploy
- `Task.Run` wrapper in `ExecuteBackgroundMergeAsync` preserved per spec

## PR Summary
Decomposes the 962-line `CatalogRepository` (25 constructor params) into three focused collaborators — `CatalogCacheStore` (cache layer), `CatalogMergeService` (merge orchestration), `CatalogDataRefreshService` (19 refresh methods) — plus `CatalogMergeCallbackWiring` (`IHostedService` that fixes the latent bug where a transient constructor was overwriting a singleton scheduler's callback on every DI resolution). The slimmed `CatalogRepository` is now a 170-line delegating facade. `ICatalogRepository`'s 89 consumers compile without modification.

### Changes
- `CatalogRepository.cs` — reduced from 962 to 170 LOC
- `Infrastructure/CatalogCacheStore.cs` — new singleton, sole IMemoryCache owner
- `Infrastructure/CatalogMergeService.cs` — new singleton, merge logic
- `Infrastructure/CatalogDataRefreshService.cs` — new transient, 19 refresh methods
- `Infrastructure/CatalogMergeCallbackWiring.cs` — new IHostedService, startup callback wiring
- `ICatalogRepository.cs` — `ManufactureCostLoadDate` removed (obsolete, no callers)
- `CatalogModule.cs` — DI registrations updated
- Tests — new suites for each new class; existing tests updated for new constructors

## Status
DONE