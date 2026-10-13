# Implementation: code-review-fixes (round 1)

## What was implemented
Fixed the sole Blocking finding from `code-review.r1.md`: `UpsertPhotoBatchAsync` was calling
`GetOccupiedTagPairsAsync(scopeToTagName: null, ct)` unscoped, once per 200-photo batch, causing
a full non-Rule `PhotoTags` table scan on every batch in a run — `O(batches × table_size)` work
instead of the bounded-per-batch cost NFR-1 requires. This reintroduces, one level up, the exact
scaling problem `arch-review.r1.md` Decision 1 called out and the design conditioned on.

Per Decision 1 option (b), added a photo-ID-scoped sibling method (matching the existing
`RemovePhotoTagsBySourceAsync(IReadOnlyList<int> photoIds, ...)` convention) instead of reusing
the unscoped method:

- `IPhotobankPhotoTagRepository.GetOccupiedTagPairsByPhotosAsync(IReadOnlyCollection<int> photoIds, CancellationToken ct)`
- `PhotobankPhotoTagRepository.GetOccupiedTagPairsByPhotosAsync` — `WHERE Source != Rule AND PhotoId IN (...)`, short-circuits to an empty result for an empty `photoIds` (mirrors `GetPhotoTagsByPhotosAndSourceAsync`'s existing guard).
- `PhotobankIndexJob.UpsertPhotoBatchAsync` now calls `GetOccupiedTagPairsByPhotosAsync(batchPhotoIds, ct)` instead of the unscoped call, so the query cost is bounded by the batch's own photo count, not the whole table — restoring the "O(1) queries per batch, each bounded by batch size" guarantee.

`GetOccupiedTagPairsAsync` (unscoped/tag-name-scoped) is left untouched: `ReapplyRulesHandler`'s
single, standalone, whole-table use of it is still correct and out of scope for this fix.

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankPhotoTagRepository.cs` — added `GetOccupiedTagPairsByPhotosAsync` signature.
- `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankPhotoTagRepository.cs` — implemented `GetOccupiedTagPairsByPhotosAsync`.
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` — call site now uses the batch-scoped method with `batchPhotoIds`.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` — all mocks/verifies of `GetOccupiedTagPairsAsync` updated to `GetOccupiedTagPairsByPhotosAsync`; `UpsertPhotoBatch_MultipleDistinctPhotosInOneBatch_CallsBulkTagQueriesExactlyOnceEach` now asserts the scoped overload is called with exactly the batch's photo IDs (`Count == 3`) and that the unscoped `GetOccupiedTagPairsAsync` is never called — this is the assertion the review noted was missing.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs` — added `GetOccupiedTagPairsByPhotosAsync_scopesToRequestedPhotoIdsOnly` (proves an out-of-scope photo's non-Rule pair is excluded, unlike the unscoped method) and `GetOccupiedTagPairsByPhotosAsync_emptyPhotoIds_returnsEmptyWithoutQuerying`.

## Tests
- `PhotobankIndexJobTests.cs` — existing suite updated; new assertions directly cover the previously-uncaught cost-multiplication bug (batch-photo-ID scoping, unscoped method never called).
- `PhotobankRepositoryReapplyPrimitivesTests.cs` — two new repository-level tests against the EF Core in-memory provider for the new method's scoping and empty-input behavior.

## How to verify
`dotnet build Anela.Heblo.sln` (0 errors), then `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Photobank"`.

## Notes
No deviations from the arch-review's Decision 1 guidance. `GetPhotoTagsByPhotosAndSourceAsync`
(the other bulk method from the original task) was already correctly batch-scoped and untouched.

## PR Summary
Fixed the remaining N+1-adjacent scaling bug flagged in code review round 1: the batch tag-upsert
path was preloading non-Rule tag pairs with an unscoped, whole-table query once per batch instead
of a photo-ID-scoped one, which would have made a large backlog run scale as
`O(batches × table_size)` instead of being bounded by batch size. Added a photo-ID-scoped sibling
repository method (`GetOccupiedTagPairsByPhotosAsync`), wired it into `PhotobankIndexJob`, and
strengthened the test suite to assert the scoping directly so this class of regression is caught
going forward.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankPhotoTagRepository.cs`
- `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankPhotoTagRepository.cs`
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs`

## Status
DONE
