# Implementation: thread-persist-immediately-through-logistics-stock-operation-contract

## What was implemented
Added the same `bool persistImmediately = true` parameter (after `CancellationToken`) to
`ILogisticsStockOperationService.CreateOperationAsync` and its only implementation,
`LogisticsStockOperationAdapter.CreateOperationAsync`, forwarding the value unchanged into
`IStockUpProcessingService.CreateOperationAsync` (whose new signature was introduced in task 1). This
is a pure pass-through change — no new logic, no idempotency check at this layer (that lives inside
`StockUpProcessingService`, applying uniformly regardless of which caller reaches it through this
adapter).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsStockOperationService.cs` — added `persistImmediately` parameter + XML doc.
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsStockOperationAdapter.cs` — added the parameter and forwards it into the inner service call.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/LogisticsStockOperationAdapterTests.cs` — updated the shared `SetupServiceReturnsCompleted` helper and 3 existing `Verify` calls to add `It.IsAny<bool>()` as the 7th argument (the inner `IStockUpProcessingService` mock now has 7 params); added a new test `CreateOperationAsync_PersistImmediatelyFalse_ForwardsToService`.

## Tests
`LogisticsStockOperationAdapterTests` — 5 tests total (4 pre-existing, updated to match the new 7-arg mock signature, + 1 new test asserting `persistImmediately: false` forwards through as a literal `false` to the inner service call).

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~LogisticsStockOperationAdapterTests"
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
```
All 5 tests pass; build succeeds; format reports no changes.

## Notes
No deviations from the task-context file. One addition beyond the task-context's explicit file list:
while validating the full-solution build after this task's interface change, two additional test
files outside this task's own file list broke compilation for the same reason (Moq `Setup`/`Verify`
expression trees on `IStockUpProcessingService`/`ILogisticsStockOperationService.CreateOperationAsync`
omitting the new 7th optional parameter, which C# disallows inside expression trees — CS0854):
`backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs` and
`backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs`. The
task-plan's task 3 explicitly claimed `GiftPackageManufactureServiceTests.cs` needed "no change", but
that claim only holds at the production-call-site level (regular method calls silently fill in
defaults); it does not hold for its own Moq `Setup` block, which is itself an expression tree with the
same CS0854 constraint. Both files were fixed with the same one-line addition
(`It.IsAny<bool>()` as the 7th `Setup`/mock argument) — see the combined summary in task 3's impl file
for the full list, since fixing all cross-cutting compile fallout together (rather than splitting it
awkwardly across task boundaries) kept the branch buildable at each commit.

## PR Summary
Task 2 of 3 for the #3844 fix. Threads the `persistImmediately` toggle from task 1 up one layer,
through the Logistics module's cross-module contract (`ILogisticsStockOperationService`) and its
adapter, so `ChangeTransportBoxStateHandler` (task 3) can reach it without breaking the Logistics ↔
Catalog module boundary.

### Changes
- `ILogisticsStockOperationService.cs` / `LogisticsStockOperationAdapter.cs` — pass-through parameter.
- `LogisticsStockOperationAdapterTests.cs` — updated mocks + 1 new test.

## Status
DONE
