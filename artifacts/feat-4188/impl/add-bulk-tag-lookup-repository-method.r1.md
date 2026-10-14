# Implementation: add-bulk-tag-lookup-repository-method

## What was implemented

Added a new batch-scoped repository primitive, `GetPhotoTagsByPhotosAndSourceAsync`, to
`IPhotobankPhotoTagRepository` and its EF Core implementation
`PhotobankPhotoTagRepository`. It loads all `PhotoTag` rows for a given source across a
set of photo IDs in a single query and returns them grouped into a
`IReadOnlyDictionary<int, List<PhotoTag>>` keyed by `PhotoId`. This is the bulk-preload
primitive that Task 2 (`refactor-upsertphotobatch-tag-reconciliation`) will use to
replace the per-photo `GetPhotoTagsByPhotoAndSourceAsync` call inside
`PhotobankIndexJob.UpsertPhotoBatchAsync`'s per-photo loop. `PhotobankIndexJob` itself is
not touched by this task.

An empty `photoIds` collection short-circuits to an empty dictionary without issuing a
query, mirroring the existing `GetOccupiedTagPairsAsync` style already in the class.

The existing `PhotoTagExistsAsync` and singular `GetPhotoTagsByPhotoAndSourceAsync`
methods are left untouched on the interface and implementation, per the additive-only
interface change decision recorded in `arch-review.r1.md` (Decision 2) — no other class
implements `IPhotobankPhotoTagRepository`.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankPhotoTagRepository.cs` — added the `GetPhotoTagsByPhotosAndSourceAsync` interface member.
- `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankPhotoTagRepository.cs` — implemented `GetPhotoTagsByPhotosAndSourceAsync` (single query + in-memory group-by).
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs` — added two new tests covering the multi-photo grouping/source-filtering behavior and the empty-input short-circuit.

## Tests

- `GetPhotoTagsByPhotosAndSourceAsync_multiplePhotos_returnsOnlyMatchingSourceGroupedByPhotoId` — verifies rows are grouped by `PhotoId`, filtered by `Source`, and that a photo outside the requested ID set (photo 3) never leaks into the result even though it has a matching-source tag.
- `GetPhotoTagsByPhotosAndSourceAsync_emptyPhotoIds_returnsEmptyDictionaryWithoutQuerying` — verifies the empty-input short-circuit returns an empty dictionary.

Ran: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankRepositoryReapplyPrimitivesTests"` — all 12 tests in the file passed (10 pre-existing + 2 new).

Also ran a full `dotnet build Anela.Heblo.sln` to confirm the new interface member doesn't break any other implementer — build succeeded, 0 errors.

## How to verify

1. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankRepositoryReapplyPrimitivesTests"`
2. `dotnet build Anela.Heblo.sln`

## Notes

No deviations from the task-context spec. This task is purely additive and does not
change any runtime behavior — `PhotobankIndexJob` is unmodified and continues to call
the old per-photo methods until Task 2 lands.

## PR Summary
Added the bulk repository primitive `GetPhotoTagsByPhotosAndSourceAsync` on
`IPhotobankPhotoTagRepository` / `PhotobankPhotoTagRepository`, which loads Rule-source
tags for a whole batch of photo IDs in one query and groups them by photo ID in memory.
This is the first of two changes needed to eliminate the N+1 query pattern in
`PhotobankIndexJob.UpsertPhotoBatchAsync` (issue #4188); the job itself will be wired up
to use it in a follow-up task.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankPhotoTagRepository.cs` — new interface member
- `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankPhotoTagRepository.cs` — new implementation
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs` — two new tests

## Status
DONE
