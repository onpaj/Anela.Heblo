# Implementation: add-stock-analysis-filter r1
- Created `Contracts/StockAnalysisFilter.cs` (class; `OnlyConfigured`, `StockStatus = All`).
- `IStockAnalysisCalculator.FilterItems` / `StockAnalysisCalculator.FilterItems` + `ShouldIncludeItem` now take `StockAnalysisFilter`; removed the UseCases `using` from both.
- `GetPurchaseStockAnalysisHandler` builds the filter from the request.
- `StockAnalysisCalculatorTests`: FilterItems tests use `StockAnalysisFilter`; removed unused UseCases using.
- `grep UseCases Features/Purchase/Services` is empty. `dotnet test --filter StockAnalysis`: 115 passed. Broader `Purchase` filter: 436 passed, 1 failed (Docker/Testcontainers unavailable in sandbox, unrelated).
