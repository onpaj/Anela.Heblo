## Module
KnowledgeBase

## Finding
`KnowledgeBaseDocIndexingStrategy.CreateChunksAsync` calls `_embeddingGenerator.GenerateAsync` once per chunk inside the loop, producing N separate API round trips for an N-chunk document:

```csharp
// backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs, lines 34–51
for (var i = 0; i < chunkTexts.Count; i++)
{
    var summary = await _summarizer.SummarizeAsync(chunkTexts[i], ct);   // sequential — must stay
    var embeddings = await _embeddingGenerator.GenerateAsync([summary], cancellationToken: ct);  // ← N calls
    ...
}
```

`ConversationIndexingStrategy` (same file location, `ConversationIndexingStrategy.cs`) already demonstrates the correct batching pattern — it collects all topics first, then calls `GenerateAsync` once with the full list:

```csharp
var topics = await _summarizer.SummarizeTopicsAsync(cleanText, ct);
var embeddings = await _embeddingGenerator.GenerateAsync(topics, cancellationToken: ct);  // ← 1 call
```

## Why it matters
Embedding APIs are designed for batched input and significantly more efficient per-item when called with a list. For a 20-chunk document this is 20 API round trips instead of 1, adding latency and cost to every OneDrive ingestion and every manual upload. The inconsistency with `ConversationIndexingStrategy` also makes the behaviour surprising for future maintainers.

## Suggested fix
Collect all summaries sequentially first, then batch-embed in a single call:

```csharp
var summaries = new List<string>(chunkTexts.Count);
for (var i = 0; i < chunkTexts.Count; i++)
    summaries.Add(await _summarizer.SummarizeAsync(chunkTexts[i], ct));

var allEmbeddings = await _embeddingGenerator.GenerateAsync(summaries, cancellationToken: ct);

var chunks = new List<KnowledgeBaseChunk>(chunkTexts.Count);
for (var i = 0; i < chunkTexts.Count; i++)
{
    chunks.Add(new KnowledgeBaseChunk
    {
        Id = Guid.NewGuid(),
        DocumentId = documentId,
        ChunkIndex = i,
        Content = chunkTexts[i],
        Summary = summaries[i],
        DocumentType = DocumentType.KnowledgeBase,
        Embedding = allEmbeddings[i].Vector.ToArray(),
    });
}
return chunks;
```

---
_Filed by daily arch-review routine on 2026-07-10._
