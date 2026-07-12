# [arch-review] Leaflet: AddChunksAsync inserts chunks one-by-one in a loop instead of batching

## Module
Leaflet

## Finding
`LeafletDocumentRepository.AddChunksAsync` creates and executes a separate `NpgsqlCommand` for each chunk in a sequential loop:

```csharp
// backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs:25–52
foreach (var chunk in chunkList)
{
    await using var cmd = new NpgsqlCommand(
        """
        INSERT INTO "LeafletChunks" ("Id", "DocumentId", "ChunkIndex", "Content", "Summary", "WordCount", "Embedding")
        VALUES (@id, @documentId, @chunkIndex, @content, @summary, @wordCount, @embedding)
        ON CONFLICT ("Id") DO NOTHING
        """,
        connection);
    // parameters bound...
    await cmd.ExecuteNonQueryAsync(ct);
}
```

For a document that produces N chunks, this is N sequential database round trips. The chunking parameters (`ChunkSize = 800 words`, `ChunkOverlap = 80 words`) mean a 10 000-word PDF produces ~13 chunks, and a long marketing brochure of 30 000 words produces ~38 chunks — 38 individual round trips.

The raw-Npgsql approach is necessary because EF Core cannot natively handle pgvector's `vector(1536)` type via `AddRange`, but the per-row loop is not. Npgsql supports a multi-row parameterised `INSERT` (single command with N value rows) or the binary copy protocol (`NpgsqlBinaryImporter`) for bulk data.

## Why it matters
Every OneDrive ingestion and every manual upload pays this cost. A 30-chunk document takes ~38 DB round trips instead of ~1. Over a slow or saturated connection (Azure SQL networking within a single region is fast but not free), this compounds with the sequential `await` pattern: chunk 2's insert cannot start until chunk 1's round trip completes.

## Suggested fix
Build a single multi-row `INSERT` statement with all chunk rows, parameterised per-row:

```csharp
var sb = new StringBuilder(
    """
    INSERT INTO "LeafletChunks" ("Id","DocumentId","ChunkIndex","Content","Summary","WordCount","Embedding")
    VALUES
    """);

await using var cmd = new NpgsqlCommand();
cmd.Connection = connection;

for (var i = 0; i < chunkList.Count; i++)
{
    if (i > 0) sb.Append(',');
    sb.Append($"(@id{i},@doc{i},@ci{i},@content{i},@summary{i},@wc{i},@emb{i})");
    var c = chunkList[i];
    cmd.Parameters.AddWithValue($"id{i}", c.Id);
    cmd.Parameters.AddWithValue($"doc{i}", c.DocumentId);
    cmd.Parameters.AddWithValue($"ci{i}", c.ChunkIndex);
    cmd.Parameters.AddWithValue($"content{i}", c.Content);
    cmd.Parameters.AddWithValue($"summary{i}", c.Summary);
    cmd.Parameters.AddWithValue($"wc{i}", c.WordCount);
    cmd.Parameters.AddWithValue($"emb{i}", new Vector(c.Embedding));
}

sb.Append(""" ON CONFLICT ("Id") DO NOTHING""");
cmd.CommandText = sb.ToString();
await cmd.ExecuteNonQueryAsync(ct);
```

Or use `NpgsqlBinaryImporter` for even better throughput on large batches. Either approach reduces N round trips to 1.

---
_Filed by daily arch-review routine on 2026-07-11._
