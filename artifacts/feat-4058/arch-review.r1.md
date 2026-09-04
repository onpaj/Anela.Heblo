# Architecture Review: Harden ClaudeMeetingTaskExtractor against malformed LLM JSON responses

## Skip Design: true

## Architectural Fit Assessment
This is a backend-only reliability fix to a single existing service, `ClaudeMeetingTaskExtractor` (`backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs`), which implements `IMeetingTaskExtractor` and is consumed by exactly two MediatR handlers: `IngestPlaudRecordingHandler` and `ReimportMeetingTranscriptHandler`. No new modules, no new API surface, no UI. It fits cleanly inside the existing MeetingTasks vertical slice.

Current code already implements part of what the spec asks for:
- `StripMarkdownCodeFence` already unwraps ```` ```json ... ``` ```` and ```` ``` ... ``` ```` fences before parsing (spec FR-2, markdown-fence case only — no "extract embedded JSON from surrounding prose" fallback exists).
- A `try/catch (JsonException)` around `JsonSerializer.Deserialize` already exists, and on failure logs `LogError` with the raw response text (this is exactly PR #3981's diagnostics-only addition) and returns `new MeetingExtractionResult([], [])` — this is the silent-drop path the issue is about.
- A second outer `catch (Exception)` handles transport-level failures (e.g. `HttpRequestException` from the chat client) the same way: log + empty result.
- Both `IngestPlaudRecordingHandler` and `ReimportMeetingTranscriptHandler` call `ExtractAsync`, get back a `MeetingExtractionResult`, and proceed to persist the transcript regardless of whether `Tasks` is empty because of "genuinely no tasks" or "extraction failed" — there is **no existing signal distinguishing these two cases** at the handler level, and the Plaud ingestion handler reports `Success = true` either way.
- Existing telemetry sampling config (`docs/architecture/observability.md`) keeps `Exception` events at 100% regardless of sampling elsewhere — this is *why* a caught-and-logged `JsonException` still shows up as an App Insights exception telemetry event and re-triggers the `applicationinsightsscan` skill's fingerprint match, even though the process never crashes. No telemetry/observability change is needed to fix that; fixing the underlying parse reliability (and giving retry-exhaustion a distinct log signature) is what changes the fingerprint going forward.
- Existing unit tests (`ClaudeMeetingTaskExtractorTests.cs`) pin exact log-message substrings ("malformed JSON", "no tasks", "extraction failed") and the empty-result-on-failure behavior — these tests must be updated deliberately, not incidentally broken, since the new behavior changes what "failure" means.
- `Polly` is already a project dependency and is already used for resilience in this exact adapter family (`AnthropicChatClient` wraps its HTTP call in a `ResiliencePipeline` with `RetryStrategyOptions` for transient HTTP errors — 429/529). That retry is a different concern (network transport) from this fix's concern (content/schema validity of an already-successful HTTP response), so it does not by itself solve this issue, but it establishes the codebase's retry idiom.

## Proposed Architecture

### Component Overview
```
IngestPlaudRecordingHandler ──┐
                               ├──> IMeetingTaskExtractor.ExtractAsync(summary, transcript, ct)
ReimportMeetingTranscriptHandler ┘         │
                                            ▼
                              ClaudeMeetingTaskExtractor
                              ┌─────────────────────────────────────┐
                              │ for attempt in 1..MaxAttempts:       │
                              │   1. call _chatClient.GetResponseAsync│
                              │   2. StripMarkdownCodeFence           │
                              │   3. TryExtractJsonPayload (new: also │
                              │      locates embedded {...}/[...] if  │
                              │      fence-stripping alone isn't      │
                              │      enough)                          │
                              │   4. Deserialize + validate shape     │
                              │   5. success -> return result         │
                              │      failure -> log attempt, continue │
                              │ all attempts exhausted:                │
                              │   -> log final failure (raw response, │
                              │      attempt count)                   │
                              │   -> throw MeetingTaskExtractionFailed │
                              │      Exception                        │
                              └─────────────────────────────────────┘
                                            │ (on unrecoverable failure)
                                            ▼
                              Handlers catch MeetingTaskExtractionFailedException
                              and surface it distinctly (see Decision 2)
```

### Key Design Decisions

#### Decision 1: Retry loop stays a plain in-method `for` loop, not a Polly pipeline
**Options considered:**
- (a) A plain bounded `for` loop inside `ExtractAsync` that re-invokes `_chatClient.GetResponseAsync` and re-validates.
- (b) Wrap the parse+validate step in a Polly `ResiliencePipeline`/`RetryStrategyOptions`, mirroring `AnthropicChatClient`.

**Chosen approach:** (a), plain loop.

**Rationale:** `AnthropicChatClient`'s Polly pipeline retries *transport* failures (HTTP status codes) below the `IChatClient` abstraction — that is the right layer for Polly because the predicate is a clean, stateless exception-type/status-code check. Here, the retry predicate is "did the response parse into a valid task list" — inherently tied to the freshly-received response body, and the retry needs to feed back into building a corrective follow-up message in a future iteration if the team later wants to (out of scope per spec, but keeping this a plain loop leaves that door open in the same method without threading Polly context through). A 2-3 iteration bounded loop with no backoff requirement (this is a content-quality retry, not a rate-limit backoff) is simpler to read, test, and reason about than a resilience pipeline. This keeps the change surgical: `ClaudeMeetingTaskExtractor` remains a single, self-contained class using only the primitives it already imports (`System.Text.Json`, `Microsoft.Extensions.AI`, `Microsoft.Extensions.Logging`) — no new package dependency for the Application layer.

#### Decision 2: Signal failure via a new distinct, typed exception — not a Result flag
**Options considered:**
- (a) Add a new sealed exception type `MeetingTaskExtractionFailedException` (in the same `Services` namespace) thrown by `ExtractAsync` once retries are exhausted; handlers catch it explicitly.
- (b) Extend `MeetingExtractionResult` with a `bool Success` / `string? FailureReason` flag and require every caller to branch on it.
- (c) Keep returning an empty `MeetingExtractionResult` silently, relying only on log volume to signal trouble (current behavior — rejected, this is the bug).

**Chosen approach:** (a), a new typed exception.

**Rationale:** There is no existing `Result<T>`/outcome-wrapper convention used elsewhere in this codebase's Application layer for this kind of service call (MediatR handlers here throw and let ASP.NET/MediatR pipeline behavior or a global exception filter handle errors, rather than returning outcome objects) — introducing a `Result<T>` pattern here would be a wider architectural change than this issue warrants and would touch both handlers' return-shape logic unnecessarily. A dedicated exception type is the minimal, idiomatic choice: it changes `ExtractAsync`'s contract from "never throws, may return empty" to "throws a specific type only when genuinely unrecoverable, otherwise same as today," which both existing callers can opt into handling. Each handler decides its own recovery UX (see Prerequisites/Interfaces below) — e.g. `IngestPlaudRecordingHandler` catching it and returning `Success = false` with a reason instead of the current `Success = true` regardless.

#### Decision 3: JSON-extraction-from-prose fallback is a small, local static helper — not a new dependency
**Options considered:**
- (a) A small static regex/bracket-matching helper added next to `StripMarkdownCodeFence` in the same file, applied only when a first parse attempt (post fence-stripping) fails.
- (b) Pull in a "lenient JSON" or "JSON repair" NuGet package.

**Chosen approach:** (a).

**Rationale:** The known failure modes from the telemetry fingerprint are "non-JSON / wrapped output," not deeply malformed JSON (missing commas, unquoted keys) — a bracket-matching extraction of the outermost `{...}` (searching from the first `{` to the matching last `}`, tracking string/escape state to avoid false matches inside string values) is sufficient and keeps the fix dependency-free, consistent with Decision 1's "no new package" stance. This should only run as a fallback *after* a plain re-parse of the fence-stripped text fails, to avoid masking genuinely-malformed JSON as a false "extracted" match.

## Implementation Guidance

### Directory / Module Structure
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs` — add retry loop, embedded-JSON extraction fallback, schema validation, and the exhausted-retries failure path.
- Add: `MeetingTaskExtractionFailedException` — a small `sealed class` (not a record — this is an exception type, records are irrelevant to the DTO rule but exceptions are conventionally classes anyway) in the same file or a new `MeetingTaskExtractionFailedException.cs` file next to `IMeetingTaskExtractor.cs` in `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/`. Constructor should accept a message plus the number of attempts made and (optionally) the last raw response, to support Decision 2's logging needs without re-deriving them at the catch site.
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecordingHandler.cs` and `.../ReimportMeetingTranscriptHandler.cs` — wrap the `_extractor.ExtractAsync(...)` call in a `try/catch (MeetingTaskExtractionFailedException)`; on catch, log and return the handler's existing "failure" response shape (`IngestPlaudRecordingResponse` already has a `Success` flag pattern to extend; check `ReimportMeetingTranscriptHandler`'s response type for the equivalent — mirror whatever shape it already uses for other failure paths in that handler, do not invent a new response contract).
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ClaudeMeetingTaskExtractorTests.cs` — the two existing tests `ExtractAsync_WhenJsonInvalid_LogsErrorAndReturnsEmpty` and (indirectly) the malformed-JSON assertions must change: a single malformed response should now trigger a retry rather than immediately returning empty, and only exhausting all attempts should raise the new exception. Add new tests: retry succeeds on 2nd/3rd attempt; retry exhausted raises `MeetingTaskExtractionFailedException`; embedded-JSON-in-prose extraction succeeds; existing markdown-fence test continues to pass unchanged.
- Add/modify caller-side tests for `IngestPlaudRecordingHandler` and `ReimportMeetingTranscriptHandler` to cover the new exception being caught and surfaced as a failure rather than a false `Success = true`.

### Interfaces and Contracts
- `IMeetingTaskExtractor.ExtractAsync` keeps its exact signature (`Task<MeetingExtractionResult> ExtractAsync(string summary, string transcript, CancellationToken ct = default)`). Its *contract* changes: it now throws `MeetingTaskExtractionFailedException` when the model's response cannot be parsed into a valid, schema-conforming payload after retries are exhausted. It no longer treats "response never validated" the same as "response validated to an empty task list" — both used to collapse to `MeetingExtractionResult([], [])`; now only the latter does.
- Transport-level failures (the existing outer `catch (Exception ex)` around the whole chat-client call, e.g. `HttpRequestException`) are **out of scope for this change** — per the spec and issue, the telemetry fingerprint is specifically `JsonReaderException` (a parse/content problem), not a transport error, and `AnthropicChatClient`'s own Polly pipeline already retries transient HTTP failures below this layer. Leave the outer `catch (Exception)` behavior as-is (log + empty result) unless a reviewer decides transport failures should also now throw — not required by this issue.
- New type: `MeetingTaskExtractionFailedException(string message, int attemptCount, string? lastRawResponse = null) : Exception(message)`.
- New internal validation: after JSON-deserializing into `ExtractionPayload`, additionally verify each `ExtractedTask` has a non-empty `Title` (the schema's only clearly-required field per the existing prompt's contract: "title: stručný název úkolu"); a payload that deserializes but contains a task with an empty/missing title should be treated as a validation failure for retry purposes, not silently accepted with a blank title.

### Data Flow
1. Handler calls `ExtractAsync(summary, transcript, ct)`.
2. Extractor builds the same system prompt as today (no prompt change needed for this fix; a structured-output/JSON-mode constraint was considered and explicitly deferred — see Out of Scope in spec).
3. Extractor loop (bounded, e.g. 3 attempts total): call chat client → strip markdown fence → attempt JSON parse → if that fails, attempt bracket-matched JSON extraction from the raw text → parse the extracted substring → validate shape (non-null `Tasks`, non-empty `Title` per task) → on success, normalize participants and return `MeetingExtractionResult` exactly as today.
4. On any attempt's failure (parse or validation), log a warning with attempt number and reason, and loop again (re-invoking the chat client — this is a fresh model call each retry, not a re-parse of the same text, since the same malformed text would just fail identically again).
5. After the final attempt fails, log an error with the full raw response of the last attempt and the total attempt count, then throw `MeetingTaskExtractionFailedException`.
6. Handler catches the exception, logs it (handler-level context: recording/transcript id), and returns its own existing failure-response shape instead of proceeding to persist a transcript with silently-empty tasks reported as success.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Retrying triples LLM call cost/latency on every already-broken transcript | Medium | Cap at a small bounded attempt count (2-3 attempts, no backoff); this only multiplies cost for the ~23%-of-jobs failure case identified in #3972, not the happy path. |
| Handlers now need new failure-branch code for a case that previously never threw from this call | Low-Medium | Change is localized to two call sites; both already have `try`-free straight-line `async` handler bodies, so adding one `try/catch` around a single `await` line each is small and testable. |
| New bracket-matching JSON extraction incorrectly "recovers" JSON from wildly wrong prose text and produces a task list from unrelated content | Low | Only attempt bracket-matching as a fallback after a direct parse fails; still fully re-validate the extracted substring the same as any other candidate JSON before accepting it — it goes through the same schema validation, so accidental garbage still fails validation and triggers retry, not silent corruption. |
| Existing tests pin exact log substrings and empty-result behavior; changing behavior breaks them | Low (expected, tracked) | Explicitly called out in Directory/Module Structure above — this is planned test churn, not an accidental regression, and the planner should allocate a task for updating them. |
| A telemetry occurrence of `MeetingTaskExtractionFailedException` after this ships could itself look like "the bug is back" to an automated scanner | Low | This is intentional per spec FR-4: exhausted-retries is a *new*, distinct, and rarer fingerprint (should only fire when the model fails validation on every attempt) — its presence in telemetry is expected, low-volume signal, not silent data loss, and is documented as such in the raw-response + attempt-count log payload for whoever investigates it. |

## Specification Amendments
- Spec FR-1/FR-2 described "detect a malformed response" and "strip markdown fences" as if these needed to be built from scratch; markdown-fence stripping already exists in `StripMarkdownCodeFence`. The only net-new piece of FR-2 is the "extract JSON embedded in surrounding prose" fallback (Decision 3) — fence-stripping itself just needs to remain as-is.
- Spec FR-4 asked to decide between "a distinct typed exception" and "an explicit failure/result type... determined by architecture review." This review resolves that: **use a typed exception** (`MeetingTaskExtractionFailedException`), per Decision 2.
- Spec's "Out of Scope" section already correctly defers the structured-output/JSON-mode prompt-engineering option to this review's judgment; this review defers it further (see Decision 1's rationale) — not adopted now, but not precluded for a future issue if retries alone prove insufficient in telemetry.
- Add to spec's Dependencies: the two concrete call sites (`IngestPlaudRecordingHandler`, `ReimportMeetingTranscriptHandler`) must both be updated in this change, not just `ClaudeMeetingTaskExtractor` itself — the spec's "Out of Scope" and "API / Interface Design" sections did not explicitly call out that caller changes are required, but FR-4/FR-5 cannot be satisfied (the failure can't be "loud") unless at least one caller stops treating the exception's absence as the only success signal.

## Prerequisites
- None — no migrations, no new infrastructure, no config/feature-flag needed. This is a pure code change to an existing, already-deployed service and its two existing callers.
