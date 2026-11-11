# Architecture Review: Unify TransportBoxController error handling

## Skip Design: true
Backend-only refactor; no UI.

## Architectural Fit Assessment
Fully aligned: `BaseApiController.HandleResponse` is the project-standard converter, already used by two endpoints in this controller and by other controllers. The change removes drift; no new components.

## Proposed Architecture

### Component Overview
Controller action -> `_mediator.Send` -> `HandleResponse(response)` -> `Ok` | mapped error result (via `ErrorCodes` `[HttpStatusCode]`).

### Key Design Decisions

#### Decision 1: Do not modify HandleResponse
**Options considered:** extend `HandleResponse` for null-payload 404; leave as is.
**Chosen approach:** leave as is.
**Rationale:** all five Pattern B handlers already return ErrorCodes (verified: `TransportBoxNotFound`, `TransportBoxStateChangeError`, `ValidationError`, etc.), so the helper suffices.

#### Decision 2: GetTransportBoxById
**Chosen approach:** inspect `GetTransportBoxByIdHandler`; if not-found is signalled via `Success=false` + `TransportBoxNotFound`, use `HandleResponse`; else keep the null check with an explanatory comment. Do not widen scope.

#### Decision 3: Accept status-code changes
Not-found errors become 404, state/stock errors 422, unexpected exceptions 500 (previously all 400). This is the intended effect of unification. Note the Exception path in AddItemToBox/RemoveItemFromBox (ErrorCode `Exception`) becomes 500.

## Implementation Guidance

### Directory / Module Structure
Edit only `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs` and `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxControllerTests.cs`. Update `docs/processes/flow-transport-box.md` (bump `verified_at` or note API status mapping) and run `python3 scripts/process-docs/check.py index`, since the controller is owned code (CI gate).

### Interfaces and Contracts
Return types stay `ActionResult<TResponse>`. Tests follow the existing pattern in the test file (Mock<IMediator>, ControllerContext with logging services, assert `NotFoundObjectResult` etc.).

### Data Flow
Unchanged except failure status selection.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Frontend branches on 400 for these endpoints | Low | grep frontend hooks/components; terminal hooks throw on any non-ok; add check in task |
| Process-doc CI gate fails | Medium | update flow-transport-box.md in same PR |
| Existing integration tests assert 400 | Low | grep backend tests for these endpoints and update |

## Specification Amendments
FR-2 is resolved by inspecting the handler during implementation.

## Prerequisites
None.
