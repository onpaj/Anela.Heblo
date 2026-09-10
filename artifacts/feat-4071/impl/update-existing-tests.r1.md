# Implementation: update-existing-tests

## What was implemented
Updated all direct construction call sites of `ChangeTransportBoxStateHandler` to match its new constructor shape (`IEnumerable<ITransportBoxTransitionSideEffect> sideEffects` + `ITransportBoxInventoryRestorer inventoryRestorer` in place of the old direct `IInventoryReservationService`/`ILogisticsStockOperationService` parameters). Each call site now builds real `NewToOpenedSideEffect`, `OpenToReserveSideEffect`, `OpenToQuarantineSideEffect`, `ReceivedSideEffect` instances from the existing mocks/fakes, plus a `TransportBoxInventoryRestorer` wrapping the existing `IInventoryReservationService` mock, so every pre-existing test keeps exercising the same underlying mocked dependencies it did before. No test method bodies or assertions were changed.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs` — constructor/setup block rebuilt to construct the four side effects + restorer from existing mocks and pass them into the new handler constructor signature; added `using Microsoft.Extensions.Logging.Abstractions;` for `NullLogger<ReceivedSideEffect>`.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs` — `CreateHandler` helper updated the same way, using the real `transportBoxRepository`, `currentUserService`, and the real `adapter` (`LogisticsStockOperationAdapter`) already built in that method, plus a `TransportBoxInventoryRestorer` over `Mock.Of<IInventoryReservationService>()`.
- `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs` — **not in the original task's file list**, but this file also constructs `ChangeTransportBoxStateHandler` directly and failed to compile against the new signature. Updated it with the same pattern (using its existing `_repository`, `_mockUserService`, `_mockStockUpProcessingService`, `_mockInventoryReservationService` mocks) since the build would otherwise fail. No assertions changed here either.

## Tests
- `ChangeTransportBoxStateHandlerTests` (26 unit test facts/theories) — all pass.
- `ChangeTransportBoxStateReceiveAtomicityIntegrationTests` (2 integration tests, require a shared Postgres testcontainer) — fail in this sandbox with `Docker is either not running or misconfigured`, i.e. purely an environment limitation (no Docker available), not a code/assertion issue. The file compiles and constructs the handler correctly.
- `TransportBoxUniquenessTests` — covered by the same `dotnet test` run implicitly via the build; not run standalone here but compiles and uses the identical construction pattern as the other two files.

Full filtered run (`--filter "FullyQualifiedName~ChangeTransportBoxState"`): `Failed: 2, Passed: 26, Skipped: 0, Total: 28` — the 2 failures are exclusively the Docker-dependent integration tests.

## How to verify
```bash
cd backend
dotnet build test/Anela.Heblo.Tests
dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ChangeTransportBoxState"
```
With Docker available, all 28 tests should pass, including the 2 integration tests.

## Notes
- Deviation: the task description named exactly two files to modify, but a third pre-existing test (`TransportBoxUniquenessTests.cs`) also constructs `ChangeTransportBoxStateHandler` directly via the old constructor signature and would not compile otherwise. Fixed it using the identical pattern to keep the build green, without touching any of its assertions. Flagging this so the pipeline is aware a third call site exists (in case a later task assumed only two).
- `artifacts/feat-4071/state.json` showed as modified in the working tree before this task started (pre-existing, unrelated to this change) and was left untouched/unstaged.
- No production code was touched — only test files.

## PR Summary
This change updates every direct-construction call site of `ChangeTransportBoxStateHandler` to compile against its new constructor signature introduced by the side-effect-strategy refactor (`IEnumerable<ITransportBoxTransitionSideEffect>` + `ITransportBoxInventoryRestorer` instead of the raw `IInventoryReservationService`/`ILogisticsStockOperationService` dependencies). Each site wires up real `NewToOpenedSideEffect`, `OpenToReserveSideEffect`, `OpenToQuarantineSideEffect`, `ReceivedSideEffect`, and `TransportBoxInventoryRestorer` instances built from the same mocks the tests already used, so behavior and assertions are unchanged — this is a pure constructor-adaptation/mechanical update. A third call site not mentioned in the original task (`TransportBoxUniquenessTests.cs`) needed the same treatment to keep the build compiling; it was updated the same way.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs` — rebuilt constructor/setup to pass side effects + restorer.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs` — rebuilt `CreateHandler` to pass side effects + restorer.
- `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs` — rebuilt constructor to pass side effects + restorer (required for compile; not in original file list).

## Status
DONE_WITH_CONCERNS
