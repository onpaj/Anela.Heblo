# Implementation: extract-filteritems

## What was implemented

Moved the per-item filtering predicate `ShouldIncludeItem` out of
`GetPurchaseStockAnalysisHandler` and into `StockAnalysisCalculator` as a new
public `FilterItems` method, matching the shape of the existing
`ItemFilterService.FilterItems` pattern elsewhere in the codebase. The handler
now delegates to `_stockAnalysisCalculator.FilterItems(...)` instead of doing
the filtering inline.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs` — added `FilterItems(List<StockAnalysisItemDto>, GetPurchaseStockAnalysisRequest)` to the interface.
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs` — added `FilterItems` (public) and `ShouldIncludeItem` (private helper), moved verbatim from the handler.
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs` — removed the private `ShouldIncludeItem` method and the inline `.Where(item => ShouldIncludeItem(item, request))` call; replaced with `_stockAnalysisCalculator.FilterItems(allAnalysisItems, request)`.
- `backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs` — added `MakeItem` test helper and three new test methods covering `FilterItems`: `FilterItems_StatusFilter_IncludesOnlyMatchingSeverity` (theory over all `StockStatusFilter` values), `FilterItems_OnlyConfiguredTrue_ExcludesUnconfiguredItems`, `FilterItems_OnlyConfiguredTrue_KeepsConfiguredItemsMatchingStatus`.

## Tests

- `StockAnalysisCalculatorTests.cs` — 3 new test methods (1 theory with 7 cases + 2 facts) verifying `FilterItems`'s status-filter and `OnlyConfigured` behavior, matching the logic previously tested indirectly through the handler.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Purchase"
```

Result: 368 passed, 1 failed, 369 total. The 1 failure
(`PurchaseOrderRepositoryHistorySqlShapeTests.GetHistoryAsync_EmitsSqlThatTouchesOnlyHistoryTable`)
is a pre-existing environmental failure — Testcontainers/Docker is not
available in this sandbox — and is unrelated to this task's change (it uses
`PostgresSharedContainerFixture`, no relation to `StockAnalysisCalculator` or
`GetPurchaseStockAnalysisHandler`). All `StockAnalysisCalculatorTests`,
including the new `FilterItems` tests, passed.

`dotnet build` of `Anela.Heblo.Application.csproj` also passed cleanly
(pre-existing warnings only, 0 errors).

## Notes

No deviations from the task context. The `using` for
`GetPurchaseStockAnalysisRequest`/`StockStatusFilter` was already present in
`StockAnalysisCalculatorTests.cs`, so no import changes were needed there.
`StockSeverity`/`StockStatusFilter` remain used elsewhere in
`GetPurchaseStockAnalysisHandler.cs` (in `CalculateSummary`), so no unused
`using` directives were introduced by removing `ShouldIncludeItem`.

## PR Summary

Moved the `ShouldIncludeItem` filtering predicate out of
`GetPurchaseStockAnalysisHandler` and into `StockAnalysisCalculator.FilterItems`,
continuing the extraction of business logic from the handler into the
dedicated calculator service (following the same pattern as the
`AnalyzeItem` extraction done in the prior task). The handler now delegates
filtering to the calculator instead of implementing it inline.

### Changes
- `IStockAnalysisCalculator.cs` — added `FilterItems` to the interface
- `StockAnalysisCalculator.cs` — added `FilterItems` + `ShouldIncludeItem` (moved from handler)
- `GetPurchaseStockAnalysisHandler.cs` — removed `ShouldIncludeItem`, delegates to `_stockAnalysisCalculator.FilterItems`
- `StockAnalysisCalculatorTests.cs` — added `FilterItems` test coverage

## Status
DONE
