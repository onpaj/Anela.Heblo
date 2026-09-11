# Implementation Plan: Remove handler-to-handler MediatR dispatch from ChangeTransportBoxStateHandler

## Overview

`ChangeTransportBoxStateHandler` dispatches a redundant `GetTransportBoxByIdRequest` through `IMediator` after it has already loaded, mutated, and saved the `TransportBox` it needs — re-reading the same row from the database and re-running the full MediatR pipeline purely to populate the response DTO. This plan replaces that dispatch with a direct `IMapper` projection of the in-memory, already-persisted `box`, matching the pattern already used by `AddItemToBoxHandler` and `RemoveItemFromBoxHandler` in the same folder. Two tasks: change the handler, then update its unit tests to match. No API contract, schema, or UI change.

Full context: `artifacts/feat-4090/spec.r1.md`, `artifacts/feat-4090/arch-review.r1.md`, `artifacts/feat-4090/design.r1.md`.

## Ground truth (verified in this codebase)

- Target file: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`
- Anti-pattern is at the end of the success path in `Handle(...)`, right after `_repository.SaveChangesAsync(cancellationToken);`:
  ```csharp
  var updatedBoxRequest = new GetTransportBoxByIdRequest { Id = request.BoxId };
  var updatedBox = await _mediator.Send(updatedBoxRequest, cancellationToken);
  ```
- `ChangeTransportBoxStateResponse.UpdatedBox` is typed `GetTransportBoxByIdResponse?` (not `TransportBoxDto?` directly) — see `ChangeTransportBoxStateResponse.cs`.
- `GetTransportBoxByIdResponse` is `{ TransportBoxDto? TransportBox { get; set; } }` (extends `BaseResponse`).
- The `TransportBox → TransportBoxDto` AutoMapper map is already registered in `backend/src/Anela.Heblo.Application/Features/Logistics/TransportBoxMappingProfile.cs` and already used successfully by three sibling handlers in the same `UseCases` folder: `GetTransportBoxByIdHandler`, `AddItemToBoxHandler`, `RemoveItemFromBoxHandler` — all take `AutoMapper.IMapper mapper` via constructor and call `_mapper.Map<TransportBoxDto>(box)`. No new DI registration is needed.
- Test file: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs`. It currently has a `Mock<IMediator> _mediatorMock` field, passes it into the handler constructor, and has ~14 references to `_mediatorMock` across the file, including two hard `Verify` assertions at (approximately) lines 140 and 625:
  ```csharp
  _mediatorMock.Verify(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), It.IsAny<CancellationToken>()), Times.Once);
  ```
  and several `_mediatorMock.Setup(...)` calls arranging a `GetTransportBoxByIdRequest` → `GetTransportBoxByIdResponse` result for success-path tests (approximately lines 128, 170, 214, 259, 285, 314, 499, 606, 754 — re-verify exact line numbers when editing, since earlier edits shift line numbers).
- Sibling test file `AddItemToBoxHandlerTests.cs` shows the exact target pattern for mocking `IMapper`:
  ```csharp
  private readonly Mock<IMapper> _mapperMock;
  ...
  _mapperMock = new Mock<IMapper>();
  _mapperMock
      .Setup(x => x.Map<TransportBoxDto>(It.IsAny<TransportBox>()))
      .Returns(new TransportBoxDto());
  ```
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs` exists in the same directory — check it for any `IMediator`/`GetTransportBoxByIdRequest` dependency before assuming it needs no change.
- Out of scope: `UpdateTransportBoxDescriptionHandler.cs` has the identical anti-pattern but must NOT be touched — it is a separate, unfiled finding.

---

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

### task: update-handler-tests

**Goal:** `ChangeTransportBoxStateHandlerTests` compiles and passes against the new `IMapper`-based constructor, with the same test intent (state-transition outcomes, error codes, side effects) preserved. Full backend build and test suite for the touched area is green.

**Depends on:** `remove-mediator-dependency` (constructor signature must already be changed).

**Files:**
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs` (verify only)

**Steps:**
1. Add `using AutoMapper;` to the test file if not already present.
2. Replace `private readonly Mock<IMediator> _mediatorMock;` with `private readonly Mock<IMapper> _mapperMock;`.
3. In the constructor, replace `_mediatorMock = new Mock<IMediator>();` with `_mapperMock = new Mock<IMapper>();` and add a default setup mirroring `AddItemToBoxHandlerTests`:
   ```csharp
   _mapperMock
       .Setup(x => x.Map<TransportBoxDto>(It.IsAny<TransportBox>()))
       .Returns(new TransportBoxDto());
   ```
4. Update the `_handler = new ChangeTransportBoxStateHandler(...)` construction call to pass `_mapperMock.Object` at the position where `_mediatorMock.Object` currently sits (matching the constructor's new parameter order from `remove-mediator-dependency`).
5. Search the whole file for every remaining use of `_mediatorMock` (`grep -n "_mediatorMock" backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs`). For each:
   - A `_mediatorMock.Setup(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), ...)).ReturnsAsync(new GetTransportBoxByIdResponse { TransportBox = someDto })` becomes, where the test asserts on `response.UpdatedBox.TransportBox` content: either (a) an equivalent `_mapperMock.Setup(x => x.Map<TransportBoxDto>(It.IsAny<TransportBox>())).Returns(someDto)`, if the test needs a specific DTO shape returned, or (b) simply removed if the default setup from step 3 already covers it and the test doesn't assert on `UpdatedBox` contents specifically.
   - A `_mediatorMock.Verify(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), It.IsAny<CancellationToken>()), Times.Once)` is removed outright (there is no mediator call left). If the test's intent was "the box gets re-fetched/mapped after save," replace it with `_mapperMock.Verify(x => x.Map<TransportBoxDto>(box), Times.Once)` only where `box` is a variable already in scope in that test and the assertion adds real value; otherwise just delete the line.
6. Do not change any assertion unrelated to `UpdatedBox`/mediator plumbing — error-code assertions, repository-call assertions, inventory-reservation assertions, and state-transition assertions stay exactly as they are.
7. Open `ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs` and check for `IMediator`, `_mediator`, or `GetTransportBoxByIdRequest` references tied to `ChangeTransportBoxStateHandler`'s construction or dispatch. If none exist (expected — it's a real-persistence integration test), make no changes there. If any exist, update them the same way as steps 2-5.
8. Run `dotnet build` for the backend solution and fix any compile errors surfaced by the constructor/mock changes.
9. Run `dotnet format` per repo convention (see CLAUDE.md validation steps).
10. Run the full test suite for this test file (and the integration test file) and fix any behavioral regressions; all previously-passing tests in both files must still pass.

**Verification:**
- `grep -n "IMediator\|_mediatorMock" backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs` returns no matches.
- `dotnet build` succeeds for the whole backend solution with no new warnings/errors introduced by this change.
- `dotnet test --filter FullyQualifiedName~ChangeTransportBoxStateHandlerTests` passes, same test count as before the change (no tests silently dropped).
- `dotnet test --filter FullyQualifiedName~ChangeTransportBoxStateReceiveAtomicityIntegrationTests` passes unchanged.
- Manual code read of the final `ChangeTransportBoxStateHandler.cs` confirms: no `IMediator` usage, `UpdatedBox` populated via direct `_mapper.Map<TransportBoxDto>(box)`, and `ChangeTransportBoxStateResponse`'s public shape (`Success`, `ErrorCode`, `Params`, `UpdatedBox`) is unchanged from before this plan.
