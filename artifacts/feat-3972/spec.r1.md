# Specification: Fix silent data loss on malformed LLM JSON in meeting task extraction

## Summary
`ClaudeMeetingTaskExtractor.ExtractAsync` currently catches `JsonException` on the LLM's meeting-extraction response and silently returns an empty result, discarding all tasks and participants with only a `LogError` and no data to diagnose the cause and no signal visible to the user who triggered the extraction. Telemetry shows this happens at roughly a 23% rate over a recent 7-day window, driven by the model occasionally emitting an unescaped raw character (e.g. a Czech name, quote, or dash) inside a JSON string value. This spec covers making the failure diagnosable (log the raw malformed response), making the failure less destructive (recover whatever can be salvaged from a malformed payload instead of discarding everything), and making it visible (flag the affected meeting so the user knows tasks may be missing and can retry).

## Background
Meeting recordings are ingested from Plaud (via `IngestPlaudRecordingHandler`, a Hangfire background job) or manually reimported (`ReimportMeetingTranscriptHandler`). Both call `IMeetingTaskExtractor.ExtractAsync`, which prompts Claude with the meeting transcript/summary and asks it to "return ONLY a JSON object" (no schema-constrained/tool-use output), then deserializes the response text with `JsonSerializer.Deserialize<ExtractionPayload>`.

Telemetry (P7D window 2026-08-21 → 2026-08-28) recorded 3 `JsonReaderException` occurrences out of ~13 total extraction calls (~23% failure rate). All 3 failures are a JSON syntax error partway through the `tasks` array (not at the response tail), which rules out truncation from the 8192 `MaxOutputTokens` cap and points instead at the model emitting an improperly-escaped character inside a string value (title/description/assignee), most likely a Czech name, quote mark, or em-dash.

Today, on any parse failure, the entire meeting is imported with **zero tasks and zero participants**, silently. The only trace is a `LogError` with the exception (no raw response text), so the malformed payload itself has never been captured and root cause cannot be confirmed further from telemetry alone. There is also no user-visible signal — a meeting that lost all its action items looks identical, in the UI, to a meeting that genuinely had none.

This is a bug fix, not a redesign. The prompt-only JSON approach and the two call sites are left as-is; the current lenient `MeetingExtractionResult([], [])` fallback contract is preserved, extended with enough information for callers to flag the transcript.

## Functional Requirements

### FR-1: Log the raw malformed response on JSON parse failure
When `JsonSerializer.Deserialize<ExtractionPayload>` throws `JsonException` in `ExtractAsync`, log the full raw response text (the `text` variable, i.e. post-markdown-fence-stripping, pre-deserialization) alongside the existing exception, so the next occurrence's actual malformed payload is captured in telemetry/logs instead of only the exception message.

**Acceptance criteria:**
- On `JsonException`, the log entry (still `LogError`, same event) includes the full raw response text as a structured logging property (not string-interpolated into the message), e.g. `_logger.LogError(ex, "... {RawResponse}", text)`.
- The logged text is the value after `StripMarkdownCodeFence`, matching what was actually passed to `JsonSerializer.Deserialize`.
- No truncation is applied that would cut off the region identified by `ex.BytePositionInLine` / `ex.LineNumber` — logging the full text is acceptable given `MaxOutputTokens = 8192` bounds its size.
- Existing exception object (`ex`) continues to be passed to the logger so stack trace / `JsonException.Path`, `LineNumber`, `BytePositionInLine` remain available in the log record.
- A unit test asserts that on a deliberately malformed JSON input, the logger receives a log call whose structured state includes the raw response text.

### FR-2: Recover partial results instead of discarding the entire extraction
When the top-level JSON fails to parse, attempt a best-effort salvage of whatever `tasks` and `participants` can be individually parsed, instead of unconditionally returning an empty result. The goal is to reduce the blast radius of one malformed field (e.g. one bad `title` string) from "lose everything" to "lose that one task."

**Acceptance criteria:**
- On full-document parse failure, the extractor attempts a fallback parse strategy that parses the `participants` array and each element of the `tasks` array independently (e.g., via `JsonDocument.Parse` in permissive/best-effort mode, extracting each array element's raw text and deserializing it individually), skipping and logging (`LogWarning`, with the element's raw text and its index) any individual task or participant entry that itself fails to parse.
- If the fallback strategy also cannot locate/parse a `tasks` or `participants` array at all (e.g. the response isn't JSON-shaped at all), the extractor falls back to today's behavior: return `MeetingExtractionResult([], [])` and `LogError` with the raw response (per FR-1).
- The returned `MeetingExtractionResult` gains a way for callers to know the extraction was degraded (see Data Model) — set whenever one or more tasks/participants were dropped due to a parse error, whether from full failure or partial salvage.
- Tasks/participants that parse successfully are always included in the result, in their original relative order, even when other entries in the same response were dropped.
- Existing behavior for a fully well-formed response is unchanged: no new dropped entries, `Degraded = false`.
- Unit tests cover: (a) a response where one task in the middle of the array has an invalid character but the array is otherwise well-formed JSON except for that one malformed value — asserts the other tasks and all participants are still returned, `Degraded = true`; (b) a response that is not JSON at all — asserts empty result, `Degraded = true`; (c) a fully valid response — asserts `Degraded = false` and nothing dropped.

### FR-3: Surface degraded/failed extraction to the user
A meeting whose task extraction was degraded or failed must be visibly distinguishable, in the UI, from a meeting that genuinely had no tasks/participants, so the user knows to review the transcript manually or retry.

**Acceptance criteria:**
- `MeetingTranscript` gains a persisted flag (see Data Model, `TasksExtractionDegraded`) set from `MeetingExtractionResult.Degraded` by both `IngestPlaudRecordingHandler` and `ReimportMeetingTranscriptHandler` at the point they currently map `extraction.Tasks`/`extraction.Participants` onto the entity.
- The flag is persisted on both initial ingest and on reimport (a reimport that now succeeds cleanly must clear a previously-set flag; a reimport that still degrades must keep it set).
- The transcript API response DTO (returned to the frontend for the meeting task list/detail views) exposes this flag.
- The meeting task detail page (`MeetingTaskDetailPage.tsx`) shows a visible warning (e.g. a banner near the existing `TranscriptStatusBadge`) when the flag is set, indicating that task/participant extraction may be incomplete and suggesting the existing "Reimport" action as the remedy. Exact copy/placement is left to implementation but must be unmissable at the top of the page, not buried in a tooltip.
- The meeting task list page, if it already surfaces per-row status badges, shows an equivalent indicator for affected rows (icon/badge is sufficient; full banner not required at list-row granularity).
- A meeting with the flag set is not filtered out or hidden from normal review queues — it remains fully reviewable/approvable; the flag is informational only and does not block approval or task work in this iteration.

## Non-Functional Requirements

### NFR-1: Performance
- The fallback per-element parsing in FR-2 only runs on the already-rare parse-failure path (~23% of extraction calls per current telemetry, itself a background/manual-trigger operation, not a hot request path); it must not add measurable latency to the success path (FR-2's fallback logic must not execute at all when the top-level `JsonSerializer.Deserialize` succeeds).
- Logging the full raw response (FR-1) only occurs on the failure path and must not be added to the success path.

### NFR-2: Security / data sensitivity
- Meeting transcripts and their extracted tasks routinely contain personal names, and occasionally e-mail addresses, of employees (see `assignee`, `assigneeEmail`, `participants`). Logging the raw LLM response (FR-1) will therefore put this same class of personal data into application logs on the failure path. This is treated as acceptable here because: (a) the equivalent raw transcript content is already persisted at rest in `MeetingTranscript.RawTranscript`/`Summary` in the application database, so this does not introduce a new category of stored personal data, only a new sink (logs/Application Insights) with the org's existing log retention policy; (b) the failure rate (~23% of a low-volume background job) keeps the volume small. No additional redaction/scrubbing is required for this change; flag as an open question if the team's log-retention policy for Application Insights disagrees.
- No new authentication/authorization surface is introduced. The new `TasksExtractionDegraded` flag is exposed through the existing transcript read endpoints/DTOs and inherits their existing access control (`IMeetingAccessGuard`) unchanged.

## Data Model

`MeetingExtractionResult` (application-layer record, `Services/IMeetingTaskExtractor.cs`) gains one field:

```csharp
public record MeetingExtractionResult(
    List<ExtractedTask> Tasks,
    List<string> Participants,
    bool Degraded = false); // true when one or more tasks/participants could not be parsed
```
(Default parameter keeps this a source-compatible, non-breaking change for any other implementers of `IMeetingTaskExtractor`, though `ClaudeMeetingTaskExtractor` is currently the only one.)

`MeetingTranscript` (domain entity, EF-mapped) gains one persisted field:

```csharp
public bool TasksExtractionDegraded { get; set; }
```
Requires a manual EF Core migration per project convention (migrations are applied manually, not automated in deployment — see project facts). Default `false` for existing rows.

The transcript read DTO(s) returned by the meeting-tasks API/controller gain the corresponding `tasksExtractionDegraded: boolean` field, and the frontend `TranscriptDto`/equivalent type in `useMeetingTasks.ts` (currently generated from the OpenAPI spec) picks it up on the next generated-client build.

## API / Interface Design
- No new endpoints. `IMeetingTaskExtractor.ExtractAsync`'s return type gains the `Degraded` field (non-breaking, see Data Model).
- Existing transcript GET/list endpoint(s) used by `MeetingTasksPage.tsx` / `MeetingTaskDetailPage.tsx` gain `tasksExtractionDegraded` in their response payload; regenerate the OpenAPI TypeScript client (`npm run` client-gen step, per `docs/development/api-client-generation.md`) rather than hand-editing generated types.
- `MeetingTaskDetailPage.tsx`: add a warning banner, conditionally rendered on `transcript.tasksExtractionDegraded`, positioned near the existing `TranscriptStatusBadge` (around line 294), pointing the user at the existing "Reimport" button (around line 364) as the recommended next action.
- `MeetingTasksPage.tsx`: add an equivalent lightweight indicator (icon/badge) per affected row, if the existing list rendering has a natural slot for one.

## Dependencies
- No new external services or libraries. Uses `System.Text.Json` (`JsonDocument`, `JsonSerializer`) already in use in this file for the FR-2 fallback parsing.
- Depends on the existing manual EF Core migration process for the new `TasksExtractionDegraded` column.
- Depends on the existing OpenAPI client generation pipeline to propagate the new DTO field to the frontend.
- No change to the `Microsoft.Extensions.AI` / Claude chat client integration itself in this iteration (see Out of Scope).

## Out of Scope
- Switching the LLM call to Anthropic's structured-output / tool-use mode with a JSON schema to eliminate the failure mode at the source. The brief raises this as a longer-term option; it is a larger change (prompt restructuring, tool-call response handling, `Microsoft.Extensions.AI` tool-use plumbing) that should be scoped and evaluated separately once FR-1's logging has surfaced more concrete examples of the malformed output to confirm the root cause and design the schema against.
- Automatic retry of the LLM call on parse failure (e.g., re-prompting Claude to fix its own malformed JSON). Not requested by the brief's next steps and adds cost/latency/complexity; can be considered later if FR-2's partial recovery proves insufficient.
- Any change to `IngestPlaudRecordingHandler`'s or `ReimportMeetingTranscriptHandler`'s control flow beyond reading the new `Degraded` field and setting `TasksExtractionDegraded` on the entity — no new user-facing retry workflow beyond the existing "Reimport" button.
- Alerting/dashboards on the extraction failure rate itself (e.g. an Application Insights alert rule). The brief is about the code-level swallow-and-drop behavior, not about building new monitoring.
- Historical backfill of `TasksExtractionDegraded` for meetings ingested before this change ships — existing rows simply default to `false` (not degraded), even if some historically lost tasks silently.

## Open Questions
None.

## Status: COMPLETE
