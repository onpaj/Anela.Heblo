# Code Review: extract-sortitems

## Summary
The `SortItems` method was moved verbatim from `GetPurchaseStockAnalysisHandler` into `StockAnalysisCalculator`, exposed through `IStockAnalysisCalculator`, and the handler now delegates to it — exactly matching the task's Steps 3–5. All five specified test cases were added exactly as given in the task context, and the full switch/fallback/descending logic is unchanged.

## Review Result: PASS

### task: extract-sortitems
**Status:** PASS

## Docs to Update
(none — this is an internal refactor of existing business logic with no change to public API surface, request/response contracts, or operational behavior)

## Overall Notes
- Interface and implementation match the task spec's Step 3/4 snippets exactly, including XML doc comments.
- The handler's call site correctly changed from `SortItems(...)` to `_stockAnalysisCalculator.SortItems(...)`, and the private method was removed.
- Test run (`dotnet test ... --filter "FullyQualifiedName~Purchase"`): 373 passed, 1 failed. The failure (`PurchaseOrderRepositoryHistorySqlShapeTests`) is a pre-existing, unrelated environment limitation (Docker/testcontainers unavailable in this sandbox) — not caused by this change and not something a headless review can hold against this task.
