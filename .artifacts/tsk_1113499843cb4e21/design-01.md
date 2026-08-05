# Design: independent `CatalogAggregate` instances per merge pass

No UI surface — this is an internal correctness fix inside `CatalogMergeService` /
`CatalogAggregate` and its owned value objects. UX/UI section omitted.

## Decision (resolves plan's open questions)

Use **deep-copy-then-overlay** (plan option (b)), not rebuild-from-scratch. Confirmed
necessary, not just lower-risk: `CatalogModule.cs:298` sets `product.Margins = ...` on
cached aggregates *outside* `Merge()` (a `RefreshMarginData` background task, structurally
the same "mutates live cache in place" anti-pattern as the three call sites the plan
already marked out-of-scope). `Margins` is never touched by any `Merge*` helper. A
rebuild-from-scratch strategy would silently reset every product's `Margins` to `new()`
on every merge pass; deep-copy-then-overlay preserves it automatically because copying
forward is the default and overlay only touches fields a source map actually has.
This generalizes the plan's FR-2 "copy forward, then overlay found sources" requirement:
it must hold for *every* field the merge loop doesn't touch, not just the ones with an
explicit `TryGetValue`-miss case.

**Clone helpers live on the domain types** (`CatalogAggregate`, `ManufactureDifficultyConfiguration`),
per the plan's stated default — that's where the fields and invariants live, and it lets
`CatalogMergeService.Merge()` stay a one-line change.

## Component design

### `CatalogAggregate.Clone()` (new public instance method, domain layer)

```csharp
public CatalogAggregate Clone()
{
    var clone = (CatalogAggregate)MemberwiseClone();

    clone.Stock = Stock with { Lots = Stock.Lots.ToList() };
    clone.Properties = Properties with { };
    clone.ManufactureDifficultySettings = ManufactureDifficultySettings.Clone();
    clone.StockTakingHistory = StockTakingHistory.ToList();
    clone.SaleHistorySummary = new SaleHistorySummary
    {
        MonthlyData = new Dictionary<string, MonthlySalesSummary>(SaleHistorySummary.MonthlyData),
        LastUpdated = SaleHistorySummary.LastUpdated,
    };
    clone.ConsumedHistorySummary = new ConsumedHistorySummary
    {
        MonthlyData = new Dictionary<string, MonthlyConsumedSummary>(ConsumedHistorySummary.MonthlyData),
        LastUpdated = ConsumedHistorySummary.LastUpdated,
    };
    clone.PurchaseHistorySummary = new PurchaseHistorySummary
    {
        MonthlyData = new Dictionary<string, MonthlyPurchaseSummary>(PurchaseHistorySummary.MonthlyData),
        LastUpdated = PurchaseHistorySummary.LastUpdated,
    };

    return clone;
}
```

Rationale for the split between "`MemberwiseClone()` handles it" and "explicit override
needed":

| Field(s) | Treatment | Why |
|---|---|---|
| `ProductCode`/`Id`, `ErpId`, `Type`, scalars (`Volume`, `NetWeight`, …), `EshopPrice`, `ErpPrice`, `Url`, `Margins`, `SalesHistory`/`ConsumedHistory`/`PurchaseHistory`/`ManufactureHistory` (backing fields), `Location`, `SupplierCode`/`Name`, image/dimension fields | `MemberwiseClone()` (reference copy) | Every `Merge*` helper that touches these **wholesale-reassigns** the property (`product.EshopPrice = eshopPrice;`, `product.SalesHistory = sales.ToList();`, …) — confirmed by grep, no code path anywhere calls `.Add`/`.Remove`/element-mutation on these. A wholesale reassignment on the clone can never affect the old instance's reference, so sharing the pre-merge reference is safe until/unless overlaid. |
| `Stock` (`StockData`) | `with { Lots = ... }` (record copy + list copy) | `MergeStockData`/`MergeErpData`/`MergeEshopData` mutate `product.Stock.<field> = ...` **in place** on whatever `StockData` instance `clone.Stock` currently references — a plain record `with` on `CatalogAggregate` isn't in play here since `Stock` itself is a property, so cloning the aggregate must independently clone `StockData` too, or `clone.Stock` would be the *same* `StockData` object as the source aggregate's, and mutating it would corrupt the pre-merge/`Stale` generation. `Lots` needs its own `.ToList()` beyond the record's own member-wise copy because `SyncStockTaking(record, updatedLots)` (out of scope, `CatalogAggregate.cs:357-358`) calls `Stock.Lots.Clear()`/`AddRange()` — an in-place list mutation that would otherwise still be shared across generations after a record-level `with`. |
| `Properties` (`CatalogProperties`) | `with { }` | `MergeAttributes` mutates `product.Properties.<field> = ...` in place — same reasoning as `Stock`. No element inside `Properties` is itself mutated in place (its one collection, `SeasonMonths`, is only ever wholesale-reassigned), so a plain record `with` (no manual list copy) is sufficient. |
| `ManufactureDifficultySettings` | `.Clone()` (new domain method, see below) | `MergeManufactureDifficultySettings` calls `Assign()`, which mutates the object's `Settings`/`ManufactureDifficulty` in place. It's a plain class (not a record), so needs an explicit clone method. |
| `StockTakingHistory` | `.ToList()` | `MergeHistoryData` wholesale-reassigns it (safe to reference-share) **but** `SyncStockTaking()` (out of scope, `CatalogAggregate.cs:321`) calls `StockTakingHistory.Add(...)` — an in-place mutation on whatever list instance the aggregate currently holds. Left un-copied, a `SyncStockTaking` call on one generation would silently corrupt every other generation's history list. Broadens the plan's FR-3 scope (which only named `Stock.Lots`) to the one other list with the same in-place-`Add` hazard, found by grepping for `.Add(`/`.Clear(`/`.AddRange(` call sites against every `CatalogAggregate` collection — see verification notes below. |
| `SaleHistorySummary`/`ConsumedHistorySummary`/`PurchaseHistorySummary` | fresh instance, dictionary copy-constructed | These are **not** reassigned wholesale — `UpdateSaleHistorySummary()` etc. (invoked as a side effect of the `SalesHistory`/`ConsumedHistory`/`PurchaseHistory` setters, which *do* run during a normal merge pass whenever the corresponding source map has a match) mutate `SaleHistorySummary.MonthlyData`/`.LastUpdated` **in place on the existing object**. Left un-copied, any pass where a product's sales/consumed/purchase history changes would mutate the summary object still referenced by the previous generation (`Stale`), reproducing exactly the "reader observes inconsistent aggregate mid-merge" failure mode the issue describes — just on a derived field instead of `Stock`/`EshopPrice`. This was not named in the plan's FR-3 and is the main correctness finding of this design step. |

`MemberwiseClone()` is `protected` on `object`; calling it from `Clone()` (an instance
method declared on `CatalogAggregate` itself) is the standard, allowed pattern — no
reflection, no serialization, O(1) plus the explicit field copies above.

### `ManufactureDifficultyConfiguration.Clone()` (new public instance method, domain layer)

```csharp
public ManufactureDifficultyConfiguration Clone()
    => new()
    {
        Settings = Settings.ToList(),
        ManufactureDifficulty = ManufactureDifficulty,
    };
```

`Settings` and `ManufactureDifficulty` both have `private set`; the object-initializer
assignment is legal because `Clone()` is a member of the same type. No change to
`Assign()`'s contract — it continues to mutate whatever instance it's called on;
`Merge()` now calls it on the clone's fresh `ManufactureDifficultyConfiguration`, not a
cache-shared one. `ManufactureDifficultySetting` list elements are never mutated in
place anywhere (verified by grep) so `Settings.ToList()` (shallow list copy) is
sufficient — matches the treatment `MergeManufactureDifficultySettings` already gives
the incoming `difficultySettings.ToList()`.

### `CatalogMergeService.Merge()` — the only call-site change

```csharp
else
{
    products = catalogData.Select(p => p.Clone()).ToList();
}
```

replaces the current

```csharp
else
{
    products = catalogData;
}
```

(`CatalogMergeService.cs:82`). Every `Merge*` private helper (`MergeErpData`,
`MergeStockData`, `MergeAttributes`, …) is **unchanged** — they still mutate `product`
in place; the fix is entirely in *what instance* `product` refers to. The bootstrap
branch (`!catalogData.Any()`, `CatalogMergeService.cs:74-78`) already constructs fresh
`CatalogAggregate()` instances and is untouched. `SetLastMergeDateTime()` and the final
`return products.ToList();` are untouched — no change to `ChangesPendingForMerge` /
`LastMergeDateTime` semantics (FR-4).

No changes to `CatalogCacheStore` (`GetCatalogData()`, `ReplaceCacheAtomicallyAsync()`,
the semaphore, `Current`/`Stale` keys) or `CatalogRepository` — both already assumed
`Merge()` handed them isolated snapshots; this fix makes that assumption true instead
of changing the contract.

## Data model

No DB schema, request/response, or event payload changes — this is an in-memory
object-graph fix. The only "schema" affected is the shape of the cloned object graph
per merge pass:

```
CatalogAggregate (clone: MemberwiseClone, then overridden below)
├─ Stock: StockData            → new instance (record `with`), Lots → new List
├─ Properties: CatalogProperties → new instance (record `with`)
├─ ManufactureDifficultySettings → new instance (.Clone()), Settings → new List
├─ StockTakingHistory: List<>   → new List (.ToList())
├─ SaleHistorySummary           → new instance, MonthlyData → new Dictionary
├─ ConsumedHistorySummary       → new instance, MonthlyData → new Dictionary
├─ PurchaseHistorySummary       → new instance, MonthlyData → new Dictionary
└─ everything else (EshopPrice, ErpPrice, SalesHistory, ConsumedHistory,
   PurchaseHistory, ManufactureHistory, Margins, scalars, Url, Location, …)
                                → reference-shared with the pre-merge instance until
                                  a Merge* helper wholesale-reassigns it this pass
```

Two generations (`Current` post-swap and the freshly promoted `Stale`) now share zero
mutable object identity below the aggregate level for anything either a `Merge*`
helper or an out-of-scope direct-mutation call site (`SyncStockTaking`) can touch
in place. Fields that are only ever wholesale-reassigned continue to be safely
reference-shared until overlaid — matches FR-3 AC2's "no need to deep-copy" list.

## Test additions (shape only — implementation step writes them)

- `CatalogMergeServiceTests`: capture a `CatalogAggregate` from one `ExecutePriorityMergeAsync()` result, run a second merge with changed source data, assert the first instance's fields are unchanged (FR-1).
- `CatalogMergeServiceTests`: assert `Merge()` output is not reference-equal, product-by-product, `Stock`-by-`Stock`, `Properties`-by-`Properties`, etc., to the input `catalogData` passed in via `TryGetCurrent()` before the pass.
- `CatalogMergeServiceTests`: a product missing from `erpProductsMap`/`salesMap`/etc. in pass 2 (present in pass 1) keeps its pass-1 field values on the pass-2 instance (FR-2 copy-forward).
- New: a product's `SaleHistorySummary`/`ConsumedHistorySummary`/`PurchaseHistorySummary` after a pass that *does* update its history is not reference-equal to the pre-merge instance's summary object (covers the finding above, not previously in FR-3).
- `CatalogCacheStoreTests`: after `ReplaceCacheAtomicallyAsync`, `TryGetStale()`'s elements are not reference-equal to the elements of the `newData` just passed in, nor to the elements a subsequent `Merge()` pass produces.

## Out of scope (unchanged from plan, confirmed still accurate)

`CatalogDataRefreshService.RefreshManufactureCostData`, the single-product branch of
`RefreshManufactureDifficultySettingsData`, `CatalogAggregate.SyncStockTaking` callers,
and now also the `RefreshMarginData` background task in `CatalogModule.cs:296-299`
(same anti-pattern: mutates a `CatalogAggregate` obtained from `GetAllAsync()` in
place) all remain call sites that mutate a live cached aggregate directly, outside
`Merge()`. This design's `Clone()` neutralizes the specific hazards they'd otherwise
reintroduce (`Stock.Lots`, `StockTakingHistory` in-place mutation) by ensuring those
collections aren't shared across generations, but does not change these call sites'
own behavior — they still mutate whatever instance the repository currently hands
them. Flag for the same follow-up ticket the plan already calls for.
