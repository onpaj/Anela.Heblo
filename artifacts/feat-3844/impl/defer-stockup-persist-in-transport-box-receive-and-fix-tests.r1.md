# Implementation: defer-stockup-persist-in-transport-box-receive-and-fix-tests

## What was implemented
This is the actual bug fix for GitHub issue #3844. Changed the single call site inside
`ChangeTransportBoxStateHandler.HandleReceived` to pass `persistImmediately: false`, so the
`StockUpOperation` inserts it stages are no longer flushed immediately — they now ride along with
`Handle`'s existing, unchanged box-update `SaveChangesAsync` call at the end of `Handle`, making the
two writes commit as one atomic unit (FR-1). Combined with the idempotency pre-check added in task 1,
retrying a Receive whose operations were partially created in a prior interrupted attempt now succeeds
instead of permanently failing on a unique-constraint violation (FR-2). No control-flow restructuring
was needed: `HandleReceived` already ran, in full, before `transition.ChangeStateAsync`,
`_repository.UpdateAsync`, and `_repository.SaveChangesAsync` — only the one call site changed.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` — the one call to `_stockOperationService.CreateOperationAsync` inside `HandleReceived` now passes `persistImmediately: false`. No other line in this file changed.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs` — added a new regression test `Handle_InTransitToReceived_PassesPersistImmediatelyFalse`; updated the constructor's shared mock `Setup` and all 12 pre-existing `Verify` call sites targeting `CreateOperationAsync` to include an explicit 7th argument (`It.IsAny<bool>()`, or a literal where the test cares about the value), since the production call now always passes `false` and the compiler bakes an omitted-parameter's default into Moq's expression-tree `Setup`/`Verify` lambdas.
- `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs` — **not in the original task-plan's file list**, but its Moq `Setup(x => x.CreateOperationAsync(...))` (an in-memory-DB integration-style test exercising the real handler + repository) also omitted the new 7th parameter inside an expression tree, which is a compile error (CS0854), not just a runtime mismatch — fixed with the same one-line addition.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs` — **the task-plan explicitly said this file needed no change**, reasoning that both the production call sites and the test's mock omit `persistImmediately` and so resolve to the same default. That reasoning is correct for plain method calls, but incorrect for the test's own `Setup(x => x.CreateOperationAsync(...))`, which is an expression tree subject to the same CS0854 constraint as every other Moq call site touched in this fix — fixed with the same one-line addition. Production code in `GiftPackageManufactureService.cs` was **not** touched, confirming FR-3 (no behavior regression to this consumer): its 4 real call sites still omit `persistImmediately` entirely and keep getting `persistImmediately: true` by default.

## Tests
- `ChangeTransportBoxStateHandlerTests` — 13 tests exercise `CreateOperationAsync`, including the new `Handle_InTransitToReceived_PassesPersistImmediatelyFalse` regression test that directly pins the fix (`persistImmediately == false` on Receive).
- `TransportBoxUniquenessTests` — unaffected in behavior, fixed only to keep compiling.
- `GiftPackageManufactureServiceTests` — unaffected in behavior (FR-3), fixed only to keep compiling.

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ChangeTransportBoxStateHandlerTests|FullyQualifiedName~GiftPackageManufactureServiceTests|FullyQualifiedName~TransportBoxUniquenessTests|FullyQualifiedName~StockUpProcessingServiceTests|FullyQualifiedName~LogisticsStockOperationAdapterTests"
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test Anela.Heblo.sln
```
All 47 tests across the 5 directly-relevant test classes pass; `dotnet build` succeeds with 0 errors;
`dotnet format --verify-no-changes` reports no diff. Full-solution `dotnet test Anela.Heblo.sln` run
to confirm no other test file in the solution has a `CreateOperationAsync` expectation that was missed.

## Notes
Two test files outside this task's originally-enumerated list needed the identical one-line fix to
keep the solution compiling (`TransportBoxUniquenessTests.cs`, `GiftPackageManufactureServiceTests.cs`)
— see the "Files created/modified" section above for why. This is a plan gap (the planner's grep for
`CreateOperationAsync` test call sites during the planning phase covered 3 of the 5 relevant test
files), not a scope change to the production fix itself: only the single call site inside
`HandleReceived` changes production behavior, exactly as FR-1/FR-2 require.

## PR Summary
Task 3 of 3 — the actual bug fix for #3844. `ChangeTransportBoxStateHandler.HandleReceived` now
passes `persistImmediately: false` when creating `StockUpOperation` rows during Receive, so those
inserts are only staged on the shared `ApplicationDbContext`'s change tracker and get flushed together
with the box's own state-transition update in the single `SaveChangesAsync` call that already runs at
the end of `Handle`. Combined with task 1's idempotency pre-check, a Receive that is retried after a
partial failure (some or all `StockUpOperation` rows already committed from an interrupted prior
attempt) now completes successfully instead of permanently failing on the `DocumentNumber` unique
constraint. No explicit database transaction was introduced — this repo's `PollyExecutionStrategy`
(retrying execution strategy) is incompatible with caller-owned transactions and this is enforced in
CI by `scripts/check-no-managed-tx.sh`, so the deferred-single-`SaveChangesAsync` approach is the only
compliant atomicity mechanism, as established in the architecture review.

### Changes
- `ChangeTransportBoxStateHandler.cs` — one call site now passes `persistImmediately: false`.
- `ChangeTransportBoxStateHandlerTests.cs` — 1 new regression test + 13 updated mock expressions.
- `TransportBoxUniquenessTests.cs`, `GiftPackageManufactureServiceTests.cs` — mock signature fix only, no behavior change (cross-cutting fallout from the shared interface change, outside this task's original file list but required to keep the solution buildable).

## Status
DONE
