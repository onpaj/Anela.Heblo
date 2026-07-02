# Implementation: fix-tests-and-add-arch-guard

## What was implemented

- **Verified `FinancialOverviewModuleTests.cs`**: Task 1 had already correctly updated the module tests. Each test that resolves `IStockValueService` pre-registers `FinancialOverviewStockValueAdapter` directly (before calling `AddFinancialOverviewModule()`), so no `IStockValueService` resolution happens through `FinancialOverviewModule` alone. No changes were needed to this file.

- **Added `FinancialOverview -> Catalog` arch boundary rule** to the `Rules()` `TheoryData` in `ModuleBoundariesTests.cs`. The new rule enforces that `Anela.Heblo.Application.Features.FinancialOverview` types must not reference any Catalog namespaces (`Anela.Heblo.Domain.Features.Catalog`, `Anela.Heblo.Application.Features.Catalog`, `Anela.Heblo.Persistence.Catalog`). The allowlist is empty because task 1 already moved the Catalog-referencing adapter (`FinancialOverviewStockValueAdapter`) into `Catalog.Infrastructure`, eliminating all boundary violations.

## Files modified

- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — added `FinancialOverview -> Catalog` rule entry in the `Rules()` TheoryData

## Test results

```
Targeted tests (--no-build):
  FinancialOverviewModuleTests: Failed: 0, Passed: 7, Total: 7
  ModuleBoundariesTests:        Failed: 0, Passed: 28, Total: 28  (including new FinancialOverview->Catalog rule)
  StockValueServiceTests:       Failed: 0, Passed: 3, Total: 3

Full suite (dotnet test, exit code 0):
  Failed: 64 (all Docker/TestContainers — Docker not available in this environment, pre-existing)
  Passed: 5390
  Skipped: 4
  Total: 5458, Duration: ~25s
```

## Status
DONE
