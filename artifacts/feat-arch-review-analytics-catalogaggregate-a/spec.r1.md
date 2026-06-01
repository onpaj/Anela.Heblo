# Specification: Extract `CatalogAggregate` → `AnalyticsProduct` Mapping Helper

## Summary
Eliminate verbatim duplication of the `CatalogAggregate` → `AnalyticsProduct` mapping logic in `AnalyticsRepository` by extracting a single private helper method. This consolidates ~60 lines of identical code, removes a latent inconsistency where one of the two call sites failed to filter `SalesHistory` by date range, and reduces the migration surface for the upcoming cross-module refactor (#1805).

## Background
`backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs` contains two methods — `StreamProductsWithSalesAsync` and `GetProductAnalysisDataAsync` — that each materialize an `AnalyticsProduct` from a `CatalogAggregate`. The mapping bodies are identical step-for-step:

1. Extract `marginData = product.Margins`.
2. Filter `marginData.MonthlyData` to entries inside `[fromDate, toDate]` to obtain `relevantMargins`.
3. Pick the latest entry, with a fallback to `marginData.MonthlyData.LastOrDefault()` when none falls in range.
4. Repeat the same `latestMarginEntry.Equals(default(KeyValuePair<DateTime, MarginData>))` guard for `MarginAmount`, `MaterialCost`, `HandlingCost`, `M0Amount`/`M1Amount`/`M2Amount`, and the three `*Percentage` fields.
5. Resolve `PurchasePrice` from the most recent `PurchaseHistory` entry.
6. Project the result into a `new AnalyticsProduct { ... }`.

The only behavioural difference today is that `StreamProductsWithSalesAsync` filters `SalesHistory` by `[fromDate, toDate]` while `GetProductAnalysisDataAsync` returns the full `SalesHistory` unfiltered. Per the brief, this is treated as latent drift to be corrected — both call sites should filter consistently going forward.

The mapping will need to move into a `CatalogAnalyticsSourceAdapter` when issue #1805 (cross-module Analytics refactor) lands. Removing the duplication now halves the migration surface for that work.

## Functional Requirements

### FR-1: Introduce `MapToAnalyticsProduct` helper
Add a private instance method on `AnalyticsRepository` with the signature:

```csharp
private AnalyticsProduct MapToAnalyticsProduct(
    CatalogAggregate product,
    DateTime fromDate,
    DateTime toDate)
```

The method encapsulates the full mapping pipeline (margin selection, fallback, per-field guards, purchase-price resolution, sales-history projection, and `AnalyticsProduct` construction) exactly as described in the brief.

**Acceptance criteria:**
- A single private method on `AnalyticsRepository` performs the complete mapping.
- All property assignments on the returned `AnalyticsProduct` match the existing mapping verbatim (same source expressions, same fallback semantics).
- The fallback to `marginData.MonthlyData.LastOrDefault()` when `relevantMargins` is empty is preserved.
- The `MarginAmount` field falls back to `marginData.Averages.M0.Amount` when no margin entry is available, matching current behaviour.
- All other margin-derived fields (`M0Amount`, `M1Amount`, `M2Amount`, `M0Percentage`, `M1Percentage`, `M2Percentage`, `MaterialCost`, `HandlingCost`) fall back to `0` when no margin entry is available, matching current behaviour.
- `PurchasePrice` resolves from the most recent `PurchaseHistory` entry ordered by `Date` descending, defaulting to `0` when absent or null.
- `SellingPrice` uses `product.EshopPrice?.PriceWithoutVat ?? 0`, and `EshopPriceWithoutVat` uses `product.EshopPrice?.PriceWithoutVat` (nullable), matching current behaviour.

### FR-2: Replace both duplicated blocks with helper calls
Both `StreamProductsWithSalesAsync` (lines 52–116) and `GetProductAnalysisDataAsync` (lines 168–231) must invoke `MapToAnalyticsProduct(product, fromDate, toDate)` in place of the inline mapping block.

**Acceptance criteria:**
- The inline mapping blocks in both methods are removed.
- Each call site invokes the helper with the method's own `fromDate` and `toDate` parameters.
- No other surrounding logic in these methods (streaming, iteration, batching, repository calls) is changed.
- The two methods are functionally equivalent to their previous behaviour in every respect *except* the `SalesHistory` filtering correction (see FR-3).

### FR-3: Correct `SalesHistory` filtering drift
`GetProductAnalysisDataAsync` currently emits `AnalyticsProduct.SalesHistory` without applying the date filter. After this change, both call sites must apply the same `s.Date >= fromDate && s.Date <= toDate` filter when projecting `SalesHistory` into `SalesDataPoint` entries.

**Acceptance criteria:**
- The helper's `SalesHistory` projection filters by `[fromDate, toDate]` inclusively.
- `GetProductAnalysisDataAsync` callers receive only sales data points inside the requested window.
- This is documented in the commit message / PR description as an intentional consistency fix.

### FR-4: Preserve external behaviour beyond the documented fix
No change is made to:
- Method signatures, return types, async semantics, or streaming behaviour of `StreamProductsWithSalesAsync` and `GetProductAnalysisDataAsync`.
- Repository registration, DI configuration, or call sites consuming these methods.
- The `AnalyticsProduct`, `SalesDataPoint`, `CatalogAggregate`, or `MarginData` types.

**Acceptance criteria:**
- Public surface of `AnalyticsRepository` is unchanged.
- Callers of either method require no modification.
- No new types, interfaces, or files are introduced.

## Non-Functional Requirements

### NFR-1: Performance
The helper is a straight extraction. No additional allocations, enumerations, or repository calls are introduced. Streaming semantics of `StreamProductsWithSalesAsync` (per-product yielding) are preserved — the helper is invoked once per product, exactly as the inline block was.

### NFR-2: Maintainability
A future change to the mapping (new margin field, altered fallback, different purchase-price source) must require modification in exactly one location. This is the primary motivation for the refactor and the chief acceptance metric for code review.

### NFR-3: Test coverage
Existing unit tests covering `StreamProductsWithSalesAsync` and `GetProductAnalysisDataAsync` must continue to pass. Any test asserting the *previously incorrect* unfiltered `SalesHistory` behaviour in `GetProductAnalysisDataAsync` must be updated to reflect the corrected, date-filtered output. If no test currently exercises the date-filter consistency between the two methods, add one.

### NFR-4: Validation
Before completion: `dotnet build` and `dotnet format` must succeed; all touched tests must pass.

## Data Model
No data-model changes. The refactor touches mapping logic only.

Entities referenced (read-only, unchanged):
- `CatalogAggregate` — source, supplies `Margins` (`MarginData` with `MonthlyData : IDictionary<DateTime, MarginData>` and `Averages`), `PurchaseHistory`, `SalesHistory`, `EshopPrice`, `ProductCode`, `ProductName`, `Type`, `ProductFamily`, `ProductCategory`.
- `AnalyticsProduct` — target DTO; field layout unchanged.
- `SalesDataPoint` — projection target for `SalesHistory`; unchanged.

## API / Interface Design
No public API changes. Internal change only:

- **Added:** `private AnalyticsProduct AnalyticsRepository.MapToAnalyticsProduct(CatalogAggregate product, DateTime fromDate, DateTime toDate)`
- **Modified bodies:** `AnalyticsRepository.StreamProductsWithSalesAsync`, `AnalyticsRepository.GetProductAnalysisDataAsync` — inline mapping replaced with helper call.
- **Removed:** ~60 lines of duplicated mapping logic per method.

## Dependencies
- File scope: `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs` only.
- Related (informational, not blocked on / blocking): issue #1805 — cross-module Analytics refactor that will later relocate this helper into `CatalogAnalyticsSourceAdapter`. This spec does **not** perform that relocation.
- No new NuGet packages, no new project references, no new DI registrations.

## Out of Scope
- Moving the helper into `CatalogAnalyticsSourceAdapter` (deferred to #1805).
- Changing the shape, fields, or semantics of `AnalyticsProduct` or `SalesDataPoint`.
- Altering margin-fallback logic beyond the verbatim extraction (e.g. choosing a different fallback strategy, changing the `default(KeyValuePair<…>)` sentinel pattern).
- Adjusting how `PurchasePrice`, `SellingPrice`, or `EshopPriceWithoutVat` are sourced.
- Filtering `PurchaseHistory` by date range (current code uses the latest entry overall; this is preserved).
- Refactoring `StreamProductsWithSalesAsync` streaming/batching or `GetProductAnalysisDataAsync` query semantics.
- Adding new unit tests beyond what's required to lock in the `SalesHistory` filtering consistency (NFR-3).

## Open Questions
None.

## Status: COMPLETE