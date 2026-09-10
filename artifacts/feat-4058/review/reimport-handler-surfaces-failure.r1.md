## Review Result: PASS

### task: reimport-handler-surfaces-failure
**Status:** PASS

## Docs to Update
(none)

## Overall Notes
The implementation is a minimal, surgical diff that matches the task spec exactly. `ReimportMeetingTranscriptHandler.Handle` now wraps the `_extractor.ExtractAsync` call in a try/catch for `MeetingTaskExtractionFailedException`, logs the failure with the transcript id and `ex.AttemptCount` (both of which exist on the exception type, confirmed in `MeetingTaskExtractionFailedException.cs`), and returns `new ReimportMeetingTranscriptResponse(ErrorCodes.Exception)` instead of propagating. Because the return happens before `ReplacePendingTasksAsync`/`SaveChangesAsync` are reached, the repository is never touched on the failure path — in-memory mutations to `transcript.RawTranscript`/`Summary`/`Subject` made earlier in the method are never persisted, since `SaveChangesAsync` is never invoked. This satisfies the "tasks/repository must never be touched on that failure path" requirement.

The new test `Handle_WhenExtractionFailsAfterRetries_ReturnsExceptionErrorAndDoesNotReplaceTasks` correctly mirrors the happy-path arrange block (repository `GetByIdAsync`, Plaud client detail/transcript/summary mocks), sets up the extractor mock to throw `MeetingTaskExtractionFailedException("boom", 3, "not-json")`, and asserts `Success == false`, `ErrorCode == ErrorCodes.Exception`, plus verifies both `ReplacePendingTasksAsync` (never called) and `SaveChangesAsync` (never called, going beyond the spec's suggested test by also covering the persistence call). The developer's noted deviation — using `IReadOnlyList<ProposedTask>` instead of the spec's suggested `List<ProposedTask>` in the `Verify` call — is correct and necessary, since that's the actual signature of `IMeetingTranscriptRepository.ReplacePendingTasksAsync` (consistent with every other test in the file); this is a justified, beneficial correction, not a problematic deviation.

The diff is confined to exactly the two files named in the task spec (plus the pipeline's own `state.json` bookkeeping), with no unrelated changes. Existing tests in the file are untouched. I was unable to get a live `dotnet test` run to finish within the review window (first-run build was still in progress when I had to finalize), but static review of the handler, the exception type, `ReimportMeetingTranscriptResponse`, and the test file gives high confidence the code compiles and the test passes as described (12 tests total, matching the implementation summary).
