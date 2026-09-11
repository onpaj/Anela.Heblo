# Specification: Remove handler-to-handler MediatR dispatch from ChangeTransportBoxStateHandler

## Summary
`ChangeTransportBoxStateHandler.Handle` currently re-fetches the transport box it just saved by dispatching a second MediatR request (`GetTransportBoxByIdRequest`) to `GetTransportBoxByIdHandler`, purely to obtain a `TransportBoxDto` for the response. This wastes a database round-trip and the full MediatR pipeline on data the handler already holds in memory, and it creates an invisible handler-to-handler dependency. This change replaces that dispatch with a direct `IMapper` projection of the in-memory, already-updated `box` aggregate, matching the pattern already used by the sibling `AddItemToBoxHandler` and `RemoveItemFromBoxHandler` in the same `Logistics/UseCases` folder.

## Background
`ChangeTransportBoxStateHandler` (`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`) loads a `TransportBox` via `_repository.GetByIdWithDetailsAsync`, mutates it in-memory (state transition, location/description updates, side-effect callbacks), persists it with `_repository.UpdateAsync` + `_repository.SaveChangesAsync`, and then — at lines 138-139 — issues:

```csharp
var updatedBoxRequest = new GetTransportBoxByIdRequest { Id = request.BoxId };
var updatedBox = await _mediator.Send(updatedBoxRequest, cancellationToken);
```

`GetTransportBoxByIdHandler` (`.../GetTransportBoxById/GetTransportBoxByIdHandler.cs`) does nothing more than `_repository.GetByIdWithDetailsAsync(request.Id)` followed by `_mapper.Map<TransportBoxDto>(transportBox)`, wrapped in a `GetTransportBoxByIdResponse`. Because `box` in `ChangeTransportBoxStateHandler` was already loaded with the same `GetByIdWithDetailsAsync` call and then updated and saved, the mediator round-trip re-reads the identical row from the database and re-runs the whole MediatR pipeline (logging/validation/caching behaviors on the read path) for no new information.

This is a pure internal-dependency cleanup driven by an arch-review finding (issue #4090); it changes no externally observable behavior of the `ChangeTransportBoxStateRequest` API — the response shape and success/error semantics are unchanged, only how `UpdatedBox` is populated.

Two other handlers in the same folder already exhibit the *good* pattern this change moves toward:
- `AddItemToBoxHandler.Handle` maps its already-updated `transportBox` directly: `var transportBoxDto = _mapper.Map<TransportBoxDto>(transportBox);`
- `RemoveItemFromBoxHandler.Handle` does the same.

Note: `UpdateTransportBoxDescriptionHandler` has the identical anti-pattern (also dispatches `GetTransportBoxByIdRequest` via `_mediator`), but it is **not** part of this issue's scope and must not be touched — see Out of Scope.

## Functional Requirements

### FR-1: Replace mediator re-fetch with direct in-memory mapping
After `_repository.UpdateAsync(box, cancellationToken)` and `_repository.SaveChangesAsync(cancellationToken)` succeed in `ChangeTransportBoxStateHandler.Handle`, the handler must build `ChangeTransportBoxStateResponse.UpdatedBox` by mapping the in-memory `box` instance directly, instead of dispatching a new `GetTransportBoxByIdRequest` via `IMediator`.

`ChangeTransportBoxStateResponse.UpdatedBox` is typed as `GetTransportBoxByIdResponse` (not `TransportBoxDto` directly), so the replacement must preserve that response shape:

```csharp
var updatedBoxDto = _mapper.Map<TransportBoxDto>(box);

return new ChangeTransportBoxStateResponse
{
    Success = true,
    UpdatedBox = new GetTransportBoxByIdResponse { TransportBox = updatedBoxDto }
};
```

**Acceptance criteria:**
- `ChangeTransportBoxStateHandler.Handle` on the success path no longer calls `_mediator.Send` (or any `IMediator` member) at all.
- `ChangeTransportBoxStateResponse.UpdatedBox.TransportBox` on a successful state change is byte-for-byte equivalent (same field values) to what `GetTransportBoxByIdHandler` would have produced for the same box — i.e. `_mapper.Map<TransportBoxDto>(box)` using the same `IMapper`/`AutoMapper` profile already registered for `GetTransportBoxByIdHandler`.
- No other field of `ChangeTransportBoxStateResponse` (`Success`, `ErrorCode`, `Params`) changes behavior.

### FR-2: Remove the now-unused `IMediator` dependency
`IMediator` must be removed from `ChangeTransportBoxStateHandler`'s constructor and field list once no code path in the class uses it.

**Acceptance criteria:**
- `ChangeTransportBoxStateHandler` has no `IMediator _mediator` field and no `IMediator` constructor parameter.
- `using MediatR;` is removed from the file only if nothing else in the file still needs it (the class itself still implements `IRequestHandler<...>`, which lives in the `MediatR` namespace, so the `using` directive for `MediatR` must stay — only the injected `IMediator` service is removed, not the `IRequestHandler` interface implementation).
- The now-unused `using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;` import is kept only if `GetTransportBoxByIdRequest`/`GetTransportBoxByIdResponse` types are still referenced elsewhere in the file (they are, via `ChangeTransportBoxStateResponse.UpdatedBox`'s type and the new direct construction in FR-1) — verify at implementation time whether the import is still needed and remove it only if genuinely unused.

### FR-3: Add `IMapper` as a constructor dependency
`ChangeTransportBoxStateHandler` must take an `AutoMapper.IMapper` constructor parameter (the same interface already injected into `GetTransportBoxByIdHandler`, `AddItemToBoxHandler`, and `RemoveItemFromBoxHandler` in this codebase) and store it in a `_mapper` field, following the existing field/constructor ordering conventions used by the sibling handlers in this folder.

**Acceptance criteria:**
- `IMapper` is resolved through the existing DI container without any new registration (AutoMapper and its profile for `TransportBox` → `TransportBoxDto` are already registered application-wide, since `GetTransportBoxByIdHandler` already depends on it successfully).
- No change to how `ChangeTransportBoxStateHandler` is registered in DI is required beyond the constructor signature change (MediatR's assembly-scanning handler registration resolves constructor parameters automatically).

### FR-4: Update existing unit tests
`ChangeTransportBoxStateHandlerTests` (`backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs`) currently constructs the handler with a `Mock<IMediator>` and, in multiple success-path tests, arranges `_mediatorMock.Setup(...).ReturnsAsync(...)` for `GetTransportBoxByIdRequest`, plus two explicit `_mediatorMock.Verify(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), ...), Times.Once)` assertions (at least at lines 140 and 625 in the current file). These must be updated to reflect the new dependency:

**Acceptance criteria:**
- The test class replaces its `Mock<IMediator> _mediatorMock` field (and its use in the handler constructor call) with a `Mock<IMapper> _mapperMock`, mirroring the pattern in `AddItemToBoxHandlerTests`/`RemoveItemFromBoxHandlerTests` (default setup: `_mapperMock.Setup(x => x.Map<TransportBoxDto>(It.IsAny<TransportBox>())).Returns(new TransportBoxDto())`, overridden per-test where a specific mapped shape is asserted on).
- Every test-specific `_mediatorMock.Setup(...)` arranging a `GetTransportBoxByIdRequest`/`GetTransportBoxByIdResponse` result is removed; any assertion on the response's `UpdatedBox.TransportBox` content is re-expressed against the mapper mock's return value or against expected `box` state directly.
- The two `_mediatorMock.Verify(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), ...), Times.Once)` assertions are removed (there is no longer a mediator call to verify) and, where meaningful, replaced with an equivalent `_mapperMock.Verify(x => x.Map<TransportBoxDto>(box), Times.Once)`-style assertion or dropped if redundant with existing response-content assertions.
- All references to `Mock<IMediator>`/`IMediator` in this test file are removed; the `using MediatR;` import is dropped from the test file only if nothing else in the test file needs it (check for other `MediatR` type usages, e.g. `IRequestHandler`, before removing).
- The full existing test suite in this file continues to pass with the same test *intent* (state-transition behavior, error codes, side effects) unchanged — only the plumbing for how `UpdatedBox` is produced changes.
- `ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs` is reviewed for any direct or incidental dependency on `IMediator`/`GetTransportBoxByIdRequest` being dispatched from `ChangeTransportBoxStateHandler`; update only if such a dependency exists (expected: none, since it is an integration test against real persistence, not a mock-based unit test).

## Non-Functional Requirements

### NFR-1: Performance
Eliminates one redundant database read (`GetByIdWithDetailsAsync`) and one redundant full MediatR pipeline execution (including any registered `IPipelineBehavior<,>` for logging/validation/caching on the read path) per successful `ChangeTransportBoxStateRequest`. No new performance requirement is introduced; this is a pure removal of avoidable work.

### NFR-2: Backward compatibility
The public contract of `ChangeTransportBoxStateRequest`/`ChangeTransportBoxStateResponse` (as consumed by the MVC controller and, transitively, the generated OpenAPI/TypeScript client) does not change. No controller, frontend, or DTO shape change is required or permitted by this change.

## Data Model
No data model changes. `TransportBox` (domain aggregate), `TransportBoxDto` (application DTO, mapped via the existing AutoMapper profile), `GetTransportBoxByIdResponse`, and `ChangeTransportBoxStateResponse` all keep their current shapes.

## API / Interface Design
No HTTP/API surface change. This is an internal application-layer refactor confined to `ChangeTransportBoxStateHandler`'s implementation and its unit test.

## Dependencies
- `AutoMapper.IMapper` and the existing `TransportBox` → `TransportBoxDto` mapping profile, already registered and in use by `GetTransportBoxByIdHandler`, `AddItemToBoxHandler`, and `RemoveItemFromBoxHandler` — no new registration needed.
- No dependency on `GetTransportBoxByIdHandler` remains after this change (that is the point of the fix).

## Out of Scope
- `UpdateTransportBoxDescriptionHandler`, which has the same handler-to-handler `IMediator` dispatch anti-pattern, is explicitly **not** touched by this change. It is a separate, un-filed arch-review finding; fixing it here would exceed this issue's scope (issue #4090 names only `ChangeTransportBoxStateHandler`).
- No behavior change to the state-machine transitions, validation rules, error codes, or inventory-reservation/stock-operation side effects in `ChangeTransportBoxStateHandler` — only the `UpdatedBox` construction on the success path changes.
- No change to `GetTransportBoxByIdHandler`, `GetTransportBoxByIdRequest`, or `GetTransportBoxByIdResponse` themselves; `ChangeTransportBoxStateResponse.UpdatedBox` keeps its existing `GetTransportBoxByIdResponse` type for backward compatibility with any code/tests/clients that read that shape.
- No introduction of a shared "map box to response" helper across handlers — the brief mentions this only as a fallback if response shapes diverge, and here they do not (direct `_mapper.Map<TransportBoxDto>(box)` is sufficient, matching the existing pattern in `AddItemToBoxHandler`/`RemoveItemFromBoxHandler`).

## Open Questions
None.

## Status: COMPLETE
