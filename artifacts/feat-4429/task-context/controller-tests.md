### task: controller-tests
Extend `TransportBoxControllerTests.cs` (follow existing Moq/ControllerContext pattern). For each of the five changed endpoints add a success test (assert `OkObjectResult`) and a failure test asserting the mapped result: `TransportBoxNotFound` -> `NotFoundObjectResult`; `TransportBoxInvalidStateTransition` -> `ObjectResult` with 422; a failure with no ErrorCode or `ValidationError` -> `BadRequestObjectResult`. Add a not-found test for `GetTransportBoxById`. Also grep `backend/test` for other tests (integration/API) asserting 400 on these endpoints and update them to the mapped codes.
Check: `cd backend && dotnet test --filter "FullyQualifiedName~TransportBox"` passes.

