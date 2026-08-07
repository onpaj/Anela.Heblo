# Specification: Fix cache isolation violations in CatalogDataRefreshService

## Summary
`CatalogCacheStore` guarantees readers only ever observe a fully-merged, atomically-swapped `CatalogAggregate` snapshot (`ReplaceCacheAtomicallyAsync`). Issue #3827 fixed one violation of that guarantee in `CatalogMergeService.Merge()` by cloning before mutating. Two sibling methods in `CatalogDataRefreshService` — `RefreshManufactureDifficultySettingsData` (single-product branch) and `RefreshManufactureCostData` — still mutate live cached objects directly, bypassing the clone-before-mutate discipline and the `Set*Data`/`InvalidateSourceData`/`SetLoadDateInCache` plumbing every other refresh method goes through. This feature closes both violations using the same pattern already established for `Merge()`.

## Background
`CatalogCacheStore` stores per-source raw data (dictionaries/lists behind `Get*Data`/`Set*Data` accessors) plus a merged `List<CatalogAggregate>` behind a current/stale double-buffer (`ReplaceCacheAtomicallyAsync`). Concurrent readers call `GetCatalogData()`/`TryGetCurrent()` and receive a reference to whatever list is currently installed — there is no defensive copy on read. The contract that makes this safe is: nothing mutates the list or its elements in place after it has been installed; a full new list is built by `CatalogMergeService.Merge()` (which clones each aggregate before touching it, since #3827) and swapped in atomically.

Two methods in `CatalogDataRefreshService.cs` violate that contract:

1. **`RefreshManufactureDifficultySettingsData(string? product, ct)`, single-product branch (lines 208-219)** — reads the live cached dictionary via `_cacheStore.GetManufactureDifficultySettingsData()` (a plain `IMemoryCache.Get`, not a copy) and mutates it in place with `existingDict[product] = ...`, bypassing `SetManufactureDifficultySettingsData` (and therefore `InvalidateSourceData`/`SetLoadDateInCache`). It then reads `_cacheStore.TryGetCurrent()` and calls `productAggregate.ManufactureDifficultySettings.Assign(...)` directly on the live, shared `CatalogAggregate` that concurrent readers may be holding a reference to. This is reachable in production via `CreateManufactureDifficultyHandler`, `UpdateManufactureDifficultyHandler`, and `DeleteManufactureDifficultyHandler`.
2. **`RefreshManufactureCostData(ct)` (lines 231-247)** — reads the live catalog list via `_cacheStore.GetCatalogData()` and sets `product.ManufactureHistory = manufactures.ToList()` directly on each shared aggregate. Currently dead code (not registered as a refresh task, not called anywhere in `backend/src`), but a live landmine: reviving it, or copying its shape for a new task, silently reproduces #3827.

## Functional Requirements

### FR-1: `RefreshManufactureDifficultySettingsData` single-product path must not mutate shared state in place
When called with a non-null `product`, the method must not write into the dictionary returned by `GetManufactureDifficultySettingsData()` and must not call `.Assign(...)` on an aggregate obtained from `TryGetCurrent()`/`GetCatalogData()`.

Instead it must:
- Build a new dictionary (copy of the existing one with the single key replaced) and install it via `SetManufactureDifficultySettingsData(...)` so `InvalidateSourceData`/`SetLoadDateInCache` run exactly as they do for the "all products" branch and every other `Set*Data` call.
- Update the live "current" snapshot, if one exists, by producing a new `List<CatalogAggregate>` where the matching product is a **clone** with its `ManufactureDifficultySettings` reassigned (not the original mutated in place), and install that list via `ReplaceCacheAtomicallyAsync`. Products other than the matching one may be passed through by reference into the new list — they are not mutated, only the list container changes, so this is safe (the mutation is confined to a cloned copy of the one product being updated). If no current snapshot exists, skip this step (nothing to update).

**Acceptance criteria:**
- After calling this method with a product code, `_cacheStore.GetManufactureDifficultySettingsData()` returns the updated dictionary and no other holder of a reference to the pre-call dictionary sees the change (dictionary identity changed).
- After calling this method with a product code, the `CatalogAggregate` object reference returned by a `TryGetCurrent()` snapshot taken *before* the call is unchanged (its `ManufactureDifficultySettings` are untouched) — i.e. old references are immutable after the fact, matching `Merge()`'s clone-before-mutate contract.
- A `TryGetCurrent()` snapshot taken *after* the call reflects the new difficulty settings for the target product.
- `GetLoadDateFromCache(CachedManufactureDifficultySettingsDataKey)` is updated by the call (proves `Set*Data` plumbing ran).
- Behavior for `product == null` (the "All" branch) is unchanged.
- If there is no current snapshot in cache (`TryGetCurrent()` is `null`), the method still updates the dictionary and does not throw.
- If the product is not present in the current snapshot, the method still updates the dictionary and does not throw.

### FR-2: `RefreshManufactureCostData` must not mutate shared state in place
The method must stop writing `product.ManufactureHistory = ...` directly onto aggregates obtained from `GetCatalogData()`. It must instead build a new `List<CatalogAggregate>` of clones with `ManufactureHistory` set on the clones, and install the result via `ReplaceCacheAtomicallyAsync`, mirroring the pattern used for FR-1 and `Merge()`.

**Acceptance criteria:**
- After calling this method, a `TryGetCurrent()`/`GetCatalogData()` snapshot taken *before* the call is unchanged (its elements' `ManufactureHistory` are untouched).
- A snapshot taken *after* the call reflects the new `ManufactureHistory` per product, matching the pre-existing grouping/lookup logic (unchanged).
- The now-stale in-code comment ("Pre-existing behavior: mutates live aggregate in-place...") is removed since it no longer describes the code.
- No behavioral change to the method's data-source grouping logic — only the mutation path changes.

## Non-Functional Requirements

### NFR-1: Performance
Both fixes replace an O(1)/O(n) direct mutation with an O(n) clone of the current snapshot (n = product count) followed by an atomic swap. This is the same cost `Merge()` already pays on every refresh cycle and is not expected to be a bottleneck; no specific latency budget is introduced beyond "no regression relative to a full merge pass."

### NFR-2: Thread safety
Both methods must be safe under the same concurrency model `CatalogCacheStore` already assumes: multiple readers may call `GetCatalogData()`/`TryGetCurrent()` concurrently with a refresh in progress. After the fix, a reader either sees the pre-refresh snapshot in full or the post-refresh snapshot in full — never a partially-updated aggregate.

## Data Model
No schema changes. Existing types used: `CatalogAggregate` (has `Clone()`), `ManufactureDifficultyConfiguration` (has `Clone()`/`Assign(...)`), `Dictionary<string, List<ManufactureDifficultySetting>>`, `IDictionary<string, List<ManufactureDifficultySetting>>` from `GetManufactureDifficultySettingsData()`.

## API / Interface Design
No public interface changes. `ICatalogRepository.RefreshManufactureDifficultySettingsData(string?, CancellationToken)` signature is unchanged; only the internal implementation in `CatalogDataRefreshService` changes. `CatalogCacheStore`'s public surface (`Set*Data`, `ReplaceCacheAtomicallyAsync`, `TryGetCurrent`, `GetCatalogData`) is unchanged — the fix is a consumer of existing methods, not a change to them.

## Dependencies
- `CatalogAggregate.Clone()` (existing, used by `CatalogMergeService.Merge()`).
- `ManufactureDifficultyConfiguration.Clone()` / `.Assign(...)` (existing).
- `CatalogCacheStore.SetManufactureDifficultySettingsData`, `.TryGetCurrent`, `.ReplaceCacheAtomicallyAsync`, `.GetCatalogData` (existing, no changes needed).

## Out of Scope
- Any change to `CatalogCacheStore`, `CatalogMergeService`, or the atomic-swap mechanism itself.
- Wiring `RefreshManufactureCostData` into `CatalogModule`'s registered refresh tasks (it stays dead code; only its internal safety is fixed so that reviving it later is safe by default).
- Broader concurrency audit of the rest of `CatalogDataRefreshService` (all other `Refresh*` methods already go through `Set*Data` correctly, per the brief's evidence).
- Changing `ManufactureDifficultyConfiguration.Assign`'s semantics.

## Open Questions
None.

## Status: COMPLETE
