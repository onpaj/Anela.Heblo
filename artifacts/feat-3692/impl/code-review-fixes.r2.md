# Implementation: code-review-fixes (r2)

## What was implemented
Fixed the Phase B "poison item" bug in `PhotobankIndexJob.UpsertPhotoBatchAsync`. When the
same `SharePointFileId` appeared twice as a non-deleted item in one batch, the per-item
"remove existing rule tags, add new ones" loop iterated over `itemMatches` — one entry per
raw item — and its DB-reading checks (`GetPhotoTagsByPhotoAndSourceAsync`,
`PhotoTagExistsAsync`) couldn't see the other occurrence's not-yet-flushed changes within the
same batch. This left stale tags behind when the two occurrences matched different tag rules,
or crashed `SaveChangesAsync` with a composite-primary-key violation when they matched the
same tag rule.

The fix collapses `itemMatches` into a `Dictionary<Photo, IReadOnlyList<string>>` keyed by the
shared `Photo` instance (reference equality, same pattern as Phase A's `photosByFileId`
last-write-wins collapse) before the tag-application loop runs. Each distinct `Photo` in the
batch is now processed exactly once, using the matched tag names from the *last* item in
`batch` that maps to it — matching the acceptance criteria and mirroring Phase A's dedup
semantics.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` — in `UpsertPhotoBatchAsync`, added a `tagNamesByPhoto` dedup step (keyed by `Photo` reference, last-write-wins) between building `itemMatches`/resolving tag IDs and the tag removal/application loop; the loop now iterates `tagNamesByPhoto` instead of `itemMatches`.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` — added two regression tests (see below).

## Tests
- `UpsertPhotoBatch_DuplicateSharePointFileIdMatchingDifferentTagRules_OnlyLastItemsTagsSurvive` — two items share `SharePointFileId = "file-dup"`, with different `FolderPath`/`Name` matching two different tag rules (`Fotky/A` → `tag-a`, `Fotky/B` → `tag-b`). Asserts `GetPhotoTagsByPhotoAndSourceAsync`/`RemovePhotoTagsAsync`/`AddPhotoTagAsync` are each called exactly once (once per distinct photo, not per raw item), and that the single applied tag is `tag-b` (id 2) — the last item's match, not the first's.
- `UpsertPhotoBatch_DuplicateSharePointFileIdMatchingSameTagRule_AppliesTagOnceWithNoDuplicateKeyRisk` — two items share `SharePointFileId = "file-dup"` and both match the same tag rule (`common`, id 99). `PhotoTagExistsAsync` is mocked to always return `false` (simulating that it can never see the batch's own unflushed insert, as in production). Asserts `AddPhotoTagAsync` for `TagId == 99` is called exactly once — proving the fix does not attempt to insert the same `(PhotoId, TagId)` pair twice, which would violate `PhotoTag`'s composite primary key on a real `SaveChangesAsync`.

Both existing tests (`UpsertPhotoBatch_DuplicateSharePointFileIdWithinOneBatch_ResultsInSinglePhotoRow` for Phase A, and all others) continue to pass unchanged in intent.

## How to verify
```
cd /home/user/worktrees/feature-3692-Arch-Review-Photobank-Photobankindexjob-Calls-Save
dotnet build Anela.Heblo.sln 2>&1 | tail -20
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankIndexJobTests" --no-build -v minimal 2>&1 | tail -30
```
Result: build succeeds with 0 errors (pre-existing nullable warnings only, unrelated to this change). All 11 `PhotobankIndexJobTests` pass (9 pre-existing + 2 new).

`dotnet format Anela.Heblo.sln --verify-no-changes --include <the two changed files>` reports no formatting issues.

## Notes
No deviations from the prescribed fix approach. `Photo` is a plain class with no `Equals`/`GetHashCode` override, so using it as a `Dictionary` key relies on (and correctly gets) reference equality — the same instance is shared across duplicate `SharePointFileId` occurrences via Phase A's `photosByFileId` cache, so the dedup keys correctly collapse to one entry per distinct photo.

## Status
DONE
