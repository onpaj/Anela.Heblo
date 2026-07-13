# Architecture Review: Batch LeafletChunk inserts in AddChunksAsync

## Skip Design: true

Backend-only performance refactor of a single repository method. No UI components, screens, layouts, or visual decisions are involved. No public contract, schema, or caller-visible behavior changes.

## Architectural Fit Assessment

The change fits cleanly into the existing pattern and requires no new architecture. It is a localized rewrite of one method body.

Relevant grounding from the codebase:

- **Target:** `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs`, method `AddChunksAsync` (lines 23–53). It obtains the underlying `NpgsqlConnection` from the shared `ApplicationDbContext`, opens it if closed, then loops one `NpgsqlCommand` per chunk. Everything but the loop stays.
- **Raw-SQL rationale is real and documented.** `LeafletChunkConfiguration` (`backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletChunkConfiguration.cs:18`) calls `builder.Ignore(x => x.Embedding)` — EF does not map the pgvector column, so the embedding must be written via raw Npgsql with `Pgvector.Vector`. This confirms the "raw SQL is necessary, the per-row loop is not" framing.
- **Column list must mirror EF mapping.** The gotcha at `memory/gotchas/raw-sql-insert-must-match-ef-mapping.md` records a real prior incident (silent `Summary = ""` data loss) and names this exact method. The existing inline comment (line 34) referencing that file must survive the rewrite. The correct column set is `("Id", "DocumentId", "ChunkIndex", "Content", "Summary", "WordCount", "Embedding")`.
- **A sibling method has the identical anti-pattern.** `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs` `AddChunksAsync` (lines 24–53) is the same per-row loop against `KnowledgeBaseChunks`. It is explicitly out of scope for feat-3600 but is the obvious next candidate; keeping the two shaped identically eases a future parallel fix. Do **not** touch it in this change.
- **No abstraction layer to disturb.** `ILeafletDocumentRepository` exposes `AddChunksAsync(IEnumerable<LeafletChunk>, CancellationToken)`; the signature is unchanged, so no interface, DI registration (`LeafletModule`), or caller changes are needed.

Conclusion: the suggested fix in the brief/spec is the right shape and is consistent with every convention in this codebase. Proceed as specified.

## Proposed Architecture

### Component Overview

```
LeafletIndexingService / LeafletIngestionJob (Application)
        │  calls AddChunksAsync(chunks)
        ▼
ILeafletDocumentRepository  (Domain contract — unchanged)
        ▼
LeafletDocumentRepository.AddChunksAsync  (Persistence — THIS CHANGE)
        │  1. chunks.ToList()
        │  2. if empty → return (no command)
        │  3. get NpgsqlConnection from ApplicationDbContext, open if closed
        │  4. for each batch of ≤ BatchSize chunks:
        │        build one multi-row INSERT ... VALUES (row0),(row1),...
        │        + ON CONFLICT ("Id") DO NOTHING
        │        bind uniquely-named params per row (Embedding → new Vector(...))
        │        ExecuteNonQueryAsync(ct)   ← one round trip per batch
        ▼
PostgreSQL "LeafletChunks" (pgvector vector(1536) column)
```

Only the boxed method body changes. Everything above and below the box is untouched.

### Key Design Decisions

#### Decision 1: Multi-row parameterised INSERT vs. NpgsqlBinaryImporter (COPY)

**Options considered:**
- (A) Single multi-row `INSERT ... VALUES (…),(…)` with per-row bound parameters.
- (B) Binary COPY via `NpgsqlBinaryImporter` (`connection.BeginBinaryImport`).

**Chosen approach:** (A) multi-row parameterised INSERT.

**Rationale:** COPY does not support `ON CONFLICT ... DO NOTHING`; preserving idempotent re-ingestion (FR-3, and covered by an existing integration test) would require a staging-table + merge dance that dwarfs the value. Expected batch sizes are tens of chunks (a 30k-word brochure ≈ 38 chunks), where INSERT-vs-COPY throughput is indistinguishable — the win here is round-trip count (N → 1), not raw copy bandwidth. The spec explicitly places COPY out of scope. Confirmed correct.

#### Decision 2: Parameter-limit safety via a fixed batch size

**Options considered:**
- (A) Assume chunk counts are always small; emit one command.
- (B) Chunk the input into batches sized by a compile-time constant so a single command never approaches Npgsql's 65,535-parameter ceiling.

**Chosen approach:** (B), with a named constant.

**Rationale:** 7 params/row → hard ceiling ≈ 9,362 rows/command. Realistic input is far below that, but a pathological document must not throw `NpgsqlException`. A conservative constant (e.g. `MaxRowsPerBatch = 1000`, ~7,000 params — comfortably under the limit and well within a readable SQL string) makes the method total-over-input-size without a per-input guard. Realistic inputs (≤ ~100 chunks) still emit exactly one command, satisfying FR-1 and FR-6 simultaneously.

#### Decision 3: Parameter binding — reuse the existing `AddWithValue` + `Vector` approach

**Options considered:** typed `NpgsqlParameter` construction vs. the current `AddWithValue`.

**Chosen approach:** keep `AddWithValue`, wrapping `Embedding` as `new Vector(chunk.Embedding)` exactly as today, just with indexed names (`id0`, `doc0`, …).

**Rationale:** minimal diff, matches the established convention in both this method and `SearchSimilarAsync`. The pgvector data-source is already configured with `UseVector()` (see the integration test setup), so `Vector` binds correctly. No reason to change the binding mechanism.

## Implementation Guidance

### Directory / Module Structure

No new files. Single edit:

- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs` — replace the body of `AddChunksAsync` (lines 25–52). Add one `private const int` for the batch size at class scope.

Add/adjust tests in the existing file:

- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletRepositoryIntegrationTests.cs` (Testcontainers `pgvector/pgvector:pg16`, already exercises `AddChunksAsync` for single-chunk, idempotency, and multi-chunk search). Note this test schema declares `Embedding vector(3)` — multi-row inserts with 3-dim vectors work identically to 1536-dim, so the existing harness is sufficient. No production `vector(1536)` dependency in tests.

### Interfaces and Contracts

Unchanged. Signature stays:

```csharp
Task AddChunksAsync(IEnumerable<LeafletChunk> chunks, CancellationToken ct = default)
```

Invariants the implementation must hold:
- Column list and order: `("Id", "DocumentId", "ChunkIndex", "Content", "Summary", "WordCount", "Embedding")` — mirror `LeafletChunkConfiguration`; keep the `memory/gotchas/raw-sql-insert-must-match-ef-mapping.md` comment.
- Trailing `ON CONFLICT ("Id") DO NOTHING` on every emitted command.
- Every value bound as a parameter; only fixed, code-generated placeholder names (`@id{i}`) may be interpolated into SQL text (NFR-2). Never concatenate chunk field values.
- `ct` passed to `OpenAsync` and every `ExecuteNonQueryAsync`.
- Do not close/dispose the connection — it is owned by `ApplicationDbContext` (NFR-3). The current code correctly never disposes it; preserve that.
- Empty input → return before building/executing any command (FR-5).

### Data Flow

1. Caller (`LeafletIndexingService` / `LeafletIngestionJob`) produces `LeafletChunk` list (with embeddings) and calls `AddChunksAsync`.
2. Method materializes to a list; if empty, returns.
3. Acquires the DbContext's `NpgsqlConnection`, opens if closed.
4. For each slice of ≤ `MaxRowsPerBatch` chunks: build one INSERT via `StringBuilder`, append `(@id{i},@doc{i},…)` per row, bind indexed params (wrap `Embedding` in `Vector`), append `ON CONFLICT DO NOTHING`, `ExecuteNonQueryAsync(ct)`.
5. Rows land in `LeafletChunks`; already-present `Id`s are skipped by conflict clause. Read paths (`SearchSimilarAsync`, `GetChunkByIdAsync`) are unaffected.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Column list drifts from EF mapping again (repeat of the `Summary` incident) | High | Keep the exact 7-column list and the inline gotcha comment; existing `AddChunksAsync_PersistsSummary` integration test guards `Summary`. |
| Parameter placeholder / value index mismatch when generating `@id{i}` names | Medium | Bind params in the same loop that appends the placeholder; add a multi-chunk (≥2) round-trip test asserting all rows persist with correct per-row values. |
| Exceeding Npgsql's 65,535-param limit on pathological documents | Low | Fixed `MaxRowsPerBatch` constant well under the ceiling; loop over batches. |
| Embedding written as string/array instead of `vector` | Medium | Reuse `new Vector(chunk.Embedding)` binding (unchanged); existing `SearchSimilarAsync` test verifies vector semantics end-to-end. |
| Idempotency regression (double-insert throws) | Medium | Retain `ON CONFLICT ("Id") DO NOTHING` on every command; existing `AddChunksAsync_IsIdempotent_WhenCalledTwiceWithSameId` test guards it. |
| Empty-input emits invalid `VALUES ()` SQL | Low | Early return on empty list (FR-5); add a test asserting no throw and no rows for empty input. |
| Accidentally disposing the DbContext-owned connection | Low | Do not wrap the connection in `await using`; only the command is disposed. Matches current code. |

## Specification Amendments

None required — the spec is complete, accurate, and consistent with the codebase. Two clarifying notes for the implementer (not spec changes):

1. **Batch constant naming/value.** The spec leaves the threshold as "safe" (FR-6). Recommend a named `private const int MaxRowsPerBatch = 1000;` (≈7,000 params) rather than computing against 65,535 at runtime — simpler, obviously safe, and keeps generated SQL readable. Any value ≤ ~9,000 satisfies the requirement.
2. **Test additions.** Current integration tests cover single-chunk persist, idempotency, and 2-chunk search, but none asserts the FR-1 "exactly one command / one round trip" property or a batch with N ≥ 3 preserving per-row values. Add: (a) a multi-chunk (e.g. 5) insert asserting all rows present with correct distinct values, and (b) an empty-input no-op test. The "exactly one round trip" claim is hard to assert against Testcontainers without command interception; treat it as covered by construction (single `ExecuteNonQueryAsync` per batch) plus the multi-row correctness test, rather than adding a mock-based counting harness.

## Prerequisites

None. No migration, schema, config, infrastructure, or new NuGet dependency. `Npgsql` and `Pgvector` are already referenced; the pgvector extension and `vector(1536)` column already exist. Implementation can start immediately.
