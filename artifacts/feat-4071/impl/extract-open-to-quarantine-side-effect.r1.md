# Implementation: extract-open-to-quarantine-side-effect

## What was implemented
Extracted the Opened→Quarantine transition handling out of `ChangeTransportBoxStateHandler` into a standalone `OpenToQuarantineSideEffect` class implementing `ITransportBoxTransitionSideEffect`, matching the style of the already-committed `NewToOpenedSideEffect` and `OpenToReserveSideEffect` siblings in the same directory. The side effect has no validation or state mutation of its own — `TransportBox.ToQuarantine()` already clears `Location`, so `ExecuteAsync` always returns `null` (meaning: continue the transition, no short-circuit failure). It exists as an explicit, registered strategy (rather than being omitted from dispatch) so future Quarantine-entry behavior has one obvious place to be added, and so dispatch-uniqueness tests can assert exactly one strategy handles this from/to pair.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/OpenToQuarantineSideEffect.cs` — new `ITransportBoxTransitionSideEffect` implementation for the Opened→Quarantine transition; `Supports` matches that exact pair, `ExecuteAsync` always returns `null`.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenToQuarantineSideEffectTests.cs` — new xUnit test file for the class above.

## Tests
`OpenToQuarantineSideEffectTests` (3 tests, all passing):
- `Supports_OpenedToQuarantine_ReturnsTrue` — confirms the strategy claims the Opened→Quarantine pair.
- `Supports_AnyOtherPair_ReturnsFalse` — confirms it does not claim an unrelated pair (Opened→Reserve).
- `ExecuteAsync_AlwaysReturnsNull` — confirms `ExecuteAsync` never short-circuits the handler with a failure response.

## How to verify
```
cd backend
dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~OpenToQuarantineSideEffectTests"
```
Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3`.

Note: in this sandboxed environment, a plain Debug-config `dotnet test`/`dotnet build` on this solution hangs indefinitely inside `Anela.Heblo.API.csproj`'s `GenerateAccessMatrix` MSBuild target (`BeforeTargets="Build"`, Debug-only), which shells out to a nested `dotnet run --project tools/Anela.Heblo.AccessMatrixGen`. The generator completes and writes its output files, but the nested process then never exits, deadlocking the outer build (reproduced twice, unrelated to this change — pre-existing in the repo). Building/testing with `-c Release` (which skips the Debug-only target) avoided the hang and completed normally in this environment; this is worth flagging to the team as it will also affect any other Debug-config `dotnet build`/`dotnet test` run in a similar sandboxed/offline setup.

## Notes
- No DI registration was added for `OpenToQuarantineSideEffect`, matching the fact that neither sibling (`NewToOpenedSideEffect`, `OpenToReserveSideEffect`) has one yet either — wiring these into the handler/DI container appears to be a separate, later step in this multi-task pipeline, out of scope for this task per its exact instructions.
- Implementation and test files match the task's specified code verbatim.

## Status
DONE
