I have enough context now to write the architecture review.

# Architecture Review: Decompose CatalogRepository

## Skip Design: true

## Architectural Fit Assessment

This refactor aligns cleanly with existing project conventions:

- **Vertical Slice + Infrastructure subfolder.** The Catalog feature already groups feature-scoped infrastructure under `Application/Features/Catalog/Infrastructure/` (`CatalogMergeScheduler`, `CatalogResilienceService`, `CatalogCacheOptions`). The proposed `CatalogCacheStore`, `CatalogMergeService`, and `CatalogDataRefreshService` fit that bucket exactly.
- **Public surface preserved.** `ICatalogRepository` lives in `Anela.Heblo.Domain.Features.Catalog` and is consumed by 89 sites including `MockCatalogRepository`, dashboard tiles, `CatalogModule.RegisterBackgroundRefreshTasks`, and a `RegisterRefreshTask<ICatalogRepository>` registration that depends on the owner-type name as a literal in task IDs (e.g. `"ICatalogRepository.RefreshTransportData"`). Keeping the interface intact preserves all of that — confirmed by reading `CatalogModule.cs:128–216`.
- **DI lifetime story is already in place.** `ICatalogMergeScheduler` is a singleton wired by `CatalogModule.cs:69`. `IMemoryCache` is a singleton. `ICatalogRepository` is currently transient (`CatalogModule.cs:44`). The callback wiring in the constructor (`CatalogRepository.cs:118`) is the leak that makes this refactor necessary: a transient constructor is mutating singleton state on every resolve.
- **`IHostedService` pattern is in use.** `BackgroundRefreshExtensions` already calls `AddHostedService` for `BackgroundRefreshSchedulerService`/`TierBasedHydrationOrchestrator`. A new `CatalogMergeCallbackWiring : IHostedService` (the spec's option 1) is consistent with that pattern.

**Risk reduction in the existing setup that this work removes:** today, every transient resolution of `ICatalogRepository` calls `_mergeScheduler.SetMergeCallback(ExecuteBackgroundMergeAsync)`. Each scope's repository instance overwrites the singleton's callback with a closure bound to a different instance — when a scope disposes, the callback still points to the disposed instance's `Merge()` (which reads `_cache` and `_timeProvider`, both singletons, so it accidentally still works). The new singleton `CatalogMergeService` resolves this latent bug.

## Proposed Architecture

### Component Overview

```
                        ┌─────────────────────────────────┐
                        │     ICatalogRepository (89      │
                        │     consumers, unchanged)       │
                        └────────────────┬────────────────┘
                                         │ implements
                                         ▼
                ┌────────────────────────────────────────────────┐
                │  CatalogRepository (transient, ≤ 250 LOC)      │
                │  - read-side queries (Get/Find/Single/Any/...) │
                │  - LoadDate properties (delegating)            │
                │  - Refresh* methods (delegating)               │
                └───────┬────────────────┬───────────────────────┘
                        │                │
              delegates │                │ delegates
                        ▼                ▼
   ┌────────────────────────────┐   ┌──────────────────────────────────┐
   │ CatalogDataRefreshService  │   │ CatalogMergeService (singleton)  │
   │ (transient)                │   │ - Merge(), GetProductType()      │
   │ - 19× Refresh*Data         │   │ - ExecuteBackgroundMergeAsync    │
   │ - GetProductsInTransport/  │   │ - ExecutePriorityMergeAsync      │
   │   Reserve/Quarantine/      │   │                                  │
   │   Ordered/Planned          │   └──────────────┬───────────────────┘
   └──────────────┬─────────────┘                  │
                  │                                │
                  │                                │
                  └───────────┬────────────────────┘
                              │ reads/writes
                              ▼
                ┌──────────────────────────────────────────┐
                │ CatalogCacheStore (singleton)            │
                │ - IMemoryCache wrapper (only this class  │
                │   touches IMemoryCache)                  │
                │ - 19 typed Get/Set per source            │
                │ - CatalogData fallback chain             │
                │ - ReplaceCacheAtomicallyAsync (semaphore)│
                │ - IsCacheValid, LoadDate helpers         │
                └──────────────────────────────────────────┘

         (startup)
   ┌─────────────────────────────┐         ┌──────────────────────────┐
   │ CatalogMergeCallbackWiring  │────────►│ ICatalogMergeScheduler   │
   │ : IHostedService            │ wires   │ (singleton, unchanged)   │
   │ (resolves both singletons)  │  once   │ - SetMergeCallback       │
   └─────────────────────────────┘         └──────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Concrete types, no extra interfaces

**Options considered:**
- (A) Introduce `ICatalogCacheStore`, `ICatalogMergeService`, `ICatalogDataRefreshService` for test seams.
- (B) Use concrete classes only; tests construct real `CatalogCacheStore` against a real `MemoryCache`.

**Chosen approach:** B.

**Rationale:** These are not module boundaries — they are internal collaborators behind the existing `ICatalogRepository` seam. `CatalogResilienceService` and `CatalogMergeScheduler` *do* have interfaces because they cross test boundaries; the cache store does not. Real `MemoryCache` is a `new MemoryCache(new MemoryCacheOptions())` away — easier to fake than to mock. Reduces ceremony and matches the spec's stated preference ("mocking against the concrete sealed type is acceptable"). If a test seam is later needed, extracting an interface from a concrete class is mechanical.

#### Decision 2: Callback wiring via `IHostedService`

**Options considered:**
- (A) Wire callback inside `AddCatalogModule` by resolving both singletons through a factory delegate registered on `CatalogMergeScheduler`.
- (B) Wire callback in a small `CatalogMergeCallbackWiring : IHostedService` that resolves both singletons in `StartAsync` and calls `SetMergeCallback`.

**Chosen approach:** B.

**Rationale:** Factory-time resolution forces an ordering between two singleton registrations and tangles the scheduler's constructor. `StartAsync` runs after the service provider is fully built, sidesteps ordering entirely, and is the existing pattern in `BackgroundRefreshExtensions`. The spec already recommends this — adopting it without amendment.

#### Decision 3: `CatalogMergeService` singleton, `CatalogDataRefreshService` transient

**Options considered:**
- (A) Both singletons.
- (B) Refresh service transient (matches current `CatalogRepository` lifetime), merge service singleton.

**Chosen approach:** B (matches the spec).

**Rationale:** `BackgroundRefreshExtensions.CreateWrappedMethod` (line 132–138, 147–153) creates a scope per refresh invocation and resolves the owner from `scope.ServiceProvider`. With `ICatalogRepository → CatalogRepository` registered transient and delegating to `CatalogDataRefreshService`, the refresh service must be scope-resolvable. The 17 source-specific repositories/clients it depends on (e.g. `ITransportBoxRepository`, `IPurchaseOrderRepository`) include `IRepository` types whose persistence-layer implementations are scoped — registering the refresh service singleton would create captive dependencies on a `DbContext`. Transient is correct and matches today's contract.

The merge service holds no per-request state and must outlive the scope so the callback registered at startup remains valid; singleton is correct.

#### Decision 4: Strict layer rule — only `CatalogCacheStore` references `IMemoryCache`

**Options considered:**
- (A) Allow merge service to read `IMemoryCache` directly for "performance" of the merge mapping.
- (B) All cache I/O confined to `CatalogCacheStore`; merge service receives typed collections through getters.

**Chosen approach:** B.

**Rationale:** This is the property that makes the refactor worth doing. The merge cost is dominated by dictionary projections, not by `IMemoryCache.Get` calls (one lookup per source — 19 lookups total per merge). The maintainability gain outweighs any micro-cost. A code review or grep on `IMemoryCache` in the new files becomes a one-line invariant check.

#### Decision 5: Remove `CachedManufactureCostData` and `ManufactureCostLoadDate` outright (FR-5)

**Options considered:**
- (A) Remove the property from `ICatalogRepository`.
- (B) Keep the property on the interface, return `null`.

**Chosen approach:** A.

**Rationale:** Confirmed via grep — `ManufactureCostLoadDate` is referenced only in:
- `CatalogRepository.cs:813` (definition, to be removed)
- `ICatalogRepository.cs:46` (interface declaration, to be removed)
- `MockCatalogRepository.cs:407` (mock implementation, to be removed)
- `backend/test/Anela.Heblo.Tests/Common/ManufactureOrderTestFactory.cs` (test factory — verify and update; if it only references the property to satisfy the interface, remove it)
- Two docs files (`docs/superpowers/specs/...` and `docs/superpowers/plans/...`) — not callers; no action needed beyond optional cleanup.

No production consumer exists. Cleaner to delete than to keep dead surface.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/Catalog/
├── CatalogRepository.cs                              (slimmed, ≤ 250 LOC)
└── Infrastructure/
    ├── CatalogCacheStore.cs                          (new)
    ├── CatalogMergeService.cs                        (new)
    ├── CatalogDataRefreshService.cs                  (new)
    ├── CatalogMergeCallbackWiring.cs                 (new, IHostedService)
    ├── CatalogMergeScheduler.cs                      (unchanged)
    ├── CatalogResilienceService.cs                   (unchanged)
    └── CatalogCacheOptions.cs                        (unchanged)
```

No new files in `Domain/Features/Catalog/`. The interface stays put.

### Interfaces and Contracts

**`CatalogCacheStore` (sketch — concrete, no interface):**

```csharp
public sealed class CatalogCacheStore
{
    public Task ReplaceCacheAtomicallyAsync(List<CatalogAggregate> newData);
    public bool IsCacheValid();
    public List<CatalogAggregate> GetCatalogData();          // current → stale (+schedule) → empty (+schedule)
    public List<CatalogAggregate>? TryGetCurrent();
    public List<CatalogAggregate>? TryGetStale();

    // typed per-source accessors — 19 pairs
    public IList<CatalogSaleRecord>             GetSalesData();
    public void                                  SetSalesData(IList<CatalogSaleRecord> value);
    public IList<CatalogAttributes>             GetCatalogAttributesData();
    public void                                  SetCatalogAttributesData(IList<CatalogAttributes> value);
    // ... 17 more pairs

    public DateTime? GetLoadDate(string sourceKey);          // generic — backs the 19 LoadDate properties on the repo
    public DateTime? LastMergeDateTime { get; }
    public void SetLastMergeDateTime();
}
```

Each `Set*` method internally calls `InvalidateSourceData(sourceKey)` and `SetLoadDateInCache(sourceKey)` so the merge scheduler still fires. The `sourceKey` literal **must remain `nameof(CachedSalesData)`-style** (i.e. the same string the existing code already writes to `IMemoryCache`) to preserve key stability noted in NFR-3.

**`CatalogMergeService` (sketch):**

```csharp
public sealed class CatalogMergeService
{
    public Task ExecuteBackgroundMergeAsync(CancellationToken ct);
    public Task<List<CatalogAggregate>> ExecutePriorityMergeAsync();
    internal List<CatalogAggregate> Merge();   // for tests; otherwise private
}
```

**`CatalogDataRefreshService` (sketch):**

```csharp
public sealed class CatalogDataRefreshService
{
    public Task RefreshTransportData(CancellationToken ct);
    // ... 18 more, signatures identical to ICatalogRepository
}
```

**`CatalogMergeCallbackWiring`:**

```csharp
public sealed class CatalogMergeCallbackWiring : IHostedService
{
    public CatalogMergeCallbackWiring(
        ICatalogMergeScheduler scheduler,
        CatalogMergeService mergeService) { ... }

    public Task StartAsync(CancellationToken ct)
    {
        _scheduler.SetMergeCallback(_mergeService.ExecuteBackgroundMergeAsync);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

**Slimmed `CatalogRepository`:**

```csharp
public sealed class CatalogRepository : ICatalogRepository
{
    public CatalogRepository(
        CatalogCacheStore cacheStore,
        CatalogMergeService mergeService,
        CatalogDataRefreshService refreshService,
        ICatalogMergeScheduler mergeScheduler) { ... }

    // 8 read-side query methods → delegate to cacheStore.GetCatalogData()
    // 19 LoadDate properties → delegate to cacheStore.GetLoadDate("CachedXxx")
    // LastMergeDateTime / ChangesPendingForMerge / WaitForCurrentMergeAsync → delegate
    // 19 Refresh*Data methods → delegate to refreshService
}
```

### Data Flow

#### Query with warm cache
```
Handler → ICatalogRepository.GetAllAsync(ct)
        → CatalogRepository._cacheStore.GetCatalogData()
           → returns current cache, no I/O
```

#### Query with empty cache (priority merge)
```
Handler → ICatalogRepository.GetAllAsync(ct)
        → CatalogRepository.GetCatalogDataAsync()
           → _cacheStore.TryGetCurrent() = null
           → _cacheStore.IsCacheValid() = false
           → if AllowStaleDataDuringMerge && _mergeScheduler.IsMergeInProgress
              → _cacheStore.TryGetStale() — return if non-null
           → otherwise → _mergeService.ExecutePriorityMergeAsync()
              → Merge() (reads from _cacheStore typed getters)
              → _cacheStore.ReplaceCacheAtomicallyAsync(newList)
              → returns newList
```

#### Background refresh → debounced merge
```
BackgroundRefreshSchedulerService
  → scope.GetRequiredService<ICatalogRepository>().RefreshSalesData(ct)
     → CatalogRepository delegates → CatalogDataRefreshService.RefreshSalesData(ct)
        → _resilienceService.ExecuteWithResilienceAsync(_salesClient.GetAsync(...))
        → _cacheStore.SetSalesData(result)
           → _cache.Set("CachedSalesData", value)
           → InvalidateSourceData("CachedSalesData")
              → _mergeScheduler.ScheduleMerge("CachedSalesData")
           → SetLoadDateInCache("CachedSalesData")
... DebounceDelay elapses (default 5s) ...
CatalogMergeScheduler.Timer fires → ExecuteMergeAsync
  → _mergeCallback(ct)  // wired at startup to CatalogMergeService.ExecuteBackgroundMergeAsync
     → CatalogMergeService.Merge() reads via _cacheStore typed getters
     → _cacheStore.ReplaceCacheAtomicallyAsync(newList)
     → _cacheStore.SetLastMergeDateTime()
```

#### Single-product manufacture difficulty refresh
```
RefreshManufactureDifficultySettingsData("ABC123", ct)
  → _cacheStore.GetManufactureDifficultySettings()["ABC123"] = settings
  → _cacheStore.GetCatalogData().SingleOrDefault(p => p.ProductCode == "ABC123")
      ?.ManufactureDifficultySettings.Assign(settings, now)
```
This requires `CatalogCacheStore` to expose a *read-only* `GetCatalogData()` accessor that does **not** trigger a merge schedule (or that the refresh service uses `TryGetCurrent()` + fallback explicitly to avoid scheduling churn from a refresh path that already invalidates). Prefer the latter to keep `GetCatalogData()` semantics single-purpose.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Callback wiring runs before `ICatalogMergeScheduler` is ready | Medium | `IHostedService.StartAsync` runs after DI build; scheduler ctor only requires logger/options/lifetime — safe. Add a unit test that resolves both from a real `ServiceProvider` and asserts callback is non-null after `StartAsync`. |
| Singleton `CatalogMergeService` holding a callback into a singleton scheduler — circular references via DI graph | Low | Both are singletons, both registered before `AddHostedService`. No DI cycle (scheduler doesn't depend on merge service constructor-wise; wiring is push, not pull). |
| `CatalogDataRefreshService` requires 17+ ctor params, may exceed NFR-3's "≤ 10" target | Medium | Acceptable temporarily; spec already flags this as the worst case. Do **not** introduce `IServiceProvider` injection or keyed services as a workaround — that hides dependencies. Tracking #2058 (cross-module adapter extraction) is the real fix. If hard cap is required, group `(ITransportBoxRepository, IPurchaseOrderRepository, IManufactureOrderRepository, IManufacturedProductInventoryRepository)` behind a single `ICatalogStockSources` collaborator in this Catalog module. |
| Existing tests construct `CatalogRepository` with 25 mocks and may not compile | Low | All tests in scope listed in FR-7. Refactor test setup at the same time. `MockCatalogRepository` is independent of `CatalogRepository` and only needs the `ManufactureCostLoadDate` removal. |
| Cache key strings drift between old and new code if `nameof()` is moved to a different containing type | High | Use literal string constants in `CatalogCacheStore` matching today's `nameof(CachedSalesData)` results (`"CachedSalesData"`, `"CachedCatalogAttributesData"`, etc.). Add a static dictionary `SourceKeys` to make the registry explicit and grep-able. |
| Concurrent `CatalogRepository.GetCatalogDataAsync()` calls trigger duplicate priority merges before `ReplaceCacheAtomicallyAsync` writes | Pre-existing | Out of scope. Today's code has the same behavior. Do not "fix" as part of refactor. |
| Background refresh hot-path now goes through one extra delegation hop per refresh method | Negligible | 19 `Task` delegations per minute is unmeasurable. No mitigation required. |
| Single-product `RefreshManufactureDifficultySettingsData` reads `CatalogData` through the cache store — if the store's `GetCatalogData()` schedules a merge on empty, this path now causes extra merge churn | Medium | Have the refresh service use `_cacheStore.TryGetCurrent()` (returns null without scheduling), and skip the `.Assign(...)` call if no current cache exists. The next merge will pick up the new dictionary entry anyway. |

## Specification Amendments

1. **Drop `IManufactureClient` from `CatalogDataRefreshService` dependencies (FR-3).** The spec already calls this out as a verification step; verified — `_manufactureClient` is referenced only at the null-check in the constructor at `CatalogRepository.cs:103`, never used in any method body. Remove it.
2. **`CachedManufactureCostDataKey` is removed entirely (FR-1, FR-5).** No remaining reader exists. The spec's conditional retention is unnecessary — final answer is: delete the constant, the property, and the interface member.
3. **`MockCatalogRepository.ManufactureDifficultyLoadDate` (line 406, not currently on the interface) is dead.** It looks like a leftover. Not in scope for this refactor; mention to the user but do not delete.
4. **`_sourceLastUpdated` `ConcurrentDictionary` in `CatalogRepository.cs:61` has no external reader** — confirmed by grep. The spec says "if no consumer reads it, remove it." Remove. Keep `_mergeScheduler.ScheduleMerge(dataSource)` inside `InvalidateSourceData`; that is the only effect of the existing code.
5. **`InvalidateSourceData` behavior when `EnableBackgroundMerge=false` (lines 549–556)** must move to `CatalogCacheStore` and continue to evict both current and stale caches plus the update-time key. This is the "fallback to old behavior" branch — preserve verbatim.
6. **Add an assertion test for callback wiring**: spin up a host with `AddCatalogModule`, call `host.StartAsync()`, then resolve `ICatalogMergeScheduler` and assert `HasPendingMerge()` works end-to-end after `RefreshErpStockData` (this validates the wiring without depending on internals).
7. **Sealed types.** All three new classes should be `sealed` — no design intent for inheritance.
8. **Keep the `Task.Run(() => Merge(), ct)` wrapper in `ExecuteBackgroundMergeAsync`** when moving the method. Removing the `Task.Run` would block the calling thread (the timer callback in `CatalogMergeScheduler`) on CPU-bound merge work. Preserve as-is.

## Prerequisites

None. The refactor is self-contained:

- No migrations.
- No config schema changes (`CatalogCacheOptions`, `DataSourceOptions`, `BackgroundRefresh:ICatalogRepository:*` all stay as-is).
- No new infrastructure.
- No frontend changes.
- No new package dependencies.
- No dependency on PR #2058 (cross-module adapter extraction) — can land before or after; if before, FR-3's constructor parameter count improves automatically.

Implementation can start immediately. Suggested commit/PR ordering for review safety:

1. Introduce `CatalogCacheStore`; have `CatalogRepository` delegate to it but keep all current responsibilities. Verify all tests pass.
2. Extract `CatalogMergeService` + `CatalogMergeCallbackWiring`. Verify scheduler callback fires.
3. Extract `CatalogDataRefreshService`. Verify each refresh path end-to-end.
4. Slim `CatalogRepository`, remove `CachedManufactureCostData` and `ManufactureCostLoadDate`. Update `MockCatalogRepository`, `CatalogRepositoryTests`, `CatalogRepositoryCacheOptimizationTests`, `CatalogRepositoryDebugTest`.