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
