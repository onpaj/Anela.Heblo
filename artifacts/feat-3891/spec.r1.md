# Specification: Validate LLM-returned PhotoId against the batch before applying auto-tags

## Summary
`PhotobankAutoTagJob.ApplyTagsForPhotoAsync` applies AI-sourced tags using the `id` field parsed straight out of the LLM's JSON response, with no check that the id belongs to the batch of photo ids that was actually sent to the model in that call. Because `PhotoId` is a real foreign key into `Photos` and ids are small sequential integers, a hallucinated or (via a crafted `FolderPath`/`FileName`) maliciously-influenced id can silently mistag a real, unrelated photo. This change adds a validation step that drops (and logs) any LLM result whose `id` is not a member of the batch actually sent, before any tags for that result are applied.

## Background
`PhotobankAutoTagJob` runs nightly (`CronExpression = "0 4 * * *"`) and unattended, and is also invoked ad hoc via `ExecuteForPhotosAsync` (e.g. retagging). For each batch of photos:

1. `ProcessBatchAsync` computes `batchIds` (the ids of the photos in the batch) and builds a prompt listing `id={p.Id} path={p.FolderPath}/{p.FileName}` for each candidate.
2. The LLM's raw text response is parsed into `AutoTagLlmPayload.Results`, a list of `AutoTagResult { Id, Tags }`.
3. `ApplyTagsForPhotoAsync` validates `Tags` against the known tag vocabulary (`tagsByName`) but uses `result.Id` verbatim as `PhotoTag.PhotoId` — it is never checked against `batchIds`.
4. `batchIds` is used only for `StampAutoTaggedAtAsync(batchIds, ...)` at the end of `ProcessBatchAsync`.

`FolderPath`/`FileName` (embedded directly into the prompt in `BuildUserPrompt`) originate from SharePoint and are controlled by anyone with upload access to the configured drive — a plausible prompt-injection vector. Independent of adversarial intent, LLM id hallucination/miscopying is also a routine failure mode. Either way, since `PhotoId` has a real FK to `Photos`, an id belonging to a different, real photo passes `SaveChangesAsync` without error and silently applies an AI tag to the wrong photo. Because the job is unattended and nightly, such mistagging has no built-in detection and accumulates over time.

## Functional Requirements

### FR-1: Reject LLM results whose id is outside the sent batch
Before `ApplyTagsForPhotoAsync` is invoked for a given `AutoTagResult`, the result's `Id` must be checked for membership in the set of photo ids that were actually sent to the model for that call (`batchIds` in `ProcessBatchAsync`, or the equivalent set of candidate ids in `ExecuteForPhotosAsync`/batch-splitting there).

**Acceptance criteria:**
- If `result.Id` is a member of the batch's id set, tag application proceeds unchanged (existing behavior/tests continue to pass).
- If `result.Id` is not a member of the batch's id set, no `PhotoTag` row is written for that result, and `ApplyTagsForPhotoAsync` (or its caller) is not invoked with that result, or is invoked with a filtered result.
- A rejected result is logged (warning or higher) with enough context to investigate: the out-of-batch id and, if feasible, the batch's own ids or size. Do not log full prompt/response text (may contain large payloads); the id(s) are sufficient.
- Rejection of one result in a batch does not affect processing of other, valid results in the same batch.
- `StampAutoTaggedAtAsync(batchIds, ...)` behavior is unchanged — all photos actually sent in the batch are still stamped as processed regardless of whether the LLM returned a valid, rejected, or no result for them (unchanged from current behavior; out of scope to alter which ids get stamped).

### FR-2: Cover both call paths
Both `ExecuteAsync` → `ProcessBatchAsync` (the nightly scheduled run) and `ExecuteForPhotosAsync` (ad hoc retagging) must apply the same validation, since both ultimately funnel through `ProcessBatchAsync`.

**Acceptance criteria:**
- A single shared validation point (inside `ProcessBatchAsync` or `ApplyTagsForPhotoAsync` itself, taking the batch id set as a parameter) is used by both entry points — no duplicated validation logic per call path.

### FR-3: Preserve existing tag-vocabulary validation and cap behavior
The existing behavior — filtering `Tags` against `tagsByName`, deduplicating, and capping at `_options.MaxTagsPerPhoto` — must be unaffected by this change.

**Acceptance criteria:**
- Existing passing tests (`PhotobankAutoTagJobTests`) continue to pass without modification to their assertions on tag filtering/capping.

## Non-Functional Requirements

### NFR-1: Performance
Validation must be O(1) average-case per result (e.g. a `HashSet<int>` built once per batch from `batchIds`), not a linear scan repeated per result. Batch sizes are bounded by `_options.BatchSize` (already small, existing default), so this is not a hot-path concern, but the implementation should not introduce quadratic behavior.

### NFR-2: Security
This is itself a security/data-integrity hardening fix: it closes an FK-scoped write that is currently reachable via untrusted, externally-controllable input (SharePoint file/folder names feeding LLM prompt injection) or by ordinary LLM hallucination. No new external inputs or attack surface are introduced by the fix itself.

### NFR-3: Observability
The dropped-result log entry must be distinguishable from ordinary informational logging (i.e., use a level ≥ Warning) so it can be alerted on or spotted during log review, since the job runs unattended.

## Data Model
No schema changes. `PhotoTag { PhotoId (FK -> Photos), TagId, Source, CreatedAt }` is unchanged. `PhotoAutoTagCandidate(int Id, string FolderPath, string FileName)` (existing domain record) already carries the ids that constitute the trusted batch set — no new type is strictly required, though an internal `HashSet<int>` derived from it is expected.

## API / Interface Design
No public API, controller, or MediatR contract changes. This is an internal fix confined to `PhotobankAutoTagJob` (and, if the validation is factored out, a private/internal helper within that class). No changes to `IPhotobankAutoTagRepository`, `IPhotobankPhotoTagRepository`, or any DTOs.

## Dependencies
None beyond what `PhotobankAutoTagJob` already depends on (`ILogger<PhotobankAutoTagJob>` for the new log entry). No new packages or services.

## Out of Scope
- Changing what gets stamped by `StampAutoTaggedAtAsync` (which ids are marked as processed) — unchanged.
- Broader prompt-injection hardening of `BuildSystemPrompt`/`BuildUserPrompt` (e.g. sanitizing `FolderPath`/`FileName` before embedding them in the prompt) — a plausible follow-up but not required to close this specific vulnerability, since the fix here is a structural allow-list on the write path regardless of how the untrusted id was produced.
- Retroactively auditing/cleaning up any `PhotoTags` rows that may already have been mistagged by prior runs of this job.
- Alerting/metrics infrastructure beyond a log line (e.g. dashboards, paging) — out of scope; the log entry is the deliverable for this fix.
- Changing the LLM response schema/prompt to ask the model to only return ids from the batch — the fix must not rely on model compliance; it enforces the invariant server-side regardless of what the model claims.

## Open Questions
None.

## Status: COMPLETE
