## Goal
Fix the code review findings below

## Blocking findings from code-review.r1.md

- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs:200-221` — Phase B's per-item "remove stale rule tags, then add new ones" logic reads `GetPhotoTagsByPhotoAndSourceAsync(photo.Id, ...)` and checks `PhotoTagExistsAsync(photo.Id, tagId, ...)` directly against the database, not against the DbContext's local/pending change-tracker state. Within Phase B, `PhotoTag` rows added via `AddPhotoTagAsync` for earlier items in the same batch are only tracked in memory — the single `SaveChangesAsync` for the whole batch hasn't run yet. So when the *same* `SharePointFileId` appears twice as a non-deleted item in one batch, Phase B breaks in two ways:
  1. If the two occurrences match *different* tag names, the first occurrence's freshly-added (but unflushed) `PhotoTag` is invisible to the second occurrence's `existingRuleTags` query, so it is never removed. Both tags end up persisted, even though only the tags recomputed from the *last* processed state of that photo should survive (matching today's per-item-flush semantics).
  2. If the two occurrences match the *same* tag name, `PhotoTagExistsAsync` also can't see the first occurrence's unflushed insert, so the loop calls `AddPhotoTagAsync` a second time for the same `(PhotoId, TagId)` pair — `PhotoTag`'s primary key is the composite `(PhotoId, TagId)`, so the batch's `SaveChangesAsync` throws a primary-key-violation `DbUpdateException`. Since `root.DeltaLink` only advances after the whole item loop succeeds, and the exception is swallowed by `IndexRootAsync`'s catch-log-continue block, the *next* run re-fetches the identical delta and hits the identical crash — a poison-item condition that permanently blocks that root's indexing.

## Fix

Dedupe `itemMatches` by `Photo.Id` (or by `SharePointFileId`) before the Phase B loop, so each distinct photo's *final* matched tag set is computed and applied exactly once per batch — using the last-seen item's data for that photo (mirroring Phase A's `photosByFileId` last-write-wins collapse). Do NOT process tag application once per raw item in `batch`; process it once per distinct `Photo` in the batch.

Add a new test case (in the same style as the existing `UpsertPhotoBatch_*` tests in `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs`) that exercises the tag-application path for a duplicate `SharePointFileId` within a batch: two non-deleted items with the same `SharePointFileId` but different `FolderPath`/`Name` that match *different* tag rules (so before the fix, both tags would incorrectly survive; after the fix, only the tags matching the second/last item's folder should be applied). Also verify `AddPhotoTagAsync`/`SaveChangesAsync` aren't called in a way that would throw a duplicate-key error when both occurrences match the *same* tag rule.

## Acceptance criteria
- `dotnet build Anela.Heblo.sln` succeeds.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankIndexJobTests" --no-build -v minimal` passes, including the new regression test(s).
- The existing `UpsertPhotoBatch_DuplicateSharePointFileIdWithinOneBatch_ResultsInSinglePhotoRow` test (Phase A) and all other existing tests still pass unchanged in intent.
