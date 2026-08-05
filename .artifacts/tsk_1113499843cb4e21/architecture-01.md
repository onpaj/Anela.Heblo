# Architecture review: `CatalogAggregate.Clone()` deep-copy-then-overlay design

## Verdict

**Approved, no changes required.** I independently re-derived every mutation call
site the design relies on by reading the actual source (`CatalogAggregate.cs`,
`StockData.cs`, `CatalogProperties.cs`, `ManufactureDifficultyConfiguration.cs`,
`CatalogMergeService.cs`, `CatalogCacheStore.cs`, `CatalogRepository.cs`,
`CatalogModule.cs`, `CatalogDataRefreshService.cs`, the two stock-taking handlers,
and `FlatManufactureCostProvider.cs`) rather than trusting the design doc's grep
claims at face value. Every classification in the design's field-by-field table
checks out. No invariant of this codebase is violated.

## What I verified against the codebase

**`CatalogAggregate` shape matches the design's assumptions exactly.**
`Stock`/`Properties`/`ManufactureDifficultySettings`/`StockTakingHistory`/
`SaleHistorySummary`/`ConsumedHistorySummary`/`PurchaseHistorySummary` are all
plain `{ get; set; }` properties (not `init`-only), so `clone.X = ...` assignment
inside an instance method compiles. `StockData` and `CatalogProperties` are
records with settable properties, so `with { }` / `with { Lots = ... }` is valid
C#. `ManufactureDifficultyConfiguration` is a class with `private set` on both
members — legal to set via object-initializer from a `Clone()` method that is
itself a member of the type. `Entity<T>` (the base class) is trivial (`Id` only)
— `MemberwiseClone()` handles it with no special case needed.

**Every field the design puts in the "`MemberwiseClone()` is safe" bucket is,
in production code, only ever wholesale-reassigned, never element-mutated.**
Grepped for `.EshopPrice.X =`, `.ErpPrice.X =`, `.Margins.X =`,
`SalesHistory.Add/Clear/Remove/AddRange`, and the `ConsumedHistory`/
`PurchaseHistory`/`ManufactureHistory` equivalents across `src/` — zero hits.
Confirms the design's central safety argument: reference-sharing these fields
between generations is safe *because* mutation always goes through the setter
(replacing the reference on one instance only), never through in-place element
mutation on a shared object.

**Every field the design puts in the "needs an explicit copy" bucket has a real,
reachable in-place mutation path, not a hypothetical one:**
- `Stock.Lots` / `StockTakingHistory`: `CatalogAggregate.SyncStockTaking(record)`
  (`CatalogAggregate.cs:321`, `StockTakingHistory.Add(...)`) is called from
  **production code**, not just the unused two-arg overload — both
  `SubmitStockTakingHandler.cs:64` and `SubmitManufactureStockTakingHandler.cs:85`
  call the one-arg `SyncStockTaking`, obtaining `product` via
  `ICatalogRepository.GetByIdAsync` / `IManufactureCatalogSource.GetByIdAsync`,
  i.e. the live cached instance. This is a genuine, currently-reachable hazard,
  not defensive over-engineering — cloning `StockTakingHistory` is necessary now,
  independent of the `Merge()` fix. (The two-arg `SyncStockTaking(record, lots)`
  overload that does `Stock.Lots.Clear()/AddRange()` is defined but never called
  in `src/` — only in tests — so that specific path is currently dormant, but the
  design's `Lots.ToList()` copy is cheap insurance for a public method that could
  be called tomorrow, and it's already required for `StockTakingHistory` via the
  same object graph.)
- `SaleHistorySummary`/`ConsumedHistorySummary`/`PurchaseHistorySummary`: I traced
  the actual call chain — `MergeSalesHistory` sets `product.SalesHistory = ...`,
  which invokes the `SalesHistory` property setter (`CatalogAggregate.cs:94-102`),
  which calls `UpdateSaleHistorySummary()`, which does
  `SaleHistorySummary.MonthlyData = monthlyData; SaleHistorySummary.LastUpdated = ...`
  — **in-place property mutation on whatever `SaleHistorySummary` object the
  aggregate currently references.** If `Clone()` reference-shared this object
  (as plain `MemberwiseClone()` would), every merge pass that touches a
  product's sales/consumed/purchase history would silently corrupt the `Stale`
  generation's summary object out from under any concurrent reader — this is
  the exact bug class the whole task is about, just on a derived field the
  original issue report didn't name. The design's identification of this is
  correct and is the most valuable finding in the design step; FR-3 in the plan
  did not cover it.

**No call site outside `Merge()`/`SyncStockTaking()` mutates `Stock` or
`Properties` fields in place.** Grepped `\.Stock\.[A-Za-z]+ =` and
`\.Properties\.[A-Za-z]+ =` across all of `src/` — every hit is inside
`CatalogMergeService.cs`. So a plain record `with` copy (no manual field-by-field
handling beyond `Lots`) is sufficient for `Stock`/`Properties` — matches the
design.

**`CatalogRepository`/`CatalogCacheStore` need zero changes**, confirmed by
reading both in full: `GetCatalogData()`, `TryGetCurrent()`, `TryGetStale()`,
`ReplaceCacheAtomicallyAsync()`, `IsCacheValid()`, and the semaphore are all
already written as if `Merge()` handed them isolated snapshots — the fix makes
that true rather than changing any contract. `GetCatalogDataAsync`'s
`AllowStaleDataDuringMerge` branch (`CatalogRepository.cs:43-51`) will now
correctly serve genuinely-frozen stale data once `Clone()` lands.

**No aggregate-identity assumption breaks.** `CatalogAggregate` has no
`Equals`/`GetHashCode` override, so any reference-keyed collections rely on
default reference equality. Found one: `FlatManufactureCostProvider
.CalculateWeightedManufactureTotals` builds a
`Dictionary<CatalogAggregate, ManufactureSummary>` — but it's built and consumed
within a single method call against a single `products` list, never persisted
across a merge boundary, so new instance identity per merge pass doesn't affect
it.

**Out-of-scope call sites the design flags are real and correctly scoped.**
Confirmed `CatalogDataRefreshService.RefreshManufactureCostData` (comment at
line ~243 admits "mutates live aggregate in-place"), the single-product branch
of `RefreshManufactureDifficultySettingsData` (`CatalogDataRefreshService.cs`
~207-220, calls `productAggregate.ManufactureDifficultySettings.Assign(...)`
directly on `TryGetCurrent()`'s instance), and `CatalogModule.cs`'s
`RefreshMarginData` task (`product.Margins = await marginService...`) all still
mutate whatever instance the repository/cache currently hands them, unchanged by
this fix. Since `Margins`/`ManufactureHistory` are wholesale-reassigned properties
(not in-place-mutated objects), these sites don't reproduce the "torn read"
failure mode `Clone()` fixes — they're a separate, correctly-deferred hazard
class, exactly as the design says.

## Points worth carrying into implementation (not design defects)

1. **`Clone()`'s placement and visibility**: `public CatalogAggregate Clone()`
   on the domain type is consistent with the codebase — no existing
   `IDeepCloneable`-style interface or convention to match, and no naming
   collision (grepped `Clone()` usage repo-wide; the only other hits are
   unrelated `JsonElement.Clone()` calls). No architectural reason to make it
   `internal` — nothing prevents other callers from also using it correctly if
   the follow-up ticket for the out-of-scope call sites eventually adopts it.
2. **`ManufactureDifficultyConfiguration.Clone()`'s object-initializer
   assignment to `private set` properties** compiles only because `Clone()` is
   declared on the same type — confirmed, no accessibility problem.
3. Implementation should keep `Clone()` and `ManufactureDifficultyConfiguration
   .Clone()` in the domain project (`Anela.Heblo.Domain`), matching where every
   other type referenced (`StockData`, `CatalogProperties`, the summary types)
   already lives — no cross-project boundary issue.
4. No DTO/OpenAPI-client concern: `CatalogAggregate` and its owned types are
   internal domain models, never serialized across the API boundary, so the
   project's "DTOs are classes, never records" rule is inapplicable here and the
   design correctly didn't touch it.

## Summary for implementation

Proceed exactly as designed: add `CatalogAggregate.Clone()` (`MemberwiseClone()`
plus the explicit overrides for `Stock`, `Properties`,
`ManufactureDifficultySettings`, `StockTakingHistory`, and the three history
summary objects) and `ManufactureDifficultyConfiguration.Clone()`, then change
`CatalogMergeService.cs:82` from `products = catalogData;` to
`products = catalogData.Select(p => p.Clone()).ToList();`. No other production
code needs to change. Add the tests the design already specified (instance
non-identity after a second merge pass, `Stale` isolation post-swap,
copy-forward-on-source-miss for FR-2, and non-identity of the three summary
objects after a pass that updates them).
