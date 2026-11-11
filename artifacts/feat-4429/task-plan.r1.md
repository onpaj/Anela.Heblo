# Implementation Plan: Unify TransportBoxController error handling

Goal: replace inline `BadRequest`/`Ok` checks in `TransportBoxController` with `HandleResponse`. Files: `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs`, `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxControllerTests.cs`, `docs/processes/flow-transport-box.md`.

### task: controller-handle-response
Edit `TransportBoxController.cs`. In `CreateNewTransportBox`, `AddItemToBox`, `RemoveItemFromBox`, `UpdateTransportBoxDescription`, `GetTransportBoxByCode`, replace
`if (!response.Success) { return BadRequest(response); } return Ok(response);` with `return HandleResponse(response);`. Then read `GetTransportBoxByIdHandler`: if not-found is signalled with `Success=false` + `ErrorCodes.TransportBoxNotFound`, use `HandleResponse` there too; otherwise keep the `TransportBox == null` check and add a one-line comment explaining it. Do not change `BaseApiController`, routes, signatures or attributes.
Check: `cd backend && dotnet build` succeeds; `grep -n "BadRequest(response)" src/Anela.Heblo.API/Controllers/TransportBoxController.cs` returns nothing.

### task: controller-tests
Extend `TransportBoxControllerTests.cs` (follow existing Moq/ControllerContext pattern). For each of the five changed endpoints add a success test (assert `OkObjectResult`) and a failure test asserting the mapped result: `TransportBoxNotFound` -> `NotFoundObjectResult`; `TransportBoxInvalidStateTransition` -> `ObjectResult` with 422; a failure with no ErrorCode or `ValidationError` -> `BadRequestObjectResult`. Add a not-found test for `GetTransportBoxById`. Also grep `backend/test` for other tests (integration/API) asserting 400 on these endpoints and update them to the mapped codes.
Check: `cd backend && dotnet test --filter "FullyQualifiedName~TransportBox"` passes.

### task: frontend-and-docs-check
Grep `frontend/src` (`api/hooks/useTransportBoxes.ts`, `components/terminal`, `components/transport`) for logic branching on literal HTTP 400 from the five endpoints; none is expected (hooks throw on any non-ok); fix only if found. Update `docs/processes/flow-transport-box.md`: note failures map to HTTP status via ErrorCodes, bump `verified_at`, then run `python3 scripts/process-docs/check.py index`.
Final validation: `cd backend && dotnet build && dotnet format --verify-no-changes` and the TransportBox tests pass.
