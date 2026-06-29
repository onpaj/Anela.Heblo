## Module / File
`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs`

## Coverage
Line coverage: 20.7% (filter threshold: 60%)
1 existing test file: `GetProductMarginsHandlerTests`.

## What's not tested

**`ApplyFilters` — default product type filter**:
- When `request.ProductType` is `null`, the handler applies a hardcoded default: only items of type `Product` or `Goods` are returned. Items of type `SemiProduct`, `Material`, or any other type are silently excluded. No test verifies this default. If a future refactor accidentally removes or broadens this condition, the margin report would start showing semi-products (which have very different cost structures) mixed in, corrupting averages.

**`ApplyFilters` — explicit type filter**:
- When `request.ProductType` has a specific value, the filter changes to an equality match. No test exercises this path either.

**`ApplySorting` — unknown sort field fallback**:
- When `sortBy` does not match any of the named cases (`productcode`, `productname`, `pricewithoutvat`, `purchaseprice`, `manufacturedifficulty`, `m0amount` … `m2percentage`), the switch falls through to the default branch and silently sorts by `ProductCode`. No error is returned and nothing is logged. A frontend typo in the sort field name would produce an unexplained ordering change with no diagnostic trail.

## Why it matters
The default type filter is a business rule enforced only in application code with no database-level constraint — if it silently disappears, the margin dashboard becomes misleading. The silent sort fallback makes frontend/API contract mismatches invisible.

## Suggested approach
- Add a test with a catalog containing one `Product`, one `SemiProduct`, and one `Material` item, `ProductType = null`, and assert only the `Product` is returned.
- Add a test with `ProductType = SemiProduct` and assert only the semi-product is returned.
- Add a test with `sortBy = "nonexistent"` and assert results are ordered by `ProductCode`. ~0.5 day effort.

---
_Filed by weekly coverage-gap routine on 2026-06-29. Based on CI run #28295125598 (23c3b5d571c976074ee31869c96e29487098040c)._
