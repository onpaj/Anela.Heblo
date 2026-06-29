# Design: Coverage Gap – ChangeTransportBoxStateHandler (Opened→Reserve and Reserve→Received)

## Component Design

### Test file location

All three tests are added to the existing test class:
`backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs`

No new files are created. No production code is touched.

### Test 1 – Handle_OpenedToReserve_EmptyLocation_ReturnsTransportBoxStateChangeError

- Box state: `Opened` (via `CreateTestBox(TransportBoxState.Opened)`)
- Request: `BoxId = 1`, `NewState = TransportBoxState.Reserve`, `Location = null` (or omitted)
- Flow: `AssignLocationIfAny(null)` is a no-op → `box.Location` stays null → transition condition `b => b.Location != null` fails → returns `TransportBoxStateChangeError`
- Repository mocks: only `GetByIdWithDetailsAsync` is needed (UpdateAsync and SaveChangesAsync never called)
- Assertions:
  - `result.Success.Should().BeFalse()`
  - `result.ErrorCode.Should().Be(ErrorCodes.TransportBoxStateChangeError)`
  - `_repositoryMock.Verify(x => x.UpdateAsync(...), Times.Never)`
  - `_stockUpProcessingServiceMock.Verify(x => x.CreateOperationAsync(...), Times.Never)`

**Note:** The arch-review clarified that `null` Location is caught by the transition condition (returns `TransportBoxStateChangeError`), not by `HandleOpenToReserve` (which returns `RequiredFieldMissing`). To cover `HandleOpenToReserve`'s guard specifically, use `Location = ""` — `AssignLocationIfAny("")` sets `box.Location = ""` (non-null), so the condition passes but `string.IsNullOrEmpty("")` returns `RequiredFieldMissing`. Both cases (null and empty) are worth testing; the primary test uses empty string.

### Test 2 – Handle_OpenedToReserve_WithValidLocation_Succeeds

- Box state: `Opened` (via `CreateTestBox(TransportBoxState.Opened)`)
- Request: `BoxId = 1`, `NewState = TransportBoxState.Reserve`, `Location = "SHELF-A1"`
- Flow: `AssignLocationIfAny("SHELF-A1")` sets `box.Location = "SHELF-A1"` → condition passes → `HandleOpenToReserve` returns null → state transitions to Reserve → box persisted → updated box fetched
- Repository mocks: `GetByIdWithDetailsAsync`, `UpdateAsync`, `SaveChangesAsync` (using existing inline setup pattern)
- Mediator mock: `Send(GetTransportBoxByIdRequest)` returns a `GetTransportBoxByIdResponse`
- Assertions:
  - `result.Success.Should().BeTrue()`
  - `result.ErrorCode.Should().BeNull()`
  - `result.UpdatedBox.Should().Be(updatedBoxResponse)`
  - `_repositoryMock.Verify(x => x.UpdateAsync(...), Times.Once)`

### Test 3 – Handle_ReserveToReceived_AggregatesDuplicateProductCodes_IntoSingleStockUpOperation

- Box state: `Reserve` (via `CreateTestBoxWithMultipleItems(TransportBoxState.Reserve, ...)`)
- Items: two lines for `"P-001"` with amounts `3.0` and `5.0`
- Request: `BoxId = 1`, `NewState = TransportBoxState.Received`
- Flow: `HandleReceived` groups items by ProductCode → aggregates amount to 8 → calls `CreateOperationAsync("BOX-000001-P-001", "P-001", 8, TransportBox, 1, ...)`
- Repository/mediator mocks: `SetupReceivedTransitionMocks(box)` (existing helper)
- Assertions:
  - `result.Success.Should().BeTrue()`
  - `_stockUpProcessingServiceMock.Verify(x => x.CreateOperationAsync("BOX-000001-P-001", "P-001", 8, LogisticsStockOperationSource.TransportBox, 1, It.IsAny<CancellationToken>()), Times.Once)`
  - `_stockUpProcessingServiceMock.Verify(x => x.CreateOperationAsync(It.IsAny<string>(), ...), Times.Once)` — exactly one operation total

## Data Schemas

No new data schemas. The tests exercise existing request/response types:

| Type | Relevant fields |
|---|---|
| `ChangeTransportBoxStateRequest` | `BoxId`, `NewState`, `Location` |
| `ChangeTransportBoxStateResponse` | `Success`, `ErrorCode`, `Params`, `UpdatedBox` |
| `TransportBox` | `State` (reflection), `Code` (reflection), `Location`, `Items` |
| `TransportBoxItem` | `ProductCode`, `Amount` |
