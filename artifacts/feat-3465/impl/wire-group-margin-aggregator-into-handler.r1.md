# Implementation: wire-group-margin-aggregator-into-handler

## What was implemented
Removed `GetProductMarginSummaryHandler`'s private `CalculateGroupMarginData` method and the internal `GroupMarginData` class, switching the call site in `GenerateTopProducts` to `_marginCalculator.GetGroupAggregatedMarginData(products)` (added in the previous task). Updated the one test that mocks `IMarginCalculator` directly to stub the new method.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` — removed `CalculateGroupMarginData` and internal `GroupMarginData`, updated call site.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` — added `GetGroupAggregatedMarginData` stub to `Handle_WithMockedDependencies_InvokesCalculatorAndBreakdownGenerator`.

## Tests
Existing `GetProductMarginSummaryHandlerTests.cs` suite (8 tests) — no new tests added per the task-context (this task only rewires an existing call site).

## How to verify
`dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-restore` (0 errors), then `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"` (8/8 passed).

## Notes
Followed the task-context file's exact code verbatim. No deviations.

## PR Summary
Rewired `GetProductMarginSummaryHandler` to call `IMarginCalculator.GetGroupAggregatedMarginData` instead of its own private copy of the same logic, and deleted that now-dead private method and its internal `GroupMarginData` class. This is step 2 of 4 in the SRP refactor from issue #3465.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`

## Status
DONE
