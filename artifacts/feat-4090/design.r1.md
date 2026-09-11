# Design: Remove handler-to-handler MediatR dispatch from ChangeTransportBoxStateHandler

## Component Design

### `ChangeTransportBoxStateHandler` (modified)
- **Responsibility:** unchanged — validate and execute a `TransportBox` state transition, persist it, and return the updated box in the response.
- **Removed dependency:** `MediatR.IMediator`.
- **Added dependency:** `AutoMapper.IMapper`, injected via constructor (same interface already used by `GetTransportBoxByIdHandler`, `AddItemToBoxHandler`, `RemoveItemFromBoxHandler`).
- **Changed internal step:** after `_repository.UpdateAsync(box, cancellationToken)` and `_repository.SaveChangesAsync(cancellationToken)` succeed, the handler no longer dispatches `GetTransportBoxByIdRequest` through `IMediator`. Instead it maps the in-memory `box` (already reflecting the persisted state) directly:

  ```csharp
  var updatedBoxDto = _mapper.Map<TransportBoxDto>(box);
  return new ChangeTransportBoxStateResponse
  {
      Success = true,
      UpdatedBox = new GetTransportBoxByIdResponse { TransportBox = updatedBoxDto }
  };
  ```
- **Unaffected:** constructor parameters `ITransportBoxRepository`, `IInventoryReservationService`, `ILogger<ChangeTransportBoxStateHandler>`, `ICurrentUserService`, `ILogisticsStockOperationService`, `TimeProvider`; the `CallBackMap` state-transition dispatch table; all error branches (`TransportBoxCodeRequiredException`, `TransportBoxCodeFormatException`, `TransportBoxEmptyException`, `TransportBoxInvalidStateTransitionException`, `ValidationException`, generic `Exception`); `HandleNewToOpened`, `HandleOpenToQuarantine`, `HandleOpenToReserve`, `HandleReceived`, `RestoreInventoryForItemsAsync`.

### `GetTransportBoxByIdHandler` (untouched)
No longer invoked by `ChangeTransportBoxStateHandler`. Remains reachable independently from the controller for its own `GetTransportBoxByIdRequest` use case. No code or contract change.

### `ChangeTransportBoxStateHandlerTests` (modified, test-only)
- Replace `Mock<IMediator> _mediatorMock` with `Mock<IMapper> _mapperMock`, following the exact pattern already used in `AddItemToBoxHandlerTests`/`RemoveItemFromBoxHandlerTests`:
  ```csharp
  _mapperMock = new Mock<IMapper>();
  _mapperMock
      .Setup(x => x.Map<TransportBoxDto>(It.IsAny<TransportBox>()))
      .Returns(new TransportBoxDto());
  ```
  overridden per-test wherever a test asserts on the specific mapped `TransportBoxDto` contents.
- Remove every `_mediatorMock.Setup(...)` that arranges a `GetTransportBoxByIdRequest` → `GetTransportBoxByIdResponse` result.
- Remove the two `_mediatorMock.Verify(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), It.IsAny<CancellationToken>()), Times.Once)` assertions; there is no mediator call left to verify.
- Update the handler-construction call in the test constructor to pass `_mapperMock.Object` in place of `_mediatorMock.Object`.
- Preserve every other test's intent (state-transition outcomes, error codes, side-effect calls into `_repositoryMock`/`_inventoryReservationServiceMock`/`_stockUpProcessingServiceMock`) unchanged.

### `ChangeTransportBoxStateReceiveAtomicityIntegrationTests` (verify only, likely untouched)
Check for any reference to `IMediator` or `GetTransportBoxByIdRequest` tied to `ChangeTransportBoxStateHandler`'s dispatch behavior. As an integration test exercising real persistence rather than mocks, it is expected to have none and require no change; update only if such a reference is found.

## Data Schemas

No schema changes. For reference, the shapes involved (all pre-existing, unchanged):

```csharp
// ChangeTransportBoxStateResponse — UNCHANGED shape
public class ChangeTransportBoxStateResponse : BaseResponse
{
    public GetTransportBoxByIdResponse? UpdatedBox { get; set; }
}

// GetTransportBoxByIdResponse — UNCHANGED shape
public class GetTransportBoxByIdResponse : BaseResponse
{
    public TransportBoxDto? TransportBox { get; set; }
}
```

**Before → After for `UpdatedBox` construction (internal only, no wire-format change):**

| | Before | After |
|---|---|---|
| Source of `TransportBoxDto` | Fresh DB read via `_mediator.Send(new GetTransportBoxByIdRequest{...})` → `GetTransportBoxByIdHandler` → `_repository.GetByIdWithDetailsAsync` → `_mapper.Map<TransportBoxDto>(...)` | Direct `_mapper.Map<TransportBoxDto>(box)` on the already-loaded, already-updated, already-saved in-memory `box` |
| DB reads for `UpdatedBox` | 1 extra (`GetByIdWithDetailsAsync` inside `GetTransportBoxByIdHandler`) | 0 extra |
| MediatR pipeline executions | 2 (`ChangeTransportBoxStateRequest` + nested `GetTransportBoxByIdRequest`) | 1 |
| Wire response shape (`ChangeTransportBoxStateResponse` JSON) | `{ success, updatedBox: { transportBox: {...} } }` | unchanged — `{ success, updatedBox: { transportBox: {...} } }` |
