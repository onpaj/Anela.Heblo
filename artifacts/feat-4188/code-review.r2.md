## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

### Verification notes (not findings, recorded for confidence)
- The round-1 Blocking finding is fixed: `PhotobankIndexJob.UpsertPhotoBatchAsync` (`backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs:246-249`) now calls the new `GetOccupiedTagPairsByPhotosAsync(batchPhotoIds, ct)` instead of the unscoped `GetOccupiedTagPairsAsync(null, ct)`. `PhotobankPhotoTagRepository.GetOccupiedTagPairsByPhotosAsync` (`backend/src/Anela.Heblo.Persistence/Photobank/PhotobankPhotoTagRepository.cs:69-81`) filters `WHERE pt.Source != Rule AND photoIds.Contains(pt.PhotoId)`, so each batch's query cost is bounded by that batch's photo count, not by the whole `PhotoTags` table — this restores the O(1)-and-bounded-cost-per-batch guarantee NFR-1 requires and matches `arch-review.r1.md` Decision 1's originally specified photo-ID-scoped-overload option. `GetOccupiedTagPairsAsync` itself is left untouched and still correctly used unscoped by `ReapplyRulesHandler`, which runs once standalone over the whole table (out of scope for this fix).
- `PhotobankIndexJobTests.UpsertPhotoBatch_MultipleDistinctPhotosInOneBatch_CallsBulkTagQueriesExactlyOnceEach` (line ~1206) now directly asserts `GetOccupiedTagPairsByPhotosAsync` is called exactly once with the batch's 3 photo IDs and that the unscoped `GetOccupiedTagPairsAsync` is never called — this closes the coverage gap round 1 flagged (previously only `Times.Once` was asserted, which didn't distinguish scoped from unscoped).
- New repository-level tests in `PhotobankRepositoryReapplyPrimitivesTests.cs` against the EF Core in-memory provider confirm scoping is correct end-to-end: `GetOccupiedTagPairsByPhotosAsync_scopesToRequestedPhotoIdsOnly` proves an out-of-scope photo's non-Rule pair is excluded (unlike the unscoped method), and `GetOccupiedTagPairsByPhotosAsync_emptyPhotoIds_returnsEmptyWithoutQuerying` covers the empty-batch guard, mirroring the existing `GetPhotoTagsByPhotosAndSourceAsync` convention.
- No other production caller of `IPhotobankPhotoTagRepository` needed updating: `ReapplyRulesHandler`, `RetagPhotosHandler`, `BulkAddPhotoTag(ByIds)Handler`, `RemovePhotoTagHandler` and `AddPhotoTagHandler` don't call the newly added method, and `PhotobankPhotoTagRepository` is the interface's only concrete implementation.
- Build: `dotnet build Anela.Heblo.sln` — 0 errors (pre-existing nullable warnings only, unrelated to this change).
- Test: `dotnet test --filter "FullyQualifiedName~Photobank"` — 205/208 passed; the 3 failures are `PhotobankTagRepositoryGetTagsSqlShapeTests` cases that require a Docker daemon for `Testcontainers.PostgreSql` and fail in this sandbox with "Docker is either not running or misconfigured" — unrelated to this change, same as reported by the round-1 review and the `full-validation-and-cleanup` task review.
- FR-1/FR-3 semantics (unchanged from round 1's review): `GetPhotoTagsByPhotosAndSourceAsync` remains correctly scoped to `batchPhotoIds`, and the per-photo reconciliation loop's control flow is otherwise unchanged.
