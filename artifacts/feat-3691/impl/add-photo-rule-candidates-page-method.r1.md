# Implementation: add-photo-rule-candidates-page-method

## What was implemented
Added a new paginated repository method, `GetPhotoRuleCandidatesPageAsync(int pageSize, int offset, CancellationToken)`, to `IPhotobankRepository` and its `PhotobankRepository` implementation. It returns a page of `PhotoAutoTagCandidate` projections (Id, FolderPath, FileName) ordered by `Id`, using `AsNoTracking()` + `Skip`/`Take` for pagination. This is purely additive: `GetAllPhotosAsync` and the reapply-rules handler that currently calls it are untouched, so the build and all existing tests remain green throughout.

A previous attempt had already added the two test methods to the test file (uncommitted); they matched the spec exactly, so no changes were needed there — implementation resumed from Step 4 (interface method) onward.

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs` — added `GetPhotoRuleCandidatesPageAsync` signature under a new `// Rule reapply` section, between the existing `// Auto-tagging` methods and `SaveChangesAsync`.
- `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankRepository.cs` — added the `GetPhotoRuleCandidatesPageAsync` implementation between `GetPhotosPendingAutoTagAsync` and `StampAutoTaggedAtAsync`.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs` — already contained the two new test methods from a prior interrupted attempt (verified to match spec exactly, no edits needed).

## Tests
- `PhotobankRepositoryReapplyPrimitivesTests.GetPhotoRuleCandidatesPageAsync_firstPage_returnsProjectionOrderedById` — verifies first page (pageSize 2, offset 0) returns 2 rows ordered by Id, not insertion order, with correct projection fields.
- `PhotobankRepositoryReapplyPrimitivesTests.GetPhotoRuleCandidatesPageAsync_secondPage_returnsRemainingRowsViaOffset` — verifies second page (pageSize 2, offset 2) returns the single remaining row via the offset.
- All 11 tests in `PhotobankRepositoryReapplyPrimitivesTests` pass (including the pre-existing, untouched `GetAllPhotosAsync_returnsAllPhotos`).

## How to verify
```bash
cd /home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~PhotobankRepositoryReapplyPrimitivesTests"
dotnet build Anela.Heblo.sln
```
Expected: test run shows `Passed! - Failed: 0, Passed: 11, Skipped: 0, Total: 11`; solution build shows `Build succeeded.` with `0 Error(s)`.

## Notes
- The whole-solution build produces one pre-existing, unrelated warning (MSB3073, exit code 134) from the `Anela.Heblo.API` post-build `AccessMatrixGen` step failing to parse `access-matrix.generated.json` in this sandboxed environment. This is not caused by this change (it touches only Photobank repository/domain/test files), does not fail the build (`0 Error(s)`, `Build succeeded.`), and was observed identically before this change was applied.
- The environment had several stray `dotnet test`/MSBuild node-reuse processes left over from a prior interrupted attempt, which caused early test-run timeouts. These were cleared with `dotnet build-server shutdown` before a clean, successful build+test run.
- Followed the existing (untouched) `GetPhotosPendingAutoTagAsync` method's query shape but added `.AsNoTracking()` as explicitly specified in the task spec, even though the neighboring method does not use it — this was an explicit instruction from the task, not an inconsistency I introduced silently.

## PR Summary
This change adds a new, purely additive paginated repository method, `GetPhotoRuleCandidatesPageAsync`, to `IPhotobankRepository` and `PhotobankRepository`, intended as a building block for a future rule-reapply handler that will page through photos instead of loading them all via `GetAllPhotosAsync`. The method returns `PhotoAutoTagCandidate` projections (Id, FolderPath, FileName) ordered by `Id` with standard skip/take offset paging, using `AsNoTracking()` for a read-only query. Two new repository-level tests were added to `PhotobankRepositoryReapplyPrimitivesTests` (first-page ordering and second-page offset behavior), both run against a real EF Core InMemory `ApplicationDbContext`, matching the existing test class's conventions. No production call sites were changed — `GetAllPhotosAsync` and its handler remain untouched — so this task carries no behavioral risk to existing functionality.

### Changes
- `IPhotobankRepository.cs`: new method signature under a `// Rule reapply` section.
- `PhotobankRepository.cs`: new method implementation.
- `PhotobankRepositoryReapplyPrimitivesTests.cs`: two new tests (already present from a prior interrupted attempt, verified correct).

## Status
DONE
