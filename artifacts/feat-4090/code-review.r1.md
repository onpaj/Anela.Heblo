# Code Review Round 1 — feat-4090

## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Notes

Reviewed the full feature diff (`backend/src/.../ChangeTransportBoxStateHandler.cs`, `backend/test/.../ChangeTransportBoxStateHandlerTests.cs`, `backend/test/.../TransportBoxUniquenessTests.cs`, `backend/test/.../ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs`) against `spec.r1.md`.

- `IMediator`/`_mediator` is fully removed from the handler; `IMapper`/`_mapper` takes its exact constructor position (verified no other constructor-parameter reordering). `using MediatR;` and the `GetTransportBoxById` using directive are correctly retained (`IRequestHandler<...>` and `GetTransportBoxByIdResponse` still need them).
- The success-path replacement (`_mapper.Map<TransportBoxDto>(box)` wrapped in `new GetTransportBoxByIdResponse { TransportBox = ... }`) runs after `_repository.UpdateAsync`/`SaveChangesAsync`, against the same in-memory tracked `box` instance — matches FR-1 exactly, and `_logger.LogInformation` / the final `return` statement are untouched.
- Checked the one subtle equivalence risk called out in `arch-review.r1.md` (whether `GetTransportBoxByIdHandler`'s DB re-read could produce a DTO field the in-memory `box` mapping wouldn't, e.g. `TransportBoxDto.IsReceivable` which `TransportBoxMappingProfile` explicitly `.Ignore()`s): `GetTransportBoxByIdHandler` never sets `IsReceivable` either (only `GetTransportBoxByCodeHandler` does), so both old and new code produce the same default value — no behavioral divergence introduced.
- All references to `IMediator`/`_mediatorMock`/`GetTransportBoxByIdRequest`/`GetTransportBoxByIdResponse` are gone from `ChangeTransportBoxStateHandlerTests.cs`, `TransportBoxUniquenessTests.cs`, and the integration test — confirmed with `grep`, zero matches.
- `ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs` now builds a real `IMapper` via `MapperConfiguration` + `TransportBoxMappingProfile`, mirroring the existing pattern already used in several sibling test files (`GetTransportBoxByIdHandlerTests`, `GetTransportBoxByCodeHandlerTests`, etc.) — consistent with codebase convention, not a new duplication.
- Verified independently (not just trusting the prior task reports):
  - `dotnet build Anela.Heblo.sln`: 0 errors, 260 warnings (all pre-existing, none introduced by this change, none in the touched files beyond one pre-existing nullability warning at `ChangeTransportBoxStateHandlerTests.cs:344` unrelated to this change).
  - `vstest.console.dll` filtered to `ChangeTransportBoxStateHandlerTests|TransportBoxUniquenessTests`: 28/28 passed (21 + 7, matching the pre-change counts reported in `impl/update-handler-tests.r1.md`).
  - `ChangeTransportBoxStateReceiveAtomicityIntegrationTests` could not be run here either (no Docker daemon in this sandbox) — same documented environment constraint as the developer's task; the code change there is minimal and low-risk.
- No dead code, no unused imports left behind, no scope creep beyond the two in-scope files plus the two directly-broken test files that were required for the build to pass (both justified in the task reports and confirmed necessary here).

No correctness bugs found. No cleanup findings worth flagging — the change is a faithful, minimal implementation of the spec.
