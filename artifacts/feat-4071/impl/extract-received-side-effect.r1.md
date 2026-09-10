# Implementation: extract-received-side-effect

## What was implemented
Extracted `ChangeTransportBoxStateHandler.HandleReceived`'s body into a new standalone
`ReceivedSideEffect` class implementing `ITransportBoxTransitionSideEffect`, matching the
established pattern from the already-extracted `NewToOpenedSideEffect`,
`OpenToReserveSideEffect`, and `OpenToQuarantineSideEffect` classes in the same directory.
The logic — grouping box items by product code, rounding the summed amount away from zero,
building the `BOX-{box.Id:000000}-{ProductCode}` document number, staging the stock-up
operation via `ILogisticsStockOperationService.StageOperationAsync`, and the exact
`LogDebug`/`LogInformation` message templates — was moved unchanged (verified line-by-line
against `ChangeTransportBoxStateHandler.cs` lines 273–305). `Supports` returns true for the
three known origin states (InTransit, Reserve, Quarantine) transitioning to Received, matching
the three `CallBackMap` entries that previously routed to `HandleReceived`.

Per the task instructions, the main handler was **not** modified to use this new class yet —
that wiring is deferred to the later `refactor-handler-orchestration` task. This task only adds
the new side-effect class and its unit tests.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ReceivedSideEffect.cs` — new side-effect class; constructor-injects `ILogisticsStockOperationService` and `ILogger<ReceivedSideEffect>` (the only extracted side effect so far that needs a logger, since `HandleReceived` is the only handler with logging calls).
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ReceivedSideEffectTests.cs` — new unit test file.

## Tests
`ReceivedSideEffectTests.cs` covers:
- `Supports` returns `true` for InTransit/Reserve/Quarantine → Received (theory), and `false` for New → Received.
- `ExecuteAsync` aggregates multiple item lines with the same product code into a single staged operation, and stages one operation per distinct product code (asserting the exact `documentNumber`, `productCode`, summed `amount`, `LogisticsStockOperationSource.TransportBox`, and `box.Id` arguments passed to `StageOperationAsync`).
- `ExecuteAsync` rounds fractional amounts away from zero (1.4 + 1.4 → 3, not 2).

Test items are added to the box via reflection into the private `_items` field, mirroring the
`CreateTestBoxWithMultipleItems` helper already used in `ChangeTransportBoxStateHandlerTests.cs`
(the public `TransportBox.AddItem` requires the box to be in the `Opened` state, which does not
apply here).

## How to verify
```
cd backend
dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ReceivedSideEffectTests"
```
Result: `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6`.

Also ran the broader Logistics/Transport test slice
(`--filter "FullyQualifiedName~ChangeTransportBoxState|FullyQualifiedName~Logistics.Transport"`):
211 passed, 2 pre-existing failures unrelated to this change
(`ChangeTransportBoxStateReceiveAtomicityIntegrationTests` — these are Testcontainers/Postgres
integration tests that fail in this sandbox because Docker is not available, not because of
anything touched here).

## Notes
- The sandbox's dotnet build/test tooling hung twice under concurrent/stale MSBuild node
  reuse (unrelated to this change); resolved by killing stray `dotnet`/`MSBuild`/`VBCSCompiler`
  processes and re-running with `MSBUILDDISABLENODEREUSE=1`. No code implication.
- No DI registration for `ReceivedSideEffect` exists yet, matching the pattern of the three
  prior extracted side effects (none of them are registered/wired into the handler yet either) —
  wiring is explicitly out of scope per the task description and deferred to
  `refactor-handler-orchestration`.
- `artifacts/feat-4071/state.json` had a pending working-tree modification (pipeline-managed
  status tracking) that was left untouched/unstaged, per the task's file scope.

## PR Summary
Extracts the `HandleReceived` transition handler out of `ChangeTransportBoxStateHandler` into
a standalone `ReceivedSideEffect` class implementing the existing
`ITransportBoxTransitionSideEffect` interface, continuing the same extraction pattern already
applied to the New→Opened, Opened→Reserve, and Opened→Quarantine transitions. The moved logic
(item aggregation by product code, away-from-zero rounding, `BOX-{Id:000000}-{ProductCode}`
document numbering, staging via `ILogisticsStockOperationService`, and the debug/info log
messages) is unchanged byte-for-byte from the current handler. The handler itself is not yet
updated to dispatch to this new class — that comes in a follow-up task — so this PR is purely
additive.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ReceivedSideEffect.cs` — new class extracted from `ChangeTransportBoxStateHandler.HandleReceived`
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ReceivedSideEffectTests.cs` — new unit tests for `Supports` and `ExecuteAsync` (aggregation, per-product staging, rounding)

## Status
DONE
