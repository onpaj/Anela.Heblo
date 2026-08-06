# Code Review: Thread persistImmediately through ILogisticsStockOperationService

## Summary
Pure pass-through change threading the `persistImmediately` parameter from task 1's
`IStockUpProcessingService` up through `ILogisticsStockOperationService` and its adapter. Verified
against the live source: signatures match the task spec exactly, the adapter forwards the value
unchanged, and all 5 tests in `LogisticsStockOperationAdapterTests` (4 updated + 1 new) pass.

## Review Result: PASS

### task: thread-persist-immediately-through-logistics-stock-operation-contract
**Status:** PASS

## Docs to Update
(None — internal application-layer contract change, no public API or operational behavior change.)

## Overall Notes
- Confirmed `ILogisticsStockOperationService.CreateOperationAsync` and
  `LogisticsStockOperationAdapter.CreateOperationAsync` both have `bool persistImmediately = true` as
  the 7th parameter (after `CancellationToken`), and the adapter forwards it unchanged into
  `_stockUpProcessingService.CreateOperationAsync(..., cancellationToken, persistImmediately)`.
- Confirmed `LogisticsStockOperationAdapterTests.cs`: `SetupServiceReturnsCompleted` and the 3
  pre-existing `Verify` calls were updated with a trailing `It.IsAny<bool>()` argument (required
  because Moq `Setup`/`Verify` lambdas are C# expression trees, and the compiler disallows filling in
  an omitted optional-parameter default inside an expression tree — CS0854 — once the mocked
  interface gained a 7th parameter). The new test
  `CreateOperationAsync_PersistImmediatelyFalse_ForwardsToService` correctly asserts the literal
  `false` is forwarded through to the inner mock.
- The implementation's disclosed scope addition (fixing the same CS0854 break in
  `TransportBoxUniquenessTests.cs` and `GiftPackageManufactureServiceTests.cs`, both outside this
  task's originally-listed 3 files) is accepted as necessary, not a violation of "no other file besides
  the 3 listed is modified": those two files would otherwise fail to compile once the interface change
  landed, which would break the whole solution build. The fix applied there is the identical
  one-line, no-behavior-change pattern (`It.IsAny<bool>()` added to an existing `Setup`), not a scope
  expansion of the actual behavior change. This is a plan gap (the planner's file-list search missed
  2 of 5 relevant test files), not an implementation deviation, and is fully disclosed in the impl
  summary's Notes section.
- Verified via `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~LogisticsStockOperationAdapterTests"` (run earlier in this session): 5/5 pass. `dotnet build Anela.Heblo.sln`: 0 errors. `dotnet format Anela.Heblo.sln --verify-no-changes`: no diff.
