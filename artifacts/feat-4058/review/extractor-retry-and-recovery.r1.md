# Code Review: extractor-retry-and-recovery

## Summary
The implementation is a faithful, correct rewrite of `ExtractAsync` as a bounded 3-attempt retry loop with JSON-repair (brace/string-aware embedded-object extraction) and a new `MeetingTaskExtractionFailedException` throw path on content/parse exhaustion, while preserving the deliberate "final-attempt transport failure still falls back to log+empty" scope boundary. All 6 required new/changed tests are present and assert the right things, and I independently traced the retry/parse/embedded-extraction logic against the test inputs and confirm it behaves correctly (verified manually, not just by trusting the developer's summary).

## Review Result: PASS

### task: extractor-retry-and-recovery
**Status:** PASS

## Docs to Update
(none)

## Overall Notes
- Ran `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ClaudeMeetingTaskExtractorTests"` myself: `Passed! - Failed: 0, Passed: 16, Skipped: 0, Total: 16` — matches the developer's report, and the build compiled the full solution cleanly along the way (no separate build errors).
- Verified all 7 numbered requirements against the actual `ClaudeMeetingTaskExtractor.cs`:
  1. `MaxAttempts = 3`, loop `for (attempt = 1; attempt <= MaxAttempts; attempt++)` — correct attempt budget, no off-by-one.
  2. Transport failure: `catch (Exception ex) when (attempt < MaxAttempts)` retries (logs warning, `continue`); the unconditional final `catch (Exception ex)` on the last attempt logs an error and returns `new MeetingExtractionResult([], [])`, per the explicit scope boundary — not flagged as a bug.
  3. Malformed JSON and empty-title tasks both set `failureReason` and fall through to the same retry path (same attempt budget as transport failures).
  4. `ExtractEmbeddedJsonObject` does a brace-depth scan with `inString`/`escapeNext` tracking so braces/quotes inside a task's `description` field don't break the match. I traced it manually against `ExtractAsync_WithJsonEmbeddedInProseText_ExtractsAndParsesIt`'s prose-wrapped payload and it correctly isolates the embedded object.
  5. On exhaustion, throws `MeetingTaskExtractionFailedException(message, MaxAttempts, lastRawResponse)` — `AttemptCount` and `LastRawResponse` fields confirmed on the exception class itself (`backend/src/.../MeetingTaskExtractionFailedException.cs`), and a final `_logger.LogError` includes both the raw response and attempt count in the format string, matching test `ExtractAsync_WhenAllAttemptsMalformed_LogsFinalErrorWithRawResponseAndAttemptCount`.
  6. All 5 new tests plus the 1 replaced test plus the 2 extended `Times.Exactly(3)` assertions are present in `ClaudeMeetingTaskExtractorTests.cs` and match the required names/assertions exactly (test count went from 11 to 16, consistent with 5 new + 1 replaced-in-place).
  7. Confirmed by running the filtered test command myself (see above).
- Out-of-scope observation (not a defect in this task): `ReimportMeetingTranscriptHandler` and `IngestPlaudRecordingHandler`, the two callers of `IMeetingTaskExtractor.ExtractAsync`, contain no catch for `MeetingTaskExtractionFailedException` — a full-exhaustion failure will now propagate as an unhandled exception into those handlers rather than being caught locally. This is plausibly intended (surfacing the failure loudly is the point of the exception vs. the old silent-empty-result behavior) and is outside the two files this task was scoped to touch, so it is not a revision-blocking issue here — worth a follow-up task if the pipeline wants explicit handler-level handling.
