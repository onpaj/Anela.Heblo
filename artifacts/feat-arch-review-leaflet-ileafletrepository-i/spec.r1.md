# Specification: Split `ILeafletRepository` by Aggregate Root

## Summary
Split the monolithic `ILeafletRepository` (15 methods, 2 aggregates, 1 infrastructure concern) into two narrowly-scoped repository interfaces aligned with their aggregate roots: `ILeafletDocumentRepository` and `ILeafletGenerationRepository`. Remove the leaky `SaveChangesAsync` infrastructure method from the domain interfaces and handle persistence boundaries internally inside the repository implementations. This is a refactor with no functional/behavioral change for end users; it improves Interface Segregation Principle compliance and Clean Architecture boundary hygiene.

## Background
`backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletRepository.cs` currently defines a single interface that mixes two unrelated aggregate roots and exposes a unit-of-work primitive:

- **Document/Chunk aggregate** (14 methods): document ingestion, chunk storage, vector search, paged document queries.
- **Generation/Feedback aggregate** (4 methods): LLM generation logs and user feedback aggregation.
- **Persistence infrastructure** (1 method): `SaveChangesAsync`, an EF Core unit-of-work abstraction leaked through a domain interface.

This violates the Interface Segregation Principle (ISP). Handlers that depend on document operations (`GetLeafletDocumentsHandler`, `IndexLeafletHandler`, `DeleteLeafletDocumentHandler`, `GetLeafletDocumentContentTypesHandler`, `GetLeafletChunkDetailHandler`, `GenerateLeafletHandler`, `LeafletIndexingService`, `LeafletIngestionJob`) must take a dependency on an interface that also includes feedback/generation queries they never call — and vice versa for the generation handlers (`LeafletGenerationLoggingBehavior`, `GetLeafletGenerationHandler`, `GetLeafletFeedbackListHandler`, `SubmitLeafletFeedbackHandler`). Every new generation query adds methods to mocks in document-handler tests. Additionally, `SaveChangesAsync` exposes an EF Core unit-of-work concept through the domain layer, which is a Clean Architecture boundary leak — the domain should not know that persistence uses a unit-of-work pattern at all.

This refactor was filed by the daily arch-review routine on 2026-05-14.

## Functional Requirements

### FR-1: Introduce `ILeafletDocumentRepository` interface
Define a new domain interface at `backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletDocumentRepository.cs` that contains only document- and chunk-related operations.

**Members (signatures preserved verbatim from the existing interface — no signature changes):**
- `AddDocumentAsync(LeafletDocument document, CancellationToken ct = default)`
- `AddChunksAsync(IEnumerable<LeafletChunk> chunks, CancellationToken ct = default)`
- `GetByHashAsync(string contentHash, CancellationToken ct = default)`
- `GetBySourcePathAsync(string sourcePath, CancellationToken ct = default)`
- `GetByGraphItemIdAsync(string driveId, string graphItemId, CancellationToken ct = default)`
- `DeleteDocumentAsync(Guid id, CancellationToken ct = default)`
- `SearchSimilarAsync(float[] queryEmbedding, int topK, CancellationToken ct = default)`
- `UpdateSourcePathAsync(Guid documentId, string newPath, CancellationToken ct = default)`
- `UpdateGraphItemIdAsync(Guid documentId, string driveId, string graphItemId, CancellationToken ct = default)`
- `UpdateStatusAsync(Guid documentId, LeafletDocumentStatus status, DateTime? indexedAt, CancellationToken ct = default)`
- `GetDocumentsPagedAsync(int pageNumber, int pageSize, string sortBy, bool sortDescending, string? filenameFilter, LeafletDocumentStatus? statusFilter, string? contentTypeFilter, CancellationToken ct = default)`
- `GetDistinctContentTypesAsync(CancellationToken ct = default)`
- `GetChunkByIdAsync(Guid id, CancellationToken ct = default)`
- `GetFirstChunkIdsByDocumentIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)`

**Acceptance criteria:**
- File exists at the path above and compiles.
- All method signatures match the current `ILeafletRepository` byte-for-byte (parameters, defaults, return types, `CancellationToken` naming).
- The interface contains no `SaveChangesAsync` or generation-related methods.
- The interface lives in namespace `Anela.Heblo.Domain.Features.Leaflet`.

### FR-2: Introduce `ILeafletGenerationRepository` interface
Define a new domain interface at `backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletGenerationRepository.cs` that contains only generation- and feedback-related operations.

**Members:**
- `SaveGenerationAsync(LeafletGeneration generation, CancellationToken cancellationToken)`
- `GetGenerationByIdAsync(Guid id, CancellationToken cancellationToken)`
- `GetGenerationsPagedAsync(bool? hasFeedback, string? userId, string sortBy, bool descending, int page, int pageSize, CancellationToken cancellationToken)`
- `GetGenerationStatsAsync(CancellationToken cancellationToken)`
- **New:** `UpdateFeedbackAsync(Guid generationId, int? precisionScore, int? styleScore, string? comment, CancellationToken cancellationToken)` — replaces the current pattern in `SubmitLeafletFeedbackHandler` where the handler mutates a tracked entity and then calls `SaveChangesAsync` on the repository. The repository now encapsulates the load-mutate-save cycle and returns `false` when the generation does not exist or feedback was already submitted (so the handler keeps its current error semantics without touching `SaveChangesAsync`).

**Acceptance criteria:**
- File exists at the path above and compiles.
- Generation methods preserve their current signatures (parameter names and return types unchanged).
- The interface contains no document-related methods and no `SaveChangesAsync`.
- `UpdateFeedbackAsync` signature is documented and reviewed before implementation; see Open Questions if alternative shapes are preferred.

### FR-3: Remove `SaveChangesAsync` from the domain interfaces
Neither `ILeafletDocumentRepository` nor `ILeafletGenerationRepository` exposes `SaveChangesAsync`. The persistence boundary is internalized:

- **Generation repository:** `SaveGenerationAsync` and `UpdateFeedbackAsync` each commit their own unit of work internally (the current `SaveGenerationAsync` already does so on line 246 of `LeafletRepository.cs`).
- **Document repository:** Each write method (`AddDocumentAsync`, `AddChunksAsync`, `UpdateSourcePathAsync`, etc.) MUST commit its own unit of work internally. The existing batched-then-save pattern in `IndexLeafletHandler` (lines 90 and 95) and `LeafletIndexingService` will be refactored to either (a) call individual write methods that auto-commit, or (b) use a new explicit batching primitive on `ILeafletDocumentRepository` (see FR-4).

**Acceptance criteria:**
- Neither domain interface references `SaveChangesAsync`.
- `grep` for `SaveChangesAsync` returns no matches inside `backend/src/Anela.Heblo.Application/Features/Leaflet/`.
- All existing leaflet handlers continue to produce the same observable persistence outcomes as before the refactor (verified by passing the existing test suite under `backend/test/Anela.Heblo.Tests/Features/Leaflet/`).

### FR-4: Preserve transactional batching semantics in `IndexLeafletHandler`
`IndexLeafletHandler` currently:
1. Calls `AddDocumentAsync` (no DB write, just tracks the entity).
2. Calls `AddChunksAsync` (issues raw SQL inserts — these already commit per-row, independent of `SaveChangesAsync`).
3. Calls `SaveChangesAsync` to flush the tracked document.
4. Later calls `UpdateStatusAsync` (an `ExecuteUpdateAsync`, also independent of the unit of work).
5. Calls `SaveChangesAsync` again.

To remove `SaveChangesAsync` from the interface without changing this semantic, the implementation strategy is:

- `AddDocumentAsync` on `ILeafletDocumentRepository` MUST internally commit (i.e. add to the context and call `_context.SaveChangesAsync` before returning), instead of merely tracking the entity.
- `IndexLeafletHandler` no longer calls any `SaveChangesAsync`. The two existing `_repo.SaveChangesAsync(ct)` calls (lines 90 and 95 of `IndexLeafletHandler.cs`) are removed.
- The `AddChunksAsync`, `UpdateSourcePathAsync`, `UpdateGraphItemIdAsync`, `UpdateStatusAsync`, and `DeleteDocumentAsync` implementations already commit independently (they use raw SQL or `ExecuteUpdate/ExecuteDelete`) — no behavioral change there.

**Acceptance criteria:**
- After the refactor, indexing a leaflet end-to-end still results in: a row in `LeafletDocuments`, N rows in `LeafletChunks`, and a final `Indexed` status row. Verified by `LeafletModuleIntegrationTests` and `IndexLeafletStatusTransitionTests`.
- `IndexLeafletHandler` contains no references to `SaveChangesAsync`.
- `LeafletIndexingService` contains no references to `SaveChangesAsync`.

### FR-5: Migrate `SubmitLeafletFeedbackHandler` off `SaveChangesAsync`
Refactor `SubmitLeafletFeedbackHandler` to:
1. Validate the request (user is the owner, feedback not already submitted) using `GetGenerationByIdAsync`.
2. Call `UpdateFeedbackAsync` to persist the feedback in a single atomic operation.
3. Translate the repository's return value into the existing `ErrorCodes.LeafletFeedbackNotFound` / `ErrorCodes.LeafletFeedbackAlreadySubmitted` / success responses.

The handler depends only on `ILeafletGenerationRepository` and `ICurrentUserService`.

**Acceptance criteria:**
- All four existing test behaviors in `SubmitLeafletFeedbackHandlerTests.cs` still pass without modification of test assertions (only mock setup changes).
- The handler no longer calls `SaveChangesAsync`.
- The handler no longer mutates entity fields directly; mutation is encapsulated inside the repository.

### FR-6: Update repository implementation in persistence layer
`backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletRepository.cs` is split into two implementation classes:

- `LeafletDocumentRepository : ILeafletDocumentRepository`
- `LeafletGenerationRepository : ILeafletGenerationRepository`

Both new classes live under `backend/src/Anela.Heblo.Persistence/Features/Leaflet/`. They each take `ApplicationDbContext` via constructor injection. Shared helper code (none exists today) is left duplicated rather than extracted to a base class — YAGNI.

The old `LeafletRepository` class and the old `ILeafletRepository` interface are **deleted** after all references are migrated.

**Acceptance criteria:**
- Both new files exist and compile.
- The old `LeafletRepository.cs` and `ILeafletRepository.cs` files are deleted in the same PR.
- `grep -r "ILeafletRepository" backend/` returns zero matches after the refactor.
- All existing per-method behavior (SQL, query shape, return types, AsNoTracking usage, pgvector handling, command timeouts) is preserved byte-for-byte aside from the `AddDocumentAsync` change in FR-4.

### FR-7: Update DI registration
`backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` line 129 currently registers:
```csharp
services.AddScoped<ILeafletRepository, LeafletRepository>();
```

Replace with:
```csharp
services.AddScoped<ILeafletDocumentRepository, LeafletDocumentRepository>();
services.AddScoped<ILeafletGenerationRepository, LeafletGenerationRepository>();
```

**Acceptance criteria:**
- The application starts under the existing DI container without resolution errors.
- All leaflet handlers resolve their dependencies at runtime.

### FR-8: Update all consumer handlers and services
Each of the 9 production consumers (see Background) is migrated to depend on the narrower interface(s) it actually uses:

| Consumer | New dependency |
|---|---|
| `IndexLeafletHandler` | `ILeafletDocumentRepository` |
| `DeleteLeafletDocumentHandler` | `ILeafletDocumentRepository` |
| `GetLeafletDocumentsHandler` | `ILeafletDocumentRepository` |
| `GetLeafletDocumentContentTypesHandler` | `ILeafletDocumentRepository` |
| `GetLeafletChunkDetailHandler` | `ILeafletDocumentRepository` |
| `GenerateLeafletHandler` | `ILeafletDocumentRepository` (for `SearchSimilarAsync`) |
| `LeafletIndexingService` | `ILeafletDocumentRepository` |
| `LeafletIngestionJob` | `ILeafletDocumentRepository` |
| `LeafletGenerationLoggingBehavior` | `ILeafletGenerationRepository` |
| `GetLeafletGenerationHandler` | `ILeafletGenerationRepository` |
| `GetLeafletFeedbackListHandler` | `ILeafletGenerationRepository` |
| `SubmitLeafletFeedbackHandler` | `ILeafletGenerationRepository` (per FR-5) |

No consumer takes both interfaces. If discovery during implementation finds a consumer that genuinely needs both, that is a signal to revisit the aggregate boundary; raise it as a discussion in the PR rather than papering over with a dual dependency.

**Acceptance criteria:**
- Each handler/service file is updated to reference only the narrower interface(s) it uses.
- Field, parameter, and variable names follow existing project conventions (no purely-cosmetic renames beyond what the interface split requires).
- All Leaflet handler unit tests under `backend/test/Anela.Heblo.Tests/Features/Leaflet/` pass with mocks of the narrower interfaces.

### FR-9: Update tests
All test files under `backend/test/Anela.Heblo.Tests/Features/Leaflet/` that currently mock `ILeafletRepository` are updated to mock the narrower interface(s) their subject under test depends on.

**Affected test files (13):**
- `UseCases/SubmitLeafletFeedbackHandlerTests.cs`
- `UseCases/IndexLeafletStatusTransitionTests.cs`
- `UseCases/IndexLeafletHandlerTests.cs`
- `UseCases/GetLeafletFeedbackListHandlerTests.cs`
- `UseCases/GetLeafletDocumentsHandlerTests.cs`
- `UseCases/GetLeafletDocumentContentTypesHandlerTests.cs`
- `UseCases/GetLeafletChunkDetailHandlerTests.cs`
- `UseCases/GenerateLeafletHandlerTests.cs`
- `UseCases/DeleteLeafletDocumentHandlerTests.cs`
- `Services/LeafletIndexingServiceTests.cs`
- `Pipeline/LeafletGenerationLoggingBehaviorTests.cs`
- `Infrastructure/LeafletModuleIntegrationTests.cs`
- `Infrastructure/LeafletIngestionJobTests.cs`

**Acceptance criteria:**
- All 13 test files compile and pass.
- No test setup mocks methods the handler under test does not call (i.e. the ISP win is realized — a `GetLeafletDocumentsHandler` test no longer needs to know about `SaveGenerationAsync`).
- Existing test assertions are preserved; only mock declarations change.

## Non-Functional Requirements

### NFR-1: Performance
No performance regression. All SQL queries, AsNoTracking usage, raw `NpgsqlCommand` blocks, command timeouts (`CommandTimeout = 120` for vector search), and EF Core query shapes are preserved verbatim. The only behavior change is that `AddDocumentAsync` now commits eagerly instead of tracking-only-until-`SaveChangesAsync`; this adds at most one round-trip to PostgreSQL per indexing operation, which is negligible compared to the embeddings + chunks workload already in flight.

### NFR-2: Security
No security surface change. No new endpoints, no new authentication paths, no new data exposure. Existing authorization logic in `SubmitLeafletFeedbackHandler` (owner check via `ICurrentUserService`) is preserved exactly.

### NFR-3: Backwards compatibility
None required. This is an internal refactor; no public API contracts (HTTP, MediatR requests/responses, OpenAPI schema) change. Database schema is untouched.

### NFR-4: Test coverage
The existing test suite must continue to pass at its current coverage level. No new behavior is introduced, so no new tests are required beyond updates to mock declarations. However, if the refactor reveals previously-hidden coupling or edge cases (e.g. `AddDocumentAsync` semantics changing from track-only to commit), add targeted tests to lock the new behavior.

### NFR-5: Code style
Both new repository classes follow the existing `LeafletRepository.cs` style:
- `sealed` is **not** added unless the original class had it (the original does not — leave it as `public class` to match).
- Nullable reference types enabled (project default).
- `CancellationToken` is passed through every public async method.
- `dotnet format` passes after the change.

## Data Model
No data model changes. The following domain types are unchanged:
- `LeafletDocument` (entity)
- `LeafletChunk` (entity)
- `LeafletGeneration` (entity)
- `LeafletDocumentStatus` (enum)
- `LeafletFeedbackStats` (value object)

Aggregate-root boundaries that drive the interface split (informational only — not codified in code):
- **Document aggregate:** `LeafletDocument` (root) ←—owns— `LeafletChunk` (entity).
- **Generation aggregate:** `LeafletGeneration` (root, contains embedded feedback fields).
- The two aggregates have no shared entities and no direct foreign-key reference. They are linked only logically via vector search (a generation queries chunks via `SearchSimilarAsync` but does not reference them by FK).

## API / Interface Design

### Domain interfaces (new)

**`ILeafletDocumentRepository`** — see FR-1 for full member list. All members preserve current signatures.

**`ILeafletGenerationRepository`** — see FR-2 for full member list. One signature change: addition of `UpdateFeedbackAsync(Guid generationId, int? precisionScore, int? styleScore, string? comment, CancellationToken cancellationToken)`. Return type: `Task<UpdateFeedbackResult>` where `UpdateFeedbackResult` is an enum with values `Updated`, `NotFound`, `AlreadySubmitted`. This keeps the handler in charge of error-code translation without exposing EF Core's tracking model.

### Public API (unchanged)
- MediatR request/response contracts (`SubmitLeafletFeedbackRequest`, etc.) — unchanged.
- HTTP endpoints under `/api/leaflet/*` — unchanged.
- OpenAPI schema — unchanged (no client regeneration needed).

### File layout (post-refactor)
```
backend/src/Anela.Heblo.Domain/Features/Leaflet/
├── ILeafletDocumentRepository.cs       (new)
├── ILeafletGenerationRepository.cs     (new)
├── UpdateFeedbackResult.cs             (new — enum returned by UpdateFeedbackAsync)
├── LeafletChunk.cs                     (unchanged)
├── LeafletDocument.cs                  (unchanged)
├── LeafletDocumentStatus.cs            (unchanged)
├── LeafletFeedbackStats.cs             (unchanged)
└── LeafletGeneration.cs                (unchanged)
backend/src/Anela.Heblo.Persistence/Features/Leaflet/
├── LeafletDocumentRepository.cs        (new)
└── LeafletGenerationRepository.cs      (new)
```
Deleted:
- `backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletRepository.cs`
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletRepository.cs`

## Dependencies
- **EF Core 8** — `ApplicationDbContext` (existing).
- **Npgsql + pgvector** — used by `AddChunksAsync` and `SearchSimilarAsync` (existing).
- **MediatR** — handler dispatch (existing).
- **xUnit + FluentAssertions + Moq/NSubstitute** — test framework (existing).

No new NuGet packages. No new external services. No infrastructure changes (DB schema, indexes, migrations).

## Out of Scope
- **Generic `IRepository<T>` base interface.** Tempting but speculative; introducing it now would be YAGNI given only two repositories share no operations.
- **Explicit `ILeafletUnitOfWork` interface.** The brief floats this as an option if transaction control is needed by handlers; current handlers do not need cross-aggregate transactions, so do not introduce it. Per-method auto-commit covers all current call sites.
- **Refactoring `LeafletRepository.AddChunksAsync` away from raw SQL.** The raw-SQL pgvector insert is intentional for performance; preserve as-is.
- **Renaming or restructuring `LeafletGeneration` to extract feedback as a separate aggregate.** That would be a larger domain change; today, feedback fields are embedded in `LeafletGeneration` and the repository boundary follows the aggregate boundary.
- **Changes to non-Leaflet repositories** even if they exhibit similar ISP issues. One module at a time.
- **API/HTTP contract changes, OpenAPI regeneration, or frontend changes.** None are needed; this refactor is invisible at the API boundary.

## Open Questions
None.

## Status: COMPLETE
