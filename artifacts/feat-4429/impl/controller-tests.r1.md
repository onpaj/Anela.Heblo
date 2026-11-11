# controller-tests r1
- Extended `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxControllerTests.cs`: for each of CreateNewTransportBox, AddItemToBox, RemoveItemFromBox, UpdateTransportBoxDescription, GetTransportBoxByCode added 5 tests (success->Ok, TransportBoxNotFound->404, TransportBoxInvalidStateTransition->422, ValidationError->400, failure without ErrorCode->400). Added GetTransportBoxById null-box -> 404 test.
- No other tests in backend/test assert 400 on these endpoints (grep).
- Verification: `dotnet test --filter FullyQualifiedName~TransportBoxControllerTests` 33 passed. The wider `~TransportBox` filter has 5 failures from Testcontainers/Docker being unavailable in this environment (unrelated).

## Status
DONE
