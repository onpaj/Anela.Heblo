# Implementation: ingest-handler-surfaces-failure

## What was implemented
`IngestPlaudRecordingHandler.Handle` now catches `MeetingTaskExtractionFailedException` around the call to `_extractor.ExtractAsync`, logs the failure (including the recording id and attempt count), and returns `IngestPlaudRecordingResponse { Success = false }` instead of letting the exception propagate and silently dropping the recording. `PlaudPollingJob.ExecuteAsync` now distinguishes this outcome from a true "ingested" success: a new `failed` counter is incremented when a non-skipped response has `Success == false`, and the job's completion log line reports the failed count alongside the existing ingested/skipped/not-generated counts.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs` — wraps the extractor call in a try/catch for `MeetingTaskExtractionFailedException`; returns a failed response instead of propagating.
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs` — adds a `failed` counter distinct from `ingested`, and includes it in the completion log.
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs` — adds `Handle_WhenExtractionFailsAfterRetries_ReturnsFailureWithoutPersistingTranscript`, asserting `Success == false`, `Skipped == false`, and that `AddAsync` is never called when the extractor throws.

## Tests
- `IngestPlaudRecordingHandlerTests` — 10 tests total (9 existing + 1 new), all passing.
- `PlaudPollingJobTests` — 5 existing tests, all still passing (no behavioral change to their assertions, since none of them exercised the extraction-failure path).

## How to verify
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~IngestPlaudRecordingHandlerTests"
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PlaudPollingJobTests"
```

## Notes
This task depended on `extractor-retry-and-recovery` (already completed and merged into this branch), which introduced `MeetingTaskExtractionFailedException`. No deviations from the task spec.

## PR Summary
Fixes the handler so a meeting-task extraction failure (LLM returning unparseable JSON after exhausting retries) is surfaced as a failed ingest instead of either crashing the polling job or silently dropping the recording as a false success. The polling job's summary log now separately counts failed extractions.

### Changes
- `IngestPlaudRecordingHandler.cs` — catch `MeetingTaskExtractionFailedException`, return `Success = false`
- `PlaudPollingJob.cs` — add `failed` counter, include in summary log
- `IngestPlaudRecordingHandlerTests.cs` — new test for the failure path

## Status
DONE
