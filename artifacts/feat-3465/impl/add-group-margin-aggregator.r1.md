# Implementation: add-group-margin-aggregator

## What was implemented
Added `GetGroupAggregatedMarginData(List<AnalyticsProduct> products)` to `IMarginCalculator`/`MarginCalculator` as a verbatim move of the weighted-average / simple-average aggregation logic, and created the new public `GroupMarginData` class in its own file. The handler itself is untouched by this task — its private `CalculateGroupMarginData` method and internal `GroupMarginData` class still exist and are still used; they will be removed in the next task (`wire-group-margin-aggregator-into-handler`).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/GroupMarginData.cs` — new public class with the 8 decimal properties (M0-M2 amounts/percentages, SellingPrice, PurchasePrice).
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs` — added `GetGroupAggregatedMarginData` to `IMarginCalculator` interface and its implementation in `MarginCalculator`.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/MarginCalculatorTests.cs` — added 3 new test methods.

## Tests
- `GetGroupAggregatedMarginData_EmptyList_ReturnsDefaultGroupMarginData` — empty list returns all-zero `GroupMarginData`.
- `GetGroupAggregatedMarginData_ZeroTotalSales_ReturnsSimpleAverage` — two products with no sales history average all 8 fields arithmetically.
- `GetGroupAggregatedMarginData_MultipleProductsWithSales_ReturnsWeightedAverage` — two products with sales history are weighted by units sold.

## How to verify
`dotnet test --filter "FullyQualifiedName~MarginCalculatorTests"` from the repo root.

## Notes
Followed the task-context file's exact code verbatim. No deviations.

## PR Summary
Extracted the weighted/simple-average margin aggregation logic that was embedded in `GetProductMarginSummaryHandler` into `IMarginCalculator.GetGroupAggregatedMarginData`, alongside a new public `GroupMarginData` DTO, so the calculation is unit-testable independent of the handler. This is step 1 of 4 in the SRP refactor from issue #3465; the handler still uses its own private copy until the next task switches the call site over.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/GroupMarginData.cs` (new)
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs`
- `backend/test/Anela.Heblo.Tests/Features/Analytics/MarginCalculatorTests.cs`

## Status
DONE
