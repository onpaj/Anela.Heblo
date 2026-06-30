# Implementation: create-adapter-and-rewire-di

## What was implemented

Created `FinancialOverviewStockValueAdapter` in `Catalog.Infrastructure` as the correct home for the cross-module adapter that implements FinancialOverview's `IStockValueService` using Catalog-owned ERP clients. Removed the misplaced `StockValueService` from `FinancialOverviewModule` and rewired DI so the provider module (Catalog) owns the registration.

One deviation from the task spec: the `using Anela.Heblo.Application.Features.FinancialOverview.Services;` directive was kept in `FinancialOverviewModule.cs` (not removed entirely) because `IFinancialAnalysisService` and `FinancialAnalysisService` — also in that namespace — are still registered there. Only the `StockValueService` registration line was removed.

Two test files were updated to reflect the renamed class and the DI ownership change:
- `StockValueServiceTests.cs` — updated to construct `FinancialOverviewStockValueAdapter` directly.
- `FinancialOverviewModuleTests.cs` — updated assertions from `BeOfType<StockValueService>()` to `BeOfType<FinancialOverviewStockValueAdapter>()`, and added explicit adapter registration in tests that only call `AddFinancialOverviewModule` (since `IStockValueService` is now registered by `CatalogModule`).

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs` — created; `internal sealed` adapter in Catalog.Infrastructure implementing `IStockValueService`
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — added `using Anela.Heblo.Domain.Features.FinancialOverview;` and `services.AddScoped<IStockValueService, FinancialOverviewStockValueAdapter>();` after the `IPackingProductSource` registration
- `backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialOverviewModule.cs` — removed `services.AddScoped<IStockValueService, StockValueService>();`
- `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/StockValueService.cs` — deleted
- `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/StockValueServiceTests.cs` — updated to use `FinancialOverviewStockValueAdapter`
- `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialOverviewModuleTests.cs` — updated DI assertions and test setup to reflect new ownership

## How to verify

```
dotnet build
```
Run from the repository root (`/home/user/worktrees/feature-3433-Arch-Review-Financialoverview-Stockvalueservice-Di`). Build succeeds with 0 errors, 82 warnings (pre-existing).

## Status
DONE
