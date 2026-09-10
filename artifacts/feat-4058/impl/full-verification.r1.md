# Implementation: full-verification

## What was implemented
Ran full verification; no code changes needed. The full backend build/format/test suite was run, and the current state of `ClaudeMeetingTaskExtractor` and its two callers (`IngestPlaudRecordingHandler`, `ReimportMeetingTranscriptHandler`) plus `PlaudPollingJob` was re-read against spec.r1.md's FR-1 through FR-5 and NFR-1/NFR-2. Every requirement is satisfied by the already-committed changes from the four prior tasks in this plan, matching the architecture review's decisions verbatim. No gap was found, so no fix was applied and no commit was made.

Verification detail, mapped to spec:
- **FR-1/FR-2** (validate response shape; strip markdown fences / extract embedded JSON): `ClaudeMeetingTaskExtractor.TryParseAndValidate` deserializes and validates (rejects a task with an empty/whitespace title), falling back to `ExtractEmbeddedJsonObject` (a string/escape-aware brace-matching scan) when a direct parse fails, on top of the pre-existing `StripMarkdownCodeFence`.
- **FR-3** (bounded retry): `ExtractAsync` loops `for (attempt = 1; attempt <= MaxAttempts; attempt++)` with `MaxAttempts = 3`; a malformed/invalid response before the last attempt logs a warning (attempt number + reason) and retries; a successful retry returns the recovered result with no error-level log.
- **FR-4** (loud, distinct failure): after exhausting all 3 attempts on a content/parse failure, `ExtractAsync` logs an error with the raw response and attempt count, then throws `MeetingTaskExtractionFailedException(message, attemptCount, lastRawResponse)` — a type distinct from `JsonReaderException`.
- **FR-5** (no silent task loss): both `IngestPlaudRecordingHandler.Handle` and `ReimportMeetingTranscriptHandler.Handle` wrap the `_extractor.ExtractAsync` call in `try/catch (MeetingTaskExtractionFailedException)`, log the failure with the recording/transcript id and attempt count, and return an explicit failure response (`Success = false` / `ErrorCodes.Exception`) instead of persisting a transcript with silently-empty tasks. `PlaudPollingJob.ExecuteAsync` tracks a `failed` counter distinct from `ingested`/`skipped`/`notGenerated` and reports it in the completion log.
- **NFR-1** (happy-path performance): a valid first-attempt response returns immediately from inside the loop body (`attempt == 1`), making exactly one `_chatClient.GetResponseAsync` call — no added latency versus the pre-existing behavior.
- **NFR-2** (observability): every attempt is logged — `LogWarning` on each retryable failure (transport or content/validation, with attempt number and reason) and `LogError` only on final exhaustion (with the full raw response and attempt count) — so "recovered via retry" (warnings only, no error) is distinguishable in telemetry from "failed after exhausting retries" (a final error log plus the new exception's fingerprint).

One documented, deliberate design boundary confirmed as intentional (not a gap): a transport failure (e.g. `HttpRequestException`) on the *final* attempt still falls through to the pre-existing "log error + return empty result" contract rather than throwing `MeetingTaskExtractionFailedException` — this is explicitly called out in `arch-review.r1.md`'s "Interfaces and Contracts" section as out of scope for this issue (the telemetry fingerprint being fixed is a content/parse failure, not a transport error, and `AnthropicChatClient`'s own Polly pipeline already retries transient HTTP failures below this layer). The implementation matches this decision exactly.

## Files created/modified
None (verification-only; no gap found requiring a code fix).

## Tests
Run from the repo root (`Anela.Heblo.sln` lives there, not under `backend/`):
- `dotnet build` — succeeded, 0 errors (261 pre-existing warnings across test projects, none introduced by this change).
- `dotnet format --verify-no-changes` — exit code 0, no formatting changes required.
- `dotnet test` (full solution) — `Anela.Heblo.Tests.dll`: **6769 passed, 105 failed, 4 skipped** (Total 6878). Every one of the 105 failures is `System.ArgumentException: Docker is either not running or misconfigured` from Testcontainers-backed integration tests (`KnowledgeBaseRepositoryIntegrationTests`, `ArticleRepositoryFeedbackProjectionSqlTests`, `MeetingTranscriptRepositorySearchIntegrationTests`, etc.) — a pre-existing sandbox limitation (no Docker daemon available here), unrelated to this change. `Anela.Heblo.Adapters.Flexi.Tests.dll` (72 failed) and `Anela.Heblo.Adapters.Shoptet.Tests.dll` (13 failed) fail for the same reason plus missing live-service configuration (`FlexiIntegrationTestFixture`, `Missing Shoptet:StatusId:EXP in configuration`) — both pre-existing, environment-dependent, and outside the MeetingTasks vertical slice this issue touches. All other test assemblies (Logeto, HomeAssistant, Plaud, OpenMeteo, OpenAI) passed 100%.
- Filtered run `dotnet test --filter "FullyQualifiedName~MeetingTasks"`: **160 passed, 7 failed** — the 7 failures are exactly the `MeetingTranscriptRepositorySearchIntegrationTests` Docker-dependent cases above; every unit test for `ClaudeMeetingTaskExtractorTests`, `MeetingTaskExtractionFailedExceptionTests`, `IngestPlaudRecordingHandlerTests`, `ReimportMeetingTranscriptHandlerTests`, and `PlaudPollingJobTests` passed.

No test changes were needed.

## How to verify
```bash
cd /home/user/worktrees/feature-4058-Telemetry-Claudemeetingtaskextractor-Jsonreaderexc
dotnet build
dotnet format --verify-no-changes
dotnet test --filter "FullyQualifiedName~MeetingTasks"
```
Expected: build succeeds with 0 errors; format reports no changes; the MeetingTasks-filtered test run passes all non-Docker-dependent tests (160 passed; 7 pre-existing Testcontainers/Docker-environment failures unrelated to this change, only if Docker is unavailable in the sandbox — they pass with a working Docker daemon).

## Notes
- The repo's `dotnet build`/`dotnet test`/`dotnet format` commands must be run from the repo root (where `Anela.Heblo.sln` lives), not from `backend/` — there is no project/solution file directly under `backend/`. Ran them from the worktree root instead; this is a documentation nuance, not a code gap, so no doc change was made per this task's verification-only scope.
- Running two concurrent `dotnet test` invocations against the same test project from this sandbox caused MSBuild file-lock contention; tests were run serially to avoid that.
- No gap was found between the spec and the implementation. All four prior tasks' commits (`ca6fd92`, `9c0c236`, `4e6cb3e`, `64765eb`, and the underlying `16df62d`, `5fef7bb`, `355e183`, `b9f193f`, `8f8a54d`, `5472b9b`) were verified in place, matching their own impl artifacts and the architecture review's decisions exactly.
- Only the pipeline's own `artifacts/feat-4058/state.json` shows as modified in `git status`; that is the orchestration process's own progress tracking, not a code change, and was left untouched.

## PR Summary
This branch hardens `ClaudeMeetingTaskExtractor` (Application layer, `MeetingTasks` vertical slice) against malformed Claude/LLM JSON responses, which previously caused an unhandled `JsonReaderException` that silently dropped all extracted meeting tasks for a run (issue #4058, a recurrence of #3972 after PR #3981's diagnostics-only fix). `ExtractAsync` now validates the response shape, strips markdown-fence wrapping and extracts a JSON object embedded in surrounding prose before giving up, and retries the underlying Claude call up to 3 attempts total on a parse/validation failure — each retry logged as a warning with attempt number and reason. If all attempts are exhausted, a new distinct `MeetingTaskExtractionFailedException` (carrying the attempt count and the last raw response) is thrown and logged at error level, instead of silently returning an empty task list. Both call sites into the extractor — `IngestPlaudRecordingHandler` (the Plaud polling ingest path) and `ReimportMeetingTranscriptHandler` (the manual reimport path) — now catch this exception, log it with their own request-level context, and return an explicit failure response rather than persisting a transcript with falsely "successful" empty tasks; `PlaudPollingJob`'s completion log now reports a distinct `failed` count alongside `ingested`/`skipped`/`notGenerated`. A deliberate, documented scope boundary (per the architecture review) leaves transport-level failures (e.g. network errors) on the final retry attempt on their pre-existing "log + empty result" contract, since this issue's fingerprint and root cause is specifically a content/parse failure, not a transport error. This final verification task ran the full backend build/format/test suite and re-confirmed the implementation against every FR/NFR in the spec with no gaps found, so no additional code changes were made.

### Changes
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingTaskExtractionFailedException.cs` (new) — distinct exception type with `AttemptCount` and `LastRawResponse`.
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs` — retry loop, embedded-JSON extraction fallback, schema validation, throws the new exception after exhausting retries.
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs` — catches the exception, returns `Success = false`.
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs` — adds a distinct `failed` counter in the completion log.
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptHandler.cs` — catches the exception, returns `ErrorCodes.Exception`.
- Corresponding test files under `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/` updated/added for all of the above.

## Status
DONE
