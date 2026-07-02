# Specification: Test Coverage – GetPurchaseStockAnalysis Dual-Bucket Invariant and Summary Assertions

## Summary

Two untested invariants exist in `GetPurchaseStockAnalysisHandler`: (1) that the `Summary` always reflects the full item population regardless of active display filters, and (2) that specific `Summary` field values are arithmetically correct. Both gaps are high-risk because a regression would manifest as silently wrong dashboard totals rather than a crash or obvious failure.

## Background

`GetPurchaseStockAnalysisHandler` deliberately maintains two item buckets: `allAnalysisItems` (used for `CalculateSummary`) and `analysisItems` (filtered for display and paging). This split means a user can apply a severity or configuration filter and still see summary totals that represent the entire catalog snapshot. The existing filter tests assert only on `Items`; none verify that `Summary` is sourced from the unfiltered bucket. Separately, `CalculateSummary` aggregates six severity counts plus `TotalInventoryValue`, but no test pins any of those values — an off-by-one or sign error would be undetectable in CI.

## Functional Requirements

### FR-1: Dual-Bucket Invariant Test

Add a test that arranges a snapshot containing items of mixed severity (at least two severity levels), applies a `StockStatus` filter that excludes one severity level, and asserts both the filtered `Items` list and the unfiltered `Summary` counts independently.

**Acceptance criteria:**
- A new test method `Handle_FilterByCriticalStatus_SummaryReflectsAllItems` exists in `GetPurchaseStockAnalysisHandlerTests.cs`.
- The snapshot contains items of at least two distinct `StockSeverity` values (e.g. `Critical` and `Optimal`).
- `request.StockStatus` is set to `StockStatusFilter.Critical`.
- The assertion on `response.Items` confirms only `Critical` items are present.
- The assertion on `response.Summary.TotalProducts` equals the total snapshot count (not the filtered count).
- The assertion on `response.Summary.OptimalCount` is greater than zero, proving non-critical items are counted in the summary even though they are absent from `Items`.
- The test fails if either bucket assertion is removed.

### FR-2: Summary Field Value Assertions

Add a test that pins every field produced by `CalculateSummary` to known, concrete values derived from a fully controlled snapshot.

**Acceptance criteria:**
- A new test method `Handle_CalculateSummary_AllFieldsAreCorrect` exists in `GetPurchaseStockAnalysisHandlerTests.cs`.
- The snapshot is fully deterministic: exact counts per severity, known `EffectiveStock` values, and known `LastPurchase.UnitPrice` values for each item.
- The test asserts `Summary.TotalProducts`, `Summary.CriticalCount`, `Summary.LowStockCount`, `Summary.OptimalCount`, `Summary.OverstockedCount`, `Summary.NotConfiguredCount`, and `Summary.TotalInventoryValue` each against a hand-computed expected value.
- `Summary.AnalysisPeriodStart` and `Summary.AnalysisPeriodEnd` are asserted to equal `fromDate` and `toDate` as supplied in the request.
- `Summary.TotalInventoryValue` is computed as `Sum(EffectiveStock * UnitPrice)` across all snapshot items (not filtered items), and the expected value in the test comment documents this formula explicitly.
- No `StockStatus` filter is applied in this test so that `allAnalysisItems` and `analysisItems` are identical; the purpose is purely to validate aggregation arithmetic, not the dual-bucket split (FR-1 covers that).

## Non-Functional Requirements

### NFR-1: Test Isolation

Each new test must set up its own mock data via the existing mock/stub pattern already used in `GetPurchaseStockAnalysisHandlerTests.cs`. No shared mutable state between tests.

### NFR-2: Readability

Expected values in FR-2 must be accompanied by an inline comment showing the arithmetic (e.g. `// 2 items × 10.0 stock × 5.00 price = 100.00`). This makes future maintenance tractable without re-deriving the formula.

### NFR-3: No Production Code Changes

These tests must pass against the existing handler implementation without modifying any production source file.

## Data Model

No new entities. Tests operate on the types already present:

- `GetPurchaseStockAnalysisRequest` — `StockStatus`, `PageNumber`, `PageSize`, `IsExport`, `FromDate`, `ToDate`, `OnlyConfigured`, `SearchTerm`
- `StockAnalysisItemDto` — `Severity`, `EffectiveStock`, `LastPurchase.UnitPrice`, `IsConfigured`
- `StockAnalysisSummaryDto` — `TotalProducts`, `CriticalCount`, `LowStockCount`, `OptimalCount`, `OverstockedCount`, `NotConfiguredCount`, `TotalInventoryValue`, `AnalysisPeriodStart`, `AnalysisPeriodEnd`
- `StockSeverity` enum — `Critical`, `Low`, `Optimal`, `Overstocked`, `NotConfigured`
- `StockStatusFilter` enum — mirrors `StockSeverity` plus an `All` / default case

## API / Interface Design

Not applicable — this spec covers test additions only, with no change to the handler's public interface or HTTP surface.

## Dependencies

- Existing test infrastructure in `GetPurchaseStockAnalysisHandlerTests.cs` (xUnit, NSubstitute or Moq — whichever is already in use).
- `IMaterialCatalog.GetStockAnalysisSnapshotsAsync` mock — already stubbed in existing tests; the new tests extend the same pattern with richer fixture data.

## Out of Scope

- The `Handle_InvalidDateRange_ReturnsError` branch — already covered.
- The `Handle_ExportTrue_BypassesPaginationAndReturnsAllFilteredItems` branch — already covered.
- The `GetPurchaseStockAnalysisHandlerDiacriticsTests` file — diacritic normalisation is already tested; no additions needed there.
- Czech diacritic search interaction with summary — not a known risk, excluded from this work.
- Refactoring `CalculateSummary` into a separate class or adding unit tests directly on it — not required; handler-level tests are sufficient and consistent with the existing test style.
- Performance or load testing of the handler.

## Open Questions

None.
