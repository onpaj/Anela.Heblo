# Specification: GetLeafletGenerationHandler not-found path unit test

## Summary
`GetLeafletGenerationHandler` returns `ErrorCodes.LeafletFeedbackNotFound` when the requested leaflet generation record does not exist, but no test currently verifies this branch. This is a coverage-gap task: add one unit test that mocks the repository to return `null` and asserts the handler's not-found response is correct.

## Background
`backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletGeneration/GetLeafletGenerationHandler.cs` handles `GetLeafletGenerationRequest` via MediatR. It calls `ILeafletGenerationRepository.GetGenerationByIdAsync(id, ct)`; when the result is `null`, it returns `new GetLeafletGenerationResponse(ErrorCodes.LeafletFeedbackNotFound)` — an error response with `Success = false`, `ErrorCode = LeafletFeedbackNotFound`, and none of the data properties (`Id`, `Topic`, `Audience`, etc.) explicitly set, so they retain their type defaults. Line coverage for this file is 16% (threshold 60%); this specific branch is the reported gap. The frontend relies on the returned error code to render a "not found" message rather than an empty success state, so an incorrect or missing error code on this path is user-visible.

## Functional Requirements

### FR-1: Unit test for the not-found path
Add a test to a new file `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletGenerationHandlerTests.cs` that:
- Mocks `ILeafletGenerationRepository.GetGenerationByIdAsync` to return `null` (`Task<LeafletGeneration?>`) for a given request `Id`.
- Invokes `GetLeafletGenerationHandler.Handle` with a `GetLeafletGenerationRequest { Id = <guid> }`.
- Asserts the returned response has:
  - `Success == false`
  - `ErrorCode == ErrorCodes.LeafletFeedbackNotFound`
  - `Id == Guid.Empty` (default)
  - `Topic == string.Empty`, `Audience == string.Empty`, `Length == string.Empty`, `FinalMarkdown == string.Empty` (class-level defaults, since the error-path constructor does not set them)
  - `KbSourceCount == 0`, `LeafletSourceCount == 0`, `DurationMs == 0` (numeric defaults)
  - `CreatedAt == default(DateTimeOffset)`
  - `UserId == null`, `PrecisionScore == null`, `StyleScore == null`, `FeedbackComment == null`

**Acceptance criteria:**
- New test file builds and the new test passes under `dotnet test`.
- Test follows the existing pattern in this test directory (xUnit `[Fact]`, Moq `Mock<ILeafletGenerationRepository>`, FluentAssertions `.Should()`, Arrange/Act/Assert comments, a `CreateHandler()` factory method), matching the style of the sibling file `GetLeafletChunkDetailHandlerTests.cs`.
- No production code in `GetLeafletGenerationHandler.cs` or its DTOs is changed — this is a test-only addition. If the handler's actual behavior were found to differ from what's described here, that discrepancy must be flagged rather than "fixed" silently, since this task is a coverage gap, not a bug report.

## Non-Functional Requirements

### NFR-1: Performance
N/A — this is a unit test using mocks; no timing constraints.

### NFR-2: Security
N/A — no auth, secrets, or sensitive data involved; the test uses arbitrary in-memory GUIDs.

## Data Model
No changes. Relevant existing types (unchanged):
- `GetLeafletGenerationRequest { Guid Id }`
- `GetLeafletGenerationResponse : BaseResponse` — data fields listed in FR-1, plus inherited `Success`, `ErrorCode`, `Params` from `BaseResponse`.
- `ILeafletGenerationRepository.GetGenerationByIdAsync(Guid, CancellationToken) : Task<LeafletGeneration?>` — the method to mock.

## API / Interface Design
N/A — no new or changed endpoints. This exercises the existing MediatR handler directly, in-process, per the codebase's established handler-unit-test convention.

## Dependencies
- xUnit, Moq, FluentAssertions — already used throughout `backend/test/Anela.Heblo.Tests`, no new packages.
- `ILeafletGenerationRepository` (domain interface) and `ErrorCodes` (`Anela.Heblo.Application.Shared`) — existing types, no changes needed.

## Out of Scope
- Testing the success (found) path of `GetLeafletGenerationHandler` — not part of this coverage gap, though it may be added later.
- Any change to `GetLeafletGenerationHandler.cs`, `GetLeafletGenerationRequest.cs`, `BaseResponse.cs`, or `ILeafletGenerationRepository.cs`.
- Integration/E2E test coverage for this endpoint.
- Improving coverage of other files with low coverage.

## Open Questions
None.

## Status: COMPLETE
