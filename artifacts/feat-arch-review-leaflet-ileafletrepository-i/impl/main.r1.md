All 17 tasks complete. Now writing the output artifact.

---

# Implementation: Split `ILeafletRepository` by Aggregate Root

## What was implemented

Refactored the monolithic `ILeafletRepository` (15 methods, 2 aggregates, 1 EF Core unit-of-work leak) into two narrowly-scoped domain interfaces aligned with aggregate roots. No public-API or behavioral change for end users — pure ISP/Clean Architecture refactor.

Key non-trivial changes beyond the mechanical type swaps:
- `AddDocumentAsync` now commits eagerly (eager `SaveChangesAsync` after `Add`) so `IndexLeafletHandler` no longer needs to call `SaveChangesAsync`
- `WordCount` computation moved from `LeafletIndexingService.IndexAsync` into `IndexLeafletHandler` before `AddDocumentAsync` is called — required because eager commit happens before the service ran
- `UpdateFeedbackAsync` new repo method encapsulates the load-validate-mutate-save feedback cycle, returning `UpdateFeedbackResult` enum (`Updated`/`NotFound`/`AlreadySubmitted`) instead of exposing `SaveChangesAsync` through the domain layer

## Files created/modified

**Created (domain):**
- `backend/src/Anela.Heblo.Domain/Features/Leaflet/UpdateFeedbackResult.cs` — enum for feedback update outcomes
- `backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletDocumentRepository.cs` — 14-method document/chunk aggregate interface
- `backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletGenerationRepository.cs` — 5-method generation/feedback aggregate interface (4 existing + new `UpdateFeedbackAsync`)

**Created (persistence):**
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs` — EF Core implementation with eager-commit `AddDocumentAsync`
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletGenerationRepository.cs` — EF Core implementation including `UpdateFeedbackAsync`

**Deleted:**
- `backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletRepository.cs`
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletRepository.cs`

**Modified (application layer — type swaps):**
- `IndexLeafletHandler.cs` — WordCount-before-Add + no SaveChangesAsync
- `LeafletIndexingService.cs` — no WordCount mutation
- `DeleteLeafletDocumentHandler.cs`, `GetLeafletDocumentsHandler.cs`, `GetLeafletDocumentContentTypesHandler.cs`, `GetLeafletChunkDetailHandler.cs` — ILeafletDocumentRepository
- `GenerateLeafletHandler.cs`, `LeafletIngestionJob.cs` — ILeafletDocumentRepository
- `LeafletGenerationLoggingBehavior.cs`, `GetLeafletGenerationHandler.cs`, `GetLeafletFeedbackListHandler.cs` — ILeafletGenerationRepository
- `SubmitLeafletFeedbackHandler.cs` — ILeafletGenerationRepository + UpdateFeedbackAsync

**Modified (DI):**
- `PersistenceModule.cs` — removed legacy registration, two new registrations

**Test files updated (13):**
- All 13 test files migrated to mock narrower interfaces
- `LeafletRepositoryTests.cs` → `LeafletDocumentRepositoryTests` class
- New `LeafletGenerationRepositoryTests.cs` with 3 `UpdateFeedbackAsync` tests
- New WordCount TDD test in `IndexLeafletHandlerTests.cs`
- Deleted obsolete WordCount-mutation test in `LeafletIndexingServiceTests.cs`

## Tests

- **Leaflet suite**: 112 passed, 3 skipped (pgvector integration — pre-existing), 0 failed
- **Full solution**: 3,148 passed, 3 skipped, 0 failed
- New tests: `UpdateFeedbackAsync_returns_NotFound`, `UpdateFeedbackAsync_returns_AlreadySubmitted`, `UpdateFeedbackAsync_persists_scores_and_comment_then_returns_Updated`, `Handle_happy_path_stamps_WordCount_on_document_before_AddDocumentAsync`
- TDD confirmed RED→GREEN on WordCount test and SubmitLeafletFeedbackHandler tests

## How to verify

```bash
# from repo root worktree
dotnet build Anela.Heblo.sln                      # 0 errors
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Leaflet"  # 112 pass / 3 skip
grep -rn "ILeafletRepository" backend/             # 0 matches
grep -rn "SaveChangesAsync" backend/src/Anela.Heblo.Application/Features/Leaflet/  # 0 matches
dotnet format Anela.Heblo.sln --verify-no-changes  # exit 0
```

## Notes

- Leaflet is now the first module in the codebase without `SaveChangesAsync` on its domain interface — other modules (`IKnowledgeBaseRepository`, etc.) still expose it. This is intentional; out of scope per spec.
- `AddDocumentAsync` eager-commit adds one extra round-trip to PostgreSQL per indexing operation; negligible compared to the embeddings workload.
- EF Core change tracker returns the same tracked instance within the same scoped DbContext, so `UpdateFeedbackAsync` calling `FindAsync` after the handler already loaded the generation does not add an extra DB round-trip in the common case.

## PR Summary

Splits the monolithic `ILeafletRepository` (15 methods, 2 aggregate roots, 1 EF Core unit-of-work leak) into `ILeafletDocumentRepository` (14 document/chunk methods) and `ILeafletGenerationRepository` (4 existing generation methods + new `UpdateFeedbackAsync`). Removes `SaveChangesAsync` from both domain interfaces — persistence boundaries are now internal to each repository method. Also fixes a latent bug: `WordCount` was previously computed after `AddDocumentAsync` tracked (but before it committed), so with eager-commit it would have persisted as 0. The computation now happens before `AddDocumentAsync`.

This is a no-op refactor at the public API boundary: no HTTP contracts, database schema, or OpenAPI schema changed.

### Changes
- `Domain/Features/Leaflet/ILeafletDocumentRepository.cs` — new 14-method document aggregate interface
- `Domain/Features/Leaflet/ILeafletGenerationRepository.cs` — new 5-method generation aggregate interface (4 existing + UpdateFeedbackAsync)
- `Domain/Features/Leaflet/UpdateFeedbackResult.cs` — new enum returned by UpdateFeedbackAsync
- `Persistence/Features/Leaflet/LeafletDocumentRepository.cs` — new implementation (AddDocumentAsync commits eagerly)
- `Persistence/Features/Leaflet/LeafletGenerationRepository.cs` — new implementation (UpdateFeedbackAsync encapsulates feedback load-mutate-save)
- `Persistence/PersistenceModule.cs` — two new registrations, legacy removed
- 9 application-layer files — type swaps to narrower interfaces
- 13 test files — mock type updates + 4 new tests, 1 deleted

## Status
DONE