## Module
Leaflet

## Finding
`LeafletDocumentRepository.AddChunksAsync` (`backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs`, lines 33–50) inserts chunks via raw SQL that lists only six columns:

```sql
INSERT INTO "LeafletChunks" ("Id", "DocumentId", "ChunkIndex", "Content", "WordCount", "Embedding")
VALUES (@id, @documentId, @chunkIndex, @content, @wordCount, @embedding)
ON CONFLICT ("Id") DO NOTHING
```

The `"Summary"` column is absent from both the column list and the parameter bindings. Yet:

- `LeafletIndexingService.IndexAsync` (lines 46–54) calls `_summarizer.SummarizeAsync` (an LLM call) for every chunk and stores the result in `chunk.Summary`.
- `LeafletChunkConfiguration` (`LeafletChunkConfiguration.cs`, line 16) marks `Summary` as `IsRequired()`.
- The column was added in migration `20260505080157_AddSummaryToLeafletChunk` with `defaultValue: ""`.

Because the raw INSERT never writes `Summary`, every persisted chunk has an empty string in that column regardless of what the summarizer returned. The `GetLeafletChunkDetailResponse.summary` field returned to the frontend is therefore always `""`.

## Why it matters
- **Silent data loss**: LLM summarization cost is paid on every ingestion, but the results are permanently discarded.
- **Broken feature**: The chunk-detail UI shows `summary` to the user; it will always be blank.
- **Correctness gap between EF config and raw SQL**: the EF `Configuration` knows about `Summary`; the hand-written INSERT does not — a maintenance hazard that will recur if more columns are added to `LeafletChunk`.

## Suggested fix
Add `"Summary"` to the INSERT column list and bind the parameter:

```csharp
// LeafletDocumentRepository.cs — AddChunksAsync (lines 34–49)
await using var cmd = new NpgsqlCommand(
    """
    INSERT INTO "LeafletChunks" ("Id", "DocumentId", "ChunkIndex", "Content", "Summary", "WordCount", "Embedding")
    VALUES (@id, @documentId, @chunkIndex, @content, @summary, @wordCount, @embedding)
    ON CONFLICT ("Id") DO NOTHING
    """,
    connection);

// ... existing parameters ...
cmd.Parameters.AddWithValue("summary", chunk.Summary);
```

No migration is needed — the column already exists with a default value. The fix is a one-line column addition in the SQL and a one-line parameter binding.

---
_Filed by daily arch-review routine on 2026-06-12._