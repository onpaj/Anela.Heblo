### task: remove-mediator-dependency

**Goal:** `ChangeTransportBoxStateHandler` no longer depends on `IMediator`; it builds `UpdatedBox` by mapping the in-memory, already-saved `box` directly via `IMapper`.

**Files:**
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`

**Steps:**
1. Add `using AutoMapper;` to the file's using list if not already present (it is not, currently).
2. Add a `private readonly IMapper _mapper;` field, placed alongside the other injected-service fields (near `_stockOperationService`/`_timeProvider`, matching this file's existing field-declaration order/style).
3. In the constructor: remove the `IMediator mediator` parameter and the `_mediator = mediator;` assignment; add an `IMapper mapper` parameter and `_mapper = mapper;` assignment. Keep the rest of the parameter order stable — insert/remove only at the position where `mediator` currently sits, don't reorder unrelated parameters.
4. Remove the `private readonly IMediator _mediator;` field declaration.
5. In `Handle(...)`, replace:
   ```csharp
   // Get updated box details
   var updatedBoxRequest = new GetTransportBoxByIdRequest { Id = request.BoxId };
   var updatedBox = await _mediator.Send(updatedBoxRequest, cancellationToken);
   ```
   with:
   ```csharp
   // Map the already-updated box directly — avoids a redundant DB read and MediatR round-trip
   var updatedBoxDto = _mapper.Map<TransportBoxDto>(box);
   var updatedBox = new GetTransportBoxByIdResponse { TransportBox = updatedBoxDto };
   ```
   (Keep the existing `_logger.LogInformation("Transport box {BoxId} state changed to {NewState}", ...)` line and the `return new ChangeTransportBoxStateResponse { Success = true, UpdatedBox = updatedBox };` line exactly as they are — only the two lines producing `updatedBox` change.)
6. Check whether `using MediatR;` is still needed in the file: it is — the class still declares `IRequestHandler<ChangeTransportBoxStateRequest, ChangeTransportBoxStateResponse>`, which lives in the `MediatR` namespace. Do not remove that using directive.
7. Check whether `using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;` is still needed: it is — `GetTransportBoxByIdResponse` (used in step 5) and the `ChangeTransportBoxStateResponse.UpdatedBox` property type both come from that namespace. Do not remove it.
8. Confirm no other member of the class references `_mediator` before finalizing (search the whole file, not just the `Handle` method, since private helper methods like `HandleNewToOpened`/`HandleOpenToReserve`/`HandleOpenToQuarantine`/`HandleReceived`/`RestoreInventoryForItemsAsync` do not use it today, but re-verify).

**Verification:**
- `grep -n "IMediator\|_mediator" backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` returns no matches.
- `grep -n "_mapper\b" backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` shows the field, the constructor assignment, and the one usage in `Handle`.
- File compiles as part of the next task's build check (this task alone will not compile standalone since the test project also needs updating — that's expected, don't attempt to build until after the test task, or build with `dotnet build backend/src/Anela.Heblo.Application` in isolation if you want an earlier signal).

---

