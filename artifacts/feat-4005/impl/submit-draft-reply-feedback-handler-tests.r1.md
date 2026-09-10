# Implementation: submit-draft-reply-feedback-handler-tests

## What was implemented
Added a new unit test file covering `SubmitDraftReplyFeedbackHandler` with 7 test cases: log-not-found, wrong `Feature` type (also treated as not-found), ownership mismatch (`Forbidden`), duplicate submission via `PrecisionScore` already set, duplicate submission via `StyleScore` already set, the success path (writes scores/comment and calls `SaveChangesAsync`), and the success path with a `null` comment.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SubmitDraftReplyFeedbackHandlerTests.cs` — new xUnit test class with 7 `[Fact]` methods, `Mock<IRagInteractionLogRepository>` and `Mock<ICurrentUserService>` fields, and a `CreateHandler()` helper.

## Tests
- `SubmitDraftReplyFeedbackHandlerTests` (7 tests, all passing):
  - `Handle_LogNotFound_ReturnsNotFound`
  - `Handle_WrongFeature_ReturnsNotFound`
  - `Handle_OwnershipMismatch_ReturnsForbidden`
  - `Handle_PrecisionScoreAlreadySet_ReturnsAlreadySubmitted`
  - `Handle_StyleScoreAlreadySet_ReturnsAlreadySubmitted`
  - `Handle_Success_WritesScoresAndSaves`
  - `Handle_Success_NullComment_WritesNull`

## How to verify
1. `dotnet build Anela.Heblo.sln` (run from repo root — the `.sln` lives at the repo root, not under `backend/`) — 0 errors.
2. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SubmitDraftReplyFeedbackHandlerTests"` — `Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7`.
3. `dotnet format Anela.Heblo.sln --verify-no-changes` — exits 0, no formatting changes needed.
4. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` (full suite) — `Failed: 105, Passed: 6630, Skipped: 4, Total: 6739`. All 105 failures are pre-existing integration tests that require a Docker daemon (Testcontainers/PostgreSQL — `System.ArgumentException: Docker is either not running or misconfigured`), unrelated to this change; none touch Smartsupp draft-reply-feedback or the new test file. No regressions introduced.

## Notes
- The task-context's exact code snippets were verified against the real handler, request/response types, `ErrorCodes`, `RagInteractionLog`, `RagFeature`, `IRagInteractionLogRepository`, `ICurrentUserService`, and `CurrentUser` — every signature, namespace, and constructor shape matched exactly. No adjustments to the test code itself were needed.
- The task instructions referenced `backend/Anela.Heblo.sln`, but the solution file actually lives at the repo root (`Anela.Heblo.sln`). All build/test/format commands were run from the repo root using that path instead; this is purely a path correction, not a deviation in test content.
- Pre-existing, unrelated working-tree change to `artifacts/feat-4005/state.json` was present before this task started and was intentionally left out of the commit (only the new test file was staged), per the task's own guidance to add only the new file.

## PR Summary
Adds full unit test coverage for `SubmitDraftReplyFeedbackHandler`, a previously untested MediatR handler that lets a Smartsupp agent submit precision/style feedback scores plus an optional comment on an AI-generated draft reply. The 7 new tests exercise every branch of the handler: the log-not-found path (including the case where the log exists but belongs to a different `RagFeature`), the ownership-mismatch `Forbidden` path, both duplicate-submission guards (`PrecisionScore` already set, `StyleScore` already set), and the success path with and without a comment. All test code was written directly against the verified real signatures of the handler and its dependencies (`IRagInteractionLogRepository`, `ICurrentUserService`, `RagInteractionLog`, `ErrorCodes`) — everything matched the task-context's proposed code exactly, so no signature adjustments were required. Build, the new test filter, `dotnet format --verify-no-changes`, and the full test suite were all run and confirmed; the full-suite run shows 105 pre-existing, unrelated integration-test failures caused by the sandbox having no Docker daemon available for Testcontainers, which is an environment limitation and not a regression from this change.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SubmitDraftReplyFeedbackHandlerTests.cs` — new file, 7 unit tests covering all branches of `SubmitDraftReplyFeedbackHandler`.

## Status
DONE
