# Implementation: remove-mediator-dependency

## What was implemented
Removed the `IMediator` dependency from `ChangeTransportBoxStateHandler`. Instead of issuing a redundant `GetTransportBoxByIdRequest` through MediatR to re-fetch the box it had just saved, the handler now maps the in-memory, already-updated `box` entity directly to `TransportBoxDto` via `IMapper` and wraps it in a `GetTransportBoxByIdResponse` to build the `UpdatedBox` response field. No state-machine, validation, or side-effect behavior changed — only how `UpdatedBox` is constructed.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` — added `using AutoMapper;`, replaced the `IMediator _mediator` field/constructor parameter with `IMapper _mapper` (same position in the parameter/field list), and replaced the `_mediator.Send(new GetTransportBoxByIdRequest {...})` call in `Handle` with `_mapper.Map<TransportBoxDto>(box)` wrapped in a `GetTransportBoxByIdResponse`. Kept the `using MediatR;` (still required for `IRequestHandler<...>`) and `using ...GetTransportBoxById;` (still required for `GetTransportBoxByIdResponse`) directives.

## Tests
None — out of scope for this task. Updating `ChangeTransportBoxStateHandlerTests.cs` is a separate, later task.

## How to verify
1. `grep -n "IMediator\|_mediator" backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` — returns no matches.
2. `grep -n "_mapper\b" backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` — shows the field declaration, the constructor assignment, and the one usage in `Handle`.
3. `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — builds with 0 errors (138 pre-existing warnings unrelated to this change, none introduced by it).

## Notes
- Confirmed `TransportBoxMappingProfile.cs` already declares a `CreateMap<TransportBox, ...>` mapping, so `_mapper.Map<TransportBoxDto>(box)` resolves correctly at runtime (not exercised by a test in this task, per scope).
- Did not touch `ChangeTransportBoxStateHandlerTests.cs` or `UpdateTransportBoxDescriptionHandler`, per the stated out-of-scope constraints.
- No deviations from the specified steps.

## PR Summary
This change is part of arch-review issue #4090's cleanup of a redundant-dependency anti-pattern in the Logistics module. `ChangeTransportBoxStateHandler` previously re-fetched the transport box it had just saved via a `IMediator.Send(GetTransportBoxByIdRequest)` round-trip purely to build the `UpdatedBox` field of its response — an unnecessary DB read and an internal MediatR dependency for what is really just a DTO projection. The handler now uses `IMapper` to map the already-in-memory, already-persisted `box` entity straight to `TransportBoxDto`, dropping the `IMediator` dependency entirely. No behavior change to state transitions, validation, error codes, or side effects; only the construction of `UpdatedBox` changed.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`

## Status
DONE
