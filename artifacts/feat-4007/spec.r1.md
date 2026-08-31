# Specification: Unit test coverage for UpdateTransportBoxDescriptionHandler

## Summary
`UpdateTransportBoxDescriptionHandler` (Logistics module) currently has only 25% line coverage against a 60% threshold. Its two error-return branches (box-not-found, and the catch-all exception handler) are completely untested, and the happy path is only partially exercised. This is a test-only change: add a dedicated xUnit test fixture that mocks `ITransportBoxRepository` and `IMediator` to exercise all three execution paths of the handler.

## Background
The handler updates a transport box's description and returns the refreshed box via a nested mediator call. It has three branches:
1. Repository lookup returns `null` → returns `ErrorCodes.TransportBoxNotFound`.
2. Any exception thrown inside the try block (repository, mediator, etc.) → caught and returns `ErrorCodes.TransportBoxStateChangeError`.
3. Success → description is set, `UpdateAsync` + `SaveChangesAsync` are called, the updated box is re-fetched via `IMediator.Send(GetTransportBoxByIdRequest)`, and returned in `UpdatedBox`.

Without tests on paths 1 and 2, a regression in error-code constants or response construction would silently break API consumers' ability to distinguish "not found" from "update failed" — this is a coverage-gap remediation, not new behavior.

## Functional Requirements

### FR-1: Test — Box not found
When `ITransportBoxRepository.GetByIdWithDetailsAsync(request.BoxId)` returns `null`, the handler must return a response where:
- `Success` is `false`
- `ErrorCode` equals `ErrorCodes.TransportBoxNotFound`
- `Params` contains key `"BoxId"` with the requested box id (as string)
- `UpdatedBox` remains unset/null
- Neither `UpdateAsync`, `SaveChangesAsync`, nor `IMediator.Send` are called (verify no side effects run past the not-found check)

**Acceptance criteria:**
- A test named along the lines of `Handle_BoxNotFound_ReturnsTransportBoxNotFoundError` asserts the above.

### FR-2: Test — Repository/mediator throws
When any step inside the try block throws (repository call, `UpdateAsync`, `SaveChangesAsync`, or the mediator `Send` call), the handler must catch it and return a response where:
- `Success` is `false`
- `ErrorCode` equals `ErrorCodes.TransportBoxStateChangeError`
- `Params` contains key `"boxId"` with the requested box id (as string) — note the lowercase key, distinct from FR-1's `"BoxId"`, this is existing handler behavior and must be asserted as-is, not "fixed"
- The exception does not propagate out of `Handle`

**Acceptance criteria:**
- At least one test throwing from `GetByIdWithDetailsAsync` (e.g. `Handle_RepositoryThrows_ReturnsTransportBoxStateChangeError`) asserts the above.
- Optionally, a second test throwing from the mediator `Send` call to confirm the catch also covers the post-lookup path (nice-to-have, not required for the coverage gap — the try block wraps both).

### FR-3: Test — Happy path (full flow)
When the repository returns a valid box:
- `box.Description` is set to `request.Description`
- `_repository.UpdateAsync(box, cancellationToken)` is called exactly once with that same box
- `_repository.SaveChangesAsync(cancellationToken)` is called exactly once
- `_mediator.Send(...)` is called exactly once with a `GetTransportBoxByIdRequest` whose `Id` equals `request.BoxId`
- The response has `Success == true`, and `UpdatedBox` equals the value returned by the mocked `IMediator.Send`

**Acceptance criteria:**
- A test named along the lines of `Handle_ValidRequest_UpdatesDescriptionAndReturnsUpdatedBox` asserts all of the above (this supersedes/extends whatever partial happy-path test already exists, if any).

## Non-Functional Requirements

### NFR-1: Test isolation
Tests must not touch a real database or external service — `ITransportBoxRepository` and `IMediator` are mocked (Moq, matching existing repo convention). No `ILogger` assertions are required; a `Mock<ILogger<UpdateTransportBoxDescriptionHandler>>` is passed in per existing sibling-test convention (see `RemoveItemFromBoxHandlerTests.cs`) but its calls need not be verified.

### NFR-2: Style consistency
New test file must follow the existing test project conventions in `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/` — xUnit `[Fact]`, FluentAssertions (`result.Success.Should().BeTrue()` style), Moq for mocks, Arrange/Act/Assert comment sections, one test class per handler.

## Data Model
No data model changes. Relevant existing types (unchanged):
- `UpdateTransportBoxDescriptionRequest { int BoxId; string? Description; }`
- `UpdateTransportBoxDescriptionResponse : BaseResponse { GetTransportBoxByIdResponse? UpdatedBox; }`
- `TransportBox` domain entity (existing, has settable `Description`, `Id`)
- `ErrorCodes.TransportBoxNotFound`, `ErrorCodes.TransportBoxStateChangeError` (existing enum members, already used elsewhere)

## API / Interface Design
No public API/interface changes. This is additive test code only, calling the existing `UpdateTransportBoxDescriptionHandler.Handle` method directly (no HTTP/controller layer involved).

## Dependencies
- Existing `ITransportBoxRepository`, `IMediator`, `ILogger<T>` abstractions (mocked via Moq).
- Existing test project `backend/test/Anela.Heblo.Tests` (xUnit + FluentAssertions + Moq, already referenced).
- No new NuGet packages required.

## Out of Scope
- No changes to `UpdateTransportBoxDescriptionHandler.cs` production logic.
- No changes to `ErrorCodes`, `BaseResponse`, or `TransportBox` domain entity.
- No controller/endpoint-level or integration/E2E test coverage — unit tests only, per the issue's suggested approach.
- No refactor of the handler's inconsistent `"BoxId"` vs `"boxId"` param casing — documented as existing behavior to test, not a bug to fix (out of scope for a coverage-gap task; would be a separate issue if desired).

## Open Questions
None.

## Status: COMPLETE
