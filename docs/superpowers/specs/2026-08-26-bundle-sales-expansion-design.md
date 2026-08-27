# Bundle Sales Expansion: Counting Set Components in Sales Figures

## Context

Manufacturing volumes are driven by sold quantities. A product sold inside a gift package
("balíček") is invisible to that calculation today, so the planner under-produces every product
that sells through bundles.

The gap is in the sales ingestion path:

1. `FlexiCatalogSalesClient` (Flexi user query 37) returns one row per product per day.
2. A bundle sells as a single invoice line carrying the **bundle's own** product code.
3. `CatalogMergeService.MergeSalesHistory` maps rows onto products purely by product code.

Components therefore receive zero from that sale. Everything reading
`CatalogAggregate.SalesHistory` inherits the error: `GetManufacturingStockAnalysisHandler`,
`BatchPlanningService`, `PurchaseMaterialCatalogAdapter`, `LowStockAlertTile`, and the catalog
detail screen.

**Goal**: a sold bundle contributes its BoM quantities to each component's sales history, so
manufacturing and purchasing plan against real demand.

**Non-goal**: changing revenue, margin, or financial reporting in any way.

## Decisions Taken

These were settled during brainstorming and constrain the design:

| Decision | Choice | Reason |
|---|---|---|
| Bundle scope | All bundles are ERP sets | Confirmed by the user; no eshop-only bundles exist |
| Revenue handling | Quantity only, `Sum* = 0` | Company revenue totals stay exactly correct; no double counting |
| Demand event | Bundle **sale**, not bundle assembly | Matches how manufacturing is actually steered; smooth signal; works retroactively across the whole history window |
| Fix location | Application ingestion, not the ERP query | See below |

### Why not fix Flexi user query 37

This was attempted first and abandoned. Two findings are worth recording because they cost time:

- **Flexi's user-query preprocessor treats `:` as a named-parameter prefix and scans SQL comments.**
  A comment containing `bundles: quantity` fails with
  `Space is not allowed after parameter prefix ':'` before any SQL is parsed. Angle brackets are
  likewise hazardous, since `<<PARAM>>` is Flexi's own parameter syntax.
- **Set composition is not in the kusovník.** `Agenda.cs` in the FlexiBee SDK separates
  `BoM = "kusovnik"` from `Sets = "sady-a-komplety"`. A join against the kusovník table returns no
  rows for `BAL` products — it fails silently, yielding package counts with no component rows.

Beyond that, SQL in Flexi is unversioned, invisible to this repository, and untestable in CI.

## Architecture

Expansion happens at **merge**, not at refresh. Two independent refresh tasks fill two caches;
`Merge()` reads both together.

```
RefreshSalesData     ──► cache: CatalogSaleRecord[]  ┐
                                                     ├──► Merge(): expand ──► salesMap
RefreshSetPartsData  ──► cache: CatalogSetPart[]     ┘                            │
                                                                                        ▼
                                                                          CatalogAggregate.SalesHistory
                                                                                        │
                    ┌───────────────────────────────────────────────────────────────────┤
                    ▼                    ▼                    ▼                         ▼
        GetStockAnalysis      BatchPlanningService   PurchaseMaterial…      LowStockAlertTile
```

Refresh-time expansion was rejected: it couples the two refresh tasks' ordering, so the result
would depend on which task ran last. Merge-time expansion always combines the latest of each, and
keeps the raw sales cache raw — the expansion stays inspectable and reversible.

### Integration point

`CatalogMergeService.Merge()` currently builds the map directly from cache:

```csharp
var salesMap = _cacheStore.GetSalesData()
    .GroupBy(s => s.ProductCode)
    .ToDictionary(k => k.Key, v => v.ToList());
```

becomes:

```csharp
var salesMap = _bundleExpander
    .Expand(_cacheStore.GetSalesData(), _cacheStore.GetSetPartsData())
    .GroupBy(s => s.ProductCode)
    .ToDictionary(k => k.Key, v => v.ToList());
```

No consumer changes anywhere else.

## Components

### `ICatalogSetPartsClient` (Catalog domain)

```csharp
public interface ICatalogSetPartsClient
{
    Task<IReadOnlyList<CatalogSetPart>> GetAsync(
        IEnumerable<string> setCodes,
        CancellationToken cancellationToken = default);
}
```

```csharp
public record CatalogSetPart
{
    public string SetCode { get; init; }
    public string ComponentCode { get; init; }
    public string ComponentName { get; init; }
    public double Amount { get; init; }
}
```

Implemented in `Adapters.Flexi` over `IProductSetsClient` — the same client
`FlexiManufactureClient.GetSetPartsAsync` already uses successfully for the gift-package screen.
It reads evidence `sady-a-komplety`, filtered `cenikSada eq "code:{setCode}"`, with quantity
`mnozMj`.

A Catalog-owned interface (rather than reusing `IManufactureClient`) matches the existing
convention — every catalog data source has a Catalog-owned interface with a Flexi implementation —
and avoids a Catalog → Manufacture dependency.

### `RefreshSetPartsData`

Added to `CatalogDataRefreshService`, alongside `Get/SetSetPartsData` on `CatalogCacheStore` and a
`RegisterRefreshTask` entry in `CatalogModule`. Identical in shape to the seventeen existing
refresh tasks, including the `_resilienceService` wrapper.

Set codes are derived from `_cacheStore.GetErpStockData()` using the shared bundle rule below.

### Shared bundle rule

`CatalogMergeService.GetProductType` currently owns the definition:

```csharp
if (type == ProductType.Product && (s.ProductCode.StartsWith("BAL") || s.ProductCode.StartsWith("SET")))
    return ProductType.Set;
```

`RefreshSetPartsData` needs the same rule. It must be **extracted to one shared helper**, not
copied. If the two ever disagree, bundles silently stop expanding — reintroducing exactly the bug
being fixed, with no error to show for it.

### `BundleSalesExpander`

Pure function, no I/O, no state.

```csharp
IReadOnlyList<CatalogSaleRecord> Expand(
    IEnumerable<CatalogSaleRecord> sales,
    IEnumerable<CatalogSetPart> setParts);
```

`setParts` arrives as the flat list held in cache and is grouped by `SetCode` inside `Expand`.
This matches how every other source is cached (flat list) and grouped (at merge).

Returns every input record unchanged, plus one synthetic record per component per bundle sale:

| Field | Value |
|---|---|
| `Date` | the bundle sale's date |
| `ProductCode` / `ProductName` | the component's |
| `AmountB2B` / `AmountB2C` | bundle amount × `part.Amount` |
| `AmountTotal` | `AmountB2B + AmountB2C` |
| `SumB2B` / `SumB2C` / `SumTotal` | `0` |
| `SourceBundleCode` | the bundle's product code |

`SourceBundleCode` is a new nullable property on `CatalogSaleRecord`. It exists because revenue on
these rows is deliberately zero, so a product showing 300 sold against 200 invoice lines will get
questioned; this field answers it immediately. `CatalogSaleRecord` is a C# `record`, which is
correct — the DTO-must-be-a-class rule covers OpenAPI contract types, not internal domain types.

Why `AmountB2B`/`AmountB2C` and not `AmountTotal`: `CatalogAggregate.GetTotalSold` sums
`AmountB2B + AmountB2C`. Quantities placed only in `AmountTotal` would not count.

## Edge Cases

| Case | Behaviour |
|---|---|
| Component is itself a bundle | Not recursed. One level only, matching the flat `sady-a-komplety` structure. |
| Bundle has no parts in Flexi | Logged warning, bundle skipped. Never a silent zero. |
| Component absent from catalog | Record produced, no product matches it at merge. Harmless. |
| Same component twice in one BoM | Two records; they aggregate correctly downstream. |
| Set BoM changed since an old sale | Today's BoM is applied to the whole history window. Accepted approximation — Flexi has no BoM versioning. Documented, not fixed. |
| Materials listed in a bundle BoM | They gain sales figures. Harmless for planning: `PurchaseMaterialCatalogAdapter` uses `GetConsumed` for `ProductType.Material`, not `GetTotalSold`. |

## Error Handling

Per-set fetch failure must not poison the refresh. `RefreshSetPartsData` wraps the fetch in the
existing `_resilienceService` and retains stale cache on failure, mirroring `RefreshSalesData`:

```csharp
_logger.LogWarning(ex, "RefreshSetPartsData failed after all retries — retaining stale cache. Sets in cache: {Count}", ...);
```

If the set-parts cache is empty, `Expand` returns the input unchanged — degrading to today's
behaviour rather than throwing.

## Testing

**Unit — `BundleSalesExpander`** (AAA, pure, no mocks):
- multiplies component quantity by BoM amount for B2B and B2C independently
- leaves `SumB2B`/`SumB2C` at zero on synthetic records
- passes non-bundle records through untouched
- does not recurse when a component is itself a bundle
- returns input unchanged when the parts map is empty

**Integration — `CatalogMergeService`**:
- a component's `SalesHistory` includes quantities from a sold bundle
- `SaleHistorySummary` monthly revenue is unchanged by expansion
- `GetTotalSold` for the component reflects bundle quantities

**Integration — `CatalogDataRefreshService`**:
- mirrors `RefreshSalesData_WhenResilienceThrows_RetainsStaleCacheAndLogsWarning`

## Known Risk: Flexi Call Volume

`RefreshSetPartsData` issues one Flexi call per bundle per refresh. At ~20 bundles this is
negligible; at several hundred it needs batching or a longer refresh interval.

**The bundle count is still unmeasured** (as of implementation, 2026-08-27). Counting it needs the
Gift Package screen against deployed staging, which the implementation environment cannot reach.
Implemented without batching: `FlexiCatalogSetPartsClient` loops set codes sequentially.

`ICatalogSetPartsClient` takes `IEnumerable<string>`, so batching stays an internal detail of that
one class and can be added later without touching any caller. If the catalog refresh becomes slow,
count the bundles and batch there.

## Assembly-Lag Caveat

Gift packages are physically assembled — `GiftPackageManufactureService` consumes component stock
and produces set stock — so a component leaves stock at assembly time while this design attributes
demand at sale time. In steady state the two rates converge and the numbers are right. After a
single large assembly batch, component stock is already gone while the corresponding bundle demand
still arrives over following weeks, so the planner reads as more urgent than reality for that
window.

This was accepted deliberately in favour of the smoother, more retroactive sale-based signal.

## Validation Before Completion

- `dotnet build` and `dotnet format`
- all touched tests pass
- manual check: pick one bundle with recent sales, confirm each component's
  `GetTotalSold` rose by exactly `bundleQty × partAmount`, and that
  `SaleHistorySummary` revenue for those components did not move
