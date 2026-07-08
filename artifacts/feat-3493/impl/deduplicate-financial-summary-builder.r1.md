# Implementation: deduplicate-financial-summary-builder (r1)

## Summary

Deduplicated the three copies of `FinancialSummaryDto` construction in
`backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`
per the task plan.

## Changes

- Added `private static FinancialSummaryDto BuildSummary(List<MonthlyFinancialDataDto> data, bool includeStockData)`
  computing the six aggregates (`TotalIncome`, `TotalExpenses`, `TotalBalance`,
  `AverageMonthlyIncome`, `AverageMonthlyExpenses`, `AverageMonthlyBalance`) and
  conditionally attaching `StockSummary`.
- `GetHybridWithCurrentMonthAsync` and `GetCachedFinancialOverview` now call
  `BuildSummary(allData, includeStockData)` / `BuildSummary(orderedData, includeStockData)`
  instead of inlining the object initializer.
- `GetFinancialOverviewRealTimeAsync` now materializes its DTO list once into
  `orderedData` (previously computed inline for `Data` only) and reuses it for
  both `Data` and `Summary = BuildSummary(orderedData, includeStockData)`.
- Removed the now-unused `CreateStockSummary(List<MonthlyFinancialData>, List<MonthlyStockChange>)`
  overload (the domain-object variant). The single remaining
  `CreateStockSummary(List<MonthlyFinancialDataDto> monthlyData)` overload serves
  all three call paths via `BuildSummary`.

## Verification

- `dotnet build Anela.Heblo.sln` — succeeded, 0 errors.
- `dotnet test .../Anela.Heblo.Tests.csproj --filter FullyQualifiedName~FinancialOverview` —
  27/27 passed (matches pre-change baseline count, no test files modified).
- `dotnet test Anela.Heblo.sln` (full suite) — 5414 passed, 4 skipped, 38 failed;
  all 38 failures are pre-existing DB-backed integration tests unrelated to this
  change (GridLayouts, Purchase, Photobank, MeetingTasks, Catalog, Bank, Leaflet,
  Article repositories — all require a live database connection not available in
  this sandbox). None touch `FinancialOverview` or `FinancialAnalysisService`.
- `dotnet format Anela.Heblo.sln --no-restore --verify-no-changes` — clean, no
  formatting diff.
- `grep -c "new FinancialSummaryDto" FinancialAnalysisService.cs` → `1` (only inside `BuildSummary`).
- `grep -c "private static StockSummaryDto CreateStockSummary" FinancialAnalysisService.cs` → `1`.
- `git status` on the FinancialOverview test directory — clean, no test files touched.

Pure refactor: no public interface, DTO shape, or behavior change.

**Status:** DONE
