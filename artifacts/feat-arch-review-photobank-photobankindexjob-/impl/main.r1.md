# Implementation: Refactor PhotobankIndexJob to use IPhotobankRepository

## What was implemented

`PhotobankIndexJob` was refactored from directly injecting `ApplicationDbContext` to using the `IPhotobankRepository` abstraction, bringing it in line with its sibling `PhotobankAutoTagJob` and satisfying Clean Architecture layer boundaries.

Seven new methods were added to `IPhotobankRepository` (Domain layer) and implemented in `PhotobankRepository` (Application layer). The job's constructor, field, and all data-access calls were updated to use the repository. The private `CreateTagAsync` helper was removed in favour of the existing `GetOrCreateTagAsync`. Tests were fully migrated from an EF Core in-memory database to `Mock<IPhotobankRepository>`.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs` — added 7 new methods: `GetActiveRootsWithDriveAsync`, `GetActiveTagRulesAsync`, `GetPhotoBySharePointFileIdAsync`, `AddPhotoAsync`, `RemovePhotoAsync`, `GetPhotoTagsByPhotoAndSourceAsync`, `RemovePhotoTagsAsync`
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs` — implemented all 7 new interface methods
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` — replaced `ApplicationDbContext _db` with `IPhotobankRepository _repo`; removed EF Core usings; removed private `CreateTagAsync`
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` — migrated from EF Core InMemoryDatabase to `Mock<IPhotobankRepository>`; all 4 test scenarios preserved

## Tests

`backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` covers:
- `ExecuteAsync_InsertsNewPhoto_WithRuleTagsApplied` — new photo created with rule tags
- `ExecuteAsync_RemovesPhoto_WhenDeleted` — deleted Graph item removes photo
- `ExecuteAsync_PersistsDeltaLink_AfterRun` — delta link and LastIndexedAt persisted
- `ExecuteAsync_SkipsInactiveRoots` — no Graph calls when no active roots

All 4 tests use `Mock<IPhotobankRepository>`; no EF Core or InMemoryDatabase references remain.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~PhotobankIndexJobTests"

grep -r "ApplicationDbContext\|Microsoft.EntityFrameworkCore" \
  backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs
# Expected: no output

grep -r "ApplicationDbContext\|Microsoft.EntityFrameworkCore" \
  backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs
# Expected: no output
```

## Notes

- The spec explicitly requires the existing `SaveChangesAsync` call sequence to be preserved (FR-5), which it is.
- The spec explicitly prohibits adding new test scenarios (FR-4); only the 4 existing scenarios were migrated.
- `GetOrCreateTagAsync` returns `Task<Tag?>` by interface contract, but its implementation never returns null (creates if absent). The `tag!.Id` null-forgiving operator is safe and consistent with the pre-refactoring pattern. This is a minor null-annotation concern, not a runtime risk.
- `GetActiveRootsWithDriveAsync` returns EF-tracked entities (no `AsNoTracking`), so in-job mutations to `root.RootItemId`, `root.DeltaLink`, and `root.LastIndexedAt` are persisted correctly by the subsequent `SaveChangesAsync` calls.

## PR Summary

Refactor `PhotobankIndexJob` to route all data access through `IPhotobankRepository` instead of directly injecting `ApplicationDbContext`. This fixes a Clean Architecture violation where the Application layer was bypassing the domain abstraction and coupling directly to EF Core internals — making the job untestable without a real database and inconsistent with its sibling `PhotobankAutoTagJob`.

Seven new operations were added to the repository interface and implemented in `PhotobankRepository`. The job's constructor and all `_db.*` calls were replaced with `_repo.*` equivalents. The private `CreateTagAsync` helper (a duplicate of `GetOrCreateTagAsync`) was removed. Tests were migrated from an EF Core in-memory database to Moq-based mocks, eliminating the in-memory/PostgreSQL divergence risk.

No schema changes, no behavioural changes, no new test scenarios — pure architectural cleanup.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs` — 7 new repository method declarations
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs` — 7 new method implementations
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` — replaced `ApplicationDbContext` with `IPhotobankRepository`; removed EF Core usings; removed `CreateTagAsync`
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` — migrated from InMemoryDatabase to `Mock<IPhotobankRepository>`

## Status
DONE
