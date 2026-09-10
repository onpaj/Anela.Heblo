# Implementation: create-side-effect-interface

## What was implemented
Added the `ITransportBoxTransitionSideEffect` interface in the `ChangeTransportBoxState` use case folder. It defines the contract for per-transition side effects that `ChangeTransportBoxStateHandler` will dispatch to in a later refactor: a `Supports(from, to)` predicate to select the matching side effect, and an `ExecuteAsync(box, request, cancellationToken)` method returning `Task<ChangeTransportBoxStateResponse?>` — `null` lets the transition continue, a populated response short-circuits `Handle()` with a failure result. This mirrors the exact contract of the private `Handle*` methods currently on `ChangeTransportBoxStateHandler` (e.g. `HandleNewToOpened`, `HandleOpenToReserve`, `HandleOpenToQuarantine`, `HandleReceived`).

Before creating the file, I read `ChangeTransportBoxStateHandler.cs`, `ChangeTransportBoxStateRequest.cs`, `ChangeTransportBoxStateResponse.cs`, and confirmed `TransportBox`/`TransportBoxState` live in `Anela.Heblo.Domain.Features.Logistics.Transport` — the namespace, using directives, and type names in the task spec match the existing codebase exactly, so the file was created verbatim as specified.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ITransportBoxTransitionSideEffect.cs` — new interface `ITransportBoxTransitionSideEffect` with `Supports(TransportBoxState from, TransportBoxState to)` and `Task<ChangeTransportBoxStateResponse?> ExecuteAsync(TransportBox box, ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)`.

## Tests
none — interface only

## How to verify
1. `cd backend && dotnet build src/Anela.Heblo.Application` — build succeeded with 0 errors (139 pre-existing warnings unrelated to this change).
2. `git show 051de71` on branch `feature/4071-Arch-Review-Logistics-Changetransportboxstatehandl` shows the single new file.

## Notes
No deviations from the task spec — the interface was created exactly as given, since the existing handler/request/response/domain types already matched the specified namespace and using directives. No implementers of the interface were added; this is intentionally the first step of a larger refactor extracting per-transition side effects out of `ChangeTransportBoxStateHandler`, per the task description.

## PR Summary
Adds the `ITransportBoxTransitionSideEffect` interface that will let per-transition side effects (currently private methods on `ChangeTransportBoxStateHandler`) be extracted into standalone, dispatchable classes. This is a pure addition with no behavior change — the handler does not yet use it.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ITransportBoxTransitionSideEffect.cs` — new interface defining `Supports` and `ExecuteAsync` for transport box state-transition side effects.

## Status
DONE
