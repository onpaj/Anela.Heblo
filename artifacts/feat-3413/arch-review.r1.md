# Architecture Review: Unit Test Coverage for GetProductMarginsHandler — ApplyFilters and ApplySorting

## Skip Design: true

## Architectural Fit Assessment

This feature is a pure test-layer addition with zero production code changes. It fits cleanly into the existing pattern: a single xUnit test class (`GetProductMarginsHandlerTests`) in `backend/test/Anela.Heblo.Tests/Features/Catalog/` that constructs the handler with mocked dependencies and exercises it through its `Handle` method. The two existing tests already use this pattern correctly.

Integration points are minimal:
- `ICatalogRepository.GetAllAsync` — the only mock that must be configured per test
- `TimeProvider` — already mocked with a fixed UTC time in the existing tests; the new tests must do the same
- `CatalogAggregate` / `ProductType` — domain types used directly in test data setup
- `GetProductMarginsRequest` / `GetProductMarginsResponse` — the request/response contract under test

The existing `BuildAggregate` private helper already constructs `CatalogAggregate` instances with a hardcoded `Type = ProductType.Product`. It must be extended to accept a `ProductType` parameter — this is the only structural change to the test file.

## Proposed Architecture

### Component Overview

```
GetProductMarginsHandlerTests (existing class)
│
├── constructor — unchanged (mocks already set up for ICatalogRepository + TimeProvider + ILogger)
│
├── BuildAggregate (private static helper) — EXTEND signature to accept ProductType parameter
│   Current:  BuildAggregate(string productCode, IEnumerable<DateTime> monthlyKeys)
│   New:      BuildAggregate(string productCode, IEnumerable<DateTime>? monthlyKeys = null, ProductType type = ProductType.Product)
│
├── [Fact] Handle_FiltersMonthlyMarginsTo13MonthsBeforeInjectedUtcNow — existing, unchanged
├── [Fact] Handle_UsesUtcNotLocalTime_AtDayBoundary — existing, unchanged
│
├── [Fact] Handle_DefaultFilter_ReturnsOnlyProductAndGoods — NEW
├── [Fact] Handle_ExplicitProductTypeFilter_ReturnsOnlyMatchingType — NEW
└── [Fact] Handle_UnknownSortField_FallsBackToProductCodeAscending — NEW
```

No new files. All work is additive inside the one existing test class.

### Key Design Decisions

#### Decision 1: Extend `BuildAggregate` rather than inline construction

**Options considered:**
- Inline `new CatalogAggregate { ... }` in each new test body
- Extend the existing `BuildAggregate` helper with a `type` parameter

**Chosen approach:** Extend `BuildAggregate` with an optional `ProductType type = ProductType.Product` parameter and an optional `IEnumerable<DateTime>? monthlyKeys = null` parameter (defaulting to an empty sequence). Keep it `private static`.

**Rationale:** The existing tests call `BuildAggregate` and rely on it for consistent aggregate construction. Extending the signature with optional parameters keeps the existing call sites unchanged while making the new tests concise and expressive. Inline construction would duplicate the `Margins.MonthlyData` setup boilerplate and diverge from the established pattern.

#### Decision 2: `TimeProvider` mock in filter/sort tests

**Options considered:**
- Skip `TimeProvider` mock setup in tests that only exercise `ApplyFilters` / `ApplySorting`
- Always configure a fixed UTC time (as existing tests do)

**Chosen approach:** Always configure a fixed UTC time (`_timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(...)`).

**Rationale:** `MapToMarginDto` is called for every item that passes the filter, and it calls `_timeProvider.GetUtcNow()`. If `TimeProvider` is not configured, Moq returns `default(DateTimeOffset)`, which produces a `dateFrom` of `DateTime.MinValue.AddMonths(-13)` — this would throw an `ArgumentOutOfRangeException`. The new tests will exercise the full `Handle` path and must configure a valid fixed time.

#### Decision 3: Aggregate setup for the sort test

**Options considered:**
- Use products that have real `MonthlyData` entries to exercise the full mapping path
- Use products with empty `MonthlyData` (relying on `MonthlyMarginHistory.Averages` returning `MarginData()` defaults)

**Chosen approach:** Use products with empty `MonthlyData`. The `Averages` property returns a zeroed `MarginData` when `MonthlyData.Count == 0`, and `MapToMarginDto` handles this correctly. This keeps the sort test focused on ordering logic with minimum noise.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. All changes are confined to:

```
backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs
```

### Interfaces and Contracts

The three new test method signatures:

```csharp
[Fact]
public async Task Handle_DefaultFilter_ReturnsOnlyProductAndGoods()

[Fact]
public async Task Handle_ExplicitProductTypeFilter_ReturnsOnlyMatchingType()

[Fact]
public async Task Handle_UnknownSortField_FallsBackToProductCodeAscending()
```

The updated `BuildAggregate` helper signature:

```csharp
private static CatalogAggregate BuildAggregate(
    string productCode,
    IEnumerable<DateTime>? monthlyKeys = null,
    ProductType type = ProductType.Product)
```

The `monthlyKeys` default of `null` (treated as empty) allows the new tests to call `BuildAggregate("A001", type: ProductType.SemiProduct)` without needing to pass dummy date lists.

### Data Flow

**FR-1 (default filter):**
1. Arrange: Four aggregates — one each of `Product`, `Goods`, `SemiProduct`, `Material` — returned by `GetAllAsync`. `ProductType = null` on request.
2. Handler calls `ApplyFilters` → else branch fires → `Where(x => x.Type == ProductType.Product || x.Type == ProductType.Goods)`.
3. Assert: `response.Items.Count == 2`, `response.TotalCount == 2`.

**FR-2 (explicit filter):**
1. Arrange: Two aggregates — one `Product`, one `SemiProduct`. `ProductType = ProductType.SemiProduct` on request.
2. Handler calls `ApplyFilters` → `HasValue` branch fires → `Where(x => x.Type == ProductType.SemiProduct)`.
3. Assert: `response.Items.Count == 1`, `response.TotalCount == 1`.

**FR-3 (unknown sort field):**
1. Arrange: Three aggregates with `ProductCode` values "B001", "A001", "C001" (deliberately out of order). `SortBy = "nonexistent"` on request.
2. Handler calls `ApplySorting` → switch default branch → `OrderBy(x => x.ProductCode)` ascending.
3. Assert: `response.Items.Select(i => i.ProductCode)` equals `["A001", "B001", "C001"]` in order. `response.Success == true`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `TimeProvider` not mocked → `ArgumentOutOfRangeException` in `MapToMarginDto` | Medium | Configure a fixed UTC time in every new test, same as existing tests |
| `BuildAggregate` signature change breaks existing call sites | Low | Use optional parameters with matching defaults — existing callers pass positional args that align with the new signature |
| `Margins.Averages` throws when `MonthlyData` is empty | Low | Verified: `Averages` returns `new MarginData()` (zeroed) when count is 0; `MapToMarginDto` accesses `marginHistory.Averages.*` which is safe |
| FR-1 test inadvertently includes `Goods` only because `BuildAggregate` defaults to `Product` | Low | Explicitly pass `type: ProductType.Goods` for the Goods item using the extended helper |

## Specification Amendments

**Amendment 1 — `BuildAggregate` default parameter handling**

The spec says "The existing `BuildAggregate` helper must be extended to accept a `ProductType` parameter." The correct extension makes `monthlyKeys` optional (default `null` → empty) as well, so tests that only care about product type are not forced to pass a dummy date enumerable. The implementation should treat `null` monthlyKeys as an empty sequence.

**Amendment 2 — FR-3 assertion precision**

The spec says assert "results are ordered by `ProductCode`". The assertion should use FluentAssertions' `BeInAscendingOrder(i => i.ProductCode)` or `Equal(new[] {"A001","B001","C001"})` with `WithStrictOrdering()` — not `BeEquivalentTo` which ignores order.

**Amendment 3 — FR-1 should include a `Goods` item explicitly**

The brief says "assert only the Product and Goods is returned" but the spec acceptance criteria describe a four-item catalog: `Product`, `Goods`, `SemiProduct`, `Material`. This is consistent. Confirm the test includes an explicit `Goods` aggregate (not just relies on the `Product` default) so that the test actually exercises the `|| x.Type == ProductType.Goods` branch.

## Prerequisites

None. All required types (`CatalogAggregate`, `ProductType`, `ICatalogRepository`, `TimeProvider`, xUnit, Moq, FluentAssertions) are already present and referenced. No migrations, configuration changes, or new NuGet packages are needed.
