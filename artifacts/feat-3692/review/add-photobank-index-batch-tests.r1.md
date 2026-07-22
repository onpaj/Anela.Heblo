# Code Review: add-photobank-index-batch-tests

## Summary
The implementation appends exactly the 4 test methods specified in the task plan, verbatim, to `PhotobankIndexJobTests.cs`, with no production code changes (correct, since the prior `batch-photobank-index-upserts` task already implemented the batching). Build succeeds and all 9 tests (5 pre-existing + 4 new) pass. Each new test's Moq setup and assertions were traced through the actual `PhotobankIndexJob` logic and genuinely exercise the claimed scenario rather than passing vacuously.

## Review Result: PASS

### task: add-photobank-index-batch-tests
**Status:** PASS

## Verification performed

- **Diff vs. plan:** `git show 7b8cf6e` shows a pure addition of 364 lines to `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs`, matching the 4 test methods specified in `task-plan.r1.md` character-for-character. No production file touched, consistent with the impl summary's claim.
- **Build:** `dotnet build Anela.Heblo.sln` → `0 Error(s)` (251 pre-existing warnings unrelated to this change).
- **Targeted tests:** `dotnet test ... --filter "FullyQualifiedName~PhotobankIndexJobTests" --no-build` → `Passed! - Failed: 0, Passed: 9, Skipped: 0` — matches the task's expected output exactly.

## Scenario-by-scenario correctness check

- **`UpsertPhotoBatch_MultipleItemsInSameBatch_...`:** Both items match the single active `TagRule` (`Fotky/Produkty`), so `GetOrCreateTagsAsync` is called once (bulk, for the union of tag names), `AddPhotoTagAsync` is called twice (once per item), and `SaveChangesAsync` is called 3 times (Phase A + Phase B + root bookkeeping). These follow directly from the production code's control flow — not tautological, since a non-batched (per-item) implementation would produce 2×(2+2)+1 = 9 `SaveChangesAsync` calls, which would fail this assertion.
- **`UpsertPhotoBatch_DeltaLargerThanBatchSize_...`:** 201 non-deleted items with zero active tag rules forces exactly 2 batches (200 + 1) under `BatchSize = 200`. Asserts `SaveChangesAsync` called exactly 5 times (2 batches × 2 flushes + 1 root bookkeeping) and `GetOrCreateTagsAsync` never called (empty tag-name union). This is the correct discriminator for chunking behavior — a single-batch (unbounded) implementation would yield 3, not 5.
- **`UpsertPhotoBatch_DuplicateSharePointFileIdWithinOneBatch_...`:** `GetPhotoBySharePointFileIdAsync("file-dup", ...)` is stubbed to unconditionally return `null` (not lazy). Item 1 (cache miss) triggers `AddPhotoAsync` + caches the new `Photo` keyed by `ItemId`. Item 2 (same `ItemId`) hits the batch-local cache before ever re-querying the repo, so it mutates the *same* tracked instance instead of creating a second one. The assertions (`AddPhotoAsync` `Times.Once`, `GetPhotoBySharePointFileIdAsync` `Times.Once`, and the captured photo's fields reflecting item2's later values) genuinely fail without the batch-local `Dictionary<string, Photo>` cache — without it, item 2 would re-query the always-null-returning mock and create a second `Photo`, violating both count assertions. This is a real, non-trivial test of the dedup mechanism.
- **`UpsertPhotoBatch_UpsertThenDeleteSameItemInSameDelta_...`:** `GetPhotoBySharePointFileIdAsync("file-x", ...)` is stubbed lazily (`ReturnsAsync(() => capturedPhoto)`), returning whatever `capturedPhoto` currently holds. On the upsert-item's Phase-A lookup (`capturedPhoto` still null) it returns null → new `Photo` created → callback sets `capturedPhoto`. Because `IndexRootAsync` flushes the pending batch (which runs Phase A/B and thus the `AddPhotoAsync` callback) *before* processing the following delete item, the delete's lookup then observes the non-null `capturedPhoto` and calls `RemovePhotoAsync`. If the flush-before-delete ordering were broken (delete processed first, or batch not flushed until end of the whole delta), the delete's lookup would still see `capturedPhoto == null` and `RemovePhotoAsync` would never fire, failing the `Times.Once` assertion. This test correctly exercises ordering, not just "no throw."

All four `SaveChangesAsync`/count expectations match the task-plan's stated math (`2 * ceil(N/BatchSize) + 1`), and the numeric assertions in the diff are identical to those specified in the plan.

## Docs to Update
None — this is a test-only change; no user-facing behavior or public API changed.

## Overall Notes
Implementation is a faithful, unmodified transcription of the task plan's prescribed test code. Verified independently (not just via the impl summary) that the diff matches, the build is clean, and the assertions are semantically load-bearing against the actual `PhotobankIndexJob`/`UpsertPhotoBatchAsync` control flow described in the plan and read from the production source referenced by the plan.
