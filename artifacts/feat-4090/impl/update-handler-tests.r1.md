# Implementation: update-handler-tests

## What was implemented
Updated `ChangeTransportBoxStateHandlerTests` to compile and pass against `ChangeTransportBoxStateHandler`'s new `IMapper`-based constructor (from `remove-mediator-dependency`). Replaced `Mock<IMediator> _mediatorMock` with `Mock<IMapper> _mapperMock`, added a default `_mapperMock.Setup(x => x.Map<TransportBoxDto>(It.IsAny<TransportBox>())).Returns(new TransportBoxDto())` mirroring `AddItemToBoxHandlerTests`, and removed every `GetTransportBoxByIdRequest`/`GetTransportBoxByIdResponse` mediator setup/verify across the file. Where a test asserted `result.UpdatedBox.Should().Be(updatedBoxResponse)` (reference equality against a locally-constructed mediator response, which no longer exists), the assertion was changed to `result.UpdatedBox.Should().NotBeNull()`; the two tests that also had an explicit `_mediatorMock.Verify(x => x.Send(...), Times.Once)` got that replaced with `_mapperMock.Verify(x => x.Map<TransportBoxDto>(box), Times.Once)` since `box` was in scope and the assertion (the box actually gets mapped after save) adds real value. All other setups that only supplied a filler `GetTransportBoxByIdResponse` with no assertion on `UpdatedBox` content were deleted outright — the constructor's default mapper setup already covers them. No error-code, repository-call, inventory-reservation, or state-transition assertions were touched.

Two additional files that construct `ChangeTransportBoxStateHandler` directly, not listed in this task's file list but broken by the prior task's constructor change, were also fixed so the solution builds:
- `TransportBoxUniquenessTests.cs` — swapped its `Mock<IMediator>` for a `Mock<IMapper>` with the same default setup (no test in this file asserts on `UpdatedBox` content).
- `ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs` — per task step 7, replaced its `Mock<IMediator>` (which stubbed a `GetTransportBoxByIdRequest` send) with a real `IMapper` built via `MapperConfiguration` + `TransportBoxMappingProfile`, matching the pattern already used in `GetTransportBoxByIdHandlerTests.cs`. This is more faithful for a real-persistence integration test than a mock would be.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs` — `_mediatorMock` → `_mapperMock` (IMapper), removed all mediator/GetTransportBoxById plumbing, adjusted `UpdatedBox` assertions.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs` — `CreateHandler` now builds a real `IMapper` via `TransportBoxMappingProfile` instead of a mocked `IMediator`.
- `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs` — `_mockMediator` → `_mockMapper` (IMapper) with the same default setup pattern; not in the task's file list but required a fix for the solution to compile (it also directly constructs `ChangeTransportBoxStateHandler`).

## Tests
- `ChangeTransportBoxStateHandlerTests` — 21 tests, run directly via `vstest.console.dll` against the built test assembly (see Notes on why not through `dotnet test`): **21 passed, 0 failed**. Same test count as before the change (verified against the pre-edit file: 18 `[Fact]` + 1 `[Theory]` with 2 `[InlineData]` cases = 21).
- `TransportBoxUniquenessTests` — 7 tests: **7 passed, 0 failed**.
- `ChangeTransportBoxStateReceiveAtomicityIntegrationTests` — 2 tests (`[Trait("Category","Integration")]`, needs a real Postgres via Testcontainers/Docker). **Could not be executed in this sandbox** — no Docker daemon is available here (`docker ps` → "dial unix /var/run/docker.sock: ... no such file or directory"). The file compiles cleanly (full solution build: 0 errors) and its `CreateHandler` now wires a real AutoMapper `IMapper` the same way `GetTransportBoxByIdHandlerTests` does, so the mapping step it now exercises is production-equivalent, not a stub. This test still needs to be run in an environment with Docker before merge.

## How to verify
1. `grep -n "IMediator\|_mediatorMock" backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs` — no matches.
2. `dotnet build backend/Anela.Heblo.sln` from repo root (or `dotnet build Anela.Heblo.sln` from `backend/`'s parent) — succeeds, 0 errors, warning count unchanged from before this task (260, all pre-existing/unrelated).
3. `dotnet build` the test project, then run tests directly against the built DLL (faster and avoids an MSBuild-node hang observed with plain `dotnet test` in this sandbox):
   `dotnet exec <sdk>/vstest.console.dll backend/test/Anela.Heblo.Tests/bin/Debug/net8.0/Anela.Heblo.Tests.dll --TestCaseFilter:"FullyQualifiedName~ChangeTransportBoxStateHandlerTests"` → 21/21 passed.
   Same with `TestCaseFilter:"FullyQualifiedName~TransportBoxUniquenessTests"` → 7/7 passed.
4. With Docker available: `dotnet test --filter FullyQualifiedName~ChangeTransportBoxStateReceiveAtomicityIntegrationTests` should pass unchanged (2 tests).
5. Manual read of `ChangeTransportBoxStateHandler.cs` (unmodified by this task, from the prior task): confirms no `IMediator` usage, `UpdatedBox` populated via `_mapper.Map<TransportBoxDto>(box)`, and `ChangeTransportBoxStateResponse`'s shape (`Success`, `ErrorCode`, `Params`, `UpdatedBox`) unchanged.

## Notes
- `dotnet test` (the MSBuild-driven CLI entrypoint) reproducibly hung in this sandbox after a prior `dotnet build` + a killed `dotnet test` run — CPU usage on the spawned process stayed near zero for 10+ minutes with no output, which looks like an MSBuild-node/lock issue specific to this sandbox rather than anything about the code change. Killed the stuck processes and instead built once with `dotnet build` and ran tests directly via `dotnet exec vstest.console.dll <built dll> --TestCaseFilter:...`, which completed in seconds. Worth knowing if a future task in this sandbox sees `dotnet test` stall.
- The Postgres/Testcontainers integration test (`ChangeTransportBoxStateReceiveAtomicityIntegrationTests`) genuinely cannot run here — no Docker daemon. Its code change is minimal and mirrors an existing, working pattern (`GetTransportBoxByIdHandlerTests`'s real-`IMapper` construction), so risk is low, but it has not been executed by this task. Flagging as a concern rather than silently claiming it passed.
- Deviated from the task's file list by also fixing `TransportBoxUniquenessTests.cs`, which wasn't listed but directly constructs `ChangeTransportBoxStateHandler` and failed to compile after `remove-mediator-dependency`'s constructor change. Fixing it was necessary to satisfy this task's own acceptance criterion ("dotnet build succeeds for the whole backend solution with no new errors").
- For the two tests that previously asserted `result.UpdatedBox.Should().Be(updatedBoxResponse)`, I judged (per the task's step-5 guidance) that this was asserting response-forwarding identity, not specific DTO content, so `NotBeNull()` (plus a `_mapperMock.Verify(Map(box))` where a mediator-Verify already existed) preserves the original intent without over-fitting to the removed mediator plumbing.

## PR Summary
`ChangeTransportBoxStateHandler` moved from an `IMediator` round-trip to a direct `IMapper.Map` call for building its response's `UpdatedBox` field (previous task in this plan); this task brings its unit tests up to date with that constructor and behavior change. `ChangeTransportBoxStateHandlerTests` now mocks `IMapper` instead of `IMediator`, with the same default-DTO setup pattern used by `AddItemToBoxHandlerTests`, and all `UpdatedBox`-related assertions were adjusted to check that the box gets mapped rather than checking identity against a stub mediator response. Two other test files that construct the handler directly (`TransportBoxUniquenessTests`, `ChangeTransportBoxStateReceiveAtomicityIntegrationTests`) were also updated so the solution keeps building; the integration test now uses a real AutoMapper-configured `IMapper` rather than a mock, consistent with sibling integration/unit tests elsewhere in the Transport feature.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs`
- `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs`

## Status
DONE_WITH_CONCERNS
