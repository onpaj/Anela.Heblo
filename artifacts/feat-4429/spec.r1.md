# Specification: Unify TransportBoxController error-response handling

## Summary
`TransportBoxController` converts handler responses to HTTP results in three different ways. Five endpoints hand-roll `BadRequest`/`Ok`; two use the standard `BaseApiController.HandleResponse`. This change unifies them on `HandleResponse`, so HTTP status mapping is driven by `ErrorCodes` + `HttpStatusCodeAttribute` everywhere.

## Background
`HandleResponse<T>(T) where T : BaseResponse` returns `Ok` on success; on failure it logs a warning and maps `ErrorCode` via `[HttpStatusCode]` (default 400; 404/409/422/500 etc. where annotated), falling back to `BadRequest` when no ErrorCode is set. Pattern B endpoints always return 400 on failure, so e.g. `TransportBoxNotFound` (annotated 404) currently yields 400 from AddItemToBox/RemoveItemFromBox/UpdateTransportBoxDescription/GetTransportBoxByCode. All affected response types already derive from `BaseResponse`.

## Functional Requirements

### FR-1: Use HandleResponse in Pattern B endpoints
Replace the inline check in `CreateNewTransportBox`, `AddItemToBox`, `RemoveItemFromBox`, `UpdateTransportBoxDescription`, `GetTransportBoxByCode` with `return HandleResponse(response);`.
**Acceptance criteria:**
- No `if (!response.Success) return BadRequest(response)` remains in the controller.
- Success still returns 200 with the same body.
- Failure returns the status mapped from the response ErrorCode (400 when none or unannotated), body is the response object.

### FR-2: GetTransportBoxById decision
`GetTransportBoxById` keeps its explicit 404 when `TransportBox == null` (a null-payload check, not a Success check; the handler's not-found shape must be verified). If the handler sets `Success=false` with `TransportBoxNotFound` in that case, switch to `HandleResponse`; otherwise leave unchanged and add a short comment explaining why.
**Acceptance criteria:**
- Behaviour (404 on missing box) preserved either way.

### FR-3: Test coverage
Add unit tests in `TransportBoxControllerTests` for each changed endpoint covering success (200) and at least one mapped failure (e.g. 404 for TransportBoxNotFound, 422 for invalid state transition, 400 default).

## Non-Functional Requirements
### NFR-1: Performance
No impact.
### NFR-2: Security
No change; `[FeatureAuthorize]` attributes untouched.
### NFR-3: Compatibility
Failure status codes for some errors change from 400 to their annotated codes (404/422/500). Frontend (`useTransportBoxes.ts` and terminal components) treats any non-ok response as an error; must be verified not to branch on literal 400.

## Data Model
None.

## API / Interface Design
No route, request or response-shape changes. Only HTTP status codes on failure paths change. Generated OpenAPI clients are unaffected (return types remain `ActionResult<T>`).

## Dependencies
`BaseApiController.HandleResponse`, `ErrorCodes` with `HttpStatusCodeAttribute`.

## Out of Scope
- Changing `HandleResponse` itself.
- Other controllers.
- Success-path behaviour of the GET list/summary endpoints.

## Open Questions
None. Assumption: the status-code change to annotated values is the intended outcome (the issue asks for unification on the standard helper).

## Status: COMPLETE
