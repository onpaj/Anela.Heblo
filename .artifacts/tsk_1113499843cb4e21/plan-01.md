# Plan: CatalogMergeService must not mutate live cached aggregates in place

## Summary
`CatalogMergeService.Merge()` merges new source data directly into the `CatalogAggregate` instances currently held by `CatalogCacheStore`'s `Current` cache entry, instead of producing independent objects. This silently defeats the atomic-swap (`SemaphoreSlim`-guarded `ReplaceCacheAtomicallyAsync`) and stale-fallback (`Current`/`Stale` split, `AllowStaleDataDuringMerge`) isolation the cache was built to provide: readers can observe a `CatalogAggregate` with some fields already overwritten by an in-flight merge and others not yet touched. The fix is to make the merge build/populate fresh aggregate instances so `Current` and `Stale` are ever genuinely independent snapshots.

## Context
`CatalogCacheStore`/`CatalogRepository` are the highest fan-in part of the app (consumed by #2–#6, #13–#19, #32 per the issue). Costing/margin providers and other consumers read `product.Stock`, `product.EshopPrice`, `product.PurchaseHistory`, etc. straight off these cached aggregates. A query landing while a background merge loop (`Merge()`, `CatalogMergeService.cs:119-131`) is partway through a product can see internally-inconsistent data (e.g., new ERP stock already applied, prices not yet). This is a correctness bug in a hot, widely-depended-on path, not a style issue — worth fixing directly rather than just documenting.

## Functional requirements

**FR-1 — `Merge()` must never mutate instances reachable from the live `Current` or `Stale` cache entries.**
- AC: While a background merge (`ExecuteBackgroundMergeAsync` / `ExecutePriorityMergeAsync`) is running, a concurrent call to any `CatalogRepository` read method (`GetAllAsync`, `GetByIdAsync`, `FindAsync`, etc.) returns aggregates whose fields are either all-pre-merge or all-post-merge for a given product — never a mix.
- AC: After `ReplaceCacheAtomicallyAsync` promotes `Current` → `Stale`, the objects referenced by `Stale` are not the same instances subsequently touched by any further merge pass (no shared references between `Stale`'s elements and the next merge's working set).
- AC: A unit test can capture a reference to a `CatalogAggregate` from `TryGetCurrent()` before calling `Merge()`/`ExecutePriorityMergeAsync()` again, and assert that instance's fields are unchanged after the second merge completes.

**FR-2 — Merge output correctness must be unchanged for the single-threaded/no-contention case.**
- AC: All existing `CatalogMergeServiceTests` continue to pass unmodified in behavior (only instance identity changes, not field values).
- AC: For a product present in the prior `Current` snapshot but absent from a given source map in the new merge pass (e.g., temporarily missing from `GetErpStockData()`), the previously-merged field values are preserved on the new instance exactly as they were preserved on the mutated instance today (i.e., "copy forward, then overlay found sources" semantics — not "reset to type defaults, then overlay").
- AC: The bootstrap path (`!catalogData.Any()` → seed from ERP stock, `CatalogMergeService.cs:74-78`) is unaffected — it already creates fresh instances.

**FR-3 — Nested mutable members of `CatalogAggregate` must not be shared between the pre-merge and post-merge instance.**
- AC: `StockData` (`Stock`), `CatalogProperties` (`Properties`), and `ManufactureDifficultyConfiguration` (`ManufactureDifficultySettings`) — the members mutated in place today via property setters or `Assign()` — are independent objects (not shared references) between the old cached instance and the newly merged instance. `Stock.Lots` (a `List<CatalogLot>`) is copied, not reference-shared.
- AC: Members that are already replaced wholesale by reference (`EshopPrice`, `ErpPrice`, `SalesHistory`, `ConsumedHistory`, `PurchaseHistory`, `ManufactureHistory`, `StockTakingHistory`) don't need deep-copying themselves — only the containing `CatalogAggregate`/`Stock`/`Properties`/`ManufactureDifficultySettings` must not be the same object as before.

**FR-4 — No regression to `ChangesPendingForMerge` / load-date tracking / `LastMergeDateTime` semantics.**
- AC: `SetLastMergeDateTime()` still records a merge timestamp on every `Merge()` call, in the same place in the flow.

## Non-functional requirements
- **Performance**: catalog merges run over the full product set (background job, not per-request). The chosen approach (see Rough plan) must not turn an O(n) mutation loop into something asymptotically worse (e.g., no per-product deep clone via reflection/serialization). A field-by-field copy or "always construct from source" approach is expected to stay O(n) with a similar constant factor to today.
- **Memory**: this doubles peak live `CatalogAggregate` allocations during a merge pass (old + new instance coexist until the atomic swap and `Stale` expiry). Given `AllowStaleDataDuringMerge` already keeps two full generations alive by design, this is an acceptable, expected consequence of the fix, not a new risk class — but worth a sanity check against product catalog size in review.
- **No behavior change for callers**: `ICatalogRepository`'s public contract and `CatalogRepository`'s read/refresh methods are unaffected; this is purely an internals fix inside `CatalogMergeService`/`CatalogCacheStore`.

## Data model
No schema/entity changes. Affected types, all existing:
- `CatalogAggregate` (`backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs`) — the shared-instance problem.
- `StockData` (record, `.../Stock/StockData.cs`) — mutated in place via `product.Stock.Erp = ...` etc.; contains `List<CatalogLot> Lots`.
- `CatalogProperties` (record, `.../CatalogProperties.cs`) — mutated in place via `product.Properties.OptimalStockDaysSetup = ...` etc.
- `ManufactureDifficultyConfiguration` (class, `.../ManufactureDifficultyConfiguration.cs`) — mutated via `Assign()`, which itself reassigns its internal `Settings` list and `ManufactureDifficulty` value on the existing instance.
- `CatalogCacheStore`'s `Current`/`Stale` `IMemoryCache` entries (`List<CatalogAggregate>`) — the two "snapshots" that must stop sharing element instances.

## Interfaces
No public API, HTTP, or UI surface changes. Internal-only:
- `CatalogMergeService.Merge()` (internal method) — the seam where the new-instance vs. mutate-in-place decision is made.
- `CatalogCacheStore.GetCatalogData()` / `ReplaceCacheAtomicallyAsync()` — unchanged contracts; `Merge()` must stop treating `GetCatalogData()`'s return value as an in-place working set.

## Dependencies and scope

**In scope**
- `CatalogMergeService.Merge()` and its private `Merge*` helpers — the mutation loop identified in the issue.
- Whatever minimal change to `CatalogAggregate`/`StockData`/`CatalogProperties`/`ManufactureDifficultyConfiguration` is needed to produce an independent copy (e.g., a copy constructor / `Clone()` / rebuild-from-scratch helper — a design decision for the next step, see Open questions).
- Unit tests in `CatalogMergeServiceTests` / `CatalogCacheStoreTests` proving instance isolation (FR-1) and value-preservation (FR-2).

**Out of scope (related but separate anti-pattern, same root cause, different call sites — flag, don't fix here)**
- `CatalogDataRefreshService.RefreshManufactureCostData` (`CatalogDataRefreshService.cs:238-246`) — explicitly comments that it "mutates live aggregate in-place."
- `CatalogDataRefreshService.RefreshManufactureDifficultySettingsData`'s single-product branch (`CatalogDataRefreshService.cs:207-220`) — mutates `TryGetCurrent()`'s aggregate directly.
- `CatalogAggregate.SyncStockTaking` callers (`SubmitManufactureStockTakingHandler.cs:85`, `SubmitStockTakingHandler.cs:64`) — mutate a live cached aggregate synchronously outside the merge flow.
- These three call sites mutate a single product outside a multi-field, multi-source merge loop, so they don't reproduce the "observe a partially-merged aggregate mid-loop" failure mode this issue describes — but they do share the "cache readers and mutators hold the same instance" anti-pattern. Worth a follow-up ticket once this fix establishes the pattern to copy.
- No change to `AllowStaleDataDuringMerge` trigger conditions, `IsCacheValid()`, or the semaphore itself — those mechanisms are sound; they just weren't being given isolated data to guard.

## Rough plan
1. **Design the copy strategy** (architecture/design step): choose between (a) always rebuild each product's `CatalogAggregate` fresh from source maps every merge pass (drop the "reuse prior instance" branch entirely, keep only the bootstrap-style construction), or (b) explicitly deep-copy the prior instance (new `CatalogAggregate` + new `StockData`/`CatalogProperties`/`ManufactureDifficultyConfiguration`, with lists `.ToList()`'d) before running the existing `Merge*` mutation helpers unchanged against the copy. (b) is closer to the issue's suggested direction and lower-risk for FR-2's "preserve fields not present in this pass's sources" requirement; (a) is simpler but changes what happens when a source map temporarily omits a product. Recommend (b) unless investigation shows every `Merge*` helper is unconditional (no `TryGetValue`-miss-preserves-old-value cases) — audit this before deciding.
2. **Implement the copy/construction path** in `CatalogMergeService.Merge()`, replacing `products = catalogData;` (`CatalogMergeService.cs:82`) with construction of new instances, and add whatever copy helper (`Clone()`, copy constructor, or `with` usage for the record members) is needed on `CatalogAggregate`/`StockData`/`CatalogProperties`/`ManufactureDifficultyConfiguration`.
3. **Verify `ManufactureDifficultyConfiguration.Assign()`** either operates on a fresh instance per merge, or is replaced with a construction path that doesn't mutate a shared object (its `Settings`/`ManufactureDifficulty` are private-set, so cloning needs either a new public copy path or reconstruction via `Assign()` on a fresh instance).
4. **Add regression tests**: (a) capture a `CatalogAggregate` reference before a second merge, assert it is untouched after; (b) assert `TryGetStale()`'s elements after a swap are not reference-equal to the elements produced by the merge that just ran; (c) assert field-preservation semantics (FR-2) for a product missing from one source map on a subsequent pass.
5. **Run full validation**: `dotnet build`, `dotnet format`, and the full `Anela.Heblo.Tests` suite (at minimum `CatalogMergeServiceTests`, `CatalogCacheStoreTests`, and any costing/margin provider tests that read `CatalogAggregate` fields), per this repo's standard pre-completion checklist.
6. **File the follow-up ticket** for the out-of-scope call sites (`RefreshManufactureCostData`, single-product `RefreshManufactureDifficultySettingsData`, `SyncStockTaking` callers) noting they share the same instance-aliasing anti-pattern, so it isn't lost once this fix lands.

## Open questions
- **Copy vs. rebuild strategy (step 1 above)**: needs an explicit audit of every `Merge*` helper to confirm whether any relies on "leave old value if source map has no entry" behavior for a product that previously existed. Default assumption for this plan: yes, at least implicitly (no helper resets a field to a default when its map lookup misses) — so a deep-copy-then-overlay strategy is the safer default. Architecture step should confirm before implementation.
- **Where the clone helper lives**: whether `Clone()`/copy-construction belongs on `CatalogAggregate` itself (domain-layer method) or in a small mapper inside `CatalogMergeService` (application-layer helper). Default: put it on the domain types (`CatalogAggregate`, `StockData`, `CatalogProperties`, `ManufactureDifficultyConfiguration`) as that's where the fields and invariants live — but this is a judgment call for the architecture step, not fixed here.
- **`ManufactureDifficultyConfiguration.Assign()` mutation**: does fixing this issue require also changing this method's contract (e.g., making it return a new instance instead of mutating), or is "construct a new `ManufactureDifficultyConfiguration` then call `Assign()` on it" (mutating only the fresh, unshared instance) sufficient? Default: the latter is sufficient and less invasive — `Assign()` itself isn't the problem, sharing the object it's called on is.
- **Follow-up ticket for out-of-scope call sites**: not filing it automatically as part of this task; flagging it here per the "Dependencies and scope" section so a human/architect decides whether to open it now or bundle into a later hardening pass.
