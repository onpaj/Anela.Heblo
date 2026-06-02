# Specification: Decompose CatalogRepository

## Summary
Split `Application/Features/Catalog/CatalogRepository.cs` (962 lines, 25 constructor dependencies, five distinct responsibilities) into three focused types — `CatalogCacheStore`, `CatalogMergeService`, `CatalogDataRefreshService` — plus a thinned `CatalogRepository` that exposes only the read-side query API. Remove the unused `[Obsolete]` `CachedManufactureCostData` property. Public behavior (`ICatalogRepository`, background refresh registration, dashboard tiles, merge scheduling semantics, stale-fresh cache promotion) must remain identical.

## Background
`CatalogRepository` is the central read-side cache for the Catalog module. It currently combines:

1. **Cache management** — `CurrentCatalogCacheKey` / `StaleCatalogCacheKey` dual-key cache, `_cacheReplacementSemaphore`, `ReplaceCacheAtomicallyAsync`, `IsCacheValid`, `CatalogData` getter (lines 270–335, 523–571).
2. **Merge orchestration** — `Merge()` (lines 337–487), `ExecuteBackgroundMergeAsync`, `ExecutePriorityMergeAsync`, callback wiring with `ICatalogMergeScheduler` (line 118).
3. **Per-source data refresh** — 19 `Refresh*Data` methods declared on `ICatalogRepository` and registered as background refresh tasks in `CatalogModule.RegisterBackgroundRefreshTasks` (lines 121–268 in the repo, lines 128–216 in CatalogModule).
4. **Cross-module data fetching** — `GetProductsInTransport/Reserve/Quarantine/Ordered/Planned` (lines 892–924).
5. **Query interface** — `GetByIdAsync`, `GetByIdsAsync`, `GetAllAsync`, `FindAsync`, `SingleOrDefaultAsync`, `AnyAsync`, `CountAsync`, `GetProductsWithSalesInPeriod` (lines 926–958).

Plus dead code: `CachedManufactureCostData` (lines 768–780), marked `[Obsolete]`, never written to, never read inside the codebase outside its own definition (only its `ManufactureCostLoadDate` is exposed on the interface — and is itself unused by any caller; verify before removal).

Consequences:
- 25 constructor parameters; `CatalogRepositoryTests` mocks every one of them just to exercise a single method.
- File exceeds the project's 800-line guideline by 20%.
- Cache invalidation and the `Merge()` mapping logic are intertwined through 19 cached property setters that each call `InvalidateSourceData` — changing one strategy forces reading both halves.
- Adding a new data source means editing the largest class in the module rather than a focused adapter.

The brief notes this work is best done after or alongside #2058 (cross-module adapter extraction), but does not block on it: refactor scope here is a pure internal restructure of the existing class behind the same `ICatalogRepository` contract.

## Functional Requirements

### FR-1: Extract `CatalogCacheStore`
Create a new type that owns all read/write access to `IMemoryCache` for catalog data, including the dual-key (current/stale) aggregate cache and the 19 per-source caches.

**Scope of the new class:**
- Constants `CurrentCatalogCacheKey`, `StaleCatalogCacheKey`, `CacheUpdateTimeKey`, `CachedManufactureCostDataKey` (only retained if FR-5 finds a remaining reader).
- `_cacheReplacementSemaphore` and `ReplaceCacheAtomicallyAsync(List<CatalogAggregate>)`.
- `IsCacheValid()`.
- The `CatalogData` getter (current → stale fallback → empty-with-schedule).
- Each of the 19 typed per-source cached properties (`CachedSalesData`, `CachedCatalogAttributesData`, `CachedInTransportData`, `CachedManufacturedData`, `CachedInReserveData`, `CachedInQuarantineData`, `CachedOrderedData`, `CachedPlannedData`, `CachedErpStockData`, `CachedEshopStockData`, `CachedPurchaseHistoryData`, `CachedManufactureHistoryData`, `CachedConsumedData`, `CachedStockTakingData`, `CachedLotsData`, `CachedEshopPriceData`, `CachedErpPriceData`, `CachedEshopUrlData`, `CachedManufactureDifficultySettingsData`) — exposed as typed get/set methods or properties on the store.
- `InvalidateSourceData(string)` (and the `_sourceLastUpdated` dictionary if still referenced after refactor; if no consumer reads it, remove it).
- Per-source load-date tracking: `GetLoadDateFromCache(dataKey)` and `SetLoadDateInCache(dataKey)`.
- `SetLastMergeDateTime()` and exposure of `LastMergeDateTime`.

**Collaborators (constructor params):** `IMemoryCache`, `TimeProvider`, `IOptions<CatalogCacheOptions>`, `ICatalogMergeScheduler`, `ILogger<CatalogCacheStore>`.

**Acceptance criteria:**
- All `IMemoryCache.Get`/`Set`/`Remove` calls related to catalog data live inside this class. `CatalogRepository`, `CatalogMergeService`, and `CatalogDataRefreshService` must not reference `IMemoryCache` directly.
- The class exposes a typed write API per source (e.g. `SetSalesData(IList<CatalogSaleRecord>)`, `GetSalesData()`, `GetSalesLoadDate()`) — exact method shape is the implementer's choice but each setter must continue to invoke `InvalidateSourceData(sourceName)` and `SetLoadDateInCache(sourceName)` to preserve scheduler triggering and load-date tracking semantics.
- `ReplaceCacheAtomicallyAsync` continues to demote current → stale (with `StaleDataRetentionPeriod`) before installing the new payload, under the existing semaphore guard.
- Stale-fallback behavior in `CatalogData` getter is preserved bit-for-bit: returns current when present, else stale (scheduling a merge), else empty (scheduling a merge and logging the warning).

### FR-2: Extract `CatalogMergeService`
Create a new type that owns the cross-source merge step that produces a `List<CatalogAggregate>` from the per-source caches.

**Scope of the new class:**
- The `Merge()` method (current lines 337–487), including the seed-from-ERP behavior when the current catalog is empty.
- `GetProductType(ErpStock)` helper.
- `ExecuteBackgroundMergeAsync(CancellationToken)` and `ExecutePriorityMergeAsync()`.
- Registers itself as the merge callback via `ICatalogMergeScheduler.SetMergeCallback`. The callback registration must happen during DI startup (e.g. in `CatalogModule` or via a hosted/initializer service) — preserving today's eager wiring done in the repository's constructor.

**Collaborators (constructor params):** `CatalogCacheStore`, `ICatalogMergeScheduler`, `TimeProvider`, `ILogger<CatalogMergeService>`.

**Acceptance criteria:**
- `Merge()` reads exclusively through `CatalogCacheStore` accessors — no direct `IMemoryCache` use.
- Field assignment from each source (ERP stock, attributes, eshop stock, sales/consumed/purchase/manufacture/stock-taking/lots history, eshop/ERP prices, eshop URL, in-transport/reserve/quarantine/ordered/planned stocks, manufacture difficulty settings) is functionally identical to current behavior — same fields populated, same fallbacks, same `ManufactureDifficultySettings.Assign(...)` call with `_timeProvider.GetUtcNow().UtcDateTime`.
- After merge, `CatalogCacheStore.ReplaceCacheAtomicallyAsync` is invoked with the new list and `SetLastMergeDateTime()` is called (preserving the current order: timestamp is set inside `Merge()` before atomic replace).
- The merge-scheduler callback wiring (`SetMergeCallback(ExecuteBackgroundMergeAsync)`) is preserved and executed once at startup, not in `CatalogRepository`'s constructor.

### FR-3: Extract `CatalogDataRefreshService`
Create a new type that implements the 19 `Refresh*Data` methods.

**Scope of the new class:**
- All `Refresh*Data` methods (`RefreshTransportData`, `RefreshManufacturedData`, `RefreshReserveData`, `RefreshOrderedData`, `RefreshPlannedData`, `RefreshSalesData`, `RefreshAttributesData`, `RefreshErpStockData`, `RefreshEshopStockData`, `RefreshPurchaseHistoryData`, `RefreshConsumedHistoryData`, `RefreshStockTakingData`, `RefreshLotsData`, `RefreshEshopPricesData`, `RefreshErpPricesData`, `RefreshEshopUrlData`, `RefreshManufactureDifficultySettingsData(string?, ...)`, `RefreshManufactureHistoryData`, `RefreshManufactureCostData`).
- The cross-module helpers `GetProductsInTransport/InReserve/InQuarantine/Ordered/Planned` (current lines 892–924).
- Resilience wrapping via `ICatalogResilienceService` for the existing five sources that use it (`RefreshSalesData`, `RefreshAttributesData`, `RefreshErpStockData`, `RefreshEshopStockData`).
- The `try/catch` in `RefreshSalesData` that retains stale cache and logs the count must remain.
- The single-product branch of `RefreshManufactureDifficultySettingsData` that also calls `CatalogData.SingleOrDefault(...)?.ManufactureDifficultySettings.Assign(...)` must continue to work — the refresh service may read the current merged data via `CatalogCacheStore` (read-only `CatalogData` accessor).

**Collaborators (constructor params):** the 13 source-specific clients/repositories required to call each source (`ICatalogSalesClient`, `ICatalogAttributesClient`, `IEshopStockClient`, `IConsumedMaterialsClient`, `IPurchaseHistoryClient`, `IErpStockClient`, `ILotsClient`, `IProductPriceEshopClient`, `IProductPriceErpClient`, `IProductEshopUrlClient`, `ITransportBoxRepository`, `IStockTakingRepository`, `IPurchaseOrderRepository`, `IManufactureOrderRepository`, `IManufactureHistoryClient`, `IManufactureDifficultyRepository`, `IManufacturedProductInventoryRepository`), plus `ICatalogResilienceService`, `TimeProvider`, `IOptions<DataSourceOptions>`, `CatalogCacheStore`, `ILogger<CatalogDataRefreshService>`. (`IManufactureClient` — currently unused by any refresh method, only present as a `?? throw` null-check in the constructor — should be dropped unless a usage is found.)

**Acceptance criteria:**
- All 19 refresh methods produce the same outputs and use the same upstream clients, date ranges, and option keys (`SalesHistoryDays`, `PurchaseHistoryDays`, `ConsumedHistoryDays`, `ManufactureHistoryDays`).
- Each method writes its result via `CatalogCacheStore` setters, preserving `InvalidateSourceData` and load-date side effects (so `ICatalogMergeScheduler.ScheduleMerge(dataSource)` still fires).
- `CatalogRepository.ExecuteBackgroundMergeAsync` is no longer called directly from refresh methods — invalidation via the scheduler remains the only trigger.

### FR-4: Slim `CatalogRepository` to the read-side query API
After FR-1–FR-3 the existing `CatalogRepository` becomes a thin wrapper that exposes only the read-side query API and the timestamp/state properties from `ICatalogRepository`.

**Retained on `CatalogRepository`:**
- `GetByIdAsync`, `GetByIdsAsync`, `GetAllAsync`, `FindAsync`, `SingleOrDefaultAsync`, `AnyAsync`, `CountAsync`, `GetProductsWithSalesInPeriod`.
- `GetCatalogDataAsync()` (private helper used by `GetAllAsync`).
- All `*LoadDate` properties (19) and `LastMergeDateTime`, `ChangesPendingForMerge`, `WaitForCurrentMergeAsync` — implemented by delegating to `CatalogCacheStore` / `ICatalogMergeScheduler`.
- The 19 `Refresh*Data` methods declared on `ICatalogRepository` — implemented by delegating to `CatalogDataRefreshService`.

**Collaborators (constructor params):** `CatalogCacheStore`, `CatalogMergeService` (for priority-merge invocation when no cache), `CatalogDataRefreshService`, `ICatalogMergeScheduler`. Target ≤ 5 constructor parameters.

**Acceptance criteria:**
- `ICatalogRepository` public surface is **unchanged**. All consumers (89 known usages, including handlers, dashboard tiles, adapters, mock, tests) compile without modification.
- `CatalogRepository.cs` is under 250 lines after the refactor.
- The `ICatalogRepository.Refresh*` methods continue to be registered with the background refresh framework via `RegisterRefreshTask<ICatalogRepository>(nameof(ICatalogRepository.Refresh...))` in `CatalogModule.RegisterBackgroundRefreshTasks` without changes — the task IDs (e.g. `"ICatalogRepository.RefreshTransportData"`) must remain identical so existing `BackgroundRefresh:ICatalogRepository:*` configuration in `appsettings.json` and `RefreshTaskConfiguration.FromAppSettings` keep working.

### FR-5: Remove obsolete `CachedManufactureCostData`
The `[Obsolete]` property `CachedManufactureCostData` (lines 770–780) and its key `CachedManufactureCostDataKey` (line 768) must be removed. Its corresponding load-date property `ManufactureCostLoadDate` on `ICatalogRepository` (line 46 of the interface) must also be removed — unless an external caller is found by a final grep, in which case the property stays on the interface but the cache backing is removed (and the property returns null).

**Acceptance criteria:**
- A grep for `CachedManufactureCostData`, `CachedManufactureCostDataKey`, and `ManufactureCostLoadDate` across the solution shows no references after the refactor except the removals themselves.
- `MockCatalogRepository.cs` is updated to match the trimmed interface.
- Tests that referenced these symbols are updated or removed.

### FR-6: DI registration and lifetime
Update `CatalogModule.AddCatalogModule` so the three new types are registered:

- `CatalogCacheStore` — singleton (matches the existing `ICatalogMergeScheduler` singleton lifetime; backing `IMemoryCache` is already a singleton, and `_cacheReplacementSemaphore` must be process-wide).
- `CatalogMergeService` — singleton (holds no per-request state; needs to outlive scopes so the merge callback wired at startup remains valid).
- `CatalogDataRefreshService` — transient (matches today's transient `ICatalogRepository`, since refresh tasks resolve a fresh scope per invocation via `BackgroundRefreshExtensions.CreateWrappedMethod`).
- `ICatalogRepository → CatalogRepository` — remains transient.

The merge-callback wiring (`mergeScheduler.SetMergeCallback(mergeService.ExecuteBackgroundMergeAsync)`) must be performed exactly once at startup. Options:

1. In `CatalogModule.AddCatalogModule`, build the callback via a factory delegate when registering `CatalogMergeScheduler` or via an `IHostedService` initializer.
2. Resolve both singletons in a `Configure<>` post-build hook.

Pick option 1 with an `IHostedService` initializer that resolves both singletons and wires them — this avoids ordering issues and is easy to unit test.

**Acceptance criteria:**
- After startup, calling any `Refresh*Data` method followed by a `ScheduleMerge` call results in `CatalogMergeService.ExecuteBackgroundMergeAsync` being invoked exactly as today.
- Integration test or smoke test confirming: cache hydrate → merge → `GetAllAsync` returns a non-empty list of merged aggregates.

### FR-7: Tests
- Update `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs` to mock only the dependencies the slim `CatalogRepository` actually uses. Existing test methods that exercise refresh behavior move to new test classes (`CatalogDataRefreshServiceTests`, `CatalogMergeServiceTests`, `CatalogCacheStoreTests`).
- Update `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs` similarly — cache-store tests move to `CatalogCacheStoreTests`.
- Update `backend/test/Anela.Heblo.Tests/Controllers/CatalogRepositoryDebugTest.cs` to remove any direct dependencies removed from the constructor.
- `MockCatalogRepository.cs` must continue to satisfy the interface; if any `Refresh*Data` body is moved out, the mock's stub bodies stay (they're test doubles, not implementations).
- New unit tests:
  - `CatalogCacheStoreTests`: dual-key promotion under semaphore, `IsCacheValid` boundary, `CatalogData` fallback chain (current → stale + schedule → empty + schedule), per-source `InvalidateSourceData` side-effect.
  - `CatalogMergeServiceTests`: merge correctly populates each field group from its source; seed-from-ERP when current catalog is empty; `SetLastMergeDateTime` invoked.
  - `CatalogDataRefreshServiceTests`: each refresh writes the right source; `RefreshSalesData` retains stale cache on exception; `RefreshManufactureDifficultySettingsData(product, ct)` updates both the dictionary and the live aggregate's `ManufactureDifficultySettings`.

**Acceptance criteria:**
- All existing tests in `Anela.Heblo.Tests` and `Anela.Heblo.Adapters.Shoptet.Tests` pass.
- New tests cover the three new classes with ≥ 80% line coverage on the moved code.

## Non-Functional Requirements

### NFR-1: Behavior preservation
This is a pure refactor. No public-API change, no behavioral change, no schema change, no config change.

- `ICatalogRepository` surface remains byte-identical (except possible removal of `ManufactureCostLoadDate`, see FR-5).
- `BackgroundRefresh:ICatalogRepository:*` configuration paths in `appsettings.json` work unchanged.
- Cache TTLs, stale retention, `CacheValidityPeriod`, and merge scheduling thresholds are unchanged.
- Logs at `Information` / `Warning` / `Error` levels keep the same messages so existing log queries continue to match. (Logger category name will change for log lines moved into new classes — that is acceptable and expected.)

### NFR-2: Performance
- No regression to `GetAllAsync` (currently O(n)-walk through cache list). The reads should still resolve through `IMemoryCache` in a single lookup.
- `Merge()` cost is unchanged: same dictionary projections per source.
- The semaphore around atomic replace remains a single instance (held by `CatalogCacheStore` singleton); concurrent merges and reads behave as before.

### NFR-3: Maintainability targets
- `CatalogRepository.cs` ≤ 250 lines after refactor.
- `CatalogCacheStore.cs` ≤ 500 lines.
- `CatalogMergeService.cs` ≤ 300 lines (the merge mapping is inherently large; further decomposition is **out of scope** — see Out of Scope).
- `CatalogDataRefreshService.cs` ≤ 500 lines.
- Each new class has ≤ 10 constructor parameters (the refresh service is the worst case; if it exceeds 10, group source clients into a single typed collaborator or use `IServiceProvider`/keyed services).

### NFR-4: Security
No change to security posture. No new public endpoints, no auth changes. No new external calls.

## Data Model
No domain model or persistence changes. The cache key strings (`nameof(CachedXxxData)`-derived) must remain identical so a running instance with warm cache continues to read after the new code deploys (memory cache is in-process; restart clears it anyway, but key stability avoids accidental key collisions during the rollout).

## API / Interface Design

### New types
```text
backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/
  CatalogCacheStore.cs          (new)
  ICatalogCacheStore.cs         (optional — keep concrete if no test seam needed; mocking against the concrete sealed type is acceptable, in-line with existing code)
  CatalogMergeService.cs        (new)
  CatalogDataRefreshService.cs  (new)
  CatalogMergeCallbackWiring.cs (IHostedService that wires SetMergeCallback at startup)
```

Place all four under `Application/Features/Catalog/Infrastructure/` (consistent with `CatalogMergeScheduler`, `CatalogResilienceService` already there).

### Slimmed `CatalogRepository`
Stays at `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs`. Becomes a delegating facade behind `ICatalogRepository`.

### Interface
`ICatalogRepository` stays as-is in `Domain/Features/Catalog/ICatalogRepository.cs`, except for the potential removal of `ManufactureCostLoadDate` (see FR-5).

### Sequence — refresh-triggered merge
```
BackgroundRefreshSchedulerService
  → ICatalogRepository.RefreshSalesData(ct)
     → CatalogRepository delegates to CatalogDataRefreshService.RefreshSalesData(ct)
        → _resilienceService.ExecuteWithResilienceAsync(_salesClient.GetAsync ...)
        → _cacheStore.SetSalesData(result)
           → IMemoryCache.Set(...)
           → InvalidateSourceData("CachedSalesData")
              → _mergeScheduler.ScheduleMerge("CachedSalesData")
           → SetLoadDateInCache("CachedSalesData")
... debounce window elapses ...
CatalogMergeScheduler.RunCallback()
  → CatalogMergeService.ExecuteBackgroundMergeAsync(ct)
     → Merge() (reads via _cacheStore)
     → _cacheStore.ReplaceCacheAtomicallyAsync(newList)
```

### Sequence — query with empty cache
```
Handler → ICatalogRepository.GetAllAsync(ct)
        → CatalogRepository.GetCatalogDataAsync()
           → _cacheStore.TryGetCurrent() = null
           → _cacheStore.IsCacheValid() = false
           → _cacheStore.TryGetStale() — only if AllowStaleDataDuringMerge && IsMergeInProgress
           → otherwise → _mergeService.ExecutePriorityMergeAsync()
              → Merge() → ReplaceCacheAtomicallyAsync → return new list
```

## Dependencies
- **External libraries**: none new. Continues to use `Microsoft.Extensions.Caching.Memory`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Options`.
- **Internal modules**:
  - `Anela.Heblo.Xcc.Services.BackgroundRefresh` — `RegisterRefreshTask<TOwner>` continues to be called with `ICatalogRepository` as the owner type to preserve task IDs.
  - `ICatalogMergeScheduler`, `ICatalogResilienceService` — both stay singletons, unchanged.
  - `CatalogCacheOptions`, `DataSourceOptions` — option types are unchanged.
- **Related**: #2058 (cross-module adapter extraction) reduces source-client count from 16 to fewer. This refactor is **independent** of #2058 — it can land first or second. If #2058 lands first, FR-3's constructor parameter count drops further.

## Out of Scope
- Decomposing the `Merge()` mapping logic further into per-source mapper classes. The brief calls for a three-way split; per-source mapper extraction is a follow-up and would risk over-engineering at this step.
- Replacing `IMemoryCache` with a different cache implementation (Redis, distributed cache).
- Changing `CatalogAggregate` structure or removing the `CatalogData` "magic" getter semantics.
- Splitting `ICatalogRepository` itself into multiple interfaces (`ICatalogReader`, `ICatalogRefresher`). 89 consumer files reference `ICatalogRepository` directly; interface-segregation is a separate PR.
- Cross-module adapter extraction (already tracked in #2058).
- Frontend changes — there are none.
- Database migrations — there are none.

## Open Questions
None.

## Status: COMPLETE