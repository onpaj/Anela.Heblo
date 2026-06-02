Plan written and saved to `docs/superpowers/plans/2026-06-02-decompose-catalog-repository.md`.

The plan decomposes the `CatalogRepository` refactor into 10 tasks:

1. **Pre-flight** — verify baseline build/tests pass and confirm cleanup targets exist where expected.
2. **`CatalogCacheStore`** — extracted cache layer with 5 unit tests covering the dual-key promotion, stale fallback, and `EnableBackgroundMerge=false` eviction branch.
3. **`CatalogMergeService` + `CatalogMergeCallbackWiring`** — extracted merge logic plus the `IHostedService` that wires the scheduler callback once at startup (replacing the leaky constructor wiring).
4. **`CatalogDataRefreshService`** — 19 refresh methods + cross-module stock helpers; 3 tests including stale-cache-on-throw and single-product difficulty refresh.
5. **Slim `CatalogRepository`** — full rewrite to a delegating facade under 250 LOC.
6. **Interface trim** — remove `ManufactureCostLoadDate` from `ICatalogRepository` and `MockCatalogRepository`.
7. **DI wiring** — register the three new types + hosted service; refresh task IDs explicitly preserved.
8. **Wiring integration test** — full `Host.StartAsync()` test proving end-to-end refresh→merge after callback wiring.
9. **Migrate existing tests** — `CatalogRepositoryTests`, `CatalogRepositoryCacheOptimizationTests`, `CatalogRepositoryDebugTest` to the new 4-arg constructor; also runs invariant greps (no `IMemoryCache` outside the store, no `SetMergeCallback` in the repo, 19 refresh-task registrations).
10. **Final validation** — solution build, format check, file-size budgets.

Every code step contains full code blocks; every assertion has an expected output; cache-key strings are pinned to literals to preserve continuity. All arch-review amendments (drop `IManufactureClient`, remove `_sourceLastUpdated`, sealed classes, preserved `Task.Run` wrapper, callback wiring test) are incorporated.