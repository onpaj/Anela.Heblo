# Design: Batch embedding calls in KnowledgeBaseDocIndexingStrategy

## Component Design

No new components. Single existing class's internal control flow changes; all collaborators, interfaces, and DI wiring are unchanged.

```
DocumentIndexingService
   └─ IEnumerable<IIndexingStrategy>  (unchanged: resolved by DocumentType)
        ├─ ConversationIndexingStrategy      (reference pattern — unchanged)
        └─ KnowledgeBaseDocIndexingStrategy  (← CreateChunksAsync body changes)
                ├─ IWordWindowChunker                              (unchanged)
                ├─ IChunkSummarizer                                (unchanged; still sequential, one call per chunk)
                ├─ IEmbeddingGenerator<string, Embedding<float>>   (now one call per document, not per chunk)
                └─ KnowledgeBaseOptions                            (unchanged)
```

### `KnowledgeBaseDocIndexingStrategy.CreateChunksAsync(string cleanText, Guid documentId, CancellationToken ct)`

Responsibility: turn cleaned document text into a list of `KnowledgeBaseChunk` entities, each carrying its own summary and embedding vector, with embedding generation batched into a single API call per invocation.

Internal steps (replacing the current per-chunk embedding loop):
1. `chunkTexts = _chunker.Chunk(cleanText, _options.ChunkSize, _options.ChunkOverlap)`.
2. Guard: if `chunkTexts.Count == 0`, return `[]` immediately — no call to `_embeddingGenerator.GenerateAsync`.
3. Sequential loop: for each entry in `chunkTexts`, `await _summarizer.SummarizeAsync(chunkText, ct)`, appending each result to an in-order `summaries` list. No embedding call inside this loop (unchanged sequential-summarization contract).
4. Exactly one call: `embeddings = await _embeddingGenerator.GenerateAsync(summaries, cancellationToken: ct)`, invoked once per `CreateChunksAsync` call when `chunkTexts.Count > 0`.
5. Assembly loop: for `i` in `0..chunkTexts.Count`, construct `KnowledgeBaseChunk` with `Embedding = embeddings[i].Vector.ToArray()`, preserving `chunkTexts[i]` ↔ `summaries[i]` ↔ `embeddings[i]` ↔ `chunks[i]` index alignment (the batched result preserves input order, matching `ConversationIndexingStrategy`'s existing usage).
6. Return the assembled `chunks` list.

Contract preserved exactly as before (per spec FR-3 / arch review): constructor, `Supports(DocumentType)`, injected dependencies (`IWordWindowChunker`, `IChunkSummarizer`, `IEmbeddingGenerator<string, Embedding<float>>`, `KnowledgeBaseOptions`), and the `IIndexingStrategy.CreateChunksAsync` signature. `CancellationToken ct` continues to flow into both `_summarizer.SummarizeAsync` and the single `_embeddingGenerator.GenerateAsync` call. No changes to `DocumentIndexingService`, `IIndexingStrategy`, `KnowledgeBaseModule.cs` DI registration, or `ConversationIndexingStrategy`.

### Test fixture updates (`KnowledgeBaseDocIndexingStrategyTests.cs`)

Not a new component, but a required companion change flagged by the architecture review:
- The mocked `_embeddingGenerator` setup must return one `Embedding<float>` per input summary (sized to the actual argument count via a `Callback`/`Returns` closure), rather than a fixed single-element `GeneratedEmbeddings`, so multi-chunk tests don't index out of range once embedding calls are batched.
- Add/extend an assertion that `GenerateAsync` is called `Times.Exactly(1)` for a multi-chunk document (replacing the current `Times.AtLeastOnce` check), as the primary regression guard for the N→1 call-count requirement.
- Add a test for the zero-chunk case: `_embeddingGenerator.GenerateAsync` is never invoked and `CreateChunksAsync` returns an empty list.

## Data Schemas

No data model or persistence changes. `KnowledgeBaseChunk` (`Anela.Heblo.Domain.Features.KnowledgeBase`) keeps its existing shape and is populated with equivalent values to the pre-fix implementation:

| Field          | Type       | Source (unchanged)                                  |
|----------------|-----------|-------------------------------------------------------|
| `Id`           | `Guid`     | `Guid.NewGuid()` per chunk                            |
| `DocumentId`   | `Guid`     | `documentId` parameter                                |
| `ChunkIndex`   | `int`      | loop index `i`                                        |
| `Content`      | `string`   | `chunkTexts[i]` (raw chunk text)                      |
| `Summary`      | `string`   | `summaries[i]` (from `_summarizer.SummarizeAsync`)    |
| `DocumentType` | `enum`     | `DocumentType.KnowledgeBase`                          |
| `Embedding`    | `float[]`  | `embeddings[i].Vector.ToArray()` — now sourced from the single batched `GenerateAsync(summaries, ...)` result instead of N individual calls |

### API shape: `IEmbeddingGenerator<string, Embedding<float>>.GenerateAsync`

Before (per chunk, N calls):
```
GenerateAsync(new[] { summaries[i] }, options?, ct) -> GeneratedEmbeddings<Embedding<float>>  // 1 item
```

After (per document, 1 call):
```
GenerateAsync(summaries /* IEnumerable<string>, count = N */, options?, ct)
    -> GeneratedEmbeddings<Embedding<float>>  // N items, order-preserving, embeddings[i] <-> summaries[i]
```

No change to the request/response payload shape sent to the underlying embedding provider beyond batching — same summary text content, same per-item vector shape; only the number of round trips changes (N → 1, or 0 for an empty-chunk document). No event payloads, database schemas, or public API/controller/MediatR contracts are affected.
