# Implementation: extract-open-to-reserve-side-effect

## What was implemented

Extracted the `Opened -> Reserve` transition side effect out of
`ChangeTransportBoxStateHandler.HandleOpenToReserve` into a standalone
`ITransportBoxTransitionSideEffect` implementation, `OpenToReserveSideEffect`,
following the pattern already established by `NewToOpenedSideEffect`. The new
class validates that `request.Location` is provided, returning a
`RequiredFieldMissing` error response (field `Location`) when it is missing,
and `null` (continue transition) otherwise — identical behavior to the
private handler method it replaces.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/OpenToReserveSideEffect.cs` — new stateless `ITransportBoxTransitionSideEffect` implementation for the `Opened -> Reserve` transition.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenToReserveSideEffectTests.cs` — new unit tests.

## Tests

`OpenToReserveSideEffectTests`:
- `Supports_OpenedToReserve_ReturnsTrue`
- `Supports_AnyOtherPair_ReturnsFalse`
- `ExecuteAsync_MissingLocation_ReturnsRequiredFieldMissing`
- `ExecuteAsync_LocationProvided_ReturnsNull`

## How to verify

```bash
cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~OpenToReserveSideEffectTests"
```
Result: `Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4`.

Full solution build also verified clean:
```bash
dotnet build Anela.Heblo.sln
```
Result: `0 Error(s)` (94 pre-existing warnings, none introduced by this change).

## Notes

This task only creates the new side-effect class and its tests, matching the
task context exactly. `ChangeTransportBoxStateHandler` itself is not yet
wired to use `OpenToReserveSideEffect` — that rewiring is scoped to the
later `refactor-handler-orchestration` task per the task breakdown, so the
existing private `HandleOpenToReserve` method and its dispatch table entry
are left untouched here. No deviations from the task context.

## PR Summary
Extracted the `Opened -> Reserve` side effect of `ChangeTransportBoxStateHandler` into a standalone `OpenToReserveSideEffect` class implementing `ITransportBoxTransitionSideEffect`, with unit test coverage, as part of the incremental handler decomposition.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/OpenToReserveSideEffect.cs` — new side-effect class
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenToReserveSideEffectTests.cs` — new unit tests

## Status
DONE
