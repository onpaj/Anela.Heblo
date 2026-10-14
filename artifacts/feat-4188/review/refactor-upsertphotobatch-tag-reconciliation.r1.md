# Code Review: refactor-upsertphotobatch-tag-reconciliation

## Summary
The implementation replaces the per-photo N+1 loop in `PhotobankIndexJob.UpsertPhotoBatchAsync`
with two bulk preloads (`GetPhotoTagsByPhotosAndSourceAsync`, `GetOccupiedTagPairsAsync`) plus
an in-memory reconciliation loop, matching the task's functional requirement (FR-3: identical
behavior, O(1) queries per batch instead of O(n)). All prescribed test-mock updates were made
correctly, including the `PhotoTagExistsAsync`→`GetOccupiedTagPairsAsync` "true"-return exception
case and the `Times.Once` verify update. The specified new regression test was added.

One deviation from the task context's literal suggested code was necessary and is correctly
justified: the in-batch duplicate guard (`addedPairsThisBatch`) is keyed on the `Photo` object
reference instead of `Photo.Id`, because `Photo.Id` stays at the CLR default `0` for every
newly-created, not-yet-persisted photo in this test suite's mocks, which would otherwise
collapse distinct new photos into false duplicates. This was not a hypothetical concern — it
caused a real failure in a **pre-existing** test
(`UpsertPhotoBatch_MultipleItemsInSameBatch_FlushesSaveChangesExactlyThreeTimesTotal`) that the
task context did not flag as needing changes, confirming the fix was required, not optional
polish. Reference equality is provably safe here since `tagNamesByPhoto` already guarantees at
most one dictionary entry per distinct underlying photo row within a batch.

Verified: full build (0 errors), `dotnet format --verify-no-changes` (clean), all 13 tests in
`PhotobankIndexJobTests.cs` pass, and the broader Photobank suite (206 tests) shows no
regressions — the only 3 failures are pre-existing Testcontainers/Docker-dependent tests that
cannot run in this sandbox, unrelated to this change.

## Review Result: PASS

### task: refactor-upsertphotobatch-tag-reconciliation
**Status:** PASS

## Docs to Update
(Omit — this is an internal performance refactor with no change to public behavior, operator-facing
docs, or configuration.)

## Overall Notes
The developer's documented deviation (keying the in-batch guard on `Photo` reference rather than
`Photo.Id`) is correct, minimal, and well-justified with a concrete failing test as evidence
rather than speculation. No further changes requested.
