# Implementation: reimport-handler-surfaces-failure

## What was implemented
`ReimportMeetingTranscriptHandler.Handle` now catches `MeetingTaskExtractionFailedException` around the call to `_extractor.ExtractAsync`, logs the failure (including the transcript id and attempt count), and returns `ReimportMeetingTranscriptResponse(ErrorCodes.Exception)` instead of letting the exception propagate uncaught. This mirrors the pattern already applied to `IngestPlaudRecordingHandler` in the `ingest-handler-surfaces-failure` task, so a failed re-extraction no longer crashes the request or silently wipes the transcript's pending tasks (since `ReplacePendingTasksAsync`/`SaveChangesAsync` are simply never reached on the failure path).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptHandler.cs` — wraps the extractor call in a try/catch for `MeetingTaskExtractionFailedException`; returns a response with `ErrorCodes.Exception` instead of propagating.
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ReimportMeetingTranscriptHandlerTests.cs` — adds `Handle_WhenExtractionFailsAfterRetries_ReturnsExceptionErrorAndDoesNotReplaceTasks`, asserting `Success == false`, `ErrorCode == ErrorCodes.Exception`, and that `ReplacePendingTasksAsync`/`SaveChangesAsync` are never called when the extractor throws.

## Tests
- `ReimportMeetingTranscriptHandlerTests` — 12 tests total (11 existing + 1 new), all passing.

## How to verify
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReimportMeetingTranscriptHandlerTests"
```

## Notes
This task depended on `extractor-retry-and-recovery` (already completed and merged into this branch), which introduced `MeetingTaskExtractionFailedException`. The task-context's suggested test `Verify` call used `It.IsAny<List<ProposedTask>>()`, but `IMeetingTranscriptRepository.ReplacePendingTasksAsync` actually takes `IReadOnlyList<ProposedTask>` (matching every other test in this file) — used the correct type instead. No other deviations from the task spec.

## PR Summary
Fixes `ReimportMeetingTranscriptHandler` so a meeting-task extraction failure during a manual transcript reimport (LLM returning unparseable JSON after exhausting retries) is surfaced as a failed response instead of crashing the request. This is the reimport-path counterpart to the ingest-path fix in `ingest-handler-surfaces-failure`, closing the second of the two call sites into `ClaudeMeetingTaskExtractor.ExtractAsync` that previously let this exception propagate uncaught.

### Changes
- `ReimportMeetingTranscriptHandler.cs` — catch `MeetingTaskExtractionFailedException`, return `ErrorCodes.Exception`
- `ReimportMeetingTranscriptHandlerTests.cs` — new test for the failure path

## Status
DONE
