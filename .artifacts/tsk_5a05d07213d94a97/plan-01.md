# Plan: Remove duplicate margin-total calculation in GetProductMarginSummaryHandler

## Summary

`GetProductMarginSummaryHandler.GenerateTopProducts` recomputes each group's total margin via the private
`CalculateTotalMarginForLevel` helper, even though `MarginCalculator.CalculateAsync` already computed and stored
the identical value in `calculationResult.GroupTotals[groupKey]`. This causes every product's `SalesHistory` to be
summed twice per request. The fix is a pure substitution: use the pre-computed group total instead of
recalculating it, then delete the now-unused helper method.

## Context

Confirmed by reading both files directly:

- `MarginCalculator.CalculateAsync` (`backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs:60-81`)
  iterates products, skips any with `product.MarginAmount <= 0` (line 62-63), and for surviving products computes
  `totalSold * GetMarginAmountForLevel(product, marginLevel)`, accumulating into `groupTotals[groupKey]` and
  appending the product to `groupProducts[groupKey]`.
- `GetProductMarginSummaryHandler.GenerateTopProducts` (`GetProductMarginSummaryHandler.cs:72-120`) later reads
  `products = calculationResult.GroupProducts[kvp.Key]` — i.e. exactly the same filtered set the calculator used —
  and calls `CalculateTotalMarginForLevel(products, marginLevel)` (line 84), which re-runs the identical
  `totalSold * GetMarginAmountForLevel(...)` sum over that same product list and margin level (passed in from
  `request.MarginLevel` via the handler, unchanged).
- Because both the product set (`GroupProducts[key]` vs. the products actually summed into `GroupTotals[key]`)
  and the margin level are identical, `kvp.Value` (`calculationResult.GroupTotals[kvp.Key]`) is mathematically
  equal to `CalculateTotalMarginForLevel(products, marginLevel)` in every case. The recomputation is pure wasted
  work — one full extra pass over every product's `SalesHistory` per request — with no behavioral difference.
- An existing test suite (`backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`)
  already asserts on `TotalMargin` / group totals for multiple scenarios (basic grouping, zero-sales groups,
  pre-seeded `MarginCalculationResult` with `TotalMargin = 500m`, etc.), so the fix's correctness is covered by
  the current test suite without needing new tests.

## Functional requirements

**FR-1: `TopProductDto.TotalMargin` uses the pre-computed group total instead of recomputing it.**
- In `GenerateTopProducts`, replace `var totalMarginForLevel = CalculateTotalMarginForLevel(products, marginLevel);`
  with `var totalMarginForLevel = kvp.Value;`.
- Acceptance: for every existing test case in `GetProductMarginSummaryHandlerTests.cs` that asserts on
  `TotalMargin` (request-level `result.TotalMargin` and any per-group/top-product margin values), results are
  unchanged before and after the edit.

**FR-2: Remove the now-dead `CalculateTotalMarginForLevel` private method.**
- Delete lines 122-130 (the method and its XML doc comment) from `GetProductMarginSummaryHandler.cs`.
- Acceptance: `dotnet build` succeeds with no "unused private method" warnings (if such warnings are enabled) and
  no other call site references `CalculateTotalMarginForLevel` (confirm via `grep -rn CalculateTotalMarginForLevel backend/`).

## Non-functional requirements

- **Performance**: eliminates one full extra iteration over every product's `SalesHistory` collection per
  `GetProductMarginSummary` request (previously O(2N), now O(N) for this part of the computation). No new
  allocations introduced.
- **Behavioral parity**: this is a logic-preserving refactor — response payloads (`TopProducts[].TotalMargin`,
  `TotalMargin`, ordering, ranks) must be byte-for-byte identical to current behavior for all inputs.

## Data model

No data model changes. `MarginCalculationResult.GroupTotals` (`Dictionary<string, decimal>`) already carries the
value now being reused; no new fields or types needed.

## Interfaces

No API/contract changes. `GetProductMarginSummaryResponse`, `TopProductDto`, and the `AnalyticsController` endpoint
signature are untouched — this is an internal handler implementation detail.

## Dependencies and scope

- **Depends on**: `IMarginCalculator.CalculateAsync` continuing to populate `GroupTotals` for the same
  `marginLevel` and product set used to build `GroupProducts` (already true today; no change to `MarginCalculator.cs`).
- **In scope**: `GetProductMarginSummaryHandler.cs` only — the `GenerateTopProducts` method body and removal of
  `CalculateTotalMarginForLevel`.
- **Out of scope**: `MarginCalculator.cs`, `GetGroupAggregatedMarginData` (a separate, non-duplicate weighted-average
  calculation used for `M0Amount`/`M1Amount`/`M2Amount`/percentages/prices — not touched by this finding), monthly
  breakdown generation, sorting logic, any test file changes (existing tests are expected to pass unmodified and
  serve as the regression check).

## Rough plan

1. Open `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`.
2. In `GenerateTopProducts`, replace the `CalculateTotalMarginForLevel(products, marginLevel)` call (line 84) with
   `kvp.Value`.
3. Delete the private `CalculateTotalMarginForLevel` method (lines 122-130) and its preceding XML doc comment
   (lines 122-124).
4. Confirm `products` local variable (line 78, `calculationResult.GroupProducts[kvp.Key]`) is still used elsewhere
   in the method (it is — passed to `GetGroupAggregatedMarginData(products)` on line 81); no further cleanup needed.
5. Run `grep -rn CalculateTotalMarginForLevel backend/` to confirm no remaining references (including tests).
6. Run `dotnet build` and `dotnet format` per repo validation rules.
7. Run the existing test suite, focused first on
   `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`, then the full
   backend suite, confirming all tests pass unmodified.

## Open questions

- None. The finding is a straightforward, fully-verified logic-preserving substitution; no ambiguity in scope or
  approach.
