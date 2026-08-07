# Architecture Review: Fix cache isolation violations in CatalogDataRefreshService

## Skip Design: true

## Architectural Fit Assessment
This is a targeted bug fix inside an already-established pattern, not a new capability. `CatalogCacheStore` defines the isolation contract (atomic current/stale swap, per-source `Set*Data` accessors that go through `InvalidateSourceData`/`SetLoadDateInCache`); `CatalogMergeService.Merge()` is the reference implementation of "clone before mutate" for that contract (fixed under #3827, `CatalogMergeService.cs:82` — `catalogData.Select(p => p.Clone()).ToList()`). The two methods this issue targets are the only remaining call sites in `CatalogDataRefreshService.cs` that read a live cache reference and mutate it in place instead of following that pattern. No new component, dependency, or interface is required — the fix is "make these two methods behave like every other method in the same class already does."

Verified against the codebase:
- `CatalogAggregate.Clone()` (`backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs:310-317`) already deep-copies `ManufactureDifficultySettings` via `ManufactureDifficultyConfiguration.Clone()` and `ManufactureHistory` is a plain list property set via `MemberwiseClone` + explicit reassignment pattern elsewhere — confirmed safe to reuse as-is.
- `ManufactureDifficultyConfiguration.Assign(...)` (`ManufactureDifficultyConfiguration.cs:19-23`) mutates the instance it's called on — safe to call on a **clone**, unsafe to call on the shared instance, exactly as the brief states.
- `RefreshManufactureCostData` is registered nowhere in `CatalogModule.RegisterBackgroundRefreshTasks` (`CatalogModule.cs:162-282`) and has no other callers under `backend/src` — confirmed dead code, so this fix carries zero runtime behavior risk for that method today, only latent risk if it's revived later.
- `RefreshManufactureDifficultySettingsData(null, ct)` (the "All" branch) is registered at `CatalogModule.cs:247-250` and already goes through `SetManufactureDifficultySettingsData` correctly — untouched by this fix.
- Three handlers call the single-product branch directly: `CreateManufactureDifficultyHandler.cs:64`, `UpdateManufactureDifficultyHandler.cs:84`, `DeleteManufactureDifficultyHandler.cs:42` — all just `await` the call and don't inspect cache state afterward, so the return type/signature can stay `Task`.

## Proposed Architecture

### Component Overview
No new components. Change is entirely inside `CatalogDataRefreshService` (one class, two methods), consuming existing `CatalogCacheStore` and `CatalogAggregate`/`ManufactureDifficultyConfiguration` APIs unchanged.

```
CreateManufactureDifficultyHandler ─┐
UpdateManufactureDifficultyHandler ─┼─▶ CatalogRepository.RefreshManufactureDifficultySettingsData(product, ct)
DeleteManufactureDifficultyHandler ─┘        │
                                              ▼
                          CatalogDataRefreshService.RefreshManufactureDifficultySettingsData
                                              │
                        ┌─────────────────────┴─────────────────────┐
                        ▼                                           ▼
        build NEW dictionary (copy + replace key)      build NEW List<CatalogAggregate>
        SetManufactureDifficultySettingsData(...)       (clone target product, pass through
                        │                                the rest by reference)
                        ▼                                           ▼
              CatalogCacheStore (dictionary            CatalogCacheStore.ReplaceCacheAtomicallyAsync
              cache key, InvalidateSourceData,          (current → stale swap, same as Merge())
              SetLoadDateInCache)
```

### Key Design Decisions

#### Decision 1: Route the dictionary write through `SetManufactureDifficultySettingsData` instead of hand-rolling cache invalidation
**Options considered:**
- (a) Keep reading `GetManufactureDifficultySettingsData()` but shallow-copy the dictionary before mutating the copy, then call `SetManufactureDifficultySettingsData(copy)`.
- (b) Re-fetch the full per-product list from `_manufactureDifficultyRepository` for *all* products and call the existing "All" branch logic.

**Chosen approach:** (a). Copy-then-set.

**Rationale:** (b) would change behavior (an extra full repository fetch on every single-product edit, more expensive and out of scope) and risks a race against concurrent single-product edits for other products landing between the fetch and the set. (a) is the minimal, behavior-preserving fix: it produces the exact same dictionary contents as today, just via a new dictionary instance passed through the existing setter, so `InvalidateSourceData`/`SetLoadDateInCache` run for free and for the same reasons they run on every other `Set*Data` path. This mirrors how `Merge()` never mutates `_cacheStore`'s returned collections in place either.

#### Decision 2: Update the live snapshot by building a new list with one cloned element, not by cloning the whole list
**Options considered:**
- (a) `products.Select(p => p.ProductCode == product ? p.Clone()-with-new-settings : p).ToList()` — clone only the touched product, pass every other reference through unchanged into the new list.
- (b) Clone every product in the snapshot (mirroring `Merge()`'s `catalogData.Select(p => p.Clone()).ToList()` exactly), then mutate the matching clone.

**Chosen approach:** (a).

**Rationale:** The isolation contract `CatalogCacheStore` needs is "no one mutates an object a reader might already hold a reference to." Untouched products are never mutated — only the container list is new — so passing them through by reference is safe and avoids an O(n) deep clone of the entire catalog for a single-product edit, which would be a needless cost this specific low-latency, handler-triggered path (called synchronously from create/update/delete manufacture-difficulty handlers, not from the background merge loop) shouldn't pay. `RefreshManufactureCostData` (Decision 3) is a full-catalog operation already, so there (b)'s shape — clone everything, matching `Merge()` literally — is the right call for consistency and because every product is being touched anyway.

Both methods must guard the "no current snapshot" case (`TryGetCurrent() == null`) by skipping the snapshot-update step entirely — there's nothing to replace, and the next background merge will pick up the invalidated dictionary via `InvalidateSourceData`'s `ScheduleMerge` call.

#### Decision 3: `RefreshManufactureCostData` clones every product before mutating, same shape as `Merge()`
**Options considered:** As above — clone-all vs. clone-touched-only.
**Chosen approach:** Clone every product that has a `manufactureMap` entry (the ones actually mutated); products with no entry can pass through unchanged.
**Rationale:** This mirrors `catalogData.Select(p => p.Clone()).ToList()` in `Merge()` exactly (same file, same intent, same author expectation), which keeps the two "full catalog rewrite" code paths in this file visually and structurally consistent for the next reader. Since the method is dead code today, there's no measured performance profile to protect — correctness-by-construction (matching the already-reviewed `Merge()` pattern) outweighs the marginal clone cost of skipping untouched products.

## Implementation Guidance

### Directory / Module Structure
No new files. Both changes live entirely in:
`backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs`

No changes needed to `CatalogCacheStore.cs`, `CatalogMergeService.cs`, `CatalogAggregate.cs`, or `ManufactureDifficultyConfiguration.cs` — all the primitives needed (`Clone()`, `Assign()`, `SetManufactureDifficultySettingsData`, `ReplaceCacheAtomicallyAsync`, `TryGetCurrent`) already exist and are reused as-is.

### Interfaces and Contracts
- `ICatalogRepository.RefreshManufactureDifficultySettingsData(string?, CancellationToken)` — signature unchanged.
- `CatalogDataRefreshService.RefreshManufactureCostData(CancellationToken)` — signature unchanged (still not wired into `CatalogModule`; wiring it up remains explicitly out of scope per the spec).
- No new public members on `CatalogCacheStore`.

### Data Flow
**`RefreshManufactureDifficultySettingsData(product, ct)` — single-product branch:**
1. Fetch `difficultySettings` from `_manufactureDifficultyRepository.ListAsync(product, ...)` (unchanged).
2. Read `_cacheStore.GetManufactureDifficultySettingsData()`, build a **new** `Dictionary<string, List<ManufactureDifficultySetting>>` copying all existing entries and overwriting the `product` key with the new list.
3. Call `_cacheStore.SetManufactureDifficultySettingsData(newDict)`.
4. Read `_cacheStore.TryGetCurrent()`. If non-null and it contains a product matching `product`, build a new `List<CatalogAggregate>` where that one entry is `original.Clone()` with `ManufactureDifficultySettings.Assign(difficultySettings, utcNow)` called on the **clone**, and every other entry is passed through unchanged. Call `_cacheStore.ReplaceCacheAtomicallyAsync(newList)`.
5. If `TryGetCurrent()` is null, or no product matches, skip step 4 — the dictionary update alone (step 3) is sufficient; the next merge (scheduled by `InvalidateSourceData`) will pick it up.

**`RefreshManufactureCostData(ct)`:**
1. Build `manufactureMap` from `_cacheStore.GetManufactureHistoryData()` (unchanged).
2. Read `_cacheStore.GetCatalogData()` (unchanged, still the live reference — but it is no longer mutated).
3. Build a new `List<CatalogAggregate>`: for each product, if `manufactureMap` has an entry for it, add `product.Clone()` with `ManufactureHistory` set on the clone; otherwise add `product` unchanged.
4. Call `_cacheStore.ReplaceCacheAtomicallyAsync(newList)` instead of returning void with in-place mutation.
5. Remove the now-inaccurate `// Pre-existing behavior: mutates live aggregate in-place...` comment.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Handlers awaiting `RefreshManufactureDifficultySettingsData` expect the cache to be updated synchronously before they return (e.g. so a subsequent read in the same request sees fresh data) | Medium | The fix keeps the update synchronous within the same method call — it swaps in a new dictionary/list before the method returns, it just no longer mutates the *old* objects. Any caller reading `_cacheStore` after `await`ing this call still observes the update immediately. |
| `TryGetCurrent()` returns null mid-flight (e.g. cache eviction) between the dictionary update and the snapshot update, silently skipping the aggregate update | Low | This is pre-existing behavior (the code already calls `TryGetCurrent()` and no-ops via `SingleOrDefault` returning null); the fix preserves it. Not a regression — out of scope to harden further per the spec. |
| `RefreshManufactureCostData`'s new clone-all-touched-products cost, if the method is later wired up, is more expensive than the current in-place mutation | Low | Acceptable and expected — it's the same cost `Merge()` already pays every cycle; correctness must win over micro-optimizing dead code. |
| Test coverage regresses because existing test `RefreshManufactureDifficultySettingsData_SingleProduct_UpdatesLiveAggregate` (`CatalogDataRefreshServiceTests.cs:93-127`) asserts on `_cacheStore.TryGetCurrent()!.First().ProductCode` only, which still passes after the fix, but does not currently assert reference-identity isolation | Low | Add new tests asserting: (1) the pre-call `CatalogAggregate` reference is untouched after the call, (2) the pre-call dictionary reference is untouched after the call, (3) a new snapshot reflects the update. The existing test's name should probably change since "UpdatesLiveAggregate" describes the bug being fixed — rename to reflect the new "does not mutate live aggregate" behavior. |

## Specification Amendments
None — the spec's FR-1/FR-2 acceptance criteria already match this design. One clarification: FR-1's "Products other than the matching one may be passed through by reference" is confirmed safe and intentional per Decision 2 above, not an oversight.

## Prerequisites
None. No migrations, config, or infrastructure changes required. Pure in-place code fix behind an already-deployed, already-tested `CatalogCacheStore`/`CatalogAggregate` API surface.
