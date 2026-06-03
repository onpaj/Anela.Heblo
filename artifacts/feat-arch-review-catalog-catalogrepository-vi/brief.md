## Module
Catalog

## Finding
`Application/Features/Catalog/CatalogRepository.cs` is a 962-line class with **25 constructor parameters** that combines at least five distinct responsibilities:

1. **Cache management** — stale/fresh dual-key cache, semaphore-based atomic replacement (`ReplaceCacheAtomicallyAsync`, `IsCacheValid`, `CatalogData` property, lines 270–560)
2. **Merge orchestration** — fan-out refresh across 15+ data sources, `Merge()` at lines 337–487, `ExecuteBackgroundMergeAsync`, `ExecutePriorityMergeAsync`
3. **Per-source data refresh** — 16 `Refresh*Data()` methods (e.g. `RefreshSalesData`, `RefreshErpStockData`, `RefreshLotsData`, …) that are individually called by a background scheduler
4. **Cross-module data fetching** — `GetProductsInTransport`, `GetProductsInReserve`, `GetProductsInQuarantine`, `GetProductsOrdered`, `GetProductsPlanned` (lines 892–924)
5. **Query interface** — `GetAllAsync`, `GetByIdAsync`, `FindAsync`, `SingleOrDefaultAsync`, `AnyAsync`, `CountAsync`, `GetProductsWithSalesInPeriod`

Additionally, there is an `[Obsolete]` cached property (`CachedManufactureCostData`, lines 770–780) that is never written to and never read — dead code left in the class.

## Why it matters
- A single class with 25 injected services is extremely hard to unit-test in isolation; test setup for any one method requires mocking 20+ unrelated collaborators.
- The merge logic and cache management are tightly coupled; changing cache invalidation strategy forces reading through data-refresh methods and vice versa.
- The file already exceeds the 800-line guideline by 20%.
- Adding a new data source (a common operation) means editing this already-crowded class rather than adding a focused adapter.

## Suggested fix
Split into at least three focused types:

1. **`CatalogCacheStore`** — owns the IMemoryCache keys, stale/fresh promotion, `ReplaceCacheAtomicallyAsync`, `IsCacheValid`. Exposes a typed read/write API for each data source.
2. **`CatalogMergeService`** — owns the `Merge()` method and calls `CatalogCacheStore` for reads and writes. Receives the individual source collections as method arguments.
3. **`CatalogRepository`** — thin: holds the 5 read-only query methods (`GetAllAsync`, etc.) and delegates cache reads to `CatalogCacheStore`.

The per-source `Refresh*Data()` methods can stay as a `CatalogDataRefreshService` that is called by the background scheduler.

Remove `CachedManufactureCostData` (it is obsolete and unused).

This is best done after or alongside #2058, which reduces the constructor parameter count by extracting cross-module adapters.

---
_Filed by daily arch-review routine on 2026-05-29._