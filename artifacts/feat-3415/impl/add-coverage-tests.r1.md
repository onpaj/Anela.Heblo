# Implementation: add-coverage-tests

## What was implemented

Two new `[Fact]` test methods were appended to the existing `GetPurchaseStockAnalysisHandlerTests` class:

1. **`Handle_FilterByCriticalStatus_SummaryReflectsAllItems`** — verifies the dual-bucket invariant: when filtering Items by `StockStatusFilter.Critical`, the Summary still reflects all 4 items (2 Critical + 2 Optimal), not just the visible filtered ones.

2. **`Handle_CalculateSummary_AllFieldsAreCorrect`** — a snapshot test covering every field in `PurchaseStockAnalysisSummary`: `TotalProducts`, `CriticalCount`, `LowStockCount`, `OptimalCount`, `OverstockedCount`, `NotConfiguredCount`, `TotalInventoryValue`, `AnalysisPeriodStart`, and `AnalysisPeriodEnd`. Five items (one per severity) are set up with known unit prices; `TotalInventoryValue` is asserted to be 230.00m ((10×5.00)+(20×3.00)+(30×2.00)+(15×4.00)+(0×0)).

No new files, classes, or mocks were introduced. Both tests reuse the existing `MakeSnapshot` helper and the constructor-injected `_materialCatalogMock` / `_stockSeverityCalculatorMock`.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs` — appended 95 lines (two `[Fact]` methods) before the final class-closing brace

## Tests

All tests in the `GetPurchaseStockAnalysisHandler` filter passed:

```
Passed Handle_FilterByCriticalStatus_SummaryReflectsAllItems [7 ms]
Passed Handle_CalculateSummary_AllFieldsAreCorrect [4 ms]
```

Full test run: `Test Run Successful.` (0 failures, 0 errors)

## How to verify

```bash
cd /home/user/worktrees/feature-3415-Coverage-Gap-Purchase-Getpurchasestockanalysishand
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetPurchaseStockAnalysisHandler" -v normal
```

## Notes

- The `GenerateAccessMatrix` MSBuild target exits with code 134 in this worktree environment (missing `access-matrix.generated.json`). This is a pre-existing infrastructure issue unrelated to the test project; the test project compiles and runs successfully despite that warning.
- `SetupSequence` is used for both tests to guarantee deterministic severity assignment in declaration order (the handler iterates snapshots via `Select`).

## PR Summary

Added two unit tests that close coverage gaps on `GetPurchaseStockAnalysisHandler`:
- A dual-bucket invariant test confirming the Summary counts all items regardless of the Items filter.
- A full-field snapshot test asserting every Summary property, including `TotalInventoryValue` arithmetic and date range passthrough.

## Status
DONE
