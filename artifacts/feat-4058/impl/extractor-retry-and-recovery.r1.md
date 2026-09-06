# Implementation: extractor-retry-and-recovery

## What was implemented
Added retry-and-recovery behavior to `ClaudeMeetingTaskExtractor.ExtractAsync`:
- Up to `MaxAttempts = 3` attempts are made against the chat client per extraction.
- A transient transport failure (e.g. `HttpRequestException`) on any attempt before the last is retried; on the final attempt a transport failure still falls back to the pre-existing "log error + return empty result" contract (unchanged, out of scope per the architecture review).
- A malformed/unparseable JSON response, or a parsed response containing a task with an empty/whitespace-only title, is treated as a recoverable failure and retried on subsequent attempts (consuming the same attempt budget as transport retries).
- Before giving up on a malformed response, the extractor now also tries to recover a JSON object embedded in surrounding prose (e.g. "Here is the result: {...} Let me know...") via a brace-depth/string-aware scan (`ExtractEmbeddedJsonObject`), so a chatty wrapper around otherwise-valid JSON no longer counts as a failure.
- When all `MaxAttempts` attempts produce either malformed JSON or an empty-title task, the extractor now throws `MeetingTaskExtractionFailedException` (added by the prior `extraction-failure-exception` task) carrying `AttemptCount` (always 3 in this path) and `LastRawResponse` (the raw, fence-stripped text of the final attempt), and logs a final error including the raw response and attempt count. This replaces the old behavior of silently returning an empty result on the very first malformed response.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs` — added the `MaxAttempts` constant, rewrote `ExtractAsync` as a retry loop, and added private helpers `TryParseAndValidate`, `TryDeserialize`, and `ExtractEmbeddedJsonObject`. No changes to the constructor, `BuildSystemPrompt`, `NormalizeParticipants`, `StripMarkdownCodeFence`, or the `ExtractionPayload` record.
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ClaudeMeetingTaskExtractorTests.cs` — added 5 new tests and replaced 1 existing test whose asserted behavior changed; the 2 existing transport-failure tests were extended with a `Times.Exactly(3)` assertion on the chat client call count (no behavior change needed there beyond the added assertion, since Moq's `.ThrowsAsync` already throws on every call).

## Tests
`ClaudeMeetingTaskExtractorTests` (16 tests total, all passing):
- New: `ExtractAsync_WhenFirstAttemptMalformed_RetriesAndReturnsSecondAttemptResult` — first attempt malformed, second attempt valid JSON, returns the second attempt's parsed result.
- New: `ExtractAsync_WhenAllAttemptsMalformed_ThrowsAfterExhaustingRetries` — all 3 attempts return malformed JSON; asserts the thrown `MeetingTaskExtractionFailedException.AttemptCount == 3` and `.LastRawResponse` equals the raw text, and that the chat client was called exactly 3 times.
- New: `ExtractAsync_WithJsonEmbeddedInProseText_ExtractsAndParsesIt` — JSON object embedded in prose (leading/trailing text) is extracted and parsed on the first attempt.
- New: `ExtractAsync_WhenTaskHasEmptyTitle_RetriesAndSucceedsOnNextAttempt` — a task with an empty title on the first attempt is treated as invalid and retried; the second attempt's valid title is returned.
- New: `ExtractAsync_WhenChatClientThrowsOnFirstAttemptButSucceedsOnRetry_ReturnsResult` — a transport exception on the first attempt is retried and the second (successful) attempt's result is returned.
- Replaced: `ExtractAsync_WhenJsonInvalid_LogsErrorAndReturnsEmpty` → `ExtractAsync_WhenAllAttemptsMalformed_LogsFinalErrorWithRawResponseAndAttemptCount` — same malformed-JSON-on-every-attempt setup, but now asserts the call throws `MeetingTaskExtractionFailedException` and that a final `LogLevel.Error` log entry contains the raw response text.
- Updated: `ExtractAsync_WhenChatClientThrows_ReturnsEmptyResult` and `ExtractAsync_WhenApiThrows_LogsErrorAndReturnsEmpty` — both still assert an empty result (transport failure on the final attempt keeps the old contract), now additionally asserting the chat client was invoked exactly 3 times (transport failures on earlier attempts are retried before falling through).
- Unchanged and still passing: `ExtractAsync_WithValidJsonResponse_ReturnsParsedTasks`, `ExtractAsync_ParsesParticipants`, `ExtractAsync_NormalizesParticipants_TrimsBlanksAndDeduplicates`, `ExtractAsync_ParsesAssigneeEmailWhenLlmMatchesUser`, `ExtractAsync_IncludesDirectoryUsersInPrompt`, `ExtractAsync_WithMarkdownWrappedJson_StripsFenceAndParses`, `ExtractAsync_PassesMaxOutputTokens8192ToChatClient`, `ExtractAsync_WhenResponseHasNoTasks_LogsWarningAndReturnsEmptyTasks`.

Actual run:
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ClaudeMeetingTaskExtractorTests"
Passed!  - Failed: 0, Passed: 16, Skipped: 0, Total: 16, Duration: 77 ms - Anela.Heblo.Tests.dll (net8.0)
```

Also verified:
- `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — 0 errors (172 pre-existing warnings across the test project, none introduced by this change).
- `dotnet format Anela.Heblo.sln --include backend/src/.../ClaudeMeetingTaskExtractor.cs backend/test/.../ClaudeMeetingTaskExtractorTests.cs --verify-no-changes` — exit code 0, no formatting changes required.

## How to verify
```bash
cd /home/user/worktrees/feature-4058-Telemetry-Claudemeetingtaskextractor-Jsonreaderexc
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ClaudeMeetingTaskExtractorTests"
```
Expected: `Passed! - Failed: 0, Passed: 16, Skipped: 0, Total: 16`.

## Notes
- Confirmed `MeetingTaskExtractionFailedException`'s constructor shape against the actual file before use: `MeetingTaskExtractionFailedException(string message, int attemptCount, string? lastRawResponse)` with `AttemptCount`/`LastRawResponse` properties, in the same namespace (`Anela.Heblo.Application.Features.MeetingTasks.Services`) as the extractor — no new `using` was needed.
- Implemented the spec's snippet essentially verbatim; the only adaptation from the literal snippet in the task description was none required — the "before ExtractAsync" class members (usings, fields, constructor, `BuildSystemPrompt`, `StripMarkdownCodeFence`, `ExtractionPayload`, `NormalizeParticipants`) in the actual file already matched the target end-state exactly, so those were left untouched and only the body from `MaxAttempts` through the end of `ExtractAsync` (plus the new private helpers) was added/replaced.
- Per the task's documented scope boundary, a transport failure on the *last* attempt still returns an empty result via the pre-existing log+return path rather than throwing `MeetingTaskExtractionFailedException` — only content/parse failures (malformed JSON or empty task title) that persist through all `MaxAttempts` attempts throw the new exception.
- No other files were touched; only the two specified files were staged and committed (the working tree also had an unrelated pre-existing modification to `artifacts/feat-4058/state.json` from a prior task, which was left uncommitted/untouched as instructed).

## Status
DONE
