# Implementation: batch-photobank-index-upserts

## What was implemented
Replaced `PhotobankIndexJob`'s per-item `UpsertPhotoAsync` (2 `SaveChangesAsync` calls per delta item) with a batched `UpsertPhotoBatchAsync` that accumulates up to `BatchSize = 200` non-deleted items before flushing, reducing DB round-trips from `2*N` to `~2*ceil(N/BatchSize)`. The outer loop accumulates non-deleted items into a `pendingBatch`; when a deleted item is encountered, any pending batch is flushed first (preserving the invariant that a delete always observes every prior item in the delta as committed, avoiding orphaned rows). `UpsertPhotoBatchAsync` does two phases per batch, each ending in a single `SaveChangesAsync`: Phase A upserts `Photo` entities (using a batch-local `Dictionary<string, Photo>` keyed by `SharePointFileId` to avoid creating duplicate rows when the same file ID appears twice in one batch), Phase B pre-resolves all matching tag names for the whole batch via the bulk `GetOrCreateTagsAsync` (instead of the singular `GetOrCreateTagAsync`, which has a hidden internal `SaveChangesAsync` on new-tag creation) and then applies/removes `PhotoTag` rows per item.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` — batched upsert rewrite (accumulate/flush loop, `UpsertPhotoBatchAsync`, `BatchSize` constant)
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` — updated existing mock setups from `GetOrCreateTagAsync` to `GetOrCreateTagsAsync`

## Tests
`backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` — existing 5 tests updated to mock the bulk tag-resolution call; all pass unchanged otherwise (single-item scenarios are unaffected by batch-size math). New multi-item/edge-case tests are covered by the separate `add-photobank-index-batch-tests` task.

## How to verify
```
cd backend
dotnet build Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankIndexJobTests" --no-build
```
Result: build succeeds; 5/5 tests pass.

## Notes
No deviations from the architecture review / task plan. `IPhotobankRepository`/`PhotobankRepository` already exposed `GetOrCreateTagsAsync` (bulk) — no interface changes were needed.

## PR Summary
`PhotobankIndexJob.UpsertPhotoAsync` was calling `SaveChangesAsync` twice per delta item (2N DB round-trips for N items), which scales poorly for large initial syncs. This change batches upserts into groups of up to 200 items, cutting round-trips to roughly `2*ceil(N/BatchSize)`. Deletions still flush any pending batch first so they always see prior items as committed, and a batch-local cache plus bulk tag resolution prevent two correctness regressions (duplicate rows for repeated file IDs in one batch, and a hidden per-item flush inside the old tag-resolution call) that a naive batching pass would have introduced.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` — batch the upsert path
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` — update mocks for bulk tag resolution

## Status
DONE
