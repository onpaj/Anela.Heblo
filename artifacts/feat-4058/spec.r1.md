# Specification: Harden ClaudeMeetingTaskExtractor against malformed LLM JSON responses

## Summary
`ClaudeMeetingTaskExtractor.ExtractAsync` still throws `System.Text.Json.JsonReaderException` when the underlying LLM call returns non-JSON output, silently dropping all extracted meeting tasks for that run. Issue #3972 diagnosed this (~23% of extraction jobs affected, 3x in a 7-day window) but the merged fix (PR #3981) only added raw-response logging on parse failure — it did not change extraction behavior. A fresh occurrence on 2026-09-03T09:55:50Z, >21h after that merge, confirms the root cause is still live. This spec defines the actual fix: schema validation with a bounded retry/repair step so a malformed model response no longer silently loses tasks, plus a durable failure path when repair still fails.

## Background
`ClaudeMeetingTaskExtractor.ExtractAsync` calls Claude to extract structured meeting tasks from free-form meeting notes/transcripts and deserializes the response as JSON. When the model returns non-JSON (or JSON wrapped in markdown fences, prose preamble, truncated output, etc.), `System.Text.Json` throws `JsonReaderException`, and — per the current behavior implied by #3972 — the exception propagates up and the entire job's extracted tasks are lost, with no tasks recovered and no automatic recovery attempt. PR #3981 added raw-response logging at the parse-failure point for diagnostics only, which is why the same fingerprint recurred: the underlying failure mode (bad model output) is unchanged, only visibility improved. This is a reliability issue with a known telemetry fingerprint (`exception:JsonReaderException@ClaudeMeetingTaskExtractor.ExtractAsync`) and a documented recurrence pattern, so the fix must address the parse failure itself, not just log it better.

## Functional Requirements

### FR-1: Validate structure of the LLM response before use
`ExtractAsync` must validate that the LLM's raw text response is well-formed JSON matching the expected meeting-task schema before deserializing it into domain objects.
**Acceptance criteria:**
- A response that fails `JsonReaderException` on initial parse is not immediately propagated as an unhandled exception to the caller.
- A response that parses as JSON but does not match the expected schema shape (missing required fields, wrong types) is detected and treated as a failure requiring repair, not silently coerced or partially applied.

### FR-2: Strip common non-JSON wrapping before validation
Many "malformed" LLM responses are valid JSON wrapped in extra content (e.g. ```json ... ``` markdown fences, a leading/trailing prose sentence). Before treating a response as unrecoverable, attempt to extract the JSON payload from surrounding text.
**Acceptance criteria:**
- A response wrapped in a markdown code fence (```json ... ``` or ``` ... ```) is unwrapped and re-parsed before falling back to a retry.
- A response with a JSON object/array embedded between unrelated leading/trailing text is extracted (e.g. by locating the outermost matching `{...}` or `[...]`) and re-parsed before falling back to a retry.

### FR-3: Retry the extraction call on parse/validation failure
When the response (after FR-2's unwrap attempt) still fails to parse or validate, retry the underlying LLM call up to a bounded number of times before giving up.
**Acceptance criteria:**
- On first parse/validation failure, the extraction call is retried at least once (configurable retry count, default suggested: 2 retries, 3 attempts total).
- Each retry attempt is logged (attempt number, failure reason) at a level that does not itself constitute an unhandled exception/error telemetry event for expected, in-budget retries.
- If a retry succeeds (produces valid, schema-conforming JSON), the run proceeds normally with the recovered tasks — no tasks are lost.

### FR-4: Fail loudly and distinctly when retries are exhausted
If all retry attempts are exhausted without a valid response, the extraction must fail in a way that is observable and distinguishable from a successful "zero tasks found" result — it must not silently return an empty task list.
**Acceptance criteria:**
- After exhausting retries, `ExtractAsync` raises a distinct, typed exception (e.g. `MeetingTaskExtractionFailedException`) rather than letting the raw `JsonReaderException` propagate, or returns a explicit failure/result type the caller must check — the codebase's existing error-handling convention (see Architecture Review) determines which.
- The final failure is logged with the raw response text (continuing the diagnostic logging added in PR #3981) plus the number of attempts made, so a telemetry occurrence of this new failure fingerprint carries enough information to diagnose without further code changes.
- The caller-visible/telemetry fingerprint for exhausted retries is distinct from a raw `JsonReaderException`, so recurrence of this exact issue (#4058's fingerprint) after this fix ships would itself indicate the fix regressed, rather than being indistinguishable noise.

### FR-5: No silent task loss
In no code path should a malformed LLM response result in tasks being silently dropped without either (a) a successful recovery via FR-2/FR-3, or (b) a loud, logged, distinctly-fingerprinted failure per FR-4.
**Acceptance criteria:**
- Code review / tests confirm there is no path where a caught parse exception is swallowed and an empty/default task list is returned without logging and without surfacing the failure per FR-4.

## Non-Functional Requirements

### NFR-1: Performance
- Retries must not multiply latency unacceptably for the common (already-valid) case: the happy path (valid JSON on first attempt) must incur no additional LLM calls or meaningful overhead versus current behavior.
- Retry attempts should use the same timeout/latency budget per call as the original call; total worst-case latency for a fully-exhausted retry sequence should be documented so callers/schedulers can size timeouts accordingly.

### NFR-2: Observability
- Each attempt (including the initial one) must be logged with enough context (operation id, attempt number, raw response on failure) to reconstruct what happened from telemetry alone, matching the existing raw-response logging pattern from PR #3981.
- The distinction between "recovered via retry" and "failed after exhausting retries" must be visible in telemetry/logs so operators can track whether the retry mechanism is masking a worsening underlying model-reliability problem.

## Data Model
No new persistent data model. The existing meeting-task extraction result shape (produced by successful JSON parse) is unchanged. A new internal type may be introduced to represent an extraction attempt outcome (success/retryable-failure/exhausted), scoped to `ClaudeMeetingTaskExtractor` and its immediate caller.

## API / Interface Design
- `ClaudeMeetingTaskExtractor.ExtractAsync` signature/contract: on success, returns the parsed task list as today. On unrecoverable failure (all retries exhausted), throws a new distinct exception type or returns an explicit failure result — architecture review determines which convention this codebase already uses for this kind of recoverable-then-unrecoverable failure (see Architecture Review's job to confirm existing patterns, e.g. Result<T> vs exceptions, elsewhere in the codebase).
- No new public API endpoints. This is an internal service hardening change within `Anela.Heblo.Application.Features.MeetingTasks.Services`.

## Dependencies
- The existing Claude/LLM client used by `ClaudeMeetingTaskExtractor` for the extraction call (must support being invoked multiple times/retried).
- `System.Text.Json` (existing).
- Existing logging/telemetry infrastructure (Application Insights, per the telemetry-signal fingerprint format used in this issue).
- PR #3981's raw-response logging, which this change builds on and extends rather than replaces.

## Out of Scope
- Changing the underlying LLM/model or prompt used for extraction (unless the architecture review determines a stricter structured-output constraint, e.g. JSON mode/schema-constrained decoding, is the better fix than app-level retry — that is an architectural decision to make in the next phase, not decided here).
- Reprocessing or backfilling meeting tasks lost by past occurrences of this bug (#3972, and this #4058 occurrence) — out of scope unless explicitly requested.
- Broader meeting-task extraction feature changes unrelated to response reliability (e.g. new task fields, new extraction triggers).
- Closing or reopening issue #3972 — that is a project-management action, not part of this implementation.

## Open Questions
None.

## Status: COMPLETE
