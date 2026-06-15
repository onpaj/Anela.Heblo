# Architecture Review: Persist Summary Column in LeafletChunk Raw SQL Insert

## Skip Design: true

Pure backend persistence bug fix. No UI/UX components, screens, or visual changes are introduced — the user-visible effect (non-empty `summary` in `GetLeafletChunkDetailResponse`) is delivered by an unmodified existing endpoint and unmodified existing frontend code.

## Architectural Fit Assessment

The fix slots perfectly into existing conventions. The codebase already has a sibling implementation that does exactly this correctly: `KnowledgeBaseRepository.AddChunksAsync` (`backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs:34-49`) lists `"Summary"` in its raw `INSERT` column list and binds `cmd.Parameters.AddWithValue("summary", chunk.Summary)`. The Leaflet repository is the outlier — it was simply not updated when migration `20260505080157_AddSummaryToLeafletChunk` introduced the column.

Integration points:
- **EF configuration** (`LeafletChunkConfiguration.cs:16`): already declares `Summary` as required — no change.
- **Producer** (`LeafletIndexingService.cs:47-54`): already assigns `chunk.Summary = summary` — no change.
- **Schema** (migration `20260505080157`): column exists with `defaultValue: ""` — no change.
- **Consumer** (`GetChunkByIdAsync` → `GetLeafletChunkDetailResponse`): already projects `Summary` through EF — no change.

The only divergence from existing patterns is in the **test architecture**: `LeafletRepositoryTests.cs` is built on EF Core's in-memory provider, which cannot exercise the raw `NpgsqlCommand` path. The two existing `AddChunksAsync` / `SearchSimilarAsync` tests are `[Fact(Skip = "Requires PostgreSQL with pgvector")]`. A genuine regression test for FR-2 therefore requires a new test class using the established Testcontainers-with-pgvector pattern from `KnowledgeBaseRepositoryIntegrationTests`.

## Proposed Architecture

### Component Overview

```
LeafletIndexingService.IndexAsync
        |
        | builds LeafletChunk { ..., Summary = <LLM summary>, ... }
        v
ILeafletDocumentRepository.AddChunksAsync
        |
        | raw NpgsqlCommand → INSERT into "LeafletChunks"
        |   (Id, DocumentId, ChunkIndex, Content, Summary*, WordCount, Embedding)
        |                                          ^
        |                                          +-- column added by this fix
        v
PostgreSQL "LeafletChunks" table
        |
        | EF read path (GetChunkByIdAsync / GetLeafletChunkDetailQueryHandler)
        v
GetLeafletChunkDetailResponse.summary  (correct value, not "")
```

No new components. The fix is one production-code change in one method plus one integration test class.

### Key Design Decisions

#### Decision 1: Where the regression test lives and what infrastructure it uses

**Options considered:**
1. Add the test to existing `LeafletRepositoryTests.cs` (InMemory EF). Rejected — InMemory cannot execute raw SQL or pgvector types; the bug would slip past such a test even after a "fix".
2. Mock `NpgsqlCommand` / `NpgsqlConnection`. Rejected — the bug *is* in the raw SQL string; mocking the SQL boundary moves the assertion to a fiction.
3. New `LeafletRepositoryIntegrationTests` class using Testcontainers + `pgvector/pgvector:pg16`, mirroring `KnowledgeBaseRepositoryIntegrationTests`.

**Chosen approach:** Option 3.

**Rationale:** The KnowledgeBase integration test class already demonstrates the exact pattern needed (pgvector container, manual `SetupSchemaAsync`, `[Trait("Category", "Integration")]`, `IAsyncLifetime`). Reusing the pattern keeps test architecture consistent across the two RAG repositories and makes the test execute the real raw-SQL path that contains the bug. The shared `PostgresSharedContainerFixture` cannot be reused because it uses `postgres:16` without the pgvector extension and `LeafletChunk.Embedding` is a `vector` column.

#### Decision 2: Column position in the INSERT list

**Options considered:**
1. Append `"Summary"` at the end (after `"Embedding"`).
2. Insert `"Summary"` between `"Content"` and `"WordCount"`.

**Chosen approach:** Option 2 — `("Id", "DocumentId", "ChunkIndex", "Content", "Summary", "WordCount", "Embedding")`.

**Rationale:** Matches the property declaration order in `LeafletChunkConfiguration.Configure` (Content → Summary → WordCount, lines 15–17) and matches the order used by `KnowledgeBaseRepository.AddChunksAsync`. PostgreSQL does not care about column-list ordering, but consistency with the EF configuration reduces the recurrence risk when columns are added later.

#### Decision 3: Backfill strategy for chunks ingested before the fix

**Options considered:**
1. Ship a one-off SQL migration that calls the LLM summarizer for each existing chunk.
2. Ship no backfill; rely on the existing re-ingestion path (`LeafletIndexingService.IndexAsync` re-running for a document) to repopulate.
3. Add a Hangfire job that walks `LeafletChunks WHERE Summary = ''` and re-summarizes.

**Chosen approach:** Option 2.

**Rationale:** The spec explicitly scopes backfill out. The existing re-ingestion path already handles this correctly post-fix. A Hangfire batch would re-introduce LLM cost (the original symptom we are fixing) without an operator decision. Document the gap in `memory/gotchas/` so operators can choose deliberately later.

## Implementation Guidance

### Directory / Module Structure

Files to **modify**:
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs` — single method (`AddChunksAsync`, lines 34–47).

Files to **create**:
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletRepositoryIntegrationTests.cs` — new test class with `[Trait("Category", "Integration")]`, modeled on `backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs`. It must:
  - Use `PostgreSqlBuilder().WithImage("pgvector/pgvector:pg16")`.
  - Build `NpgsqlDataSourceBuilder` with `.UseVector()`.
  - Set `TestcontainersSettings.ResourceReaperEnabled = false` in a static constructor (Podman compatibility — confirmed convention).
  - Create the `LeafletDocuments` and `LeafletChunks` tables in `SetupSchemaAsync()` including the `Summary text NOT NULL DEFAULT ''` column and an `Embedding vector(<dim>)` column matching the production embedding dimension.
- `memory/gotchas/raw-sql-insert-must-match-ef-mapping.md` — short note: when a column is added via migration and an entity uses a raw-SQL insert path (`LeafletDocumentRepository`, `KnowledgeBaseRepository`), both the INSERT column list AND the parameter bindings must be updated alongside the EF configuration. Reference this incident, list the two known raw-SQL repositories, and call out that pre-fix `LeafletChunks` rows (ingested before this commit) have `Summary = ''` and need re-ingestion to recover.

Files explicitly **not** touched:
- `LeafletIndexingService.cs`
- `LeafletChunk.cs`, `LeafletChunkConfiguration.cs`
- Any frontend code, DTO, or migration
- The skipped tests in `LeafletRepositoryTests.cs` — leave them as-is per "surgical changes"; the new integration class supersedes them organically.

### Interfaces and Contracts

No interface changes. `ILeafletDocumentRepository.AddChunksAsync` signature is unchanged. `LeafletChunk` shape is unchanged.

The only contract change is the implicit "raw SQL ↔ EF mapping" invariant — surfaced and codified in the new gotcha note rather than enforced at the type system level (out of scope per spec).

### Data Flow

For the regression test (FR-2):

```
[Arrange]
  container start (pgvector/pgvector:pg16)
  ApplicationDbContext over Npgsql data source with UseVector()
  manual CREATE TABLE for LeafletDocuments + LeafletChunks (incl. Summary, Embedding)
  insert a LeafletDocument via EF
  build LeafletChunk { Summary = "Test summary content", Embedding = new float[<dim>] }

[Act]
  await repository.AddChunksAsync(new[] { chunk })

[Assert]
  var roundTripped = await _context.LeafletChunks.AsNoTracking().FirstAsync(c => c.Id == chunk.Id)
  Assert.Equal("Test summary content", roundTripped.Summary)
```

This test fails on current `main` (Summary persists as `""` because the parameter is never bound) and passes after the one-line fix — satisfying FR-2's "fails before, passes after" criterion.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| New integration test inflates CI duration due to container startup. | LOW | Tag with `[Trait("Category", "Integration")]` consistent with `KnowledgeBaseRepositoryIntegrationTests`; runs alongside existing integration suite, no incremental container per class (KnowledgeBase already pays this cost). |
| Embedding column dimension mismatch in test schema vs production. | MEDIUM | Production embedding dimension is set by `_embeddings.GenerateAsync` output (`Microsoft.Extensions.AI`). Test schema can use any dimension (e.g. `vector(3)`) and the test must construct a matching-length `float[]`. Document this in a comment to prevent future "fix the test by changing prod schema" reflex. |
| Operator does not know pre-fix chunks remain empty after deployment. | MEDIUM | Gotcha note in `memory/gotchas/` documenting that re-ingestion is the recovery path. No automated backfill (per spec). |
| Future migrations add a new column and the raw INSERT is silently missed again. | MEDIUM | Gotcha note explicitly lists the two raw-SQL repositories (`LeafletDocumentRepository`, `KnowledgeBaseRepository`) as places to update. A generalized reflection-based guardrail is desirable but out of scope. |
| Podman vs Docker host divergence on the test machine. | LOW | The static-ctor `TestcontainersSettings.ResourceReaperEnabled = false` is the established convention in this repo (`PostgresSharedContainerFixture`, `KnowledgeBaseRepositoryIntegrationTests`). Replicate verbatim. |

## Specification Amendments

1. **FR-2 wording is misleading about reuse.** The spec says "the existing integration-test fixture used by other leaflet repository tests" — but no such fixture exists. The Leaflet repository's only test class uses InMemory EF and the two PostgreSQL-dependent tests are `[Fact(Skip = ...)]`. Amend FR-2 to: *"Test calls `AddChunksAsync` against a real PostgreSQL test database via a new `LeafletRepositoryIntegrationTests` class modeled on `KnowledgeBaseRepositoryIntegrationTests`."*

2. **FR-3 acceptance criterion location.** The spec mentions "a note is added to `memory/gotchas/` (or equivalent)". Confirm the canonical location: `memory/gotchas/` exists in this repo and already contains entries like `ef-migration-codebase-drift.md`. Recommend the filename `raw-sql-insert-must-match-ef-mapping.md` to make the gotcha discoverable by future contributors touching either `LeafletDocumentRepository` or `KnowledgeBaseRepository`.

3. **NFR-2 optional code comment.** Recommend making the one-line comment *non-optional* and pointing it at the gotcha file, not only at `LeafletChunkConfiguration`. Suggested: `// Column list MUST mirror LeafletChunkConfiguration. See memory/gotchas/raw-sql-insert-must-match-ef-mapping.md`.

4. **Out-of-scope clarification.** The spec correctly excludes "a generalized guardrail". Recommend adding to Out of Scope: *"Migrating the two currently-skipped tests in `LeafletRepositoryTests.cs` to the new integration fixture."* They are skipped because they need pgvector; the new fixture makes them runnable, but moving them is a separate cleanup.

## Prerequisites

None blocking implementation. All of the following already exist on the branch:

- Schema column: migration `20260505080157_AddSummaryToLeafletChunk` already applied to dev/staging databases.
- Testcontainers dependency: `Testcontainers.PostgreSql` and `DotNet.Testcontainers` are already referenced (`KnowledgeBaseRepositoryIntegrationTests` uses them).
- pgvector image: `pgvector/pgvector:pg16` is already pulled by the existing KnowledgeBase integration test.
- Podman/Docker socket available on the developer machine and CI runner (existing integration tests already depend on this).

Implementation can begin immediately.