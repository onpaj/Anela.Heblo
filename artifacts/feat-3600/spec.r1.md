# Specification: Batch LeafletChunk inserts in AddChunksAsync

## Summary
`LeafletDocumentRepository.AddChunksAsync` currently inserts each `LeafletChunk` with its own `NpgsqlCommand` inside a sequential `foreach` loop, producing N database round trips per document (~38 for a 30 000-word brochure). This spec defines replacing that loop with a single parameterised multi-row `INSERT`, reducing the write to one round trip while preserving the existing idempotent (`ON CONFLICT DO NOTHING`) semantics, the pgvector column handling, and the EF-mapping-aligned column list. This is a performance refactor of one method; no public contract, schema, or behavior visible to callers changes.

## Background
Leaflet documents (PDFs, marketing brochures) are ingested via OneDrive sync and manual upload. Each document is split into chunks (`ChunkSize = 800` words, `ChunkOverlap = 80` words) and each chunk carries a `vector(1536)` embedding stored in the `LeafletChunks` table via the pgvector extension.

Raw Npgsql is used (rather than EF `AddRange`) because EF Core cannot natively bind pgvector's `vector(1536)` type. That constraint justifies raw SQL, but not the per-row loop: because each insert is `await`ed before the next begins, chunk *i*+1's round trip cannot start until chunk *i* completes. Every ingestion pays this sequential cost. A daily arch-review routine flagged the method (finding filed 2026-07-11).

The current implementation (`backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs`, lines 23–53) obtains the underlying `NpgsqlConnection` from the shared `ApplicationDbContext`, opens it if needed, then loops. The fix stays within this method — connection acquisition, opening logic, and method signature remain unchanged.

## Functional Requirements

### FR-1: Single multi-row INSERT for all chunks
Replace the per-chunk `foreach`/`NpgsqlCommand`/`ExecuteNonQueryAsync` loop with a single `NpgsqlCommand` whose `CommandText` is one `INSERT ... VALUES (row1), (row2), ..., (rowN)` statement, with each row bound to its own uniquely-named parameters.

**Acceptance criteria:**
- Inserting a document that produces N chunks (N ≥ 1) issues exactly one `ExecuteNonQueryAsync` call (one server round trip for the insert), verifiable by test double or command counting.
- All N chunks are persisted with identical column values to the previous implementation (byte-for-byte equivalent rows).
- The parameter values for each row are bound via distinct parameter names (e.g. `@id0..@idN-1`) so no value collision occurs.

### FR-2: Preserve column list and EF-mapping alignment
The `INSERT` column list must remain exactly `("Id", "DocumentId", "ChunkIndex", "Content", "Summary", "WordCount", "Embedding")`, in that order, mirroring `LeafletChunkConfiguration`. The inline comment / reference to `memory/gotchas/raw-sql-insert-must-match-ef-mapping.md` must be retained (updated wording is acceptable, but the caveat must not be lost).

**Acceptance criteria:**
- Column list and order are unchanged from the current statement.
- The gotcha reference is present in the resulting code.

### FR-3: Preserve idempotency via ON CONFLICT
The statement must retain `ON CONFLICT ("Id") DO NOTHING` so that re-ingesting a document (same chunk `Id`s) is a no-op for already-present rows and does not throw.

**Acceptance criteria:**
- Calling `AddChunksAsync` twice with the same chunk set does not throw and results in exactly one copy of each chunk row.
- A partially-overlapping batch (some new `Id`s, some existing) inserts only the new rows.

### FR-4: Correct pgvector embedding binding
Each chunk's `Embedding` (`float[]`) must be wrapped as `new Pgvector.Vector(chunk.Embedding)` and bound to the `Embedding` parameter, exactly as the current code does, so the `vector(1536)` column is populated correctly.

**Acceptance criteria:**
- Persisted `Embedding` values are identical to those written by the current implementation for the same input.
- No implicit string/array serialization of the embedding occurs; the `Vector` type is used.

### FR-5: Handle empty and single-element inputs safely
When the input enumerable is empty, the method must not execute an invalid `INSERT ... VALUES` (empty values list) statement; it should be a no-op (return without executing a command). A single chunk must produce a valid one-row insert.

**Acceptance criteria:**
- `AddChunksAsync(empty)` completes without throwing and issues no INSERT command.
- `AddChunksAsync(single chunk)` inserts exactly that one row.

### FR-6: Respect PostgreSQL parameter limit for large documents
A single Npgsql command supports at most 65 535 bound parameters. With 7 parameters per row, one command can hold ~9 362 rows — far above realistic chunk counts (tens per document). To remain safe for pathological inputs, the implementation must not exceed the parameter limit in a single command: if the chunk count would produce more than a safe threshold of parameters, split into multiple multi-row `INSERT` commands (batches).

**Acceptance criteria:**
- For realistic inputs (≤ ~100 chunks) a single command is used.
- An input large enough to exceed the parameter limit (e.g. > 9 000 chunks) is split into multiple commands, each within the limit, and all rows are still inserted. (This path may be covered by a bounded batch-size constant rather than an explicit huge-input test.)

## Non-Functional Requirements

### NFR-1: Performance
For a document of N chunks, the insert must reduce from N sequential round trips to 1 (or `ceil(N / batchSize)` for oversized inputs). No new per-row `await` in the hot path. Target: a 38-chunk document performs a single insert round trip. Memory overhead (the built SQL string plus parameters) is O(N) and acceptable for expected chunk counts.

### NFR-2: Security
No change to the security posture. All values remain bound as parameters — no chunk field is concatenated into SQL text. The only interpolation into the SQL string is the fixed, code-generated parameter placeholders (e.g. `@id0`), never user/content data. SQL injection surface stays at zero.

### NFR-3: Reliability / correctness
Behavior on conflict, ordering of columns, and the eager-commit semantics of the surrounding ingestion flow are unchanged. The method continues to use the connection obtained from `ApplicationDbContext` and must not alter the connection lifecycle (do not close a connection the DbContext owns).

## Data Model
No schema change.

- **LeafletChunks** — `Id` (Guid, PK), `DocumentId` (Guid, FK → LeafletDocuments), `ChunkIndex` (int), `Content` (text), `Summary` (text), `WordCount` (int), `Embedding` (`vector(1536)`, pgvector). Column list and order are fixed by `LeafletChunkConfiguration`.
- **LeafletDocuments** — parent; unchanged by this work.

Relationship: one `LeafletDocument` has many `LeafletChunk` rows via `DocumentId`.

## API / Interface Design
No public API change. The affected method signature is unchanged:

```csharp
Task AddChunksAsync(IEnumerable<LeafletChunk> chunks, CancellationToken ct = default)
```

Internal shape after the change (illustrative):
- Materialize `chunks.ToList()`.
- If empty, return.
- Acquire and open the `NpgsqlConnection` from the DbContext (unchanged).
- Build one `INSERT` with a `StringBuilder`, appending `(@id{i},@doc{i},...)` per row and `ON CONFLICT ("Id") DO NOTHING` at the end; bind uniquely-named parameters per row (wrapping `Embedding` in `Vector`).
- If chunk count exceeds the safe per-command threshold, loop over batches, each its own command.
- `await cmd.ExecuteNonQueryAsync(ct)` once per command.

`ct` must continue to be passed to `OpenAsync` and `ExecuteNonQueryAsync`.

## Dependencies
- **Npgsql** — already referenced; `NpgsqlCommand`, parameter binding.
- **Pgvector** (`Pgvector.Vector`) — already referenced; embedding binding.
- **pgvector** PostgreSQL extension — already installed for the `vector(1536)` column.
- **ApplicationDbContext** / EF Core — used only to obtain the underlying connection (unchanged).
- No new NuGet packages.

## Out of Scope
- Switching to the binary COPY protocol (`NpgsqlBinaryImporter`). The multi-row `INSERT` is sufficient for expected batch sizes and preserves `ON CONFLICT` semantics, which `COPY` does not support directly. Binary import may be reconsidered later if profiling shows a need, but it is not part of this change.
- Wrapping the inserts in an explicit transaction. Current behavior does not; introducing transactional semantics is a separate decision (see Open Questions — resolved by keeping existing behavior).
- Any change to chunking parameters, embedding generation, the ingestion pipeline, or other repository methods.
- Schema, migration, or `LeafletChunkConfiguration` changes.
- Frontend changes.

## Open Questions
None.

## Status: COMPLETE
