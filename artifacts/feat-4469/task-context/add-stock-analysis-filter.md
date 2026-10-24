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

