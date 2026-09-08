# Implementation: refactor-handler-orchestration

## What was implemented
Reduced `ChangeTransportBoxStateHandler` to pure orchestration by removing the `CallBackMap`
dispatch dictionary and the four private state-transition handler methods
(`HandleNewToOpened`, `HandleOpenToQuarantine`, `HandleOpenToReserve`, `HandleReceived`) plus
`RestoreInventoryForItemsAsync`. The handler's constructor now takes seven dependencies:
`ITransportBoxRepository`, `IMediator`, `ILogger<ChangeTransportBoxStateHandler>`,
`ICurrentUserService`, `TimeProvider`, `IEnumerable<ITransportBoxTransitionSideEffect>`, and
`ITransportBoxInventoryRestorer` — dropping the now-unused `IInventoryReservationService` and
`ILogisticsStockOperationService` fields/params, since those are now only used inside the
extracted side-effect/restorer classes committed by earlier tasks on this branch.

Dispatch inside `Handle()` was rewired from `CallBackMap.TryGetValue(...)` to
`_sideEffects.FirstOrDefault(s => s.Supports(box.State, request.NewState))` followed by
`sideEffect.ExecuteAsync(box, request, cancellationToken)`, matching the
`ITransportBoxTransitionSideEffect` interface already present in the repo (created by an
earlier task, contents match the task's snippet exactly). The inventory-restore call site was
rewired from the local `RestoreInventoryForItemsAsync` method to
`_inventoryRestorer.RestoreAsync(...)`, matching the already-present
`ITransportBoxInventoryRestorer` interface (also an exact match to the task's snippet).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` — constructor simplified to 7 params, `CallBackMap` and the four private `Handle*` methods plus `RestoreInventoryForItemsAsync` deleted, dispatch and inventory-restore call sites rewired to the new collaborators.

## Tests
None — the existing test project (`ChangeTransportBoxStateHandlerTests` and friends) still
constructs the handler with the old 7-arg signature (old collaborators) and will fail to
build until a later task (`update-existing-tests`) updates those constructor calls, as
expected per the task instructions.

## How to verify
```
cd backend && dotnet build src/Anela.Heblo.Application
```
Build succeeded (0 errors, pre-existing warnings only — none introduced by this change). Test
project build is expected to fail until the later `update-existing-tests` task runs; that was
not attempted here per the task scope.

## Notes
No deviations from the task's literal snippets were needed — the
`ITransportBoxTransitionSideEffect` and `ITransportBoxInventoryRestorer` interfaces already in
the repo (created by prior tasks on this branch) matched the task's expected shape and method
names exactly (`Supports`/`ExecuteAsync`, `RestoreAsync`), so the constructor and dispatch code
were wired in verbatim as specified. Only change beyond the literal diff: removed one stray
blank line before the class's closing brace left over after deleting the trailing private
methods, to match existing formatting.

## PR Summary
This change completes the extraction of `ChangeTransportBoxStateHandler`'s state-transition
side effects into standalone strategy classes by removing the handler's old `CallBackMap`
dispatch table and its four private transition-handler methods (plus the private inventory
restore helper), replacing them with calls into the `ITransportBoxTransitionSideEffect`
collection and the `ITransportBoxInventoryRestorer` service that earlier tasks on this branch
already introduced. The handler's constructor is now down to seven collaborators and its
`Handle()` method is pure orchestration: load the box, validate the transition, delegate to
whichever side effect supports the transition, apply the state change, delegate inventory
restoration, and persist. `dotnet build src/Anela.Heblo.Application` succeeds with no new
errors or warnings; the application-level test project is expected to fail to build until a
subsequent task updates its handler-construction calls to the new signature.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`

## Status
DONE
