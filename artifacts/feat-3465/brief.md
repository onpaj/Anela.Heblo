## Module
Analytics

## Finding
`GetProductMarginSummaryHandler` (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`) is 242 lines and embeds two substantial business operations as private methods:

1. **`CalculateGroupMarginData` (lines 122–158)** — Computes weighted-average margin amounts and percentages across a group of products, branching on whether total sales are zero. This is margin calculation logic that belongs in `IMarginCalculator`.

2. **`ApplySorting` (lines 163–215)** — A 13-branch `switch` over string sort keys, sorting a `List`. At 55 lines, this is a self-contained, testable operation that should not live in a handler.

Handlers are meant to orchestrate — call a service, map the result, return the response. Both methods above are business logic: `CalculateGroupMarginData` is a calculation (the same concern as `IMarginCalculator.CalculateForProduct`), and `ApplySorting` is a deterministic, multi-branch algorithm.

## Why it matters
- The handler cannot be tested for sorting or weighted-average correctness without instantiating and wiring all handler dependencies.
- `CalculateGroupMarginData` duplicates the "weighted average" pattern that `IMarginCalculator` is designed to own, creating two parallel implementations of margin math.
- Both methods grow independently of the handler's orchestration logic, making the file harder to maintain.

## Suggested fix
**Option A — minimal:** Move `ApplySorting` to a static helper (e.g., `TopProductSorter.Sort(products, sortBy, sortDescending)`) in the `Services/` folder. Move `CalculateGroupMarginData` into `IMarginCalculator` as a new method `AggregateGroupMarginData(List products)`.

**Option B — preferred:** Add a `GetGroupAggregatedMarginData` method to `IMarginCalculator` (the correct owner of all margin math), and move sorting into a dedicated `ITopProductSorter` or a static utility — consistent with the module's existing pattern of extracting each concern into a named service.

---
_Filed by daily arch-review routine on 2026-07-03._
