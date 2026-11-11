# Specification: Correct misleading "streaming / minimizes memory" comments in Analytics margin code

## Summary
`MarginCalculator.CalculateAsync` is documented as a "streaming approach to minimize memory usage", but it retains every `AnalyticsProduct` (with its full `SalesHistory`) in `groupProducts` and returns them in `MarginCalculationResult.GroupProducts`. This work makes the comments truthful. No behaviour changes.

## Background
Arch-review finding #4421. `GroupProducts` is load-bearing: `GetProductMarginSummaryHandler.GenerateTopProducts` (display name, `GetGroupAggregatedMarginData`) and `MonthlyBreakdownGenerator` (line ~50) consume it. Streaming only avoids a second full copy during the fetch; working set stays O(n_products x sales_points). The same overstated claims appear in sibling code (`GetProductMarginSummaryHandler`, `AnalyticsRepository`, class summary of `MarginCalculator`).

## Functional Requirements

### FR-1: Accurate comments in MarginCalculator
Replace the `// 🔒 PERFORMANCE FIX` / "streaming approach to minimize memory usage" wording on `MarginCalculator` (class summary) and `CalculateAsync` (summary) with text stating: products are consumed from an async stream, but all accepted products are retained per group in `GroupProducts` because downstream consumers need them; peak memory is O(products x sales history).
**Acceptance criteria:**
- No remaining claim in `MarginCalculator.cs` that memory usage is minimized/bounded by streaming.
- Comment names the retention explicitly and why (downstream consumers).

### FR-2: Accurate comments in directly misleading call sites
Correct the equivalent claims in `GetProductMarginSummaryHandler` (class summary, step 3 comment) and `AnalyticsRepository` (class/method summary) so they do not imply bounded memory.
**Acceptance criteria:**
- Comments say streaming avoids a duplicate full list at fetch time only.

### FR-3: Process doc
Update `docs/processes/calc-product-margin-summary.md` memory-related wording if it implies bounded memory (line 56 says "streams"), and bump `verified_at`; run `python3 scripts/process-docs/check.py index`.

## Non-Functional Requirements
### NFR-1: Performance
No runtime change. Behaviour, signatures, and `MarginCalculationResult` are unchanged.
### NFR-2: Security
None.

## Data Model
Unchanged.

## API / Interface Design
Unchanged.

## Dependencies
None.

## Out of Scope
The "deeper fix" (accumulating `GroupMarginData` inline, O(n_groups) memory). It changes `MarginCalculationResult`, `IMarginCalculator`, `MonthlyBreakdownGenerator` and tests, and no evidence of a memory problem exists. Can be a separate issue if memory is observed to be a problem.

## Open Questions
None.

## Status: COMPLETE
