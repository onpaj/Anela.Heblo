# Architecture Review: Decompose CatalogRepository

## Skip Design: true

Pure backend refactor with no UI, no API surface change, no new endpoints, and no visible behavior change.

## Architectural Fit Assessment

The proposal aligns cleanly with established patterns in this module:

- **Vertical Slice + Infrastructure subfolder** is already the convention. `CatalogMergeScheduler`, `CatalogResilienceService`, and `CatalogCacheOptions` live in `Application/Features/Catalog/Infrastructure/`. Adding `CatalogCacheStore`, `CatalogMergeService`, and `CatalogDataRefreshService` there is the obvious placement.
- **`ICatalogRepository` is the existing seam** for the background refresh framework. `BackgroundRefreshExtensions.RegisterRefreshTask<TOwner>` builds task IDs from `typeof(TOwner).Name` (verified at `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/BackgroundRefreshExtensions.cs:91`), so task IDs like `"ICatalogRepository.RefreshSalesData"` (and matching `appsettings.json` keys) are preserved as long as `ICatalogRepository` stays the registration owner — which is exactly what the spec requires.
- **Three distinct lifetimes already coexist:** `CatalogMergeScheduler` is singleton, `ICatalogRepository` is transient, `IMemoryCache` is singleton. The proposal's lifetime choices (cache store + merge service singleton; refresh service transient; repository transient) are correct and necessary — the merge callback wired at startup must outlive request scopes, and `_cacheReplacementSemaphore` plus the in-flight invalidation tracking are process-wide concerns.
- **Cross-module access:** the cross-source helpers (`GetProductsInTransport`/`Reserve`/`Quarantine`/`Ordered`/`Planned`) belong in `CatalogDataRefreshService`, not `CatalogMergeService`. They're invoked only from `Refresh*Data` methods today (verified: lines 121–152 of `CatalogRepository.cs`), and they hit `ITransportBoxRepository`, `IPurchaseOrderRepository`, `IManufactureOrderRepository` — wiring them through the merge service would re-import all those collaborators unnecessarily.

The only architectural risk is the merge-callback wiring cycle, addressed below.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────────────┐
│ BackgroundRefreshSchedulerService (hosted, from Xcc)               │
│   - Resolves ICatalogRepository per task, calls Refresh*Data(ct)   │
└────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────────────┐
│ CatalogRepository (transient, ≤5 deps)            « ICatalogRepository »
│   - Read API: GetByIdAsync, GetAllAsync, FindAsync, ...            │
│   - Refresh* delegates → CatalogDataRefreshService                 │
│   - *LoadDate / LastMergeDateTime / ChangesPendingForMerge         │
│     delegate → CatalogCacheStore                                   │
│   - WaitForCurrentMergeAsync → ICatalogMergeScheduler              │
└────────────────────────────────────────────────────────────────────┘
        │ read              │ refresh                │ priority merge
        ▼                   ▼                        ▼
┌──────────────────┐ ┌──────────────────────┐ ┌──────────────────────┐
│ CatalogCache     │ │ CatalogDataRefresh   │ │ CatalogMergeService  │
│ Store (singleton)│ │ Service (transient)  │ │ (singleton)          │
│                  │ │                      │ │                      │
│ - IMemoryCache   │ │ - 17 source clients  │ │ - Merge()            │
│ - dual-key cache │ │ - ICatalogResilience │ │ - Background+Priority│
│ - 19 per-source  │ │ - Reads CatalogData  │ │   merge execution    │
│   typed accessors│ │   (for difficulty    │ │ - Reads cache store, │
│ - LoadDate write │ │   single-product)    │ │   writes cache store │
│ - LastMergeDate  │ │ - Writes cache store │ │                      │
└──────────────────┘ └──────────────────────┘ └──────────────────────┘
        │                       │                        │
        │                       └─→ scheduler.ScheduleMerge(source)
        │                                                │
        ▼                                                ▼
┌─────────────────────┐                  ┌────────────────────────────┐
│ IMemoryCache        │                  │ ICatalogMergeScheduler     │
│ (singleton)         │                  │ (singleton, debounce timer)│
└─────────────────────┘                  └────────────────────────────┘
                                                         │
                                                         │ callback
                              ┌──────────────────────────┘
                              ▼
                ┌────────────────────────────────────┐
                │ CatalogMergeCallbackInitializer    │
                │ (IHostedService — runs at startup, │
                │  wires scheduler.SetMergeCallback) │
                └────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Where the merge callback is wired

**Options considered:**
1. `IHostedService` (`CatalogMergeCallbackInitializer`) that resolves both singletons in `StartAsync` and calls `SetMergeCallback`. Spec's chosen option.
2. Eager wiring inside `CatalogModule.AddCatalogModule` using a factory delegate when registering one of the singletons (resolve the other via `IServiceProvider` parameter).
3. Wire inside `CatalogMergeService`'s constructor, taking `ICatalogMergeScheduler` and calling `SetMergeCallback(this.ExecuteBackgroundMergeAsync)` (mirrors current `CatalogRepository` constructor behavior).

**Chosen approach:** Option 3 — wire inside `CatalogMergeService`'s constructor. Reverse the spec on this point.

**Rationale:** `CatalogMergeService` is registered as a singleton, so its constructor runs exactly once per process — same guarantee as `IHostedService.StartAsync`. The wiring is one line and is naturally co-located with the callback it registers; an extra `IHostedService` class adds a file and a startup-ordering question for no observable benefit. The current code already follows this pattern in `CatalogRepository:118`; moving it to the singleton fixes the bug (every transient `CatalogRepository` resolution currently re-registers the callback) without adding indirection. If a future test needs to suppress the wiring, the test can register a fake `ICatalogMergeScheduler` that no-ops `SetMergeCallback`. The IHostedService alternative is still acceptable if the implementer prefers it — both satisfy the "exactly once at startup" requirement.

#### Decision 2: Cache store API shape — typed accessors vs. raw dictionary

**Options considered:**
1. Typed setter methods per source (`SetSalesData(IList<…>)`), each internally invoking `InvalidateSourceData` + `SetLoadDateInCache`.
2. Public dictionary-style accessors that mirror today's getter/setter properties.
3. Generic `Set<T>(string sourceName, T value)` with reflection.

**Chosen approach:** Option 1 — typed methods per source. With one exception below.

**Rationale:** Type safety, IDE discoverability, and a single, audit-able place that enforces invalidation + load-date side effects. Matches the spec's FR-1 acceptance criterion.

**Exception:** `RefreshManufactureDifficultySettingsData(product, ct)` mutates the cached dictionary by single key (`CachedManufactureDifficultySettingsData[product] = …` at `CatalogRepository.cs:243`). The current code path skips `InvalidateSourceData` for the single-key write — only the all-products path goes through the setter. This subtle behavior must be preserved. Expose two methods: `SetManufactureDifficultySettings(IDictionary<…>)` (full replace, triggers invalidation) **and** `UpdateManufactureDifficultySettingsForProduct(string productCode, List<…>)` (single-key write, no invalidation — matches today). Document the difference inline.

#### Decision 3: Whether `CatalogData` accessor stays on the cache store or moves to the merge service

**Options considered:**
1. Keep the "current → stale + schedule → empty + schedule + warn" fallback chain inside `CatalogCacheStore.CatalogData` getter. Used by both `CatalogRepository` (read queries) and `CatalogMergeService.Merge()` (seed-from-ERP check) and `CatalogDataRefreshService.RefreshManufactureDifficultySettingsData` (single-product mutation).
2. Split into two accessors: `TryGetCurrent()` (pure read, no side effects) for `Merge()` and the single-product refresh, plus the side-effecting `CatalogData` for the repository's read queries.

**Chosen approach:** Option 1 — single accessor on the cache store, preserving current behavior bit-for-bit.

**Rationale:** The spec's NFR-1 demands behavior preservation. Today, `Merge()` calls `CatalogData` (`CatalogRepository.cs:340`), which on empty cache calls `_mergeScheduler.ScheduleMerge("CacheEmpty")` as a side effect. That side effect during merge-from-empty is harmless (the merge is already running) but observable. Option 2 would change observable behavior. If the future call sites turn out to need a non-side-effecting accessor, add it then.

#### Decision 4: `ICatalogCacheStore` interface or concrete-only

**Options considered:**
1. Concrete `CatalogCacheStore` only — tests use real `IMemoryCache` instances.
2. Extract `ICatalogCacheStore` for `CatalogMergeService` / `CatalogDataRefreshService` / `CatalogRepository` to mock against.

**Chosen approach:** Option 1 — concrete only.

**Rationale:** Matches existing patterns in the module (`CatalogResilienceService` has an interface because of Polly testing concerns; `CatalogMergeScheduler` has an interface because it's the explicit collaboration point with the would-be circular dependency). The cache store has no such constraint. Real `IMemoryCache` is cheap to instantiate in tests (`new MemoryCache(new MemoryCacheOptions())`), and using it gives higher-fidelity tests than mocking. Spec FR-1 leaves this open; lock it down to concrete-only.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/Catalog/
├── CatalogRepository.cs                                (slimmed — ≤250 lines)
├── CatalogModule.cs                                    (DI registration updated)
└── Infrastructure/
    ├── CatalogCacheStore.cs                            (new — ≤500 lines)
    ├── CatalogMergeService.cs                          (new — ≤300 lines)
    └── CatalogDataRefreshService.cs                    (new — ≤500 lines)

backend/src/Anela.Heblo.Domain/Features/Catalog/
└── ICatalogRepository.cs                               (remove ManufactureCostLoadDate)

backend/src/Anela.Heblo.Persistence/Repositories/
└── MockCatalogRepository.cs                            (remove ManufactureCostLoadDate property)

backend/test/Anela.Heblo.Tests/
├── Domain/Catalog/CatalogRepositoryTests.cs            (trim to ≤5 deps)
├── Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs   (mostly moves to cache store tests)
├── Features/Catalog/Infrastructure/CatalogCacheStoreTests.cs           (new)
├── Features/Catalog/Infrastructure/CatalogMergeServiceTests.cs         (new)
├── Features/Catalog/Infrastructure/CatalogDataRefreshServiceTests.cs   (new)
└── Controllers/CatalogRepositoryDebugTest.cs           (constructor cleanup)
```

No new namespace; existing `Anela.Heblo.Application.Features.Catalog.Infrastructure` is reused.

### Interfaces and Contracts

`ICatalogRepository` (in `Domain/Features/Catalog/`) — public surface preserved verbatim, with the single removal `DateTime? ManufactureCostLoadDate { get; }` (verified: no references outside `CatalogRepository.cs`, `MockCatalogRepository.cs`, and the interface itself).

`CatalogCacheStore` (concrete, sealed if practical):

```csharp
internal sealed class CatalogCacheStore
{
    public CatalogCacheStore(IMemoryCache cache, TimeProvider time,
        IOptions<CatalogCacheOptions> options, ICatalogMergeScheduler scheduler,
        ILogger<CatalogCacheStore> logger);

    // Aggregate cache (dual-key + atomic replace)
    Task ReplaceCacheAtomicallyAsync(List<CatalogAggregate> newData);
    bool IsCacheValid();
    List<CatalogAggregate> CatalogData { get; }  // side-effecting fallback chain — DO NOT change

    // Merge state
    DateTime? LastMergeDateTime { get; }
    void SetLastMergeDateTime();

    // Typed per-source set/get pairs (19 sources). Setters call InvalidateSourceData + SetLoadDateInCache.
    IList<CatalogSaleRecord> GetSalesData();
    void SetSalesData(IList<CatalogSaleRecord> data);
    // ... same shape for the other 18 sources ...

    // Special-case: per-product difficulty mutation, NO invalidation
    void UpdateManufactureDifficultySettingsForProduct(
        string productCode, List<ManufactureDifficultySetting> settings);

    // Load-date accessors used by repository LoadDate properties
    DateTime? GetLoadDate(string sourceName);
}
```

`CatalogMergeService` (concrete, sealed):

```csharp
internal sealed class CatalogMergeService
{
    public CatalogMergeService(CatalogCacheStore store,
        ICatalogMergeScheduler scheduler, TimeProvider time,
        ILogger<CatalogMergeService> logger)
    {
        // Wires callback ONCE (singleton lifetime, see Decision 1):
        scheduler.SetMergeCallback(ExecuteBackgroundMergeAsync);
    }

    public Task ExecuteBackgroundMergeAsync(CancellationToken ct = default);
    public Task<List<CatalogAggregate>> ExecutePriorityMergeAsync();
}
```

`CatalogDataRefreshService` (concrete) — exposes the 19 `Refresh*Data` methods plus the five `GetProductsIn*` helpers as private methods. Constructor parameter count is the worst case: at minimum 17 source clients + `CatalogCacheStore` + `ICatalogResilienceService` + `TimeProvider` + `IOptions<DataSourceOptions>` + `ILogger` ≈ 22 parameters. **This exceeds the NFR-3 ≤ 10 ceiling.** See "Specification Amendments" below.

`CatalogRepository` (slimmed, transient) — constructor: `(CatalogCacheStore, CatalogMergeService, CatalogDataRefreshService, ICatalogMergeScheduler)`. All 19 `Refresh*Data` methods are one-line delegations to the refresh service. All 18 `*LoadDate` properties and `LastMergeDateTime` delegate to the cache store. `ChangesPendingForMerge` stays in the repository (it composes load dates from the cache store — pure read).

### Data Flow

**Refresh-triggered merge** — unchanged from spec sequence diagram. Verified that `BackgroundRefreshSchedulerService` resolves `ICatalogRepository` via a fresh scope per task invocation (extension method creates a scope per `wrappedMethod` call at `BackgroundRefreshExtensions.cs:134`), so the transient `CatalogRepository`'s delegation to a (also transient) `CatalogDataRefreshService` resolves correctly per task.

**Query with empty cache:**

```
Handler → GetAllAsync(ct)
  → CatalogRepository.GetCatalogDataAsync()
     → CatalogCacheStore.IsCacheValid() = false, current = null
     → if (AllowStaleDataDuringMerge && scheduler.IsMergeInProgress)
          → CatalogCacheStore.GetStale() — return if present (logs "Serving stale...")
     → CatalogMergeService.ExecutePriorityMergeAsync()
        → Merge() reads from CatalogCacheStore.GetXxxData() accessors
        → CatalogCacheStore.SetLastMergeDateTime()
        → CatalogCacheStore.ReplaceCacheAtomicallyAsync(newList)
        → return newList
```

**Per-product difficulty refresh:**

```
RefreshManufactureDifficultySettingsData("ABC123", ct)
  → CatalogRepository delegates to CatalogDataRefreshService
     → diffRepo.ListAsync("ABC123", ct)
     → CatalogCacheStore.UpdateManufactureDifficultySettingsForProduct("ABC123", list)
        [no InvalidateSourceData — preserves today's behavior]
     → CatalogCacheStore.CatalogData
          .SingleOrDefault(s => s.ProductCode == "ABC123")
          ?.ManufactureDifficultySettings.Assign(list, timeProvider.GetUtcNow().UtcDateTime)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Merge callback wired more than once (today's bug — `CatalogRepository` is transient and rewires on every resolution) | Low | Move wiring to singleton `CatalogMergeService` constructor (Decision 1). Add a `CatalogModuleTests` assertion that resolving `CatalogRepository` twice does not re-register the callback. |
| Refresh service constructor parameter count (~22) violates NFR-3 ≤ 10 | Medium | See Specification Amendments below. |
| `Merge()` reads `CatalogData` (side-effecting) and on empty cache schedules another merge mid-merge | Low | Preserve today's behavior (Decision 3). Note: scheduler debounces, so re-scheduling during an active merge is benign. Document in a single comment at the call site. |
| Per-product difficulty refresh bypasses invalidation by writing dictionary key directly — easy to miss in the new API | Medium | Expose two distinct cache-store methods (Decision 2), make the bypassing one clearly named (`Update...ForProduct` vs `Set...`). Add a `CatalogDataRefreshServiceTests` case asserting that the per-product call does *not* schedule a merge. |
| Test mocks against `CatalogCacheStore` concrete type may be brittle if NSubstitute/Moq cannot mock sealed classes | Low | Use real `IMemoryCache` in tests (Decision 4). Avoid `sealed` on cache store if mocking is needed; rely on `internal sealed` with `InternalsVisibleTo` only if necessary. |
| `MockCatalogRepository.ManufactureCostLoadDate` removal could break a test we missed | Low | Verified: grep shows only the 6 files listed; the property is declared but never read outside the repository class itself. Tests will fail to compile if a hidden reader exists. |
| `IManufactureClient` removal breaks compile in an unseen call site within `CatalogRepository` | Low | Verified: in `CatalogRepository.cs`, `_manufactureClient` field is assigned in the constructor (line 103) and *never read anywhere else*. Safe to drop. |
| Hosted-service ordering if Decision 1's IHostedService route is chosen instead | Low | Decision 1 avoids this by using the singleton-constructor route. If implementer chooses the IHostedService route, ensure the wiring service is registered before `AddBackgroundRefresh` runs (or rely on the fact that `BackgroundRefreshSchedulerService` doesn't fire until after `StartAsync` completes for all earlier hosted services). |
| Behavior drift between `CatalogRepositoryTests` and the new split tests | Medium | Run the *full* existing test suite before and after the refactor. Diff coverage and assertion counts. Tests in `CatalogRepositoryCacheOptimizationTests` that depend on internal state must move to `CatalogCacheStoreTests` 1:1 — do not rewrite them. |

## Specification Amendments

1. **NFR-3 constructor parameter ceiling for the refresh service.** Even after FR-3's reduction, the refresh service needs ~22 constructor parameters. The spec lists the fallback ("group source clients into a single typed collaborator or use `IServiceProvider`/keyed services"), but does not pick one. Lock it down: **do not group or use keyed services in this refactor**. Accept the parameter count; it's an honest reflection of the surface area until #2058 lands. Adjust NFR-3 to: *"`CatalogRepository`, `CatalogMergeService`, and `CatalogCacheStore` each ≤ 10 constructor parameters. `CatalogDataRefreshService` is expected to exceed this until #2058 reduces source-client count; do not pre-emptively group dependencies."* Premature grouping ("source bundle" wrapper) just hides the same coupling behind a new type and creates a second class to keep in sync. The number is the symptom; the disease is the cross-module coupling that #2058 addresses.

2. **FR-1: Add `UpdateManufactureDifficultySettingsForProduct(string, List<…>)` to the cache-store API.** The spec implicitly requires it (FR-3 references the live-aggregate mutation) but does not explicitly call out the no-invalidation variant. Add it explicitly so the implementer doesn't accidentally route the per-product path through the full-replace setter, which would trigger an unwanted merge after every per-product call.

3. **FR-6 wiring choice.** The spec picks Option 1 (IHostedService initializer) and presents it as "easy to unit test." Reverse it to Option 3 — wire in `CatalogMergeService`'s constructor (Decision 1 above). This eliminates one file, one DI registration, and any startup-ordering question, with no loss in testability (a stub `ICatalogMergeScheduler` covers the unit test concern). If the implementer disagrees, Option 1 is still acceptable.

4. **FR-7: Coverage threshold.** "≥ 80% line coverage on the moved code" is fine, but specify *measured how*: existing test runs already cover most of this code through `CatalogRepositoryTests` and `CatalogRepositoryCacheOptimizationTests`. The intent should be: *no regression in line coverage for the moved code, measured by Coverlet against the new file paths*. Otherwise an implementer could ship a passing 80% number that actually drops coverage relative to today.

5. **Document `Merge()`'s self-scheduling behavior.** Add one inline comment at the `if (!CatalogData.Any())` check in the new `CatalogMergeService` noting that reading the cache-store `CatalogData` property has a deliberate side effect (schedules a merge if empty) and that calling it during the very merge that runs because of an empty cache is intentional and harmless (debouncer absorbs it).

## Prerequisites

None. This is a pure internal restructure:

- No migrations, no schema changes.
- No new configuration keys. Existing `BackgroundRefresh:ICatalogRepository:*` entries in `appsettings.json` keep working unchanged because task IDs are derived from `typeof(ICatalogRepository).Name` (verified at `BackgroundRefreshExtensions.cs:91`) and the spec preserves `ICatalogRepository` as the `TOwner` parameter in `RegisterRefreshTask<ICatalogRepository>(...)`.
- No new packages. Continues using `Microsoft.Extensions.Caching.Memory`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Options`, `Polly`.
- No coordination with #2058 required. This work can ship first; if #2058 lands first, the refresh service's constructor count drops naturally — no rework needed.
- Validation gates per `CLAUDE.md`: `dotnet build`, `dotnet format`, full `Anela.Heblo.Tests` + `Anela.Heblo.Adapters.Shoptet.Tests` green. No E2E or frontend gates apply.