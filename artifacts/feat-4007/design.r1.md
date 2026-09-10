# Design: Unit test coverage for UpdateTransportBoxDescriptionHandler

## Component Design

### `UpdateTransportBoxDescriptionHandlerTests` (new test class)
Location: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/UpdateTransportBoxDescriptionHandlerTests.cs`
Namespace: `Anela.Heblo.Tests.Features.Logistics.Transport`

Responsibility: exercise every branch of `UpdateTransportBoxDescriptionHandler.Handle` in isolation, with no real repository, mediator, or logger.

Fields (constructor-initialized, mirroring `RemoveItemFromBoxHandlerTests` conventions):
- `Mock<ITransportBoxRepository> _repositoryMock`
- `Mock<IMediator> _mediatorMock`
- `Mock<ILogger<UpdateTransportBoxDescriptionHandler>> _loggerMock`
- `UpdateTransportBoxDescriptionHandler _handler` — constructed once in the test class constructor from the three mocks' `.Object`

Test methods (one `[Fact]` each):
| Method | Scenario | Key assertions |
|---|---|---|
| `Handle_BoxNotFound_ReturnsTransportBoxNotFoundError` | `GetByIdWithDetailsAsync` returns `null` | `Success == false`; `ErrorCode == ErrorCodes.TransportBoxNotFound`; `Params["BoxId"]` == requested id as string; `UpdateAsync`/`SaveChangesAsync`/`mediator.Send` never called |
| `Handle_RepositoryThrows_ReturnsTransportBoxStateChangeError` | `GetByIdWithDetailsAsync` throws | `Success == false`; `ErrorCode == ErrorCodes.TransportBoxStateChangeError`; `Params["boxId"]` == requested id as string; exception does not propagate (test itself not wrapped in `Assert.ThrowsAsync`) |
| `Handle_ValidRequest_UpdatesDescriptionAndReturnsUpdatedBox` | repository returns a valid `TransportBox`, mediator returns a stub `GetTransportBoxByIdResponse` | `box.Description` mutated to `request.Description`; `_repositoryMock.Verify(UpdateAsync(box, ct), Times.Once)`; `_repositoryMock.Verify(SaveChangesAsync(ct), Times.Once)`; `_mediatorMock.Verify(Send(It.Is<GetTransportBoxByIdRequest>(r => r.Id == request.BoxId), ct), Times.Once)`; `Success == true`; `UpdatedBox` is the exact stubbed instance returned by `mediator.Send` |

Private helper: `CreateBox(int id, string? description = null)` — constructs a minimal `TransportBox` with `Id` set (public settable) and optionally `Description` pre-set, no reflection needed (unlike `State`/`Code` in sibling files, since this handler never reads those).

## Data Schemas
No schema changes. Types consumed as-is from production code:

```csharp
// Request (existing, unchanged)
class UpdateTransportBoxDescriptionRequest : IRequest<UpdateTransportBoxDescriptionResponse>
{
    int BoxId;
    string? Description;
}

// Response (existing, unchanged)
class UpdateTransportBoxDescriptionResponse : BaseResponse
{
    GetTransportBoxByIdResponse? UpdatedBox;
    // inherited from BaseResponse: bool Success, ErrorCodes ErrorCode, Dictionary<string,string>? Params
}
```

Mock setup shapes:
```csharp
// Not-found
_repositoryMock.Setup(x => x.GetByIdWithDetailsAsync(boxId)).ReturnsAsync((TransportBox?)null);

// Exception
_repositoryMock.Setup(x => x.GetByIdWithDetailsAsync(boxId)).ThrowsAsync(new InvalidOperationException("boom"));

// Happy path
_repositoryMock.Setup(x => x.GetByIdWithDetailsAsync(boxId)).ReturnsAsync(box);
_repositoryMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
_mediatorMock
    .Setup(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(new GetTransportBoxByIdResponse());
```
