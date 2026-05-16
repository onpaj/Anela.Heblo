Based on my exploration, I have enough grounding to write the review. Here's my architecture assessment.

```markdown
# Architecture Review: Split `ILeafletRepository` by Aggregate Root

## Skip Design: true

## Architectural Fit Assessment

This refactor aligns cleanly with the project's documented Clean Architecture + Vertical Slice intent (`docs/architecture/filesystem.md`, `docs/architecture/development_guidelines.md`). The Leaflet module already lives in `Domain/Features/Leaflet/` and `Persistence/Features/Leaflet/`; splitting `ILeafletRepository` into two aggregate-aligned interfaces and removing the `SaveChangesAsync` leak strengthens module hygiene without crossing any module boundary or changing public contracts.

However, the refactor **diverges from the rest of the codebase**. Six other domain repositories (`IStockUpOperationRepository`, `IKnowledgeBaseRepository`, `IPhotobankRepository`, `ISmartsuppRepository`, `IImportedMarketingTransactionRepository`, `IArticleRepository`) all still expose `SaveChangesAsync` on the domain interface. The Leaflet split is therefore a *pattern-setting* change, not a pattern-following one. That is fine — Leaflet has the most concrete pain (two unrelated aggregates in one interface) — but the review/PR should call this out explicitly so future cleanups in the other modules can follow the same recipe.

Integration points are local: DI registration in `PersistenceModule.cs:129`, 9 consumers in `Application/Features/Leaflet/`, 13 test files. No API/HTTP, no migrations, no frontend touched.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Domain.Features.Leaflet
├── ILeafletDocumentRepository       ← Document/Chunk aggregate boundary
├── ILeafletGenerationRepository     ← Generation/Feedback aggregate boundary
├── UpdateFeedbackResult (enum)      ← repo→handler outcome signal
└── (entities unchanged)

Anela.Heblo.Persistence.Features.Leaflet
├── LeafletDocumentRepository : ILeafletDocumentRepository
└── LeafletGenerationRepository : ILeafletGenerationRepository
        │
        └── both wrap ApplicationDbContext, each commits per operation

Anela.Heblo.Application.Features.Leaflet
├── UseCases/IndexLeaflet           → ILeafletDocumentRepository
├── UseCases/DeleteLeafletDocument  → ILeafletDocumentRepository
├── UseCases/GetLeafletDocuments    → ILeafletDocumentRepository
├── UseCases/GetLeafletDocumentContentTypes → ILeafletDocumentRepository
├── UseCases/GetLeafletChunkDetail  → ILeafletDocumentRepository
├── UseCases/GenerateLeaflet        → ILeafletDocumentRepository (SearchSimilarAsync)
├── UseCases/GetLeafletGeneration   → ILeafletGenerationRepository
├── UseCases/GetLeafletFeedbackList → ILeafletGenerationRepository
├── UseCases/SubmitLeafletFeedback  → ILeafletGenerationRepository
├── Services/LeafletIndexingService → ILeafletDocumentRepository
├── Pipeline/LeafletGenerationLoggingBehavior → ILeafletGenerationRepository
└── Infrastructure/Jobs/LeafletIngestionJob   → ILeafletDocumentRepository
```

### Key Design Decisions

#### Decision 1: Auto-commit per write method (no `SaveChangesAsync` on either interface)
**Options considered:**
- (a) Surface `ILeafletUnitOfWork` for the indexing flow that touches multiple writes.
- (b) Make every write method commit internally; remove `SaveChangesAsync` entirely.
- (c) Status quo — keep `SaveChangesAsync` but split interfaces.

**Chosen approach:** (b). The interfaces stay clean of EF Core unit-of-work concepts.

**Rationale:** Examining the actual call sites, no handler needs a cross-method transaction. `AddChunksAsync` already commits via raw SQL per-row; `Update*Async` methods already use `ExecuteUpdate`/`ExecuteDelete`. Only `AddDocumentAsync` and the `LeafletGeneration` mutation flow rely on a deferred SaveChanges today, and both are local single-entity writes. (a) is over-engineering for current needs; (c) preserves the leak the refactor is trying to remove.

#### Decision 2: `UpdateFeedbackAsync` returns `UpdateFeedbackResult` enum (not `bool`)
**Options considered:**
- (a) `Task<bool>` (false = anything went wrong).
- (b) `Task<UpdateFeedbackResult>` with `Updated | NotFound | AlreadySubmitted`.
- (c) Throw domain exceptions, handler catches.

**Chosen approach:** (b). The spec is internally inconsistent on this (FR-2 implies bool, API design section says enum) — settle on the enum.

**Rationale:** The handler must translate two distinct failure modes into distinct `ErrorCodes` values (`LeafletFeedbackNotFound` vs `LeafletFeedbackAlreadySubmitted`). A `bool` collapses them; the handler would need a second `GetGenerationByIdAsync` round-trip to disambiguate. Exceptions for known business outcomes violate this project's pattern (handlers return `Response.ErrorCode`, never throw for validation). The enum is the smallest construct that preserves error semantics.

#### Decision 3: Authorization stays in the handler; repository is auth-agnostic
**Options considered:**
- (a) Handler loads generation, validates ownership, then calls `UpdateFeedbackAsync(id, scores, comment, ct)`.
- (b) Push ownership check into the repo: `UpdateFeedbackAsync(id, userId, scores, comment, ct)`.

**Chosen approach:** (a).

**Rationale:** `ICurrentUserService` is an application concern; the persistence layer should not know about authorization. (a) also keeps `SubmitLeafletFeedbackHandler`'s existing flow and test assertions structurally the same. The cost is one extra DB round-trip per feedback submission (load → update), but EF Core's change tracker will return the same tracked instance to the repo's `FindAsync` inside `UpdateFeedbackAsync` within the scoped DbContext, so it is at worst one extra query for the typical case.

#### Decision 4: `WordCount` is set in the handler before `AddDocumentAsync`, not inside `LeafletIndexingService`
**Options considered:**
- (a) Keep `LeafletIndexingService.IndexAsync` mutating `document.WordCount` (current behavior, line 75 of `LeafletIndexingService.cs`).
- (b) Compute `WordCount` in `IndexLeafletHandler` before calling `AddDocumentAsync`, so the eager-committed row has the correct value.
- (c) Add a new `UpdateWordCountAsync(Guid id, int wordCount, ct)` repository method.

**Chosen approach:** (b).

**Rationale (CRITICAL):** This is **not in the spec but is required for FR-4 to be correct.** Today, `IndexLeafletHandler` calls `AddDocumentAsync` (tracks-only), then `_indexing.IndexAsync` mutates `document.WordCount` (line 75), then `SaveChangesAsync` flushes both the insert and the WordCount. After the refactor, `AddDocumentAsync` commits *eagerly* with `WordCount = 0`, and the later mutation to `document.WordCount` is lost (the entity is no longer in the change tracker after the eager save). (b) is the smallest fix: the handler already has `text` (extracted before `AddDocumentAsync`); compute word count there and stamp it on `doc.WordCount` before the eager save. (c) adds an unnecessary write. The mutation in `LeafletIndexingService.IndexAsync` should be removed.

## Implementation Guidance

### Directory / Module Structure
Files to create:
```
backend/src/Anela.Heblo.Domain/Features/Leaflet/
├── ILeafletDocumentRepository.cs
├── ILeafletGenerationRepository.cs
└── UpdateFeedbackResult.cs

backend/src/Anela.Heblo.Persistence/Features/Leaflet/
├── LeafletDocumentRepository.cs
└── LeafletGenerationRepository.cs
```
Files to delete:
```
backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletRepository.cs
backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletRepository.cs
```
DI: replace line 129 of `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` per FR-7.

### Interfaces and Contracts

**`ILeafletDocumentRepository`** — see FR-1 for the 14 verbatim members. **All write methods commit internally.** `AddDocumentAsync` must internally call `_context.SaveChangesAsync(ct)` (not `Task.CompletedTask` as today).

**`ILeafletGenerationRepository`** — 4 existing members + 1 new:
```csharp
Task<UpdateFeedbackResult> UpdateFeedbackAsync(
    Guid generationId,
    int? precisionScore,
    int? styleScore,
    string? comment,
    CancellationToken cancellationToken);
```

**`UpdateFeedbackResult`** — enum in `Domain/Features/Leaflet/`:
```csharp
public enum UpdateFeedbackResult
{
    Updated,
    NotFound,
    AlreadySubmitted,
}
```
The enum lives in `Domain` because both the interface and consuming handlers reference it; the value is a domain outcome, not an infra detail.

### Data Flow

**Indexing flow (FR-4, with Decision 4):**
1. Handler: hash, dedup, extract text.
2. Handler: build `LeafletDocument` with `WordCount = text.Split(...).Length` and `Status = Processing`.
3. Handler: `_documents.AddDocumentAsync(doc, ct)` → eager commit (row created).
4. Handler: `_indexing.IndexAsync(text, doc, ct)` → builds chunks, calls `_documents.AddChunksAsync(chunks, ct)` (raw SQL, auto-commits). No longer mutates `doc.WordCount`.
5. Handler: `_documents.UpdateStatusAsync(doc.Id, Indexed, indexedAt, ct)` → `ExecuteUpdate`.
6. On failure path: `UpdateStatusAsync(..., Failed, null, ct)` (existing `ExecuteUpdate` path).

**Feedback flow (FR-5):**
1. Handler: `_generations.GetGenerationByIdAsync(id, ct)` → load (tracked).
2. Handler: auth check using `ICurrentUserService`; return `Forbidden` early if user mismatch.
3. Handler: `_generations.UpdateFeedbackAsync(id, precision, style, comment, ct)` →
   - Repo loads (returns tracked instance from change tracker, no extra round-trip).
   - Returns `NotFound` if missing, `AlreadySubmitted` if already scored, else sets fields + `SaveChanges` + `Updated`.
4. Handler: switch on result → translate to `ErrorCodes.LeafletFeedbackNotFound` / `ErrorCodes.LeafletFeedbackAlreadySubmitted` / success.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `AddDocumentAsync` eager-commit changes `LeafletDocument.WordCount` persisted value (spec gap — `LeafletIndexingService` mutates WordCount post-save) | **HIGH** | Decision 4: compute WordCount in handler before `AddDocumentAsync`; remove the mutation from `LeafletIndexingService.IndexAsync`. Cover with a test that asserts the persisted `WordCount` matches the source text word count. |
| `SubmitLeafletFeedbackHandlerTests` lines 126–128 assert `generation.PrecisionScore == 4` after handle — relies on local instance mutation that no longer happens | MEDIUM | Add a `Callback` on the `UpdateFeedbackAsync` mock that mutates the local `generation` instance to satisfy the assertion, or change the assertion to verify the mock was called with the expected arguments. Spec says "only mock declarations change"; a callback qualifies. |
| Other handlers/tests indirectly depend on `SaveChangesAsync` being a no-op when nothing is tracked | LOW | Run full Leaflet test suite (`backend/test/Anela.Heblo.Tests/Features/Leaflet/`). All 13 test files are listed in FR-9. |
| Eager commit inside `AddDocumentAsync` interacts badly with the raw `NpgsqlConnection` use in `AddChunksAsync` (which reuses `_context.Database.GetDbConnection()`) | LOW | The connection lifetime is owned by the DbContext, not by `SaveChanges`. Verified by existing integration tests; no code change needed. |
| Divergence from other domain repos that still expose `SaveChangesAsync` reduces consistency for readers | LOW | Note explicitly in PR description that Leaflet sets the pattern; do not back-port to other modules in the same PR (out of scope per spec). |
| `UpdateFeedbackAsync` re-reads the generation inside the repo while the handler already loaded it | LOW | EF Core change tracker returns the same instance for the same key within the scoped DbContext — no extra DB round-trip in the common case. |

## Specification Amendments

1. **Add to FR-4:** The handler MUST compute `LeafletDocument.WordCount` from the extracted text *before* calling `AddDocumentAsync`, and `LeafletIndexingService.IndexAsync` MUST no longer mutate `document.WordCount`. Without this, the eager-commit semantics described in FR-4 silently regress the persisted WordCount value (it stays at 0).

2. **Resolve `UpdateFeedbackAsync` return type inconsistency.** FR-2 describes a `bool` return; the "API / Interface Design" section describes `Task<UpdateFeedbackResult>`. Adopt the enum (`Updated`/`NotFound`/`AlreadySubmitted`) — see Decision 2.

3. **FR-2 / FR-5 clarification:** Authorization (`ICurrentUserService` ownership check) stays in `SubmitLeafletFeedbackHandler` *before* `UpdateFeedbackAsync`. The repository signature stays `(generationId, scores, comment, ct)` — no `userId` parameter — keeping persistence auth-agnostic. See Decision 3.

4. **FR-9 minor accuracy:** "Only mock declarations change" — for `SubmitLeafletFeedbackHandlerTests.Handle_ValidRequest_SavesFeedbackAndReturnsSuccess`, the test asserts mutation on the local `generation` instance. Add a mock `Callback` on `UpdateFeedbackAsync` that writes the scores onto the captured instance, or switch the assertion to `_repo.Verify(r => r.UpdateFeedbackAsync(id, 4, 5, "Very helpful", default), Times.Once)`. Either counts as test-setup change, not assertion semantics change.

## Prerequisites

None. No DB migration, no config, no infrastructure change. The refactor is a single PR:

1. Add `ILeafletDocumentRepository`, `ILeafletGenerationRepository`, `UpdateFeedbackResult` in `Domain/Features/Leaflet/`.
2. Add `LeafletDocumentRepository`, `LeafletGenerationRepository` in `Persistence/Features/Leaflet/` (split the existing class verbatim; `AddDocumentAsync` becomes `_context.LeafletDocuments.Add(document); await _context.SaveChangesAsync(ct);`).
3. Migrate 9 consumers to the narrower interface(s); in `IndexLeafletHandler` move WordCount computation up and remove the two `SaveChangesAsync` calls; in `LeafletIndexingService` remove the `document.WordCount = ...` line; in `SubmitLeafletFeedbackHandler` replace the mutation+SaveChanges block with `UpdateFeedbackAsync` and a switch on `UpdateFeedbackResult`.
4. Update DI in `PersistenceModule.cs:129`.
5. Update 13 test files to mock the narrower interface(s); add the `Callback` noted in amendment 4.
6. Delete `ILeafletRepository.cs` and `LeafletRepository.cs`.
7. `dotnet build` + `dotnet format` + run `Anela.Heblo.Tests` Leaflet suite. `grep -r "ILeafletRepository" backend/` must return zero.
```