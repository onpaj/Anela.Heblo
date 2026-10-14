# Implementation: extract-sortitems

## What was implemented

Moved the `SortItems` sorting logic out of `GetPurchaseStockAnalysisHandler` and
into `StockAnalysisCalculator`, exposed via `IStockAnalysisCalculator`, following
the same pattern already used for `AnalyzeItem` and `FilterItems`. The handler now
delegates to `_stockAnalysisCalculator.SortItems(...)` instead of calling a
private method on itself.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs` — added `SortItems` to the interface with XML doc comments
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs` — added the `SortItems` implementation, moved verbatim from the handler
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs` — removed the private `SortItems` method; the call site now delegates to `_stockAnalysisCalculator.SortItems(...)`
- `backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs` — added `MakeSortableItem` helper and five `SortItems_*` test cases (ascending/descending by product code, by available stock, null-last-purchase-date handling, and the default/unknown-sort-key fallback)

## Tests

- `StockAnalysisCalculatorTests.SortItems_ByProductCode_Ascending`
- `StockAnalysisCalculatorTests.SortItems_ByProductCode_Descending_ReversesOrder`
- `StockAnalysisCalculatorTests.SortItems_ByAvailableStock`
- `StockAnalysisCalculatorTests.SortItems_ByLastPurchaseDate_NullTreatedAsMinValue`
- `StockAnalysisCalculatorTests.SortItems_DefaultFallback_SortsByStockEfficiency`

## How to verify

```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Purchase"
```

Result: 373 passed, 1 failed (`PurchaseOrderRepositoryHistorySqlShapeTests.GetHistoryAsync_EmitsSqlThatTouchesOnlyHistoryTable`) — that failure is pre-existing and unrelated: it requires Docker/testcontainers for a Postgres container, which is not available in this sandbox. All Purchase/StockAnalysisCalculator-specific tests, including the five new `SortItems_*` tests, pass.

## Notes

The implementation is a verbatim move of the existing `SortItems` logic (same
switch expression, same default fallback to `StockEfficiencyPercentage`, same
`descending` handling via `Reverse()`), per the task's Step 4 instruction. No
behavioral changes were introduced.

## PR Summary
Moved `SortItems` out of `GetPurchaseStockAnalysisHandler` and into `StockAnalysisCalculator`/`IStockAnalysisCalculator`, continuing the extraction of stock-analysis business logic out of the handler (alongside the already-completed `AnalyzeItem` and `FilterItems` extractions). The handler now only orchestrates: fetch snapshots, delegate to the calculator for analysis/filtering/sorting, page, and return.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs` — added `SortItems` to the interface
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs` — added `SortItems` implementation
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs` — removed private `SortItems`, delegates to the calculator
- `backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs` — added `SortItems` test coverage

## Status
DONE
