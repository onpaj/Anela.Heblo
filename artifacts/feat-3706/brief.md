## Module / File
`backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs`

## Coverage
Line coverage: 7.8% (filter threshold: 60%)

## What's not tested
**`AddChunksAsync`** — the batch-insert loop never executes its multi-batch path. `MaxRowsPerBatch = 1000` means any call with ≤ 1000 chunks hits the loop only once; the boundary case where a second batch is needed is never exercised. The connection-state guard (`if (connection.State != Open)`) is also uncovered.

**`SearchSimilarAsync`** — the raw-SQL vector similarity search is entirely uncovered: result reader column positions (ordinals 0–8), 120-second timeout, and the `LeafletChunk` → `LeafletDocument` reconstruction from the reader.

**`GetDocumentsPagedAsync`** — all three optional filters (`filenameFilter`, `statusFilter`, `contentTypeFilter`) are untested, as is the four-way sort switch (`Filename`, `Status`, `IndexedAt`, default) in both ascending and descending directions. Any typo in the `Like` escape logic or an off-by-one in the sort switch could silently hide or mis-order documents.

## Why it matters
- A bug in the batch-boundary logic could silently drop chunks from the second batch or insert them twice, corrupting the leaflet vector index.
- Wrong reader ordinals in `SearchSimilarAsync` would return garbage embeddings or throw at runtime.
- An error in the filter/sort switch in `GetDocumentsPagedAsync` would make documents invisible or unsortable in the UI without any test catching it.

## Suggested approach
Unit/integration tests with an in-memory or test Postgres connection:
1. `AddChunksAsync` with 1001 chunks — verify two SQL executions and row count.
2. `SearchSimilarAsync` with a known embedding — verify chunk fields match inserted rows.
3. `GetDocumentsPagedAsync` with each filter independently and a few sort/direction combinations (≈ medium effort).

---
_Filed by weekly coverage-gap routine on 2026-07-20. Based on CI run #29525794843 (bba537b141de1dba71a2c6853c4ff3f7e96153b2)._
