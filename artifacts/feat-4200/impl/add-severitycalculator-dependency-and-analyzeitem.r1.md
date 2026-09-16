# Implementation: add-severitycalculator-dependency-and-analyzeitem

## What was implemented
Moved `AnalyzeStockItem` (and its private helper `GetLastPurchaseInfo`) out of
`GetPurchaseStockAnalysisHandler` and into `StockAnalysisCalculator` as a new
public `AnalyzeItem` method on `IStockAnalysisCalculator`. `StockAnalysisCalculator`
now takes `IStockSeverityCalculator` as a constructor dependency so it can compute
severity locally instead of delegating back to the handler. The handler's
constructor dropped its own `IStockSeverityCalculator` dependency (no longer used
directly) and its `Handle()` method now calls
`_stockAnalysisCalculator.AnalyzeItem(s, fromDate, toDate)` instead of the removed
private `AnalyzeStockItem`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs` — added `AnalyzeItem(MaterialStockSnapshot, DateTime, DateTime)` to the interface
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs` — added `IStockSeverityCalculator` constructor dependency; added `AnalyzeItem` and private `GetLastPurchaseInfo`, moved verbatim from the handler (only the receiver of severity/efficiency/recommended-quantity calls changed from cross-class to local)
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs` — removed `IStockSeverityCalculator` field/constructor param, removed private `AnalyzeStockItem`/`GetLastPurchaseInfo`, `Handle()` now calls `_stockAnalysisCalculator.AnalyzeItem(...)`
- `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs` — updated handler construction to pass `new StockAnalysisCalculator(_stockSeverityCalculatorMock.Object)` and drop the now-removed constructor arg
- `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs` — same handler construction fix
- `backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs` — this file already existed (with tests for `CalculateStockEfficiency`/`CalculateRecommendedOrderQuantity`, constructed as `new StockAnalysisCalculator()`); updated its constructor call site to the new `IStockSeverityCalculator`-taking constructor and appended the four `AnalyzeItem` tests from the task context (daily consumption/days-until-stockout, zero consumption, no last purchase, with last purchase) rather than overwriting the file

## Tests
- `StockAnalysisCalculatorTests` — existing efficiency/recommended-quantity tests (updated constructor) plus 4 new `AnalyzeItem_*` tests
- `GetPurchaseStockAnalysisHandlerTests`, `GetPurchaseStockAnalysisHandlerDiacriticsTests` — constructor call sites fixed, no behavioral test changes

## How to verify
`dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — 0 errors.
`dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` then run the built
`Anela.Heblo.Tests.dll` directly via `vstest.console.dll --TestCaseFilter:"FullyQualifiedName~Purchase"`
(see Notes — plain `dotnet test` hangs in this sandbox for an unrelated, documented reason).
Result: 359 passed, 1 failed, 0 skipped, 360 total. The 1 failure
(`PurchaseOrderRepositoryHistorySqlShapeTests.GetHistoryAsync_EmitsSqlThatTouchesOnlyHistoryTable`)
requires Docker/Testcontainers to spin up a Postgres container, which isn't available in this
sandbox — pre-existing, unrelated to this change. All 40 tests in the three files this task
touched (`StockAnalysisCalculatorTests`, `GetPurchaseStockAnalysisHandlerTests`,
`GetPurchaseStockAnalysisHandlerDiacriticsTests`) pass: 40/40.
`dotnet format ... --verify-no-changes` on all six changed files: clean, no formatting issues.

## Notes
- The task context's `StockAnalysisCalculatorTests.cs` "Create" step assumed the file didn't exist yet; it already did (with pre-existing coverage for the two original calculator methods). Preserved those tests and appended the new ones rather than replacing the file, per the project's surgical-changes rule.
- Plain `dotnet test` on this test project hangs in this sandbox environment shortly after
  `Generating access matrix artifacts...` (a pre-existing, documented gotcha —
  `memory/gotchas/dotnet-build-hangs-nodereuse-accessmatrixgen.md` — the API project's
  `GenerateAccessMatrix` `<Exec>` target spawns a nested `dotnet run`, which deadlocks with
  `dotnet test`'s own build-then-run pipeline in this sandbox). Worked around it using the
  documented fallback: plain `dotnet build` on the test project (which does not hang), then
  invoking `vstest.console.dll` directly against the built `Anela.Heblo.Tests.dll`. This is a
  pre-existing environment issue, unrelated to this change, and does not affect CI/normal
  developer machines.

## PR Summary
Moved per-item stock analysis logic (`AnalyzeStockItem`/`GetLastPurchaseInfo`) out of `GetPurchaseStockAnalysisHandler` and into `StockAnalysisCalculator.AnalyzeItem`, giving `StockAnalysisCalculator` its own `IStockSeverityCalculator` dependency so severity determination moves with it. The handler no longer needs `IStockSeverityCalculator` directly and simply delegates to `_stockAnalysisCalculator.AnalyzeItem(...)`.

### Changes
- `IStockAnalysisCalculator.cs` / `StockAnalysisCalculator.cs` — new `AnalyzeItem` method, moved verbatim from the handler
- `GetPurchaseStockAnalysisHandler.cs` — dropped `IStockSeverityCalculator` dependency, delegates to `AnalyzeItem`
- Handler test fixtures updated to the new `StockAnalysisCalculator` constructor
- `StockAnalysisCalculatorTests.cs` extended with `AnalyzeItem` coverage

## Status
DONE
