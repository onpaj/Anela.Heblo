# Code Review: extract-filteritems

## Summary
The implementation moves `ShouldIncludeItem`/`FilterItems` out of
`GetPurchaseStockAnalysisHandler` into `StockAnalysisCalculator`, exactly as
specified in the task context, with the handler now delegating to the
calculator. The three specified test cases were added verbatim to
`StockAnalysisCalculatorTests.cs`.

## Review Result: PASS

### task: extract-filteritems
**Status:** PASS

Verified:
- `IStockAnalysisCalculator.FilterItems(List<StockAnalysisItemDto>, GetPurchaseStockAnalysisRequest)` added to the interface, matching the spec signature and XML doc.
- `StockAnalysisCalculator.FilterItems` and the moved private `ShouldIncludeItem` implemented verbatim per spec (status-filter switch + `OnlyConfigured` short-circuit preserved).
- `GetPurchaseStockAnalysisHandler` no longer contains `ShouldIncludeItem`; filtering now delegates to `_stockAnalysisCalculator.FilterItems(allAnalysisItems, request)` as specified. `StockSeverity`/`StockStatusFilter` remain referenced elsewhere in the handler (`CalculateSummary`), so no dangling/unused usings.
- `StockAnalysisCalculatorTests.cs` contains the exact `MakeItem` helper and the three specified test methods (`FilterItems_StatusFilter_IncludesOnlyMatchingSeverity` theory over all filter values, `FilterItems_OnlyConfiguredTrue_ExcludesUnconfiguredItems`, `FilterItems_OnlyConfiguredTrue_KeepsConfiguredItemsMatchingStatus`).
- `dotnet build` of the Application project: 0 errors.
- `dotnet test --filter "FullyQualifiedName~Purchase"`: 368 passed, 1 failed, 369 total. The 1 failure (`PurchaseOrderRepositoryHistorySqlShapeTests.GetHistoryAsync_EmitsSqlThatTouchesOnlyHistoryTable`) is a pre-existing Testcontainers/Docker-availability failure in this sandbox, unrelated to `StockAnalysisCalculator`/`GetPurchaseStockAnalysisHandler` — it exercises a Postgres container fixture for an unrelated repository test. Not a regression introduced by this task.

No functional requirement is unmet, no architecture deviation, tests present and passing for the changed behavior.

## Docs to Update
(none — internal refactor only, no public behavior or API surface change)

## Overall Notes
This continues the same extraction pattern used by the prior
`add-severitycalculator-dependency-and-analyzeitem` task (moving handler
logic into `StockAnalysisCalculator`), keeping the handler thin. No
cross-cutting concerns.

**Status:** PASS
