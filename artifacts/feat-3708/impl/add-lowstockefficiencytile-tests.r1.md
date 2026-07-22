# Implementation: add-lowstockefficiencytile-tests

## What was implemented
A focused unit test suite for `LowStockEfficiencyTile.LoadDataAsync`, covering the tile's filter business rule (`StockEfficiencyPercentage < 20 && IsConfigured`), the 20% boundary being exclusive, and the error-response branch when the mediator response is unsuccessful. No production code was changed — this is a pure coverage-gap fix.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/LowStockEfficiencyTileTests.cs` — new xUnit test class with two `[Fact]` tests, using Moq for `IMediator`/`TimeProvider` and FluentAssertions + `System.Text.Json` for assertions on the tile's anonymous-object response, matching the existing dashboard-tile test convention.

## Tests
- `LoadDataAsync_WithMixedEfficiencyAndConfiguration_CountsOnlyLowEfficiencyConfiguredItems` — covers FR-1 (only items below 20% AND configured are counted) and FR-2 (an item at exactly 20% is excluded, verifying strict `<`).
- `LoadDataAsync_WhenResponseNotSuccessful_ReturnsErrorStatus` — covers FR-3 (a `Success = false` mediator response yields `{status: "error", error: "Failed to load stock analysis data"}` via the `!response.Success` branch).

## How to verify
```
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~LowStockEfficiencyTileTests
```
Result: `Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2`.

## Notes
No deviations from the task plan. `GetPurchaseStockAnalysisResponse(ErrorCodes.InvalidDateRange)` was used to construct a `Success = false` response via the existing `BaseResponse` error-code constructor, as anticipated by the spec's Open Questions resolution. An unrelated pre-existing warning (`AccessMatrixGen` tool exiting non-zero during `Anela.Heblo.API` build) appeared in the full test run's build output but does not affect this test project or its results — it is an existing quirk of this environment's build, not something introduced by this change.

## PR Summary
Adds unit test coverage for `LowStockEfficiencyTile`, which previously had 0% line coverage. The tile counts purchase materials with critically low stock efficiency by filtering on `StockEfficiencyPercentage < 20 && IsConfigured`; this filter combination, its boundary condition, and the tile's error-response branch were all previously unexercised by any test. Two new tests lock in the current, correct behavior using a mocked `IMediator`, so a future regression (e.g. accidentally dropping `IsConfigured` from the filter, or loosening the boundary to `<=`) would be caught immediately. No production code changed.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/LowStockEfficiencyTileTests.cs` — new test file, 2 tests

## Status
DONE
