# Specification: Batch `SaveChangesAsync` calls in `PhotobankIndexJob.UpsertPhotoAsync`

## Summary
`PhotobankIndexJob.UpsertPhotoAsync` currently issues two `SaveChangesAsync` round-trips for every non-deleted item returned by the SharePoint Graph delta API, making the nightly Photobank index job O(N) in database round-trips. This spec defines a mechanical refactor that batches those writes so a delta of N items requires roughly `2 * ceil(N / BatchSize)` round-trips instead of `2 * N`, with all existing upsert and tag-rule behavior preserved exactly.

## Background
`PhotobankIndexJob` (`backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`) runs nightly (cron `0 3 * * *`) and on-demand to sync SharePoint photo libraries into the Photobank via the Microsoft Graph delta API. For each active root, it fetches the full delta (`IPhotobankGraphService.GetDeltaAsync`, which already accumulates all pages into a single in-memory `List<GraphPhotoItem>` before returning), then loops over `delta.Items` calling `UpsertPhotoAsync` for each non-deleted item.

`UpsertPhotoAsync` (lines 111–160) does two DB flushes per item:
- Line 138: `await _repo.SaveChangesAsync(ct);` — flushes the `Photo` entity insert/update so a newly created photo receives its DB-assigned `Id` (needed to write `PhotoTag.PhotoId`).
- Line 159: `await _repo.SaveChangesAsync(ct);` — flushes the `PhotoTag` rows added/removed while re-applying rule tags.

For a nightly differential sync (tens of changed items) this is unnoticeable. For an initial index of a large SharePoint library, or a re-index triggered by a root configuration change, the delta can contain tens of thousands of items, and 2N sequential round-trips can turn a job that should take seconds into one that takes many minutes.

This was flagged by the automated architecture-review routine (filed 2026-07-18, `artifacts/feat-3692/brief.md`) as a scoped, mechanical performance defect — not a functional change. The codebase already has an established precedent for batching writes across many photos in the same feature: `ReapplyRulesHandler` (`backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/ReapplyRulesHandler.cs`) resolves and applies rule tags for the entire photo set using exactly two `SaveChangesAsync` calls (one after removing stale rule tags, one after adding new ones), using the repository's existing bulk-friendly methods (`GetOrCreateTagsAsync`, `AddPhotoTagsAsync`, `RemovePhotoTagsBySourceAsync`). This fix brings `PhotobankIndexJob` in line with that pattern for the delta-processing path, without changing its per-item read/query pattern or its upsert/tag semantics.

Deletion handling (`ExecuteAsync`/`IndexRootAsync`, item `IsDeleted == true` branch) and the end-of-root bookkeeping (`root.DeltaLink`, `root.LastIndexedAt`) are already O(1) in round-trips — `RemovePhotoAsync` only marks entities for removal in the EF change tracker, and the single `SaveChangesAsync` at line 97 is the only flush for deletions and root state. These are out of scope; only the upsert path inside `UpsertPhotoAsync` needs to change.

## Functional Requirements

### FR-1: Batch the photo-entity flush
Replace the per-item `SaveChangesAsync` at line 138 with a single flush per batch of delta items being upserted, so that all photo entity inserts/updates in a batch are committed together and all newly created photos receive their DB-assigned `Id` in that one round-trip.

**Acceptance criteria:**
- For a delta batch of B non-deleted items, exactly one `SaveChangesAsync` call flushes all B photo entity upserts (new inserts and field updates), not B calls.
- After this flush, every `Photo` entity touched in the batch (new or existing) has a valid, DB-assigned `Id` available for use in FR-2.
- Field-level upsert semantics are unchanged per item: `FileName`, `FolderPath`, `SharePointWebUrl`, `FileSizeBytes`, `ModifiedAt` (`item.LastModifiedAt ?? DateTime.UtcNow`), and `DriveId` are set exactly as today; a photo not found by `SharePointFileId` is created with `SharePointFileId` and `IndexedAt = DateTime.UtcNow` exactly as today.
- `pathChanged` detection (`FolderPath` or `FileName` differs from the existing row) and the resulting `LastAutoTaggedAt = null` reset are computed and applied per item exactly as today, before the batch flush.

### FR-2: Batch the tag-rule flush
Replace the per-item `SaveChangesAsync` at line 159 with a single flush per batch that commits all `PhotoTag` removals and insertions performed while re-applying rule tags across every item in the batch.

**Acceptance criteria:**
- For a delta batch of B non-deleted items, exactly one `SaveChangesAsync` call flushes all `PhotoTag` changes (removals of stale `PhotoTagSource.Rule` rows and insertions of new ones) for the whole batch, not B calls.
- This flush happens after FR-1's flush completes (so `PhotoTag.PhotoId` values are valid) and after all per-item tag-rule resolution for the batch has been computed.
- Per-item tag-rule semantics are unchanged: existing `PhotoTagSource.Rule` tags for that photo are removed, `TagRuleMatcher.GetMatchingTags(item.FolderPath, item.Name, tagRules)` determines the candidate tag names, each name is resolved via `GetOrCreateTagAsync`/equivalent, and a `PhotoTag` is only added if one for that `(PhotoId, TagId)` pair does not already exist (preserving the existing duplicate guard against non-Rule-sourced tags on the same photo/tag pair) — with `Source = PhotoTagSource.Rule` and `CreatedAt = DateTime.UtcNow`.

### FR-3: Chunk the delta into fixed-size batches
The non-deleted items in `delta.Items` for a root are processed in fixed-size batches (default 200 items per batch) rather than either one-at-a-time (today's behavior) or as a single unbounded batch covering the entire delta.

**Acceptance criteria:**
- A root's delta of N non-deleted items results in `ceil(N / BatchSize)` batches, each producing exactly 2 `SaveChangesAsync` calls (per FR-1/FR-2), for `2 * ceil(N / BatchSize)` total round-trips for the upsert path.
- Batch size is a single named constant in code (no new configuration surface, feature flag, or app setting is introduced for this).
- Item order within `delta.Items` is preserved; batching only changes when writes are flushed, not per-item processing order or outcome.
- Deleted items (`item.IsDeleted == true`) continue to be processed inline in the existing loop, unaffected by upsert batching (per Background, this path is already O(1) in round-trips).

### FR-4: Preserve root-level bookkeeping and error handling
`root.DeltaLink`, `root.LastIndexedAt`, and the surrounding `try`/`catch` in `IndexRootAsync` (catch-log-continue-to-next-root, no rethrow) are unchanged.

**Acceptance criteria:**
- `root.DeltaLink` and `root.LastIndexedAt` are still written and saved only once, after the full item loop for that root completes successfully (line 95–97 today), independent of how many upsert batches ran inside the loop.
- If an exception is thrown while processing any batch, it is still caught by `IndexRootAsync`'s existing `catch (Exception ex)` block, logged via `_logger.LogError`, and processing moves on to the next root — the same as today. No new retry, transaction, or rollback logic is introduced.
- `upserted`/`deleted` counters in the log message at the end of `IndexRootAsync` continue to reflect the actual number of items upserted/deleted, regardless of batch boundaries.

## Non-Functional Requirements

### NFR-1: Performance
- Round-trip reduction: for a delta of N items and batch size B, total `SaveChangesAsync` calls for the upsert path drop from `2N` to `2 * ceil(N / B)`. With the default B = 200, a 10,000-item initial index drops from 20,000 round-trips to 100.
- A typical nightly differential delta (tens of items) fits in a single batch, so it already collapses to 2 round-trips regardless of exact item count — no regression for the common case.
- No new N-scaling read queries are introduced; per-item reads (`GetPhotoBySharePointFileIdAsync`, tag lookups) are unchanged and remain out of scope (see Out of Scope) since the brief identifies the `SaveChanges` pair as the dominant cost.
- Memory: `GetDeltaAsync` already materializes the entire delta into memory before this job sees it, so chunking that in-memory list into batches introduces no new memory-scaling concern; batch size exists solely to bound EF change-tracker/`SaveChanges` payload size per round-trip.

### NFR-2: Reliability / correctness
- Idempotency: because `Photo` upsert keys on `SharePointFileId` and rule-tag application is fully recomputed per item, re-processing any item (including one already committed in an earlier partial run) remains a safe no-op update, exactly as it is today.
- Retry granularity change (expected, not a regression): today, if item K of N throws, items `1..K-1` in that root are already durably committed (per-item `SaveChanges`), while `root.DeltaLink` is not advanced (it's only saved at line 97, after the full loop) — so the next run re-fetches the *same* delta from Graph and re-processes all N items regardless. With batching, a failure inside a batch means that batch's writes are not committed, so on retry the batch's items (not just the failed one) are redone. Since `DeltaLink` was never advanced either way, the *set* of items reprocessed on retry is unchanged (still the full N-item delta); the only difference is which items were durably upserted before the failure interrupted the run — a difference with no observable effect given idempotent upserts. This is an accepted, understood consequence of batching, not an open question.
- Uniqueness constraints (`PhotoTag` composite key on `(PhotoId, TagId)`, `Photo.SharePointFileId` uniqueness assumption) must continue to hold under batched writes exactly as under per-item writes — the duplicate-guard check (FR-2) is what prevents constraint violations and must not be dropped when batching.

## Data Model
No schema changes. Entities involved (all pre-existing, unchanged):
- `Photo` (`SharePointFileId`, `FileName`, `FolderPath`, `SharePointWebUrl`, `FileSizeBytes`, `ModifiedAt`, `DriveId`, `IndexedAt`, `LastAutoTaggedAt`)
- `PhotoTag` (`PhotoId`, `TagId`, `Source`, `CreatedAt`) — composite key on `(PhotoId, TagId)`
- `Tag` (`Id`, `Name`)
- `PhotobankIndexRoot` (`DeltaLink`, `LastIndexedAt`, `RootItemId`, `DriveId`, `SharePointPath`)
- `TagRule` (used read-only via `GetActiveTagRulesAsync` / `TagRuleMatcher`)

## API / Interface Design
This is an internal background-job refactor; there is no external API, controller, or UI surface change.

- `PhotobankIndexJob.IndexRootAsync`: the `foreach (var item in delta.Items)` loop is restructured so non-deleted items are grouped into fixed-size batches (FR-3) before being upserted; deleted items keep their current inline handling.
- `PhotobankIndexJob.UpsertPhotoAsync(GraphPhotoItem, ...)` (single-item, lines 111–160) is replaced by a batch-oriented method, e.g. `UpsertPhotoBatchAsync(IReadOnlyList<GraphPhotoItem> batch, List<TagRule> tagRules, string? driveId, CancellationToken ct)`, structured in two phases:
  1. **Phase A (per item, no flush):** resolve existing photo via `GetPhotoBySharePointFileIdAsync`, create via `AddPhotoAsync` if missing, set fields, compute `pathChanged` → conditionally clear `LastAutoTaggedAt`. After all items in the batch are processed this way, call `SaveChangesAsync` once (FR-1).
  2. **Phase B (per item, no flush):** using the now-populated `Photo.Id` values, fetch and remove existing `PhotoTagSource.Rule` rows, compute matching tag names via `TagRuleMatcher`, resolve/create tags, and add new `PhotoTag` rows guarded by the existing duplicate check. After all items in the batch are processed this way, call `SaveChangesAsync` once (FR-2).
- No changes to `IPhotobankRepository` or `PhotobankRepository` are required — all methods used (`GetPhotoBySharePointFileIdAsync`, `AddPhotoAsync`, `GetPhotoTagsByPhotoAndSourceAsync`, `RemovePhotoTagsAsync`, `GetOrCreateTagAsync`, `PhotoTagExistsAsync`, `AddPhotoTagAsync`, `SaveChangesAsync`) already exist and are simply invoked with different batching/ordering.
- No changes to `IPhotobankGraphService`, `GraphPhotoItem`, `ApplicationDbContext`, or any other Photobank handler (`ReapplyRulesHandler`, `RetagPhotosHandler`, etc.).

## Dependencies
- Existing `IPhotobankRepository` / `PhotobankRepository` (EF Core `ApplicationDbContext`) — no interface changes needed.
- Existing `IPhotobankGraphService.GetDeltaAsync` — unchanged; already returns the full delta as an in-memory list.
- Existing `TagRuleMatcher` — unchanged.
- No new external libraries, services, or infrastructure.

## Out of Scope
- Batching or otherwise optimizing the per-item **read** queries (`GetPhotoBySharePointFileIdAsync`, `GetOrCreateTagAsync`, `PhotoTagExistsAsync`, `GetPhotoTagsByPhotoAndSourceAsync`) — the brief explicitly identifies the paired `SaveChangesAsync` calls as the dominant cost; read-side batching is a candidate for a future, separate improvement.
- Making batch size configurable via app settings, environment variable, or feature flag — a fixed in-code constant is sufficient for this fix.
- Parallelizing batch processing within a root, or parallelizing across roots — processing remains sequential.
- Any change to the job's cron schedule, enable/disable mechanism (`IRecurringJobStatusChecker`), or logging format beyond what's needed to keep existing log statements accurate.
- Any change to deletion handling, root bookkeeping (`DeltaLink`/`LastIndexedAt`), or the outer `try`/`catch` semantics in `IndexRootAsync` — these are already O(1) and are preserved as-is.
- Any change to `ReapplyRulesHandler`, `RetagPhotosHandler`, or other Photobank use cases that already use batch-friendly repository methods.
- Database schema/migration changes.

## Open Questions
None.

## Status: COMPLETE
