# Specification: Unit Test Coverage for GetProductMarginsHandler — ApplyFilters and ApplySorting

## Summary

`GetProductMarginsHandler` has 20.7% line coverage against a 60% threshold. Two critical logic paths — the default product-type guard in `ApplyFilters` and the unknown-sort-field fallback in `ApplySorting` — have zero test coverage. This specification defines three new unit tests that bring coverage above threshold and pin the existing business rules against accidental regression.

## Background

The margin dashboard is a key business reporting tool for Anela Heblo. The handler's `ApplyFilters` method enforces a business rule that the default margin view shows only `ProductType.Product` and `ProductType.Goods` items; semi-products and materials have structurally different cost bases and would corrupt averages if included. This rule lives entirely in application-layer code with no database constraint backing it. `ApplySorting` falls back to `ProductCode` ordering when the client sends an unrecognised sort field, but this happens silently — a frontend typo or API contract drift produces an unexplained reordering with no diagnostic signal. Neither behaviour is currently tested.

## Functional Requirements

### FR-1: Default product-type filter — only Product and Goods are returned

When `GetProductMarginsRequest.ProductType` is `null`, the handler must return only items whose `CatalogAggregate.Type` is `ProductType.Product` or `ProductType.Goods`. Items of type `SemiProduct`, `Material`, `Set`, or `UNDEFINED` must be excluded.

**Acceptance criteria:**
- Given a catalog containing one item of each of `Product`, `Goods`, `SemiProduct`, and `Material`, a request with `ProductType = null` returns exactly two items: the `Product` and the `Goods` item.
- The returned `Items` collection does not contain a `ProductCode` that belongs to a `SemiProduct` or `Material` aggregate.
- `response.Success` is `true`.
- `response.TotalCount` equals 2.

### FR-2: Explicit product-type filter — equality match

When `GetProductMarginsRequest.ProductType` has a value, the handler must return only items whose `Type` equals that value exactly.

**Acceptance criteria:**
- Given a catalog containing one `Product` and one `SemiProduct`, a request with `ProductType = ProductType.SemiProduct` returns exactly one item whose `ProductCode` belongs to the `SemiProduct` aggregate.
- `response.TotalCount` equals 1.
- `response.Success` is `true`.
- The `Product` item is absent from `response.Items`.

### FR-3: Unknown sort field — silent fallback to ProductCode ascending

When `GetProductMarginsRequest.SortBy` contains a value that does not match any recognised field name (case-insensitive), the handler must sort results by `ProductCode` ascending and must not return an error.

**Acceptance criteria:**
- Given a catalog with aggregates having `ProductCode` values `"B001"`, `"A001"`, `"C001"` (in that insertion order), a request with `SortBy = "nonexistent"` returns items in the order `"A001"`, `"B001"`, `"C001"`.
- `response.Success` is `true`.
- `response.ErrorCode` is absent / default.
- No exception propagates from the handler.

## Non-Functional Requirements

### NFR-1: Performance

These are pure in-memory unit tests with no I/O; execution time per test must be under 100 ms. No async latency requirements beyond the existing test suite baseline.

### NFR-2: Test isolation

Each test must set up its own mock for `ICatalogRepository.GetAllAsync`. Tests must not share mutable state. `TimeProvider` must be mocked with a fixed UTC time so monthly-history filtering does not interfere with the assertions under test (use any recent fixed date, e.g. `2026-06-29T12:00:00Z`).

### NFR-3: Coverage threshold

After the three new tests are added, line coverage for `GetProductMarginsHandler.cs` must reach or exceed 60% as measured by the project's existing coverage tooling.

## Data Model

No new data model changes. Tests operate against the existing domain types:

- `CatalogAggregate` — `Id` / `ProductCode` (string), `Type` (ProductType), `Margins` (MonthlyMarginHistory with default empty `MonthlyData` and zeroed `Averages`)
- `ProductType` enum — `Product = 8`, `Goods = 1`, `Material = 3`, `SemiProduct = 7`, `Set = 99`, `UNDEFINED = 0`
- `GetProductMarginsRequest` — `ProductType` (nullable `ProductType`), `SortBy` (nullable string), `SortDescending` (bool), `PageNumber` (int, default 1), `PageSize` (int, default large enough to return all items in tests)
- `GetProductMarginsResponse` — `Items` (list of `ProductMarginDto`), `TotalCount` (int), `Success` (bool)

The existing `BuildAggregate` helper in `GetProductMarginsHandlerTests` creates a `CatalogAggregate` with `Type = ProductType.Product`. It must be extended (or a new overload added) to accept a `ProductType` parameter so FR-1 and FR-2 tests can construct mixed-type catalogs.

## API / Interface Design

No API surface changes. All work is confined to the test file:

```
backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs
```

Three new `[Fact]` methods are added to the existing `GetProductMarginsHandlerTests` class:

| Test method name | Covers |
|---|---|
| `Handle_DefaultFilter_ReturnsOnlyProductAndGoods` | FR-1 |
| `Handle_ExplicitProductTypeFilter_ReturnsOnlyMatchingType` | FR-2 |
| `Handle_UnknownSortField_FallsBackToProductCodeAscending` | FR-3 |

The `BuildAggregate` private helper is updated to accept an optional `ProductType type = ProductType.Product` parameter (or a new overload) so existing tests continue to compile without modification.

## Dependencies

- xUnit `[Fact]` — already in use in this test file
- Moq — already in use (`Mock<ICatalogRepository>`, `Mock<TimeProvider>`)
- FluentAssertions — already in use
- `Anela.Heblo.Domain.Features.Catalog.ProductType` — already referenced in the test file
- No new NuGet packages required

## Out of Scope

- Tests for `ProductType.Set` or `ProductType.UNDEFINED` default-filter exclusion (implied by the existing condition but not specifically called out in the brief; can be added as bonus cases but are not required)
- Tests for `SortDescending = true` on the unknown-field fallback path
- Tests for pagination logic (`PageNumber`, `PageSize`)
- Tests for `ProductCode` / `ProductName` text-filter paths in `ApplyFilters`
- Any changes to production handler code
- Integration or E2E tests
- Frontend changes

## Open Questions

None.

## Status: COMPLETE
