# Implementation: refactor-upsertphotobatch-tag-reconciliation

## What was implemented

Replaced the per-photo N+1 loop body in `PhotobankIndexJob.UpsertPhotoBatchAsync` (Phase B)
with the bulk-preload version, using the `GetPhotoTagsByPhotosAndSourceAsync` primitive
added in Task 1 plus the existing `GetOccupiedTagPairsAsync` bulk method. The loop no longer
calls `GetPhotoTagsByPhotoAndSourceAsync` or `PhotoTagExistsAsync` once per photo/pair;
instead it preloads both data sets once per batch (two queries total, independent of how
many distinct photos are in the batch) and reconciles rule tags in memory.

All existing test mocks in `PhotobankIndexJobTests.cs` were updated to stub the new bulk
methods instead of the old per-photo ones, and a new regression test
(`UpsertPhotoBatch_MultipleDistinctPhotosInOneBatch_CallsBulkTagQueriesExactlyOnceEach`) was
added to assert the bulk methods are each called exactly once per batch regardless of how
many distinct photos it contains, and that the old per-photo methods are never called.

### Deviation from the task-context spec (bug found and fixed)

The task-context's suggested code for the in-batch duplicate-insert guard
(`addedPairsThisBatch`) used `HashSet<(int PhotoId, int TagId)>`, keyed on `photo.Id`. This
does not work correctly for **multiple distinct new (unsaved) photos in the same batch**:
in the unit tests, `AddPhotoAsync` is mocked and never assigns a real database identity, so
every newly-created `Photo` in a test keeps the CLR-default `Id == 0` for the whole test.
With an `int`-keyed guard, two or three distinct new photos matching the same tag rule all
produce the identical pair `(0, tagId)`, so the guard's `.Add(...)` returns `false` for the
2nd/3rd photo and their `AddPhotoTagAsync` calls are incorrectly skipped as "duplicates."

This surfaced as a real test failure — not just in the new regression test, but in the
**pre-existing** `UpsertPhotoBatch_MultipleItemsInSameBatch_FlushesSaveChangesExactlyThreeTimesTotal`
test (2 distinct new photos, same tag), which the task context did not flag as needing any
change and which was expected to keep passing unmodified.

Fix: `addedPairsThisBatch` is now `HashSet<(Photo Photo, int TagId)>`, keyed on the `Photo`
object reference (default reference equality) instead of `Photo.Id`. Since `tagNamesByPhoto`
already guarantees one dictionary entry per distinct `Photo` instance, reference identity is
always correct for "have I already added this pair in this batch", regardless of whether the
photo's `Id` has been assigned yet. This does not change the `occupiedNonRulePairs` check,
which still correctly uses `(photo.Id, tagId)` against the real DB-backed
`GetOccupiedTagPairsAsync` result (those IDs necessarily refer to already-persisted photos).

In production this distinction is invisible — by the time Phase B runs, Phase A's
`SaveChangesAsync` has already assigned a real, unique `Id` to every new photo via EF Core's
identity-column tracking — but the guard should not silently depend on that timing to be
correct, and the test suite proves it wasn't.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` — replaced the per-photo tag-reconciliation loop (former lines 240–259) with the bulk-preload version described above.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` — updated all mocks from `GetPhotoTagsByPhotoAndSourceAsync`/`PhotoTagExistsAsync` to `GetPhotoTagsByPhotosAndSourceAsync`/`GetOccupiedTagPairsAsync` (9 + 5 setup sites, including the `UpsertPhoto_WhenTagAlreadyExists_SkipsInsert` exception case and the `Times.Once` verify at the former line 978); added the new `UpsertPhotoBatch_MultipleDistinctPhotosInOneBatch_CallsBulkTagQueriesExactlyOnceEach` regression test.

## Tests

- All 13 tests in `PhotobankIndexJobTests.cs` pass, including the new regression test.
- Ran the broader `~Photobank` filter (206 tests): 203 pass; the only 3 failures
  (`PhotobankTagRepositoryGetTagsSqlShapeTests`) are pre-existing and environmental —
  they require Testcontainers/Docker, which is not available in this sandbox — and are
  unrelated to this change.
- `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — 0 errors.
- `dotnet format Anela.Heblo.sln --include <changed files> --verify-no-changes` — no formatting changes needed.

## How to verify

1. `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
2. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankIndexJobTests"` (or run via `vstest.console.dll` directly against the built DLL if `dotnet test`'s own restore/discovery hangs in this sandbox — see Notes)
3. `dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs --verify-no-changes`

## Notes

- `dotnet test` hung indefinitely in this sandbox on both attempts (14+ minutes, no CPU
  activity, many idle established HTTPS connections) even after the underlying `dotnet build`
  succeeded in under 2 minutes. Worked around by building normally and then invoking
  `dotnet exec .../vstest.console.dll <built-test-dll> --TestCaseFilter:"..."` directly
  against the already-built test assembly, which ran instantly. This looks like a sandbox/
  network artifact of the `dotnet test` driver itself (possibly its own restore or telemetry
  path), not anything related to this change — flagging it here in case it recurs for a
  later task in this pipeline.
- The one intentional deviation from the task-context's literal suggested code (the
  `addedPairsThisBatch` key type) is documented above; everything else follows the task
  context's steps as written.

## PR Summary
Fixed the N+1 per-photo database query pattern in `PhotobankIndexJob.UpsertPhotoBatchAsync`:
the tag-reconciliation loop used to call `GetPhotoTagsByPhotoAndSourceAsync` and
`PhotoTagExistsAsync` once per photo (and per matched tag) in every indexed batch. It now
preloads both data sets once per batch via the bulk `GetPhotoTagsByPhotosAndSourceAsync`
(added in the previous task) and the existing `GetOccupiedTagPairsAsync`, then reconciles
tags in memory with an in-batch duplicate guard. Behavior is unchanged (FR-3) — all existing
tests pass with updated mocks, and a new test proves the bulk methods are now called exactly
once per batch regardless of batch size.

While implementing, found and fixed a real bug in the task's suggested in-batch duplicate
guard: keying it by `Photo.Id` breaks for multiple distinct *new* photos in one batch, since
they all share `Id == 0` until persisted. Fixed by keying on the `Photo` object reference
instead, which a pre-existing test (not flagged by the task as needing changes) caught
immediately once actually exercised.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` — bulk-preload tag reconciliation, replacing the per-photo query loop
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` — updated mocks for all existing tests, added one new regression test

## Status
DONE
