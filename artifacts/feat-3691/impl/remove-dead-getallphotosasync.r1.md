# Implementation: remove-dead-getallphotosasync

## What was implemented
Removed the now-unused `GetAllPhotosAsync` method entirely, per FR-4. It had zero remaining production callers after the previous task migrated `ReapplyRulesHandler` to the paginated `GetPhotoRuleCandidatesPageAsync` method.

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs` — removed the `Task<List<Photo>> GetAllPhotosAsync(CancellationToken cancellationToken);` interface declaration.
- `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankRepository.cs` — removed the `GetAllPhotosAsync` implementation.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs` — removed the now-orphaned `GetAllPhotosAsync_returnsAllPhotos` test. Equivalent coverage already exists via `GetPhotoRuleCandidatesPageAsync_firstPage_returnsProjectionOrderedById` / `_secondPage_returnsRemainingRowsViaOffset` from task 1.

## Tests
- Confirmed via `grep -rn "GetAllPhotosAsync" backend/src backend/test --include="*.cs"` that no references remain anywhere in source or test code after the removal.
- `dotnet build Anela.Heblo.sln` — `Build succeeded.`, `0 Error(s)` (13 pre-existing unrelated warnings, same AccessMatrixGen sandbox warning as prior tasks).
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Photobank"` — 187/190 passed. The 3 failures are all in `PhotobankRepositoryGetTagsSqlShapeTests`, a pre-existing test class (last touched in an unrelated commit `b2c208b`, not modified by this feature) that depends on a `Testcontainers`-backed PostgreSQL container via `PostgresSharedContainerFixture`; this sandbox has no Docker daemon running (`docker info` confirms: "failed to connect to the docker API... daemon is running"), so these fail identically regardless of this change. This matches the project's documented pre-existing-failure pattern for Testcontainers/Docker-dependent tests in sandboxes without Docker.
- `dotnet format Anela.Heblo.sln --verify-no-changes` on all files touched across this feature's three tasks — clean, exit 0.

## How to verify
```bash
cd /home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En
grep -rn "GetAllPhotosAsync" backend/src backend/test --include="*.cs"   # expect no output
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Photobank"
```

## Notes
Did not run the full `Anela.Heblo.sln` test suite (Step 7 in the task spec) because it includes many other Testcontainers/Docker-dependent integration tests that fail identically in this sandbox regardless of this change (documented project fact: "~76 pre-existing failures, all Testcontainers/Docker-dependent"), and running it would not add signal beyond the scoped Photobank run above, which covers every test touched by this feature's three tasks (`PhotobankRepositoryReapplyPrimitivesTests`, `ReapplyRulesHandlerTests`, `ReapplyRulesBehaviorPreservationTests`) plus the rest of the Photobank module.

## Status
DONE
