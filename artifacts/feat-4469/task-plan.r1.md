# Implementation Plan: Decouple IStockAnalysisCalculator from use case

**Goal:** Move three stock-analysis DTOs to `Purchase/Contracts/`, add `StockAnalysisFilter`, and make `FilterItems` use it. No behaviour change.

**Base path:** `backend/src/Anela.Heblo.Application/Features/Purchase` (called `$P` below). Tests in `backend/test/Anela.Heblo.Tests/Features/Purchase`.

### task: move-dtos-to-contracts
**Files:** create `$P/Contracts/StockAnalysisItemDto.cs`, `$P/Contracts/StockAnalysisSummaryDto.cs`, `$P/Contracts/LastPurchaseInfoDto.cs`; modify `$P/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs`.

- [ ] Copy each class verbatim (classes, same members and order) into its own file with `namespace Anela.Heblo.Application.Features.Purchase.Contracts;`. `StockAnalysisItemDto` uses `StockSeverity` (same namespace, no using needed).
- [ ] Remove the three classes from `GetPurchaseStockAnalysisResponse.cs`; keep `GetPurchaseStockAnalysisResponse` and its existing `using ...Contracts`.
- [ ] Add `using Anela.Heblo.Application.Features.Purchase.Contracts;` to any file that fails to compile (candidates: `$P/DashboardTiles/LowStockEfficiencyTile.cs`, `LowStockEfficiencyTileTests.cs`, controller, other Purchase tests).
- [ ] Run `dotnet build backend/Anela.Heblo.sln` (fix compile errors only); commit `refactor(purchase): move stock analysis DTOs to Contracts`.

### task: add-stock-analysis-filter
**Files:** create `$P/Contracts/StockAnalysisFilter.cs`; modify `$P/Services/IStockAnalysisCalculator.cs`, `$P/Services/StockAnalysisCalculator.cs`, `$P/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`, `StockAnalysisCalculatorTests.cs`.

- [ ] Write failing tests first: in `StockAnalysisCalculatorTests.cs` replace `new GetPurchaseStockAnalysisRequest { ... }` in the three `FilterItems_*` tests (lines ~297, 308, 319) with `new StockAnalysisFilter { StockStatus = ..., OnlyConfigured = ... }`. Build fails (type missing).
- [ ] Create `StockAnalysisFilter` class (`bool OnlyConfigured`; `StockStatusFilter StockStatus = StockStatusFilter.All`) in `Contracts/`.
- [ ] Change `IStockAnalysisCalculator.FilterItems` parameter to `StockAnalysisFilter filter` (update XML doc param); remove `using ...UseCases.GetPurchaseStockAnalysis` from the interface.
- [ ] Change `StockAnalysisCalculator.FilterItems` and `ShouldIncludeItem` to take `StockAnalysisFilter filter`; remove the unused use-case `using` if present.
- [ ] In the handler (line ~52) replace `FilterItems(allAnalysisItems, request)` with a `new StockAnalysisFilter { OnlyConfigured = request.OnlyConfigured, StockStatus = request.StockStatus }`.
- [ ] Remove the `UseCases` using from `StockAnalysisCalculatorTests.cs` only if nothing else there needs it.
- [ ] Verify `grep -rn "UseCases" $P/Services` returns nothing.
- [ ] Run `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Purchase"`; all pass. Commit `refactor(purchase): decouple FilterItems from use-case request`.

### task: update-process-doc-and-validate
**Files:** modify `docs/processes/calc-purchase-stock-analysis.md`.

- [ ] Add the four new Contracts files (`StockAnalysisItemDto.cs`, `StockAnalysisSummaryDto.cs`, `LastPurchaseInfoDto.cs`, `StockAnalysisFilter.cs`) to the `owns:` list; bump `verified_at` to today. Update any prose naming the request as the filter input (the doc references the handler's filter step).
- [ ] Run `python3 scripts/process-docs/check.py index`; resolve any complaint.
- [ ] Run `dotnet build`, `dotnet format backend/Anela.Heblo.sln --verify-no-changes` (apply formatting if needed), and the Purchase test filter again.
- [ ] Confirm the generated OpenAPI TS client shows no diff for the three DTO schemas (`git status` after build).
- [ ] Commit `docs(purchase): update stock analysis process doc for new Contracts`.
