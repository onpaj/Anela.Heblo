# Implementation: reject-out-of-batch-ids

## What was implemented
`PhotobankAutoTagJob.ProcessBatchAsync` now builds a `HashSet<int>` of the photo ids actually
sent to the LLM in the current batch (`batchIdSet`) and passes it to `ApplyTagsForPhotoAsync`.
`ApplyTagsForPhotoAsync` rejects any LLM result whose `id` is not in that set before doing any
further processing — no `PhotoTagExistsAsync`/`AddPhotoTagAsync` calls happen for it, and a
warning is logged. This closes a gap where an LLM hallucination (or a `FolderPath`/`FileName`
driven prompt-injection attempt) could return an `id` that was never part of the sent batch and
have a `PhotoTag` written for it regardless. Batch stamping (`StampAutoTaggedAtAsync`) behavior
for ids that actually were sent is unchanged — the batch is still stamped as processed even when
a result is rejected.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs` — added `batchIdSet` construction in `ProcessBatchAsync`, added a `HashSet<int> batchIds` parameter to `ApplyTagsForPhotoAsync` with an early-return + warning log when `result.Id` isn't in that set.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankAutoTagJobTests.cs` — added two new `[Fact]` tests: `ExecuteAsync_LlmReturnsIdOutsideBatch_DropsResultWithoutApplyingTags` and `ExecuteAsync_BatchWithMixedInAndOutOfBatchIds_AppliesOnlyTheInBatchResult`.

## Tests
- `ExecuteAsync_LlmReturnsIdOutsideBatch_DropsResultWithoutApplyingTags` — a single-candidate batch (id 42) where the LLM returns a result for id 999 (never sent); asserts no `PhotoTag` is ever written, but the batch (id 42) is still stamped as processed.
- `ExecuteAsync_BatchWithMixedInAndOutOfBatchIds_AppliesOnlyTheInBatchResult` — a two-candidate batch (ids 10, 11) where the LLM returns a valid result for id 10 and an out-of-batch result for id 55; asserts only id 10 gets a `PhotoTag` written and id 55 never does.
- All 7 pre-existing tests in `PhotobankAutoTagJobTests` continue to pass unchanged.

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankAutoTagJobTests"
# -> Passed: 9, Failed: 0

cd /home/user/worktrees/feature-3891-Arch-Review-Photobank-Photobankautotagjob-Applies
dotnet build Anela.Heblo.sln
# -> Build succeeded, 0 Error(s)

dotnet format Anela.Heblo.sln --verify-no-changes
# -> exits 0, no output (no formatting differences)
```

## Notes
- Confirmed before running the tests: before the fix, the two new tests failed as expected —
  `ExecuteAsync_LlmReturnsIdOutsideBatch_DropsResultWithoutApplyingTags` failed because
  `AddPhotoTagAsync` was invoked once for the out-of-batch id 999, and
  `ExecuteAsync_BatchWithMixedInAndOutOfBatchIds_AppliesOnlyTheInBatchResult` failed because
  `AddPhotoTagAsync` was invoked for id 55. After the fix all 9 tests (7 pre-existing + 2 new) pass.
- The solution file (`Anela.Heblo.sln`) lives at the repo root, not under `backend/`, so
  `dotnet build`/`dotnet format` were run from the repo root against `Anela.Heblo.sln` rather than
  a bare `dotnet build` inside `backend/` (which errors with MSB1003 — no project/solution file in
  that directory). This is an environment detail, not a change to the task's intent; the build and
  format-verify both passed cleanly.
- `artifacts/feat-3891/state.json` had a pre-existing uncommitted modification in the working tree
  before this task started (unrelated to this change); it was left untouched and not included in
  the commit, per the task's explicit file list for `git add`.
- No other method in `PhotobankAutoTagJob.cs` was changed; `ExecuteForPhotosAsync` required no
  edits as specified.

## Status
DONE
