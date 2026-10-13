# Implementation: extract-calculatesummary

## What was implemented

Moved the `CalculateSummary` logic out of `GetPurchaseStockAnalysisHandler` and
into `StockAnalysisCalculator`, exposed via `IStockAnalysisCalculator`, following
the same extraction pattern already used for `AnalyzeItem`, `FilterItems`, and
`SortItems`. The handler now delegates to
`_stockAnalysisCalculator.CalculateSummary(...)` instead of calling a private
method on itself, and no longer has any private methods below `Handle()` — it
is now a thin coordinator that only fetches snapshots and delegates to the
calculator for analysis, filtering, sorting, and summarization.

The `allAnalysisItems` invariant flagged in the task spec as highest-risk was
preserved verbatim: the summary is still calculated from the unfiltered,
category-matched item set (`allAnalysisItems`), not the status/search-filtered,
paginated set (`analysisItems`).

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs` — added `CalculateSummary` to the interface with XML doc comments
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs` — added the `CalculateSummary` implementation, moved verbatim from the handler
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs` — removed the private `CalculateSummary` method; the call site now delegates to `_stockAnalysisCalculator.CalculateSummary(...)`
- `backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs` — added `MakeSummaryItem` helper and two `CalculateSummary_*` test cases (severity bucket counting, and total-inventory-value calculation with a missing last-purchase treated as zero unit price)

## Tests

- `StockAnalysisCalculatorTests.CalculateSummary_CountsEachSeverityBucket`
- `StockAnalysisCalculatorTests.CalculateSummary_TotalInventoryValue_MissingLastPurchaseTreatedAsZeroUnitPrice`

## How to verify

```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Purchase"
```

Result: 375 passed, 1 failed (`PurchaseOrderRepositoryHistorySqlShapeTests.GetHistoryAsync_EmitsSqlThatTouchesOnlyHistoryTable`) — that failure is pre-existing and unrelated: it requires Docker/testcontainers for a Postgres container, which is not available in this sandbox. All Purchase/StockAnalysisCalculator-specific tests, including the two new `CalculateSummary_*` tests, pass.

Also confirmed no private methods remain in the handler below `Handle()`:

```
grep -n "private " backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs
```

Output: only the four `private readonly` field declarations — no private methods.

## Notes

The implementation is a verbatim move of the existing `CalculateSummary` logic
(same severity bucket counts, same `TotalInventoryValue` sum with `?? 0` for a
missing last purchase's unit price), per the task's Step 4 instruction. No
behavioral changes were introduced.

One deviation from the task spec's literal text: the given test snippet's
`MakeSummaryItem` helper assigned `unitPrice.Value` (a `double`) directly to
`LastPurchaseInfoDto.UnitPrice` (a `decimal`), which does not compile (CS0266
— no implicit `double` → `decimal` conversion). Added an explicit
`(decimal)unitPrice.Value` cast to fix the compile error; this does not change
the test's intent or expected values (5.0 and null both cast/coerce cleanly to
their decimal equivalents).

## PR Summary
Moved `CalculateSummary` out of `GetPurchaseStockAnalysisHandler` and into `StockAnalysisCalculator`/`IStockAnalysisCalculator`, completing the extraction of stock-analysis business logic out of the handler (alongside the already-completed `AnalyzeItem`, `FilterItems`, and `SortItems` extractions). The handler is now a thin coordinator: fetch snapshots, delegate to the calculator for analysis/filtering/sorting/summarization, page, and return. The highest-risk regression flagged in the architecture review — summary computed from the unfiltered `allAnalysisItems` rather than the filtered/paginated set — was explicitly preserved.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs` — added `CalculateSummary` to the interface
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs` — added `CalculateSummary` implementation
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs` — removed private `CalculateSummary`, delegates to the calculator; handler now has no private methods
- `backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs` — added `CalculateSummary` test coverage

## Status
DONE
