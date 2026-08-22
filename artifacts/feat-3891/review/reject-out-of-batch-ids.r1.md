# Code Review: reject-out-of-batch-ids

## Summary
The implementation matches the task specification exactly: `ProcessBatchAsync` builds a `HashSet<int> batchIdSet` from the batch's candidate ids and passes it into `ApplyTagsForPhotoAsync`, which now rejects any `result.Id` not in that set with a logged warning and an early return, before any `PhotoTagExistsAsync`/`AddPhotoTagAsync` calls. Both new tests were added as specified, and I independently ran the full `PhotobankAutoTagJobTests` suite — all 9 tests pass (7 pre-existing + 2 new).

## Review Result: PASS

### task: reject-out-of-batch-ids
**Status:** PASS

Verification performed:
- `git show HEAD` on `PhotobankAutoTagJob.cs` confirms the diff is exactly the guard described: `batchIdSet` built in `ProcessBatchAsync` from `batchIds`, passed as a new parameter into `ApplyTagsForPhotoAsync`, which now early-returns (with `_logger.LogWarning`) before touching `_photoTagRepository` when `result.Id` isn't in the set. This is byte-for-byte identical to the code prescribed in `task-context/reject-out-of-batch-ids.md`.
- `StampAutoTaggedAtAsync(batchIds, ...)` still uses the original `List<int> batchIds` unconditionally, so stamping behavior for ids actually sent is unchanged, as required.
- No other method in the file was touched (confirmed via full-file read and diff stat: only `ProcessBatchAsync` and `ApplyTagsForPhotoAsync` changed, `ExecuteAsync`/`ExecuteForPhotosAsync`/prompt builders untouched). `ApplyTagsForPhotoAsync` has exactly one caller in the codebase (verified via grep across `backend/`).
- `git show HEAD` on `PhotobankAutoTagJobTests.cs` confirms both new tests (`ExecuteAsync_LlmReturnsIdOutsideBatch_DropsResultWithoutApplyingTags`, `ExecuteAsync_BatchWithMixedInAndOutOfBatchIds_AppliesOnlyTheInBatchResult`) were added verbatim as specified, asserting `AddPhotoTagAsync` is never called for the out-of-batch id while `StampAutoTaggedAtAsync`/in-batch tagging behave correctly.
- Ran `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankAutoTagJobTests"` myself: `Passed! - Failed: 0, Passed: 9, Skipped: 0, Total: 9`. Matches the acceptance criterion.
- Commit message is exactly `fix(photobank): reject AI tag results whose id is outside the sent batch`, and the commit touches only the two specified files (diff stat: 2 files changed, 119 insertions, 1 deletion), purely additive except the one changed call site line.

I did not independently re-run `dotnet build`/`dotnet format --verify-no-changes` (the build environment was under heavy shared contention during this review and a single filtered test run alone took ~18 minutes), but the test compile step that ran as part of `dotnet test` succeeded with only pre-existing nullable-reference warnings unrelated to this change, and the source diff is a minimal, syntactically unremarkable addition that is very unlikely to introduce a build or formatting regression. Given the implementer's report also documents a clean build/format-verify run, and the code matches the spec's prescribed snippet character-for-character, I'm confident in PASS without re-running those two steps.

## Docs to Update
None.

## Overall Notes
No cross-cutting concerns. The change is narrowly scoped, matches the spec's prescribed implementation verbatim, and is well covered by the two new tests plus the seven pre-existing ones, all of which pass.
