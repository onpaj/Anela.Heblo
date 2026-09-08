# Implementation: extraction-failure-exception

## What was implemented
Added `MeetingTaskExtractionFailedException`, a new exception type to be thrown by the (not-yet-implemented) `IMeetingTaskExtractor.ExtractAsync` when the LLM's response cannot be parsed into a valid, schema-conforming payload after all retries are exhausted. It carries `AttemptCount` (total attempts made) and `LastRawResponse` (the raw, fence-stripped response text from the final failed attempt, nullable) for diagnostics, so callers don't mistake a hard extraction failure for "zero tasks found."

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingTaskExtractionFailedException.cs` — the exception class: `sealed class MeetingTaskExtractionFailedException : Exception` with `AttemptCount` (int) and `LastRawResponse` (string?) properties, constructor `(string message, int attemptCount, string? lastRawResponse)`.
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingTaskExtractionFailedExceptionTests.cs` — unit tests for the constructor.

## Tests
`MeetingTaskExtractionFailedExceptionTests`:
- `Constructor_SetsMessageAttemptCountAndLastRawResponse` — verifies `Message`, `AttemptCount`, and `LastRawResponse` are all set correctly from constructor args.
- `Constructor_AllowsNullLastRawResponse` — verifies `LastRawResponse` can be null.

Actual run (`dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MeetingTaskExtractionFailedExceptionTests"`):
```
Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2, Duration: 3 ms - Anela.Heblo.Tests.dll (net8.0)
```

Note on TDD step ordering: when I began this task, the two target files (test and implementation) were already present in the worktree with content matching the spec exactly, so the "write failing test, watch it fail to compile, then implement" sequence collapsed into: confirm the files as-written match spec, then run the test suite once to confirm it passes. I additionally ran `dotnet build` on the Application project (0 errors, only pre-existing unrelated warnings) and `dotnet format --verify-no-changes` scoped to the two files (no formatting issues) before committing.

## How to verify
```bash
cd /home/user/worktrees/feature-4058-Telemetry-Claudemeetingtaskextractor-Jsonreaderexc
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MeetingTaskExtractionFailedExceptionTests"
```
Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2`.

## Notes
- `IMeetingTaskExtractor` already exists in the codebase (`backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingTaskExtractor.cs`) with an `ExtractAsync(string summary, string transcript, CancellationToken ct = default)` method, so the `<see cref="IMeetingTaskExtractor.ExtractAsync"/>` XML doc cross-reference in the exception's summary is valid and was kept as-is (per the task's instruction to only drop it if the interface didn't exist).
- `IMeetingTaskExtractor` was not modified — it does not yet declare that it throws this new exception; wiring that up is presumably a later task in this issue's plan, as noted in the task description. Out of scope here.
- No other files were touched; only the two specified files were staged and committed.

## PR Summary
Adds `MeetingTaskExtractionFailedException`, a dedicated exception type carrying attempt count and last raw LLM response, for use by meeting-task extraction when parsing fails after exhausting retries — laying groundwork so callers can distinguish a genuine extraction failure from "zero tasks found."

### Changes
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingTaskExtractionFailedException.cs` — new exception class with `AttemptCount` and `LastRawResponse` diagnostics.
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingTaskExtractionFailedExceptionTests.cs` — unit tests covering the constructor.

## Status
DONE
