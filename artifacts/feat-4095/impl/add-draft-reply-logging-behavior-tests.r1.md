# Implementation: add-draft-reply-logging-behavior-tests

## What was implemented

Added a new unit test class, `DraftReplyLoggingBehaviorTests`, covering
`Anela.Heblo.Application.Features.Smartsupp.Pipeline.DraftReplyLoggingBehavior`
(the MediatR pipeline behavior that persists a `RagInteractionLog` row for each
Smartsupp draft-reply generation and stamps the response with the log id). No
production code was changed — this is a test-only task, closing a coverage gap
on a previously 0%-covered file.

The test file was created exactly as specified in the task context
(`artifacts/feat-4095/task-context/add-draft-reply-logging-behavior-tests.md`),
which was itself verified line-by-line against the real source before writing:
`DraftReplyLoggingBehavior.cs`, `GenerateDraftReplyRequest.cs`,
`GenerateDraftReplyResponse.cs`, `RagInteractionRecorder.cs`,
`RagInteractionLogFactory.cs`, `RagFeature.cs`, `IRagInteractionLogRepository.cs`,
`ICurrentUserService.cs`, `CurrentUser.cs`, and `BaseResponse.cs` all matched the
task context's quoted signatures verbatim — no adjustments were needed. The file
structurally mirrors the existing sibling `QuestionLoggingBehaviorTests.cs`
(KnowledgeBase module), substituting the Smartsupp draft-reply types and
`RagFeature.SmartsuppDraftReply`.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Smartsupp/Pipeline/DraftReplyLoggingBehaviorTests.cs` — new test class, 6 `[Fact]` tests for `DraftReplyLoggingBehavior`.

## Tests

6 new `[Fact]` tests, all passing:

- `Handle_WritesLogRow_AndReturnsResponse` — happy path: log persisted with correct Feature/Question/Answer/TopK/SourceCount/UserId/DurationMs, response returned unchanged.
- `Handle_WhenLogSaved_SetsResponseIdToLogId` — response `Id` is stamped with the persisted log's `Id`.
- `Handle_WhenDbWriteFails_StillReturnsResponse` — a `SaveAsync` exception is swallowed; the inner response is still returned.
- `Handle_WhenDbWriteFails_ResponseIdRemainsNull` — on save failure, `response.Id` stays `null`.
- `Handle_WhenNoInteractionRecorded_DoesNotSaveOrSetId` — when `IRagInteractionRecorder.HasInteraction` is false, `SaveAsync` is never called and `Id` stays `null`.
- `Handle_WhenResponseUnsuccessful_DoesNotSaveOrSetId` — when `response.Success == false`, `SaveAsync` is never called and `Id` stays `null`.

Targeted run:
```
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Smartsupp.Pipeline.DraftReplyLoggingBehaviorTests"
```
Result: `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6, Duration: 710 ms`.

## How to verify

```bash
cd backend
dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Smartsupp.Pipeline.DraftReplyLoggingBehaviorTests"
dotnet format test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --verify-no-changes
```

## Notes

- Build of the test project (`dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`) succeeded: 0 errors, 240 pre-existing warnings (all in unrelated files, none introduced by this change).
- `dotnet format test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --verify-no-changes` exited cleanly (exit code 0, no output) — no formatting violations in the new file.
- The full test-project run (`dotnet test ... --no-build -p:UseSharedCompilation=false`, no filter) was started in the background per the task's own guidance that this environment has known `dotnet test` contention/hang risk (four sibling agents were concurrently running `dotnet build`/`dotnet test` in other worktrees, confirmed via `ps aux` showing ~40 concurrent MSBuild/VBCSCompiler/dotnet-format processes). The run was actively executing (testhost consuming CPU, not stuck at 0%) but had not completed after several minutes and no output had been flushed yet; per instruction from the coordinating session, it was killed rather than blocking further, since the full-suite run is not a hard gate here — the targeted 6-test filter run and the clean project build are. No full-suite regression was observed or ruled out beyond the targeted filter; a rerun outside the contended window is recommended if a full-suite signal is needed.
- No production code was modified. No `.csproj` changes were needed — `Anela.Heblo.Tests.csproj` already references Moq and xUnit, and xUnit auto-discovers `[Fact]`s regardless of folder.

## PR Summary

Adds unit test coverage for `DraftReplyLoggingBehavior`, the Smartsupp pipeline behavior that persists a `RagInteractionLog` row per draft-reply generation and stamps the response with the log id. The new `DraftReplyLoggingBehaviorTests` class (6 facts) covers the happy path (log persisted, response id stamped), the DB-write-failure path (exception swallowed, response still returned, id stays null), and the two skip conditions (no interaction recorded; unsuccessful response) — mirroring the structure of the existing `QuestionLoggingBehaviorTests` sibling suite for the KnowledgeBase module. This closes a 0%-coverage gap on a previously untested file. No production code changed.

### Changes

- `backend/test/Anela.Heblo.Tests/Smartsupp/Pipeline/DraftReplyLoggingBehaviorTests.cs` — new test file, 6 `[Fact]` tests for `DraftReplyLoggingBehavior`.

## Status
DONE
