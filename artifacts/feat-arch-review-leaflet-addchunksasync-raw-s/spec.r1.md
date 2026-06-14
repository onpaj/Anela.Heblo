# Specification: Persist Summary Column in LeafletChunk Raw SQL Insert

## Summary
Fix `LeafletDocumentRepository.AddChunksAsync` so the raw SQL `INSERT` writes the `Summary` column. The column is currently omitted, causing every persisted leaflet chunk to store an empty string even though the LLM summarizer produces a value. This is a one-file bug fix with a regression test, no schema or migration changes.

## Background
Leaflet ingestion runs through `LeafletIndexingService.IndexAsync`, which calls an LLM via `_summarizer.SummarizeAsync` for each chunk and assigns the result to `LeafletChunk.Summary`. The chunk is then persisted by `LeafletDocumentRepository.AddChunksAsync`, which uses a hand-written `NpgsqlCommand` to bypass EF Core for bulk insertion performance.

The hand-written SQL was last updated before migration `20260505080157_AddSummaryToLeafletChunk` introduced the `Summary` column (with `defaultValue: ""`). The migration added the schema and updated the EF `LeafletChunkConfiguration` (which marks `Summary` as required), but the raw INSERT in `AddChunksAsync` was not updated to match. The result is silent data loss: the LLM call succeeds, the in-memory `chunk.Summary` is populated, but the database stores `""` because the column is never bound.

User-visible impact: `GetLeafletChunkDetailResponse.summary` returned to the frontend is always blank, so the chunk-detail UI cannot show the generated summary. Cost impact: every ingestion pays for LLM summarization tokens that are discarded.

## Functional Requirements

### FR-1: Include Summary in the raw INSERT
`LeafletDocumentRepository.AddChunksAsync` must persist the `Summary` value of each `LeafletChunk` to the `"Summary"` column.

**Acceptance criteria:**
- The `INSERT` column list includes `"Summary"` between `"Content"` and `"WordCount"` (matching the suggested column order in the brief).
- The `VALUES` clause includes the `@summary` parameter in the same position.
- `cmd.Parameters.AddWithValue("summary", chunk.Summary)` is bound for every chunk in the loop.
- `ON CONFLICT ("Id") DO NOTHING` semantics are preserved unchanged.

### FR-2: Round-trip persistence test
A repository-level integration test must demonstrate that `chunk.Summary` survives the round trip through `AddChunksAsync` and is readable from the database.

**Acceptance criteria:**
- Test arranges a `LeafletChunk` with a non-empty `Summary` (e.g., `"Test summary content"`).
- Test calls `AddChunksAsync` against a real PostgreSQL test database (or the existing integration-test fixture used by other leaflet repository tests).
- Test reads the row back via EF (`DbContext.LeafletChunks.FindAsync`) and asserts `Summary` equals the input value.
- Test fails on the current code (proves the regression) and passes after the fix.

### FR-3: Backfill consideration for existing chunks
Existing rows ingested before the fix have `Summary = ""`. No automatic backfill is required as part of this fix, but the existing re-indexing path (`LeafletIndexingService` re-running over a document) must produce correct summaries on re-ingestion after the fix is deployed.

**Acceptance criteria:**
- No new migration or backfill script is shipped with this fix.
- A note is added to `memory/gotchas/` (or equivalent) documenting that pre-fix chunks have empty summaries and need re-indexing to recover.

## Non-Functional Requirements

### NFR-1: Performance
- No regression in `AddChunksAsync` throughput. Adding one parameter binding per chunk is negligible.
- The fix must not change the bulk-insert pattern (single `NpgsqlCommand` per chunk, no EF change tracking).

### NFR-2: Correctness and maintainability
- After the fix, the raw SQL column list matches the `LeafletChunkConfiguration` mapping exactly for all persisted columns.
- A short code comment (one line) above the raw SQL block referencing `LeafletChunkConfiguration` is acceptable to reduce the risk of recurrence when columns are added in the future. Optional.

### NFR-3: Security
- No security impact. `Summary` is LLM-generated text from internal pipelines; it is already trusted output written to a Postgres `text` column via a parameterized `NpgsqlCommand`. No injection surface change.

## Data Model
No schema changes. The relevant entity is `LeafletChunk`:

- `Id` (Guid, PK)
- `DocumentId` (Guid, FK → `LeafletDocuments.Id`)
- `ChunkIndex` (int)
- `Content` (text, required)
- `Summary` (text, required, default `""`) — **the column this fix persists**
- `WordCount` (int)
- `Embedding` (`vector` / pgvector type)

`LeafletChunkConfiguration` already declares `Summary` as required. Migration `20260505080157_AddSummaryToLeafletChunk` already created the column with `defaultValue: ""`.

## API / Interface Design
No public API changes. The bug fix is purely internal to the persistence layer.

Indirect user-visible change: after deployment and re-ingestion, `GetLeafletChunkDetailResponse.summary` will contain the actual LLM-generated summary instead of `""`. No DTO or contract change is required.

## Dependencies
- Existing PostgreSQL database and `LeafletChunks` table.
- Existing `Npgsql` package already in use by the repository.
- Existing integration-test infrastructure used by other leaflet repository tests (for FR-2).

No new external services, libraries, or features.

## Out of Scope
- Migrating the raw SQL path to EF Core bulk insert. The performance reason for raw SQL remains valid.
- Adding a generalized guardrail (e.g., a test that compares the raw SQL column list against the EF mapping reflectively). Useful but a separate refactor.
- Backfilling `Summary` for chunks ingested before the fix. Re-indexing is the recovery path.
- Changes to `LeafletIndexingService`, `Summarizer`, or any frontend code.
- Modifying the `ON CONFLICT` behavior or any other column.

## Open Questions
None.

## Status: COMPLETE