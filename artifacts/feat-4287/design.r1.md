# Design: Deduplicate sort-direction ternary in GetProductMarginsHandler.ApplySorting

## Component Design

This is a backend-only, single-class refactor. No new components are introduced; one new private static method is added to the existing `GetProductMarginsHandler` class.

### `GetProductMarginsHandler` (existing class, modified)
- **Responsibility (unchanged):** Handle `GetProductMarginsRequest` — fetch catalog products, filter, sort, paginate, and map to `ProductMarginDto` items.
- **`ApplySorting(IEnumerable<CatalogAggregate> products, string? sortBy, bool sortDescending) : IEnumerable<CatalogAggregate>`** (existing private method, internals rewritten): resolves the `sortBy` field name to a selector lambda and delegates the ascending/descending choice to the new `SortBy` helper. The set of resolvable field names, the case-insensitive matching (`sortBy.ToLower()`), and the fallback-to-`ProductCode` behavior for a null/whitespace/unrecognized `sortBy` are all unchanged.
- **`SortBy<TKey>(IEnumerable<CatalogAggregate> items, Func<CatalogAggregate, TKey> key, bool desc) : IOrderedEnumerable<CatalogAggregate>`** (new private static method): the single point where the `desc` flag is turned into an `OrderBy`/`OrderByDescending` call. Stateless, pure, no side effects. Not exposed outside the class — no other component depends on it, and per the architecture review it is intentionally kept local rather than shared with the structurally-similar `TopProductSorter` in the Analytics module (out of scope for this change).

No other class, interface, or contract in the system depends on `ApplySorting`'s internals, so this component boundary change is fully contained.

## Data Schemas

No data schema changes. This refactor touches only in-memory LINQ ordering logic and does not change:
- `GetProductMarginsRequest` (`SortBy: string?`, `SortDescending: bool`, and other existing fields) — unchanged.
- `GetProductMarginsResponse` / `ProductMarginDto` — unchanged.
- `CatalogAggregate` and its `Margins.Averages.{M0,M1,M2,M3}.{Amount,Percentage}` shape — unchanged, read-only inputs to sorting.
- No database schema, migration, or persisted entity is involved.

The supported `sortBy` values and their corresponding `CatalogAggregate` selector expressions (unchanged from current behavior, now routed through the shared helper):

| `sortBy` value (case-insensitive) | Selector | Key type |
|---|---|---|
| `productcode` | `x.ProductCode` | `string` |
| `productname` | `x.ProductName` | `string` |
| `pricewithoutvat` | `x.PriceWithoutVat ?? 0` | `decimal` |
| `purchaseprice` | `x.ErpPrice?.PurchasePrice ?? 0` | `decimal` |
| `manufacturedifficulty` | `x.ManufactureDifficulty ?? 0` | corresponding non-nullable numeric type |
| `m0amount` / `m1amount` / `m2amount` / `m3amount` | `x.Margins.Averages.M{n}.Amount` | `decimal` |
| `m0percentage` / `m1percentage` / `m2percentage` / `m3percentage` | `x.Margins.Averages.M{n}.Percentage` | `decimal` |
| null / whitespace / unrecognized (default) | `x.ProductCode` | `string` |
