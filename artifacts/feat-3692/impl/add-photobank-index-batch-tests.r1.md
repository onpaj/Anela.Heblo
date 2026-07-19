# Implementation: add-photobank-index-batch-tests

## What was implemented
Added four new test cases to `PhotobankIndexJobTests` covering the batching behavior introduced in `batch-photobank-index-upserts`:
- `UpsertPhotoBatch_MultipleItemsInSameBatch_FlushesSaveChangesExactlyThreeTimesTotal` — a multi-item delta within one batch results in exactly 3 `SaveChangesAsync` calls total (Phase A + Phase B + root bookkeeping), not 2×N.
- `UpsertPhotoBatch_DeltaLargerThanBatchSize_FlushesCeilNOverBatchSizeTimes` — a 201-item delta (BatchSize=200) forces a second batch, asserting the expected total `SaveChangesAsync` count.
- `UpsertPhotoBatch_DuplicateSharePointFileIdWithinOneBatch_ResultsInSinglePhotoRow` — the same `SharePointFileId` appearing twice as non-deleted items in one batch resolves to a single tracked `Photo` instance (via the batch-local cache), not two rows.
- `UpsertPhotoBatch_UpsertThenDeleteSameItemInSameDelta_PhotoEndsUpRemoved` — an item upserted then deleted within the same delta ends up removed, verifying the pending-batch-flushed-before-delete ordering.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` — added the four new test cases above (no production code changes; `PhotobankIndexJob.cs` was already implemented in the prior task)

## Tests
All 9 tests in `PhotobankIndexJobTests.cs` (5 pre-existing + 4 new) pass.

## How to verify
```
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankIndexJobTests" --no-build -v minimal
```
Result: build succeeds; 9/9 tests pass.

## Notes
No production code changes were needed — `PhotobankIndexJob.cs`'s batching implementation from the prior task already satisfied all four new test scenarios without modification.

## PR Summary
Adds test coverage for the batching edge cases identified in the architecture review: exact `SaveChangesAsync` call counts for single- and multi-batch deltas, within-batch duplicate `SharePointFileId` handling (no duplicate rows), and upsert-then-delete ordering within one delta (no orphaned rows). These tests exercise the batching logic added in the companion `batch-photobank-index-upserts` change.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` — add 4 new test cases for batching edge cases

## Status
DONE
