# Specification: Unit Test Coverage for SubmitDraftReplyFeedbackHandler

## Summary
`SubmitDraftReplyFeedbackHandler` (Smartsupp draft-reply feedback feature) has four distinct execution paths — not-found/wrong-feature, ownership guard, duplicate-submission guard, and the success path — none of which are currently covered by unit tests (22.6% line coverage vs. a 60% threshold). This task adds a focused unit test suite for the handler, mocking `IRagInteractionLogRepository` and `ICurrentUserService`, with no production code changes.

## Background
`SubmitDraftReplyFeedbackHandler` lets a Smartsupp agent submit precision/style feedback scores (and an optional comment) on a previously generated AI draft-reply log (`RagInteractionLog`). The handler enforces an ownership check (only the user who owns the log may score it) and a duplicate-submission guard (a log can only be scored once). Neither guard is exercised by any existing test, so a regression in either would not be caught by CI: the ownership check is a security invariant (an authenticated user who knows another user's `logId` could otherwise submit feedback on their behalf), and the duplicate guard prevents silent overwriting of prior feedback. This is a coverage-gap remediation task filed by the weekly coverage-gap routine — no behavior change is intended, only test coverage.

## Functional Requirements

### FR-1: Test — log not found returns `SmartsuppDraftReplyFeedbackLogNotFound`
When `_repository.GetByIdAsync` returns `null` for the requested `LogId`, the handler must return a failed response with `ErrorCode == ErrorCodes.SmartsuppDraftReplyFeedbackLogNotFound` and must not call `_currentUserService.GetCurrentUser()` or `_repository.SaveChangesAsync`.
**Acceptance criteria:**
- Arrange: `GetByIdAsync` returns `null`.
- Assert: `result.Success` is `false`; `result.ErrorCode` equals `ErrorCodes.SmartsuppDraftReplyFeedbackLogNotFound`; `result.Params` contains key `"logId"` with the requested `LogId` (as string, via `Guid.ToString()`).
- Assert: `_currentUserService.GetCurrentUser()` is never invoked (`Mock.Verify(..., Times.Never)`).
- Assert: `_repository.SaveChangesAsync` is never invoked.

### FR-2: Test — log with wrong `Feature` returns `SmartsuppDraftReplyFeedbackLogNotFound`
When `_repository.GetByIdAsync` returns a log whose `Feature` is anything other than `RagFeature.SmartsuppDraftReply` (e.g. `RagFeature.KnowledgeBase`), the handler must return the same not-found error as FR-1, treating a wrong-feature log as indistinguishable from a missing one.
**Acceptance criteria:**
- Arrange: log with `Feature = RagFeature.KnowledgeBase` (or any non-`SmartsuppDraftReply` value), `PrecisionScore = null`, `StyleScore = null`.
- Assert: `result.Success` is `false`; `result.ErrorCode` equals `ErrorCodes.SmartsuppDraftReplyFeedbackLogNotFound`.
- Assert: `_repository.SaveChangesAsync` is never invoked.

### FR-3: Test — ownership mismatch returns `Forbidden`
When the found log has `Feature == RagFeature.SmartsuppDraftReply` but `log.UserId` differs from `_currentUserService.GetCurrentUser().Id`, the handler must return a failed response with `ErrorCode == ErrorCodes.Forbidden` and must not persist any change.
**Acceptance criteria:**
- Arrange: log with `Feature = RagFeature.SmartsuppDraftReply`, `UserId = "user-a"`; `GetCurrentUser()` returns a `CurrentUser` with `Id = "user-b"`.
- Assert: `result.Success` is `false`; `result.ErrorCode` equals `ErrorCodes.Forbidden`; `result.Params` contains key `"logId"` with the requested `LogId`.
- Assert: `_repository.SaveChangesAsync` is never invoked.

### FR-4: Test — `PrecisionScore` already set returns `SmartsuppDraftReplyFeedbackAlreadySubmitted`
When the log is found, owned by the current user, but `log.PrecisionScore` is already non-null (regardless of `StyleScore`), the handler must return a failed response with `ErrorCode == ErrorCodes.SmartsuppDraftReplyFeedbackAlreadySubmitted` and must not overwrite the existing values or call `SaveChangesAsync`.
**Acceptance criteria:**
- Arrange: log with `Feature = RagFeature.SmartsuppDraftReply`, `UserId` matching current user's `Id`, `PrecisionScore = 3`, `StyleScore = null`.
- Assert: `result.Success` is `false`; `result.ErrorCode` equals `ErrorCodes.SmartsuppDraftReplyFeedbackAlreadySubmitted`.
- Assert: the log's `PrecisionScore`/`StyleScore`/`FeedbackComment` are unchanged after `Handle` returns.
- Assert: `_repository.SaveChangesAsync` is never invoked.

### FR-5: Test — `StyleScore` already set returns `SmartsuppDraftReplyFeedbackAlreadySubmitted`
Same as FR-4 but with `StyleScore` pre-set and `PrecisionScore = null`, to independently verify the `||` branch of the duplicate guard (`log.PrecisionScore is not null || log.StyleScore is not null`).
**Acceptance criteria:**
- Arrange: log with `Feature = RagFeature.SmartsuppDraftReply`, matching `UserId`, `PrecisionScore = null`, `StyleScore = 4`.
- Assert: `result.Success` is `false`; `result.ErrorCode` equals `ErrorCodes.SmartsuppDraftReplyFeedbackAlreadySubmitted`.
- Assert: `_repository.SaveChangesAsync` is never invoked.

### FR-6: Test — success path writes scores/comment and saves
When the log is found, owned by the current user, and neither score is yet set, the handler must copy `request.PrecisionScore`, `request.StyleScore`, and `request.Comment` onto the log entity, call `_repository.SaveChangesAsync` exactly once, and return a response with `Success == true` and `ErrorCode == null`.
**Acceptance criteria:**
- Arrange: log with `Feature = RagFeature.SmartsuppDraftReply`, matching `UserId`, `PrecisionScore = null`, `StyleScore = null`. Request has `PrecisionScore = 5`, `StyleScore = 4`, `Comment = "Great answer"`.
- Act: call `Handle`.
- Assert: `result.Success` is `true`; `result.ErrorCode` is `null`.
- Assert: after `Handle`, the same log instance has `PrecisionScore == 5`, `StyleScore == 4`, `FeedbackComment == "Great answer"`.
- Assert: `_repository.SaveChangesAsync(It.IsAny<CancellationToken>())` was called exactly once (`Times.Once`).

### FR-7: Test — success path with `null` comment
The `Comment` field is optional (`string?`); a request with `Comment = null` must still succeed and write `null` to `FeedbackComment` without throwing.
**Acceptance criteria:**
- Arrange: same as FR-6 but `request.Comment = null`.
- Assert: `result.Success` is `true`; log's `FeedbackComment` is `null` after `Handle`.

## Non-Functional Requirements

### NFR-1: Performance
N/A — this is a unit-test-only change; no runtime performance impact.

### NFR-2: Security
No security-relevant production code changes. The new tests exist specifically to lock in the existing ownership-check security invariant (FR-3) so that a future code change cannot silently regress it without failing CI.

## Data Model
No schema or entity changes. Tests exercise the existing `RagInteractionLog` entity (fields used: `Id`, `Feature`, `UserId`, `PrecisionScore`, `StyleScore`, `FeedbackComment`) and the existing `CurrentUser` record (`Id`, `Name`, `Email`, `IsAuthenticated`) via mocks — no real database or `DbContext` involved.

## API / Interface Design
N/A — no new or changed endpoints, controllers, or UI. Tests call `SubmitDraftReplyFeedbackHandler.Handle(SubmitDraftReplyFeedbackRequest, CancellationToken)` directly, per the existing pattern used by sibling handler tests (e.g. `GetDraftReplyFeedbackListHandlerTests`).

## Dependencies
- **Test project:** `backend/test/Anela.Heblo.Tests`, using xUnit, Moq, and FluentAssertions (already referenced by sibling tests in `Features/Smartsupp/`).
- **Mocked collaborators:** `IRagInteractionLogRepository` (`GetByIdAsync`, `SaveChangesAsync`), `ICurrentUserService` (`GetCurrentUser`).
- **Types under test:** `SubmitDraftReplyFeedbackHandler`, `SubmitDraftReplyFeedbackRequest`, `SubmitDraftReplyFeedbackResponse` (all in `Anela.Heblo.Application.Features.Smartsupp.UseCases.SubmitDraftReplyFeedback`), `RagInteractionLog`, `RagFeature`, `ErrorCodes.SmartsuppDraftReplyFeedbackLogNotFound` (2709), `ErrorCodes.SmartsuppDraftReplyFeedbackAlreadySubmitted` (2710), `ErrorCodes.Forbidden` (0014), `CurrentUser`.
- No new NuGet packages required.

## Out of Scope
- Changes to the handler's production logic or the ownership/duplicate-guard behavior itself.
- Controller-level or integration/E2E tests for this endpoint.
- Validation of `[Range(1, 5)]` on `PrecisionScore`/`StyleScore` (a MediatR/FluentValidation pipeline concern, not exercised by `Handle` directly, and out of scope per the brief).
- Coverage of `IRagInteractionLogRepository`'s other members (`SaveAsync`, `UpdateSentAsync`, `GetFeedbackLogsPagedAsync`, `GetFeedbackStatsAsync`) — unrelated to this handler.
- Raising the file's line coverage to any specific number beyond what these six test paths naturally produce (expected to comfortably clear the 60% threshold given the handler's small size).

## Open Questions
None.

## Status: COMPLETE
