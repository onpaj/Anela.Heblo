All spec compliance checks pass. The implementation is complete and correct. Here's the output summary:

---

# Implementation: Refactor PhotobankIndexJob to use IPhotobankRepository

## What was implemented

The `PhotobankIndexJob` was refactored from directly injecting `ApplicationDbContext` to using `IPhotobankRepository` exclusively, bringing it into alignment with `PhotobankAutoTagJob` and Clean Architecture boundaries. The seven new repository methods were added to both the interface and implementation, the job was updated to use them, and the tests were rewritten using `Mock<IPhotobankRepository>`.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs` — added 7 new method signatures grouped under existing section headers (`// Roots`, `// Rules`, `// Photos`, `// Photo tags`)
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs` — implemented all 7 methods; `Add*`/`Remove*` are stage-only (no internal `SaveChangesAsync`); `Get*` methods return tracked entities
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` — replaced `ApplicationDbContext _db` with `IPhotobankRepository _repo`; removed `using Anela.Heblo.Persistence` and `using Microsoft.EntityFrameworkCore`; removed private `CreateTagAsync` helper in favour of `_repo.GetOrCreateTagAsync`
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` — rewritten to use `Mock<IPhotobankRepository>`; all 4 scenarios preserved; no `ApplicationDbContext` or `Microsoft.EntityFrameworkCore` references

## Tests

`backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` — 4 test cases:
- `ExecuteAsync_InsertsNewPhoto_WithRuleTagsApplied`
- `ExecuteAsync_RemovesPhoto_WhenDeleted`
- `ExecuteAsync_PersistsDeltaLink_AfterRun`
- `ExecuteAsync_SkipsInactiveRoots`

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~PhotobankIndexJobTests"
dotnet build backend/src/Anela.Heblo.Application
grep -r "ApplicationDbContext\|Microsoft.EntityFrameworkCore" backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs
# Expected: no output
```

## Notes

- The `SaveChangesAsync` flush sequence is preserved exactly: flush #1 (conditional RootItemId resolution), flush #2 (after photo upsert in `UpsertPhotoAsync`), flush #3 (after rule-tag reapply in `UpsertPhotoAsync`), flush #4 (after delta-link/LastIndexedAt mutation in `IndexRootAsync`).
- No DI registration changes were needed — `PhotobankIndexJob` is auto-registered via the assembly scan in `AddRecurringJobs()`.
- `Add*`/`Remove*` repository methods are stage-only (return `Task.CompletedTask` after synchronous EF operations) matching the existing convention.
- `GetActiveRootsWithDriveAsync` returns tracked entities (no `AsNoTracking`) so the in-place root mutations are flushed by subsequent `SaveChangesAsync` calls.

## PR Summary

Refactored `PhotobankIndexJob` from directly injecting `ApplicationDbContext` to using `IPhotobankRepository`, completing Clean Architecture compliance for the Photobank module's background jobs. The job previously bypassed the Domain/Application boundary by importing EF Core and Persistence types directly; it now uses repository abstractions matching the pattern already established by `PhotobankAutoTagJob`.

Seven new methods were added to `IPhotobankRepository` and implemented in `PhotobankRepository` (stage-only Add/Remove, tracked-entity reads). The test suite was migrated from an EF Core InMemoryDatabase fixture to `Mock<IPhotobankRepository>`, preserving all four test scenarios.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs` — 7 new method signatures
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs` — implements the 7 new methods
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` — swapped `ApplicationDbContext` for `IPhotobankRepository`, removed private `CreateTagAsync` helper
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` — migrated from EF InMemory to Moq, all 4 scenarios preserved

## Status
DONE