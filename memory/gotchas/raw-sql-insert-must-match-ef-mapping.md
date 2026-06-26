# Gotcha: Raw SQL INSERT Must Match EF Mapping

## Symptom

A new column is added to an entity via EF Core migration. The migration updates the entity configuration and marks the new column as required. But when data is persisted, the new column contains empty/default values even though upstream code populated it correctly. The data loss is silent — no exceptions, no logs, no warnings.

**Concrete instance:** Migration `20260505080157_AddSummaryToLeafletChunk` added `Summary` (required string) to the `LeafletChunks` table and updated `LeafletChunkConfiguration` accordingly. The raw SQL INSERT in `LeafletDocumentRepository.AddChunksAsync` was never updated, causing every persisted chunk to store `Summary = ""` instead of the LLM-generated summary.

## Root cause

When an entity has **both** an EF Core mapping **and** a raw SQL INSERT path (using `NpgsqlCommand`), the two must stay synchronized. The migration updates the EF configuration, but the raw INSERT statement's column list and parameter bindings are written by hand and can drift independently. Unlike EF inserts, raw SQL has no automatic schema awareness — it blindly executes the SQL string you provide.

In this case:
- The migration applied the new column to the physical table.
- The EF configuration knew about it (mapping + required constraint).
- The raw INSERT statement still listed only the old columns (`Id`, `DocumentId`, `ChunkIndex`, `Content`, `WordCount`).
- PostgreSQL allowed the insert because `Summary` had a server-side default (empty string).
- The application then read the persisted rows back through EF, which populated `Summary` from the database default, not from the LLM context.

## Affected repositories

Two raw-SQL repositories are known to have INSERT paths that must be kept in sync with migrations:

1. **`backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs`**
   - Method: `AddChunksAsync()`
   - Inserts into `LeafletChunks` using raw `NpgsqlCommand`
   - A one-line comment in the method now references this file as a reminder

2. **`backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs`**
   - Method: `AddChunksAsync()`
   - Inserts into `KnowledgeBaseChunks` (or equivalent) using raw `NpgsqlCommand`
   - Update this method whenever the `KnowledgeBaseChunk` entity gains a new column

3. **`backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs`**
   - Method: `UpsertAsync()`
   - Uses raw `INSERT ... ON CONFLICT ... DO UPDATE` via `ExecuteSqlInterpolatedAsync` to make the upsert atomic.
   - If a new required column is added to `GridLayout`, both the INSERT column list and the `EXCLUDED` SET clause must be updated.

## Prevention checklist

**When adding a column via migration:**

1. Update the entity class to add the new property.
2. Update the entity configuration (e.g., `LeafletChunkConfiguration`) to map it and set constraints (required, max length, etc.).
3. **Search the codebase for raw `NpgsqlCommand` INSERT statements against the affected table.**
4. Update the INSERT column list to include the new column.
5. Update the INSERT parameter bindings (`@` placeholders) to match the new schema.
6. Test the insert path in isolation (unit test or manual verification) before committing.

Example: When adding `Summary` to `LeafletChunks`, the INSERT must change from:
```sql
INSERT INTO "LeafletChunks" ("Id", "DocumentId", "ChunkIndex", "Content", "WordCount")
VALUES (@id, @documentId, @chunkIndex, @content, @wordCount)
```
to:
```sql
INSERT INTO "LeafletChunks" ("Id", "DocumentId", "ChunkIndex", "Content", "Summary", "WordCount")
VALUES (@id, @documentId, @chunkIndex, @content, @summary, @wordCount)
```

## Data recovery (LeafletChunks only)

`LeafletChunks` rows ingested before the fix was applied have `Summary = ""` (the server-side default, not the LLM-generated value). Re-running `LeafletIndexingService.IndexAsync` for those documents will re-extract and re-summarize the content, updating the `Summary` column to the correct values.

No automatic backfill is provided. Manual verification of a sample of documents is recommended after the fix is deployed.

## Why this matters

Raw SQL INSERT statements are a performance optimization used in this codebase for bulk operations. They bypass EF's change tracker and execute in a single batch. However, they trade away EF's schema awareness for speed. Every time a column is added to an entity that has a raw-SQL path, the developer must manually update that path or accept silent data loss.
