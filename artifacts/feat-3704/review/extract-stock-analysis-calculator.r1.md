# Code Review: Extract Stock Analysis Calculator

## Summary
The implementation extracts `CalculateStockEfficiency` and `CalculateRecommendedOrderQuantity` into a new `IStockAnalysisCalculator`/`StockAnalysisCalculator` pair, mirroring `IStockSeverityCalculator` exactly in namespace, doc style, and DI registration. Method bodies were moved byte-for-byte, the handler and DI module were wired correctly, and both handler test files were updated to use a real (non-mocked) `StockAnalysisCalculator` as the spec required. Verified by direct diff/file inspection plus running `dotnet build` (0 errors) and the targeted test suite (30/30 passed).

## Review Result: PASS

### task: extract-stock-analysis-calculator
**Status:** PASS

## Overall Notes
Verification performed directly against the code (not just the impl summary):
- `git show 8487156 --stat` confirms only the 7 expected files changed (`PurchaseModule.cs`, `IStockAnalysisCalculator.cs`, `StockAnalysisCalculator.cs`, `GetPurchaseStockAnalysisHandler.cs`, the two existing handler test files, and the new `StockAnalysisCalculatorTests.cs`) — no unrelated files touched.
- `IStockAnalysisCalculator.cs`: signature matches the spec exactly (parameter names, order, nullability), XML doc comments on interface and each parameter matching `IStockSeverityCalculator`'s style.
- `StockAnalysisCalculator.cs`: stateless class, no constructor, method bodies are a verbatim, byte-for-byte match against both the task spec's listed source and the original handler code (confirmed via diff — only whitespace-neutral relocation, no rewording/reformatting/added validation).
- `GetPurchaseStockAnalysisHandler.cs`: field/constructor-parameter ordering is exactly `_materialCatalog`, `_stockSeverityCalculator`, `_stockAnalysisCalculator`, `_logger` as required; the two private methods were removed entirely; the two call sites in `AnalyzeStockItem` now call `_stockAnalysisCalculator.CalculateStockEfficiency(...)` / `.CalculateRecommendedOrderQuantity(...)` with the same arguments as before. `Handle`, `GetLastPurchaseInfo`, `ShouldIncludeItem`, `SortItems`, and `CalculateSummary` are untouched.
- `PurchaseModule.cs`: `services.AddScoped<IStockAnalysisCalculator, StockAnalysisCalculator>();` registered immediately after the `IStockSeverityCalculator` registration, same `Scoped` lifetime, with its own comment (`// Register stock analysis calculator`) — satisfies the "either extend or add a second comment" allowance.
- `StockAnalysisCalculatorTests.cs`: 9 `[Fact]` tests (xUnit + FluentAssertions), constructed via `new StockAnalysisCalculator()`, no mocking. All 9 required cases from the spec are present, including the combined null/empty/unparseable-moq case implemented as three assertions inside one `[Fact]` (the spec's bullet list treats that as a single required case, so this is a faithful one-bullet-per-Fact mapping, not a shortfall).
- `GetPurchaseStockAnalysisHandlerTests.cs` and `GetPurchaseStockAnalysisHandlerDiacriticsTests.cs`: both constructor call sites now pass `new StockAnalysisCalculator()` — a real instance, not `Mock<IStockAnalysisCalculator>` — satisfying the hard requirement so `Handle_SortByStockEfficiency_ReturnsSortedItems` continues to exercise genuine computed stock-efficiency values rather than an all-zero list. `IStockSeverityCalculator` wiring in both files is untouched (still mocked with per-test `.Setup()`/`.SetupSequence()` calls). No test assertions or test cases were added, removed, or modified beyond the constructor argument list.
- Build: `dotnet build Anela.Heblo.sln` completed with 0 errors (251 pre-existing warnings, none newly introduced by this change; the warnings list contains nothing referencing the changed files).
- Tests: `dotnet test ... --filter "FullyQualifiedName~StockAnalysisCalculatorTests|FullyQualifiedName~GetPurchaseStockAnalysisHandlerTests|FullyQualifiedName~GetPurchaseStockAnalysisHandlerDiacriticsTests"` → `Passed! - Failed: 0, Passed: 30, Skipped: 0, Total: 30`. (A separate, unrelated `AccessMatrixGen` code-gen tool exception appeared as a pre-build warning during the API project's post-build step — it is an environment-specific code-gen step unrelated to this change and did not fail the build or the test run.)

No issues found. This is a clean, spec-compliant pure refactor.
