# Specification: Extract `CatalogAggregate` → `AnalyticsProduct` Mapping Helper

## Summary
The conversion logic from `CatalogAggregate` to `AnalyticsProduct` is duplicated verbatim across two methods in `AnalyticsRepository`, with one copy already silently drifted (missing date-range filter on `SalesHistory`). Extract a single private helper method `MapToAnalyticsProduct(CatalogAggregate, DateTime, DateTime)` and route both call sites through it. This eliminates duplication, corrects the latent `SalesHistory` filter bug, and reduces the migration surface for the upcoming cross-module refactor (#1805) into `CatalogAnalyticsSourceAdapter`.

## Background

`backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs` exposes two methods that both project a `CatalogAggregate` into an `AnalyticsProduct`:

- `StreamProductsWithSalesAsync` (lines 52–116)
- `GetProductAnalysisDataAsync` (lines 168–231)

Both blocks perform identical operations in the same order:
1. Extract `marginData = product.Margins`
2. Filter `relevantMargins` by `[fromDate, toDate]`
3. Resolve `latestMarginEntry` with fallback to the last available margin entry
4. Apply the `latestMarginEntry.Equals(default(KeyValuePair<DateTime, MarginData>))` ternary guard six times across `MarginAmount`, `M0Amount`/`M1Amount`/`M2Amount`, `M0Percentage`/`M1Percentage`/`M2Percentage`, `MaterialCost`, `HandlingCost`
5. Resolve `purchasePrice` from `PurchaseHistory` (latest by `Date`)
6. Project `SalesHistory` entries into `SalesDataPoint`
7. Construct an `AnalyticsProduct`

The two copies have already drifted: `GetProductAnalysisDataAsync` does **not** filter `SalesHistory` by date range, while `StreamProductsWithSalesAsync` does. This is a latent bug — both paths feed the same downstream analytics consumers and should produce identically scoped data.

Issue #1805 will move this mapping into a `CatalogAnalyticsSourceAdapter`. Keeping the duplication doubles that migration surface and increases the risk of dropping the drift fix during the move.

## Functional Requirements

### FR-1: Introduce private mapping helper
Add a private method on `AnalyticsRepository`:

```csharp
private AnalyticsProduct MapToAnalyticsProduct(
    CatalogAggregate product,
    DateTime fromDate,
    DateTime toDate)
```

The helper encapsulates the full `CatalogAggregate` → `AnalyticsProduct` projection currently duplicated in lines 52–116 and 168–231. Behavior preserved from the existing blocks:
- `relevantMargins` filtered to `[fromDate, toDate]` inclusive on `MonthlyData.Key`
- `latestMarginEntry = relevantMargins.LastOrDefault()`; if equal to `default(KeyValuePair<DateTime, MarginData>)`, fall back to `marginData.MonthlyData.LastOrDefault()`
- A single `hasEntry` boolean derived once from the same default check, replacing the six repeated ternaries
- `latestPurchase = product.PurchaseHistory?.OrderByDescending(p => p.Date).FirstOrDefault()`
- `PurchasePrice = latestPurchase?.PricePerPiece ?? 0`
- `SellingPrice = product.EshopPrice?.PriceWithoutVat ?? 0`
- `EshopPriceWithoutVat = product.EshopPrice?.PriceWithoutVat` (nullable, distinct from `SellingPrice`)
- `MarginAmount`: when no entry exists, falls back to `marginData.Averages.M0.Amount` (matches current behavior in both blocks)
- All other margin/cost fields default to `0` when no entry exists
- `SalesHistory` projected via `Where(s => s.Date >= fromDate && s.Date <= toDate).Select(...)` (date-range filter applied unconditionally)

**Acceptance criteria:**
- A `private AnalyticsProduct MapToAnalyticsProduct(CatalogAggregate, DateTime, DateTime)` method exists on `AnalyticsRepository`.
- The helper's output matches the existing field mapping for `StreamProductsWithSalesAsync` exactly when called with the same `(product, fromDate, toDate)` inputs.
- The `latestMarginEntry.Equals(default(KeyValuePair<DateTime, MarginData>))` ternary appears at most once inside the helper (collapsed to a single `hasEntry` boolean).

### FR-2: Replace duplicated block in `StreamProductsWithSalesAsync`
Lines 52–116 (the per-product projection inside the `foreach`) are replaced by a single call to `MapToAnalyticsProduct(product, fromDate, toDate)`.

**Acceptance criteria:**
- `StreamProductsWithSalesAsync` no longer contains the inline mapping block; it invokes the helper.
- Streaming/async behavior, ordering, and yielded results are unchanged for the same inputs.
- No other behavioral change in this method (date filters, iteration shape, cancellation handling).

### FR-3: Replace duplicated block in `GetProductAnalysisDataAsync`
Lines 168–231 are replaced by a call to `MapToAnalyticsProduct(product, fromDate, toDate)`.

**Acceptance criteria:**
- `GetProductAnalysisDataAsync` no longer contains the inline mapping block; it invokes the helper.
- After extraction, `SalesHistory` is filtered by `[fromDate, toDate]` — the latent drift bug is fixed as a deliberate, documented side effect of this refactor.
- No other behavioral changes (input parameters, return shape, ordering).

### FR-4: Preserve public API and observable behavior (apart from FR-3 fix)
No public signatures change. No new fields, no removed fields, no reordering of returned data, no change to logging or exception behavior.

**Acceptance criteria:**
- Public method signatures of `AnalyticsRepository` are unchanged.
- Existing callers of `StreamProductsWithSalesAsync` and `GetProductAnalysisDataAsync` compile without modification.
- Aside from the `SalesHistory` filter now being applied in `GetProductAnalysisDataAsync` (FR-3), observable outputs are identical to the pre-refactor implementation.

### FR-5: Test coverage for the helper and fix
Add or update unit tests so the mapping is covered in one place:
- A test asserting `SalesHistory` is filtered by `[fromDate, toDate]` when consumed through `GetProductAnalysisDataAsync` (regression test for the drift bug).
- A test asserting the `hasEntry == false` path: when `MonthlyData` is empty, `MarginAmount` falls back to `marginData.Averages.M0.Amount` and the other margin/cost fields are `0`.
- A test asserting the `hasEntry == true` path picks values from the last entry within `[fromDate, toDate]`, not from outside the range.
- A test asserting `PurchasePrice` resolves to the latest `PurchaseHistory` entry by `Date` and defaults to `0` when history is null/empty.

**Acceptance criteria:**
- New/updated tests live alongside existing `AnalyticsRepository` tests.
- All tests pass on `dotnet test`.
- Coverage for `AnalyticsRepository` does not regress (per global 80%+ standard).

## Non-Functional Requirements

### NFR-1: Performance
No measurable regression. The helper performs the same LINQ operations as the current inline code; allocations and enumeration patterns are preserved. `StreamProductsWithSalesAsync` must continue to stream — the helper is synchronous per-product and does not introduce buffering, materialization beyond the existing `ToList()` on `relevantMargins`, or extra DB hits.

### NFR-2: Security
N/A — pure in-memory mapping over already-authorized aggregates. No new inputs, no new boundaries.

### NFR-3: Maintainability
The mapping logic exists in exactly one place after the refactor. Future field additions, fallback fixes, or `PurchasePrice` rule changes touch one method. The helper is structured so it can be lifted into `CatalogAnalyticsSourceAdapter` (#1805) without further rework.

### NFR-4: Backward compatibility
- Database schema: unchanged.
- API contracts: unchanged.
- Wire format of `AnalyticsProduct`: unchanged.
- One intentional behavior change: `GetProductAnalysisDataAsync` now date-filters `SalesHistory`. This is the documented bug fix per FR-3 and must be called out in the PR description.

## Data Model

No schema changes. The refactor operates on existing in-memory types:

- **`CatalogAggregate`** (input) — owns `Margins` (with `MonthlyData: IDictionary<DateTime, MarginData>` and `Averages`), `PurchaseHistory`, `SalesHistory`, `EshopPrice`, and core product metadata (`ProductCode`, `ProductName`, `Type`, `ProductFamily`, `ProductCategory`).
- **`MarginData`** — exposes `M0`, `M1`, `M2`, `M1_A` margin slices, each with `Amount`, `Percentage`, `CostLevel`.
- **`AnalyticsProduct`** (output) — flat DTO consumed by analytics callers. Field set unchanged.
- **`SalesDataPoint`** — `{ Date, AmountB2B, AmountB2C }`.

## API / Interface Design

Internal refactor only. No HTTP endpoints, no MediatR handlers, no events, no UI changes.

**Internal change:**
```csharp
public class AnalyticsRepository
{
    // unchanged public methods:
    public async IAsyncEnumerable<AnalyticsProduct> StreamProductsWithSalesAsync(/* ... */) { /* now calls MapToAnalyticsProduct */ }
    public async Task<IReadOnlyList<AnalyticsProduct>> GetProductAnalysisDataAsync(/* ... */) { /* now calls MapToAnalyticsProduct */ }

    // new private helper:
    private AnalyticsProduct MapToAnalyticsProduct(CatalogAggregate product, DateTime fromDate, DateTime toDate);
}
```

## Dependencies

- **`CatalogAggregate`** and `MarginData` types — no changes required.
- **Issue #1805** (Catalog→Analytics decoupling refactor) — downstream consumer. The helper is intentionally shaped so it lifts cleanly into `CatalogAnalyticsSourceAdapter` when #1805 lands. This spec does **not** preempt #1805; it only removes duplication ahead of it.
- No new NuGet packages.

## Out of Scope

- Moving the helper into `CatalogAnalyticsSourceAdapter` — that is #1805's job.
- Changing how `MarginAmount` falls back (preserving current `marginData.Averages.M0.Amount` fallback).
- Changing `PurchasePrice` semantics (still latest `PurchaseHistory` entry by `Date`).
- Adding new fields to `AnalyticsProduct`.
- Performance optimization of the underlying `MonthlyData` lookup or `PurchaseHistory` ordering.
- Renaming `AnalyticsRepository` or restructuring the file.
- Behavior changes other than the `SalesHistory` date-filter fix in `GetProductAnalysisDataAsync`.

## Open Questions

None.

## Status: COMPLETE