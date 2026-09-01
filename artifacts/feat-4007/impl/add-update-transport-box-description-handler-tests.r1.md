# Implementation: add-update-transport-box-description-handler-tests (r1)

## Summary

Created `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/UpdateTransportBoxDescriptionHandlerTests.cs`
with three `[Fact]` tests covering `UpdateTransportBoxDescriptionHandler.Handle`:

1. `Handle_BoxNotFound_ReturnsTransportBoxNotFoundError` — repository returns `null` →
   asserts `ErrorCode == ErrorCodes.TransportBoxNotFound`, `Params["BoxId"] == "999"`,
   `UpdatedBox` is null, and that `UpdateAsync`/`SaveChangesAsync`/`mediator.Send` are
   never invoked.
2. `Handle_RepositoryThrows_ReturnsTransportBoxStateChangeError` — repository throws →
   asserts `ErrorCode == ErrorCodes.TransportBoxStateChangeError` and
   `Params["boxId"] == "42"` (asymmetric casing preserved exactly as existing
   production behavior, not "fixed").
3. `Handle_ValidRequest_UpdatesDescriptionAndReturnsUpdatedBox` — happy path — asserts
   the box's `Description` is mutated, `UpdateAsync`/`SaveChangesAsync` are each called
   exactly once, `mediator.Send` is called exactly once with a `GetTransportBoxByIdRequest`
   whose `Id` matches the request's `BoxId`, and `UpdatedBox` in the response is the
   exact object returned by the mocked mediator call.

No production code was modified — only the new test file was added, matching the
"Do not touch" list in the task context.

## Verification performed

- `dotnet build /home/user/worktrees/feature-4007-Coverage-Gap-Logistics-Updatetransportboxdescripti/Anela.Heblo.sln`
  → Build succeeded, 0 errors (82 pre-existing warnings, unrelated to this change).
- `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~UpdateTransportBoxDescriptionHandlerTests"`
  → 3/3 passed.
- `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Logistics.Transport"`
  → 84/86 passed. The 2 failures
  (`ChangeTransportBoxStateReceiveAtomicityIntegrationTests.*`) are pre-existing
  Testcontainers/Postgres integration tests that fail in this sandbox because Docker
  is unavailable (`PostgreSqlBuilder.Validate()` throws before any test body runs) —
  unrelated to this change, and they fail identically on a clean checkout without the
  new test file.
- `dotnet format Anela.Heblo.sln --verify-no-changes --include backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/UpdateTransportBoxDescriptionHandlerTests.cs`
  → no formatting changes needed.

## Acceptance criteria status

- [x] Exactly three `[Fact]` tests: not-found, repository-throws, happy-path.
- [x] Not-found test assertions as specified.
- [x] Exception test assertions as specified.
- [x] Happy-path test assertions as specified.
- [x] No file other than the new test file created or modified.
- [x] `dotnet build` succeeds with no errors.
- [x] Folder-level tests pass except for the pre-existing Docker-dependent
      integration tests (environment limitation, not a regression).
- [x] `dotnet format --verify-no-changes` reports no changes needed.
