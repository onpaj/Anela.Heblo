# Design: Batch `SaveChangesAsync` calls in `PhotobankIndexJob.UpsertPhotoAsync`

## Component Design

All changes are confined to `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`. No new files, no `IPhotobankRepository`/`PhotobankRepository` interface changes.

### `IndexRootAsync(root)` — restructured item loop

Single pass over `delta.Items`, in order, replacing today's per-item `UpsertPhotoAsync` call:

```
GetDeltaAsync ................................ unchanged (already returns full in-memory List)
GetActiveTagRulesAsync ........................ unchanged
walk delta.Items once, in order:
  non-deleted item  -> append to pendingBatch
  deleted item      -> if pendingBatch non-empty: FlushBatch(pendingBatch) first
                        -> GetPhotoBySharePointFileIdAsync + RemovePhotoAsync (unchanged, inline, no flush)
  pendingBatch.Count == BatchSize -> FlushBatch(pendingBatch)
end of items -> FlushBatch(pendingBatch) if any remain
root.DeltaLink / LastIndexedAt ................ unchanged, single SaveChangesAsync at end (line 97)
catch/log/continue ............................ unchanged
```

`private const int BatchSize = 200;` — single named constant, no config/feature-flag surface.

Deletions flush any pending upsert batch first, so a delete's lookup always sees every prior item in the delta as committed (preserves today's implicit ordering guarantee; prevents orphaned rows when an item is upserted then deleted within the same delta).

### `UpsertPhotoBatchAsync` — replaces `UpsertPhotoAsync`

```
UpsertPhotoBatchAsync(IReadOnlyList<GraphPhotoItem> batch, List<TagRule> tagRules, string? driveId, CancellationToken ct)
```

Two phases, each flushed exactly once per batch:

**Phase A — photo upsert (no per-item flush):**
- Maintain a batch-local `Dictionary<string, Photo>` keyed by `SharePointFileId`, checked before falling back to `GetPhotoBySharePointFileIdAsync`. This guards against duplicate rows when the same `SharePointFileId` appears twice as a non-deleted item within one batch (both entries must resolve to the same tracked `Photo` instance).
- For each item: resolve existing photo (cache → repo query) or create via `AddPhotoAsync`; set `FileName`, `FolderPath`, `SharePointWebUrl`, `FileSizeBytes`, `ModifiedAt` (`item.LastModifiedAt ?? DateTime.UtcNow`), `DriveId`; on create also set `SharePointFileId` and `IndexedAt = DateTime.UtcNow`; compute `pathChanged` (`FolderPath`/`FileName` differs from existing) and clear `LastAutoTaggedAt` when true.
- After all items processed: one `SaveChangesAsync()` — every `Photo` in the batch now has a valid DB-assigned `Id`.

**Phase B — tag-rule reapplication (no per-item flush):**
- Pre-resolve tag names for the *whole batch* in one call: union of `TagRuleMatcher.GetMatchingTags(item.FolderPath, item.Name, tagRules)` across all items → `GetOrCreateTagsAsync(IReadOnlyCollection<string>)` once → `name -> tagId` dictionary. Do **not** call the singular `GetOrCreateTagAsync(string)` per item inside this phase — it performs a hidden internal `SaveChangesAsync` on new-tag creation, which would defeat the round-trip reduction and break the "exactly 2 flushes per batch" accounting.
- Per item (using the pre-resolved dictionary, no further tag-resolution repo calls): remove existing `PhotoTagSource.Rule` rows via `RemovePhotoTagsAsync` (no flush); for each matching tag name, add a `PhotoTag` (`Source = PhotoTagSource.Rule`, `CreatedAt = DateTime.UtcNow`) guarded by `PhotoTagExistsAsync` so a `(PhotoId, TagId)` pair already present (including from a non-Rule source) is not duplicated.
- After all items processed: one `SaveChangesAsync()`.

### Call-site guidance

| Concern | Use | Not |
|---|---|---|
| Resolve rule tag names for the batch | `GetOrCreateTagsAsync(IReadOnlyCollection<string>)` once per batch | `GetOrCreateTagAsync(string)` per item |
| Add new `PhotoTag` rows | `AddPhotoTagAsync` per row (no internal flush); bulk `AddPhotoTagsAsync` also acceptable | — |
| Remove stale Rule tags per photo | `RemovePhotoTagsAsync` per item, as today (no internal flush) | — |
| Existing-photo lookup | `GetPhotoBySharePointFileIdAsync` guarded by in-batch `Dictionary<string, Photo>` | Raw per-item call with no batch-local cache |

### Unchanged

- `IPhotobankGraphService.GetDeltaAsync`, `GraphPhotoItem`, `ApplicationDbContext`, `TagRuleMatcher` — no changes.
- Deletion handling (`RemovePhotoAsync`, inline lookup) and root bookkeeping (`root.DeltaLink`, `root.LastIndexedAt`, single `SaveChangesAsync` at end of `IndexRootAsync`) — unchanged, still O(1) round-trips.
- Outer `try`/`catch` in `IndexRootAsync` (catch-log-continue-to-next-root) — unchanged; a failure inside any batch is still caught there.
- `ReapplyRulesHandler`, `RetagPhotosHandler`, other Photobank use cases — untouched.

## Data Schemas

No schema changes. All entities pre-existing and unchanged in shape:

- `Photo` (`SharePointFileId`, `FileName`, `FolderPath`, `SharePointWebUrl`, `FileSizeBytes`, `ModifiedAt`, `DriveId`, `IndexedAt`, `LastAutoTaggedAt`)
- `PhotoTag` (`PhotoId`, `TagId`, `Source`, `CreatedAt`) — composite key `(PhotoId, TagId)`
- `Tag` (`Id`, `Name`)
- `PhotobankIndexRoot` (`DeltaLink`, `LastIndexedAt`, `RootItemId`, `DriveId`, `SharePointPath`)
- `TagRule` (read-only, via `GetActiveTagRulesAsync` / `TagRuleMatcher`)

No external API, controller, or DTO changes — this is an internal background-job method signature change only:

```
// Before
Task UpsertPhotoAsync(GraphPhotoItem item, List<TagRule> tagRules, string? driveId, CancellationToken ct)

// After
Task UpsertPhotoBatchAsync(IReadOnlyList<GraphPhotoItem> batch, List<TagRule> tagRules, string? driveId, CancellationToken ct)
```

Round-trip shape: for a root's delta of N non-deleted items and `BatchSize = 200`, the upsert path produces `2 * ceil(N / BatchSize)` `SaveChangesAsync` calls (at most — an early flush is forced whenever a deleted item interleaves with a pending upsert batch, per the deletion-flush rule above).

### Test impact

`backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` mocks must switch from `GetOrCreateTagAsync` (singular) to `GetOrCreateTagsAsync` (bulk). Add cases for: multiple items in one batch (exactly 2 `SaveChangesAsync` total), a delta larger than `BatchSize` (`2 * ceil(N/BatchSize)` calls), duplicate `SharePointFileId` within one batch (single `Photo` row), and upsert-then-delete for the same item within one delta (photo ends up removed).
