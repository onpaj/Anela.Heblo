All 6 tasks complete. Writing the output summary.

---

# Implementation: Remove dead PlaceholderStockValueService and simplify StockValueService DI registration

## What was implemented

Deleted `PlaceholderStockValueService` from production code, replaced its two test usages with inline Moq stubs, and simplified the `FinancialOverviewModule` DI registration from a hand-rolled factory lambda to a single standard typed registration. All 23 affected tests continue to pass with zero new failures across the full test suite.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialOverviewModule.cs` — factory lambda replaced with `services.AddScoped<IStockValueService, StockValueService>();`; removed stale "tests can override this" comment; removed two now-unused `using` directives (`Catalog.Price`, `Catalog.Stock`)
- `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialOverviewModuleTests.cs` — `AddFinancialOverviewModule_UsesFactoryPattern_AvoidsServiceProviderAntipattern` renamed to `AddFinancialOverviewModule_RegistersIStockValueService_WithoutBuildServiceProviderAntipattern`; `AddFinancialOverviewModule_CanOverridePlaceholderService_ForTesting` rewritten as `AddFinancialOverviewModule_CanOverrideStockValueService_ForTesting` using `Mock.Of<IStockValueService>()` stub
- `backend/test/Anela.Heblo.Tests/Features/FinancialOverviewTests.cs` — `FinancialOverviewTestFactory.ConfigureTestServices` now registers a `Mock<IStockValueService>` returning `Array.Empty<MonthlyStockChange>()`; updated usings and stale comments
- `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/PlaceholderStockValueService.cs` — **deleted**

## Tests

- `FinancialOverviewModuleTests` (7 tests) — all pass including two renamed/rewritten tests
- `FinancialOverviewTests` (16 integration tests) — all pass with Moq stub replacing ERP calls

## How to verify

```bash
# Zero references to deleted type
grep -rn "PlaceholderStockValueService" backend/

# Typed registration confirmed
grep -n "IStockValueService" backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialOverviewModule.cs

# Run affected tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Application.FinancialOverview.FinancialOverviewModuleTests|FullyQualifiedName~Anela.Heblo.Tests.Features.FinancialOverviewTests"
```

## Notes

- `dotnet format` made no changes — code was already clean.
- Full suite shows 38 pre-existing Docker-dependent integration test failures unrelated to this change.
- 4592 unit tests pass with zero new failures.

## PR Summary

Deleted the dead `PlaceholderStockValueService` from production code and cleaned up all references. The class carried misleading XML documentation claiming it was auto-injected in test environments via `FinancialOverviewModule`, but no such registration existed. Two test files that manually instantiated the placeholder are now migrated to inline Moq stubs returning `Array.Empty<MonthlyStockChange>()`, preserving the same test behavior (no ERP calls, deterministic empty stock data) without keeping a production class alive for tests.

The `FinancialOverviewModule` DI registration was simplified from a manual factory lambda that hand-wired three constructor parameters to a single `services.AddScoped<IStockValueService, StockValueService>()`. New `StockValueService` constructor parameters now flow through DI automatically — the old factory would silently drift or break.

### Changes
- `backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialOverviewModule.cs` — factory lambda replaced with typed registration; stale comment and unused usings removed
- `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialOverviewModuleTests.cs` — two tests renamed/rewritten; `PlaceholderStockValueService` replaced with `Mock.Of<IStockValueService>()`
- `backend/test/Anela.Heblo.Tests/Features/FinancialOverviewTests.cs` — integration factory migrated to `new Mock<IStockValueService>()` with Setup returning empty array; usings and comments updated
- `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/PlaceholderStockValueService.cs` — deleted

## Status

DONE