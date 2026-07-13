# Design: Batch LeafletChunk inserts in AddChunksAsync

## Component Design

No new components. One method body is rewritten; everything around it (interface, DI, callers) is untouched.

- **`LeafletDocumentRepository.AddChunksAsync`** (`backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs`, currently lines 23–53)
  Signature unchanged: `Task AddChunksAsync(IEnumerable<LeafletChunk> chunks, CancellationToken ct = default)`.
  Responsibility: persist a batch of `LeafletChunk` rows for a document in as few round trips as possible, preserving idempotency and pgvector binding.
  New internal shape:
  1. `var list = chunks.ToList();` — if `list.Count == 0`, return immediately (no command built or executed).
  2. Acquire the `NpgsqlConnection` from `ApplicationDbContext` exactly as today; open it if closed; never dispose/close it here (connection lifecycle owned by the DbContext).
  3. Split `list` into slices of at most `MaxRowsPerBatch` rows (new `private const int MaxRowsPerBatch = 1000;` at class scope).
  4. For each slice, build one `NpgsqlCommand` via `StringBuilder`:
     - Column list fixed as `("Id", "DocumentId", "ChunkIndex", "Content", "Summary", "WordCount", "Embedding")`, matching `LeafletChunkConfiguration`. Retain the existing inline comment referencing `memory/gotchas/raw-sql-insert-must-match-ef-mapping.md`.
     - Append one `(@id{i}, @doc{i}, @idx{i}, @content{i}, @summary{i}, @wc{i}, @emb{i})` group per row in the slice, `i` being the row's index within that slice (each slice restarts its own local parameter numbering, so no command ever needs more than `MaxRowsPerBatch * 7` parameters).
     - Bind each parameter with `AddWithValue` in the same loop that appends its placeholder, so index and value never drift apart. `Embedding` is wrapped as `new Pgvector.Vector(chunk.Embedding)`, unchanged from today.
     - Append a single trailing `ON CONFLICT ("Id") DO NOTHING` for the whole statement.
     - `await cmd.ExecuteNonQueryAsync(ct)` — one call per slice/batch.
  5. Realistic inputs (tens of chunks) always take the single-slice path: one `ExecuteNonQueryAsync` call total. Only pathological inputs (> ~1000 chunks) incur `ceil(N / MaxRowsPerBatch)` calls.

- **Callers** (`LeafletIndexingService` / ingestion job) — no change; they call `AddChunksAsync(chunks)` exactly as before.
- **`ILeafletDocumentRepository`** — no change.
- **`KnowledgeBaseRepository.AddChunksAsync`** — explicitly out of scope; not touched, left with its existing per-row loop for a possible future, separate fix.

## Data Schemas

No schema change. Documented here only to confirm the INSERT shape matches the existing table exactly.

**Table: `LeafletChunks`**

| Column | Type | Notes |
|---|---|---|
| `Id` | `uuid` (PK) | one param per row, e.g. `@id0` |
| `DocumentId` | `uuid` (FK → `LeafletDocuments`) | `@doc0` |
| `ChunkIndex` | `int` | `@idx0` |
| `Content` | `text` | `@content0` |
| `Summary` | `text` | `@summary0` |
| `WordCount` | `int` | `@wc0` |
| `Embedding` | `vector(1536)` (pgvector) | `@emb0`, bound as `new Pgvector.Vector(chunk.Embedding)` |

Generated SQL shape (illustrative, 2-row batch):

```sql
INSERT INTO "LeafletChunks"
    ("Id", "DocumentId", "ChunkIndex", "Content", "Summary", "WordCount", "Embedding")
VALUES
    (@id0, @doc0, @idx0, @content0, @summary0, @wc0, @emb0),
    (@id1, @doc1, @idx1, @content1, @summary1, @wc1, @emb1)
ON CONFLICT ("Id") DO NOTHING;
```

No API request/response shapes or event payloads are affected — this is a private repository-method internal to the persistence layer, invoked in-process with no wire contract.
