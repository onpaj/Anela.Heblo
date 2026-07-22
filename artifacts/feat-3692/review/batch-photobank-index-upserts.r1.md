# Code Review: batch-photobank-index-upserts

## Summary
The implementation replaces `PhotobankIndexJob`'s per-item upsert/flush loop with a batched accumulate/flush design exactly as specified in the task context, and independently verified against `git show 389a52c` matches the prescribed code and test-mock changes verbatim. All three load-bearing correctness decisions from the architecture review (bulk `GetOrCreateTagsAsync`, flush-before-delete, batch-local `Photo` cache) are present and correctly wired. Build succeeds with 0 errors and the filtered test run passes 5/5.

## Review Result: PASS

### task: batch-photobank-index-upserts
**Status:** PASS

## Verification performed
- Read task context (`batch-photobank-index-upserts.md`), implementation summary (`.r1.md`), and architecture review (`arch-review.r1.md`).
- Read the actual diff via `git show 389a52c --stat` and `git show 389a52c` in the worktree — confirms:
  - `PhotobankIndexJob.cs`: `BatchSize = 200` constant added; outer loop accumulates non-deleted items into `pendingBatch`, flushing at `BatchSize`, at end-of-items, and (critically) before processing a deleted item; `UpsertPhotoAsync` replaced by `UpsertPhotoBatchAsync` with Phase A (photo upsert via `photosByFileId` batch-local dictionary cache, single `SaveChangesAsync`) and Phase B (per-item `TagRuleMatcher.GetMatchingTags`, union collected into `allMatchingTagNames`, resolved once via bulk `GetOrCreateTagsAsync`, then per-item `PhotoTag` add/remove, single `SaveChangesAsync`). Matches the task-context's Step 3 code block exactly.
  - `PhotobankIndexJobTests.cs`: both `ExecuteAsync_InsertsNewPhoto_WithRuleTagsApplied` and `UpsertPhoto_WhenTagAlreadyExists_SkipsInsert` mocks changed from `GetOrCreateTagAsync("produkty", ...)` to `GetOrCreateTagsAsync(It.IsAny<IReadOnlyCollection<string>>(), ...)` returning `{"produkty": 42}`. No other lines in either test changed, per instructions.
- Cross-checked the three architecture-review decisions against the diff:
  - **Decision 1** (bulk tag resolution, not per-item `GetOrCreateTagAsync`): confirmed — Phase B calls `GetOrCreateTagsAsync` once per batch on the union of matched tag names.
  - **Decision 2** (flush pending batch before a delete): confirmed — the `item.IsDeleted` branch flushes `pendingBatch` first, preserving the invariant that a delete always sees prior items in the delta as committed.
  - **Decision 3** (batch-local `Dictionary<string, Photo>` keyed by `SharePointFileId`): confirmed — `photosByFileId` is checked before falling back to `GetPhotoBySharePointFileIdAsync`, and both Photo entities and tag application reuse the same tracked instance for repeated IDs within a batch.
- Ran `dotnet build Anela.Heblo.sln` in the worktree: **Build succeeded, 0 Errors** (251 pre-existing warnings unrelated to this change, e.g. nullable-reference warnings in unrelated test files).
- Ran `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankIndexJobTests" --no-build -v minimal`: **Passed! Failed: 0, Passed: 5, Skipped: 0** — matches the task context's expected output exactly.
- Implementation summary claims are accurate: it does not overclaim (correctly notes new batching-specific test cases are deferred to the follow-up task `add-photobank-index-batch-tests`, consistent with the task context's stated scope).

## Docs to Update
None. This is an internal background-job refactor with no new public contract, DTO, API surface, or config/feature-flag surface (confirmed by the architecture review's "Skip Design: true" and the task's own scope). No doc updates required.

## Overall Notes
- Scope discipline is good: only the two files specified were touched, and only the two named test methods' mocks were changed within the test file — no unrelated cleanup or drive-by changes.
- The deviation from the original "2 * ceil(N/BatchSize)" round-trip formula when deletes interleave with upserts (extra flushes forced by Decision 2) was explicitly called out and accepted in the architecture review's Specification Amendments section, so it is not a regression against spec intent.
- New test coverage for the batching behavior itself (multi-item same-batch, >BatchSize deltas, duplicate-SharePointFileId-in-batch, upsert-then-delete-in-same-delta) is intentionally deferred to the dependent task `add-photobank-index-batch-tests`, as stated in both the task context and the implementation summary — this is expected, not a gap in this task.
