# Implementation: extract-stock-analysis-calculator

## What was implemented
Extracted `CalculateStockEfficiency` and `CalculateRecommendedOrderQuantity` out of `GetPurchaseStockAnalysisHandler` into a new `IStockAnalysisCalculator` / `StockAnalysisCalculator` service pair, mirroring the existing `IStockSeverityCalculator` pattern exactly (namespace, doc style, stateless class, Scoped DI registration). This was a pure move — method bodies were relocated verbatim with no behavioral changes, no API/DTO changes.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs` — new interface declaring `CalculateStockEfficiency` and `CalculateRecommendedOrderQuantity`, with XML doc comments matching `IStockSeverityCalculator`'s style.
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs` — new stateless implementation class, method bodies moved verbatim from the handler.
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs` — added `IStockAnalysisCalculator stockAnalysisCalculator` constructor parameter (ordered after `stockSeverityCalculator`, before `logger`), removed the two private calculation methods, updated the two call sites in `AnalyzeStockItem` to use `_stockAnalysisCalculator`.
- `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs` — registered `IStockAnalysisCalculator` → `StockAnalysisCalculator` as `Scoped`, next to the existing `IStockSeverityCalculator` registration, with its own comment.
- `backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs` — new test file, 9 `[Fact]` tests covering all required cases for both methods.
- `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs` — constructor wiring only: now passes a real `new StockAnalysisCalculator()` instead of leaving it unmocked (new required constructor argument). No assertions changed.
- `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs` — same constructor wiring change, no assertions changed.

## Tests
- `StockAnalysisCalculatorTests.cs`: covers `CalculateStockEfficiency` (optimal>0 branch, optimal<=0 & min>0 branch, both<=0 → 0) and `CalculateRecommendedOrderQuantity` (both<=0 → null, needed<=0 → null, min*2 target branch, moq > shortfall → moq, moq < shortfall → shortfall, moq null/empty/unparseable → raw shortfall ignoring moq).
- Existing `GetPurchaseStockAnalysisHandlerTests.cs` and `GetPurchaseStockAnalysisHandlerDiacriticsTests.cs` continue to pass unchanged in assertions; `Handle_SortByStockEfficiency_ReturnsSortedItems` now exercises real computed stock-efficiency values via the real `StockAnalysisCalculator` (not a zero-returning mock).

## How to verify
```
cd backend
dotnet build ../Anela.Heblo.sln
dotnet format ../Anela.Heblo.sln --verify-no-changes
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~StockAnalysisCalculatorTests|FullyQualifiedName~GetPurchaseStockAnalysisHandlerTests|FullyQualifiedName~GetPurchaseStockAnalysisHandlerDiacriticsTests"
```
All 30 tests in the filtered run pass. A full `--filter "FullyQualifiedName~Purchase"` run passes 280/281; the single failure (`PurchaseOrderRepositoryHistorySqlShapeTests.GetHistoryAsync_EmitsSqlThatTouchesOnlyHistoryTable`) is a pre-existing, unrelated Testcontainers/Docker environment failure (Docker is not available in this sandbox) — not touched by this change.

## Notes
No deviations from the task spec. Solution-wide `dotnet build` succeeds (0 errors, pre-existing warnings only, unrelated to this change). `dotnet format --verify-no-changes` reports no diffs.

## PR Summary
This is a pure refactor that extracts two pure calculation methods (`CalculateStockEfficiency`, `CalculateRecommendedOrderQuantity`) from `GetPurchaseStockAnalysisHandler` into a new stateless `IStockAnalysisCalculator` service, following the same pattern already established by `IStockSeverityCalculator`. No request/response contracts, HTTP endpoints, or business logic changed — method bodies moved verbatim. The handler now depends on `IStockAnalysisCalculator` via constructor injection (registered as Scoped in `PurchaseModule`), and its two call sites in `AnalyzeStockItem` were updated accordingly. New unit tests (`StockAnalysisCalculatorTests`) cover the extracted service directly and independently of the handler. Existing handler tests were updated only to satisfy the new constructor signature, wiring in a real (non-mocked) `StockAnalysisCalculator` instance so that `Handle_SortByStockEfficiency_ReturnsSortedItems` continues to exercise genuine sort behavior rather than collapsing into an all-zero comparison.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs` — new interface for stock analysis calculations.
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs` — new stateless implementation, methods moved verbatim from the handler.
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs` — injects and delegates to `IStockAnalysisCalculator`, private calculation methods removed.
- `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs` — registers `IStockAnalysisCalculator` as Scoped.
- `backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs` — new unit tests for the extracted service.
- `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs` — constructor wiring updated for the new dependency.
- `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs` — constructor wiring updated for the new dependency.

## Status
DONE
