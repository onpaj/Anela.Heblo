## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs:222-227` and `:259-264` — the two new characterization tests build an identical `catalogItems` array (same three `BuildAggregateWithM0Margin` calls) inline in each test. Could be hoisted into a shared private field or a small factory method to remove the duplication, but this mirrors the existing style already used by other tests in this file, so it is a minor, non-blocking suggestion.

## Notes

Reviewed the full feature diff (`GetProductMarginsHandler.cs` + `GetProductMarginsHandlerTests.cs`) against `spec.r1.md`:

- `SortBy<TKey>` helper matches the spec's FR-1 exactly: `desc ? items.OrderByDescending(key) : items.OrderBy(key)`, unconstrained `TKey`, `IOrderedEnumerable<CatalogAggregate>` return type.
- Every rewritten arm of `ApplySorting` (the pre-switch null/whitespace guard, all 13 named arms, and the `default`/`_` arm) was diffed selector-by-selector against the pre-refactor ternary it replaces. All selectors — including the null-coalescing/conditional ones (`x.PriceWithoutVat ?? 0`, `x.ErpPrice?.PurchasePrice ?? 0`, `x.ManufactureDifficulty ?? 0`) — are copied verbatim with no logic change, satisfying FR-2 and FR-3.
- The M0–M3 amount/percentage grouping comments are preserved as specified.
- No changes outside the two files the spec scoped (`GetProductMarginsHandler.cs`, `GetProductMarginsHandlerTests.cs`); `TopProductSorter` and all other out-of-scope items were correctly left untouched.
- The two new tests (`Handle_SortByM0AmountDescending_OrdersByCalculatedM0Amount`, `Handle_SortByM0PercentageAscending_OrdersByCalculatedM0Percentage`) exercise exactly the amount/percentage selector shapes flagged as highest transcription risk, and both prior task reviews report all tests passing.

No correctness bugs found. Result: CLEAN.
