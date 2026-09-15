# Specification: Photobank Batch Tag Upsert N+1 Query Fix

## Summary
`PhotobankIndexJob.UpsertPhotoBatchAsync` currently issues two database round-trips per photo (and per photo/tag pair) inside its per-photo loop when reconciling Rule-source tags, producing O(photos × rules) queries per nightly batch. This specification defines a bulk-preload approach — mirroring the existing `ReapplyRulesHandler` pattern — that reduces the tag-reconciliation phase to a constant number of queries per batch, independent of batch size or rule count.

## Background
`PhotobankIndexJob` runs nightly at 03:00 UTC and processes photos in batches (observed batch size: 200). Inside `UpsertPhotoBatchAsync` (lines 240–261 of `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`), for each photo in the batch the code:
1. Calls `GetPhotoTagsByPhotoAndSourceAsync(photo.Id, PhotoTagSource.Rule, ct)` to load that photo's existing Rule-source tags before reconciling them.
2. Calls `PhotoTagExistsAsync(photo.Id, tagId, ct)` once per candidate tag to avoid inserting a duplicate `(PhotoId, TagId)` pair.

This yields up to `200 + 200×N` extra round-trips per batch, where N is the average number of matching tag rules per photo. A large backlog (e.g., after a SharePoint reorganisation) can produce dozens of such batches in a single run, multiplying the cost.

The codebase already solves the identical problem elsewhere: `ReapplyRulesHandler` bulk-loads all non-Rule `(PhotoId, TagId)` pairs in one query via `GetOccupiedTagPairsAsync` and performs O(1) in-memory `HashSet` lookups thereafter. `PhotobankIndexJob` contains a comment (`// mirrors ReapplyRulesHandler`) indicating intent to follow this pattern, but the per-photo N+1 calls were never replaced — the mirroring is incomplete.

## Functional Requirements

### FR-1: Bulk-preload existing Rule-source tags for the batch
Before the per-photo loop in `UpsertPhotoBatchAsync`, issue a single query that loads all existing Rule-source tags for every photo ID in the current batch (`photosByFileId.Keys`), replacing the per-photo call to `GetPhotoTagsByPhotoAndSourceAsync`.
**Acceptance criteria:**
- Exactly one repository call retrieves Rule-source tags for the whole batch, regardless of batch size.
- The result is organized (e.g., grouped by `PhotoId`) so per-photo lookup inside the loop is an in-memory operation, not a query.
- Behavior (which tags are considered "existing Rule-source tags for removal/reconciliation") is unchanged from the current per-photo implementation.

### FR-2: Bulk-preload occupied (non-Rule) tag pairs for duplicate-guard checks
Before the per-photo loop, issue a single query that loads all non-Rule `(PhotoId, TagId)` pairs for the batch's photo IDs (reusing the existing `GetOccupiedTagPairsAsync` method already available on `IPhotobankPhotoTagRepository`, per the `ReapplyRulesHandler` pattern), and place them into an in-memory `HashSet<(Guid PhotoId, Guid TagId)>` (or equivalent), replacing the per-pair call to `PhotoTagExistsAsync`.
**Acceptance criteria:**
- Exactly one repository call retrieves occupied tag pairs for the whole batch, regardless of the number of photo/tag combinations evaluated.
- Duplicate-insert guarding inside the loop is a constant-time `HashSet` lookup with no additional queries.
- Duplicate-guard behavior (which pairs are treated as "already existing, do not re-insert") is unchanged from the current per-pair implementation.

### FR-3: Preserve existing tag-reconciliation and insert/delete semantics
The set of tags added, removed, or left untouched for each photo after the change must be identical to what the current (N+1) implementation produces for the same input data.
**Acceptance criteria:**
- For a given batch of photos, tag rules, and pre-existing tags, the final tag state per photo after `UpsertPhotoBatchAsync` completes is unchanged compared to before this fix.
- No change to the public behavior of `PhotobankIndexJob` as observed by downstream consumers (photo tag data, job success/failure, logs) other than fewer database round-trips.

## Non-Functional Requirements

### NFR-1: Performance
- The number of database round-trips issued by the tag-reconciliation phase of `UpsertPhotoBatchAsync` for one batch must not scale with batch size or number of matching rules — it must be O(1) additional queries per batch (in addition to whatever queries already exist outside the per-photo loop), matching the query profile of `ReapplyRulesHandler`.
- No regression in per-batch wall-clock time is acceptable; the change is expected to reduce it, especially for large nightly backlogs of many 200-photo batches.

### NFR-2: Security
- No new attack surface: all queries operate on a bounded set of photo IDs already validated/owned by the current batch (`photosByFileId.Keys`), no new external input is introduced.
- No change to authorization, data access boundaries, or data sensitivity — this is an internal performance refactor of existing repository calls scoped identically to today's per-photo calls.

## Data Model
No schema changes. This is a query-pattern change against existing entities:
- `PhotobankPhotoTag` (or equivalent) rows keyed by `(PhotoId, TagId)`, with a `Source` discriminator (`Rule` vs. non-`Rule`).
- The two repository methods involved:
  - `GetPhotoTagsByPhotoAndSourceAsync(photoId, source, ct)` on `IPhotobankPhotoTagRepository` — currently called per photo; needs a batch-scoped counterpart (e.g., `GetPhotoTagsByPhotosAndSourceAsync(photoIds, source, ct)`) or an equivalent bulk query.
  - `PhotoTagExistsAsync(photoId, tagId, ct)` on `IPhotobankPhotoTagRepository` — currently called per (photo, tag) pair; replaced by the existing `GetOccupiedTagPairsAsync` bulk method (already used by `ReapplyRulesHandler`) plus in-memory `HashSet` lookups.

## API / Interface Design
- `IPhotobankPhotoTagRepository` gains (or extends) a batch-scoped query method for Rule-source tags across a set of photo IDs, e.g.:
  `Task<IReadOnlyDictionary<Guid, IReadOnlyList<PhotoTag>>> GetPhotoTagsByPhotosAndSourceAsync(IEnumerable<Guid> photoIds, PhotoTagSource source, CancellationToken ct)`
  (exact name/shape to be finalized by the architect/design phase, mirroring existing repository conventions.)
- `GetOccupiedTagPairsAsync` (already present on `IPhotobankPhotoTagRepository`, per `ReapplyRulesHandler` usage at line 56 of `PhotobankPhotoTagRepository.cs`) is reused as-is, scoped to the current batch's photo IDs, rather than added new.
- `PhotobankIndexJob.UpsertPhotoBatchAsync`'s internal control flow changes: bulk-load both datasets once before the per-photo loop, then use dictionary/`HashSet` lookups inside the loop in place of the two per-photo/per-pair repository calls. No change to the method's public signature or to `PhotobankIndexJob`'s external contract (job trigger, schedule, batch size).

## Dependencies
- `IPhotobankPhotoTagRepository` / `PhotobankPhotoTagRepository` (existing) — the two query methods described above.
- `ReapplyRulesHandler` — the existing reference implementation this change intentionally mirrors; no changes to `ReapplyRulesHandler` itself are in scope.
- No external services, libraries, or feature flags are required.

## Out of Scope
- Changing the nightly job's schedule, batch size, or triggering mechanism.
- Any change to `ReapplyRulesHandler` itself.
- Broader refactors of `PhotobankIndexJob` beyond the tag-reconciliation phase described here.
- Database migrations or index changes (if profiling during implementation reveals a need for a supporting index, that is a candidate for architect follow-up, not part of this spec's required scope).

## Open Questions
None.

## Status: COMPLETE
