# Code Review: ingest-handler-surfaces-failure

## Summary
The implementation matches the task spec closely: `IngestPlaudRecordingHandler.Handle` now catches `MeetingTaskExtractionFailedException` around the extractor call, logs the failure with recording id and attempt count, and returns `Success = false` instead of letting the exception propagate. `PlaudPollingJob` correctly distinguishes this outcome from a true "ingested" success via a new `failed` counter reflected in the completion log. The new test follows the existing file's mock-construction conventions (`PlaudFileDetail` via `TranscriptAvailable`/`SummaryAvailable`/`AudioAvailable`, `PlaudSummaryResult` as a positional record) and asserts the required outcomes. I ran the full `IngestPlaudRecordingHandlerTests` + `PlaudPollingJobTests` suites and all 15 tests pass.

## Review Result: PASS

### task: ingest-handler-surfaces-failure
**Status:** PASS

## Overall Notes
- Verified via `git diff` that all three files match the spec's intended changes: the try/catch in the handler, the `failed` counter and `else if (!response.Success)` branch in the polling job, and the new test.
- Ran `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~IngestPlaudRecordingHandlerTests|FullyQualifiedName~PlaudPollingJobTests"` myself: `Passed! - Failed: 0, Passed: 15, Skipped: 0, Total: 15`.
- Test mock construction deviates from the literal snippet in task-context (which used `PlaudFileDetail { IsGenerated = true }` and object-initializer `PlaudSummaryResult`) but the task-context explicitly permitted adapting it to the file's existing conventions — `IsGenerated` is a computed property and `PlaudSummaryResult` is a positional record in this codebase, so the developer's construction is correct and consistent with the other tests in the file.
- Minor cosmetic deviation, not spec-blocking: the completion log message in `PlaudPollingJob.cs` reads `"{Failed} failed"` rather than the task-context's suggested `"{Failed} failed extraction"`. Purely wording; no functional or test impact.
- No public API surface, contract, or operational procedure changed in a way that requires updates to `docs/`; the new `failed` log field is an internal observability addition.
