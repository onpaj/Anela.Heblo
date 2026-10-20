# Specification: Deduplicate sort-direction ternary in GetProductMarginsHandler.ApplySorting

## Summary
`GetProductMarginsHandler.ApplySorting` (`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs`) repeats the identical `sortDescending ? OrderByDescending(...) : OrderBy(...)` ternary once per sort field (13 named switch arms, the `default` arm, and the null-guard block above the switch — 15 occurrences of the pattern in total). This refactoring extracts a single private static helper that performs the direction choice once, collapsing every site to a single line, with no change in observable behavior.

## Background
This finding was filed by the automated arch-review routine against the Catalog module. The method sorts the in-memory `IEnumerable<CatalogAggregate>` result of a product-margins query by one of a fixed set of field names (`sortBy`, case-insensitive), applying ascending or descending order per the `sortDescending` flag. Because C# `switch` expression arms cannot easily share a helper call without repeating the ternary, each arm re-implements the same two-line direction choice, differing only in the selector lambda (e.g. `x => x.ProductCode` vs. `x => x.Margins.Averages.M0.Amount`).

Note on the issue's arm count: the issue text describes "16 arms" / "17 ternary occurrences". The actual current code (as of this spec) has **13 named arms + 1 `default` arm + 1 pre-switch null-guard = 15 occurrences** of the `sortDescending ? OrderByDescending : OrderBy` pattern. The discrepancy does not change the nature or scope of the fix — the same helper-extraction approach applies uniformly regardless of exact arm count. This spec targets the code as it actually exists in the repository at `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs`, method `ApplySorting` (private instance method, roughly lines 129–186).

## Functional Requirements

### FR-1: Extract a shared sort-direction helper
Add a private static helper method to `GetProductMarginsHandler` that encapsulates the ascending/descending choice:

```csharp
private static IOrderedEnumerable<CatalogAggregate> SortBy<TKey>(
    IEnumerable<CatalogAggregate> items,
    Func<CatalogAggregate, TKey> key,
    bool desc)
    => desc ? items.OrderByDescending(key) : items.OrderBy(key);
```

(Return type note: `OrderBy`/`OrderByDescending` both return `IOrderedEnumerable<TSource>`, so the helper's return type can be `IOrderedEnumerable<CatalogAggregate>` rather than the wider `IEnumerable<CatalogAggregate>` shown in the issue's suggested fix — either compiles and satisfies `ApplySorting`'s existing `IEnumerable<CatalogAggregate>` return type; the narrower type is preferred here since it is exact.)

**Acceptance criteria:**
- A single private static method exists that takes the current products sequence, a key selector, and the `sortDescending` flag, and returns the appropriately ordered sequence.
- The method is generic over the sort key type (`TKey`) so it works for `string`, `decimal`/`decimal?`-derived, and other comparable field types used across the existing arms.

### FR-2: Rewrite every switch arm to call the helper
Replace each of the 13 named arms, the `default` arm, and the pre-switch null-guard's ternary with a single call to the new helper, preserving the exact selector expression already used in each arm (including existing null-coalescing, e.g. `x.PriceWithoutVat ?? 0`, `x.ErpPrice?.PurchasePrice ?? 0`, `x.ManufactureDifficulty ?? 0`).

**Acceptance criteria:**
- Every arm becomes a single-line `"fieldname" => SortBy(products, x => ..., sortDescending),` (or the pre-switch guard's equivalent single-line return).
- The set of supported `sortBy` field names is unchanged: `productcode`, `productname`, `pricewithoutvat`, `purchaseprice`, `manufacturedifficulty`, `m0amount`, `m1amount`, `m2amount`, `m3amount`, `m0percentage`, `m1percentage`, `m2percentage`, `m3percentage`, and the no-match/`default`/null-or-whitespace `sortBy` fallback (all fall back to `ProductCode` ordering, as today).
- Each selector lambda's expression is copied verbatim from the arm it replaces — no selector logic is altered, added, or removed.
- Existing comments grouping the M0–M3 amount/percentage arms are preserved (or reasonably relocated) so the field list remains scannable.

### FR-3: No behavior change
The refactor is purely structural. For any given `(sortBy, sortDescending)` input and any given product set, the resulting sequence (elements and their order) must be identical before and after the change.

**Acceptance criteria:**
- All existing tests covering `GetProductMarginsHandler`/`ApplySorting` (unit and/or integration) continue to pass unmodified.
- No new sortable field is added and no existing field is removed as part of this change.
- The method's public-facing behavior (via `GetProductMarginsRequest.SortBy` / `SortDescending` on the `GetProductMargins` use case) is unchanged for every previously supported value of `SortBy`, including case-insensitivity (`sortBy.ToLower()` is preserved) and the null/whitespace/unmatched fallback to `ProductCode` ordering.

## Non-Functional Requirements

### NFR-1: Performance
No performance regression. The helper method is a trivial ternary dispatch with no additional allocations or iteration beyond what `OrderBy`/`OrderByDescending` already do; LINQ deferred execution semantics for the returned `IOrderedEnumerable<CatalogAggregate>` must be preserved (sorting still happens lazily when the caller enumerates, e.g. during the subsequent `Skip`/`Take` pagination in `Handle`).

### NFR-2: Security
Not applicable — this is an internal, non-behavior-changing code structure change with no new inputs, outputs, or trust boundaries.

## Data Model
No data model changes. `CatalogAggregate` and its `Margins.Averages.{M0,M1,M2,M3}.{Amount,Percentage}` shape are read-only inputs to the sort and are not modified by this change.

## API / Interface Design
No public API changes. `GetProductMarginsRequest`, `GetProductMarginsResponse`, and the MediatR handler contract (`IRequestHandler<GetProductMarginsRequest, GetProductMarginsResponse>`) are unchanged. Only the private `ApplySorting` method's internals change, plus the addition of one new private static helper method (`SortBy<TKey>`) on the same handler class.

## Dependencies
None beyond what `GetProductMarginsHandler` already depends on (`System.Linq`, `ICatalogRepository`, `TimeProvider`, `ILogger<GetProductMarginsHandler>`). No new package or project references are needed.

## Out of Scope
- Changing, adding, or removing any sortable field.
- Changing null-handling/coalescing semantics for any field (e.g. whether `PriceWithoutVat` null sorts as `0`).
- Changing the case-insensitivity behavior of `sortBy` matching.
- Any change to `ApplyFilters`, `MapToMarginDto`, pagination, error handling, or logging in `GetProductMarginsHandler`.
- Any change to `CatalogAggregate` or the `Margins` calculation pipeline.
- Reconciling the issue's stated "16 arms / 17 occurrences" count with the actual "13 arms + default + 1 guard = 15 occurrences" found in the current code — this spec proceeds against the actual code, and the exact count does not affect the fix's design.

## Open Questions

None.

## Status: COMPLETE
