# Specification: Batch embedding calls in KnowledgeBaseDocIndexingStrategy

## Summary
`KnowledgeBaseDocIndexingStrategy.CreateChunksAsync` currently calls `IEmbeddingGenerator.GenerateAsync` once per chunk inside its indexing loop, issuing N separate embedding-API round trips for an N-chunk document. This fix restructures the method to summarize all chunks sequentially first, then issue a single batched `GenerateAsync` call for all summaries, mirroring the pattern already used by `ConversationIndexingStrategy`. The change reduces embedding API round trips from N to 1 per document with no change to indexing output or chunk ordering.

## Background
Embedding-generation APIs (e.g. Azure OpenAI / OpenAI embeddings endpoints) accept a list of inputs per call and are materially more cost- and latency-efficient when batched, since each call carries fixed network/auth overhead independent of batch size. `KnowledgeBaseDocIndexingStrategy` is invoked for every OneDrive-sourced ingestion and every manual document upload in the KnowledgeBase module; for a typical 20-chunk document it currently performs 20 sequential embedding calls instead of 1, multiplying both latency (round trips are on the critical path of indexing) and per-call cost. `ConversationIndexingStrategy`, which handles the same `IEmbeddingGenerator` dependency for conversation transcripts, already batches correctly (`GenerateAsync(topics, ...)` called once with the full topic list). This finding was raised by the daily automated arch-review routine (GitHub issue #3590) specifically to bring `KnowledgeBaseDocIndexingStrategy` in line with that existing, correct pattern.

## Functional Requirements

### FR-1: Batch all chunk embeddings into a single API call
`CreateChunksAsync` must collect the summaries for all chunks of a document first, then call `_embeddingGenerator.GenerateAsync` exactly once with the full list of summaries, instead of calling it once per chunk inside the loop.

**Acceptance criteria:**
- For a document that produces N chunks (N > 0), `_embeddingGenerator.GenerateAsync` is invoked exactly once during a single `CreateChunksAsync` call, with an input list containing all N chunk summaries in chunk order.
- The `Embedding` assigned to each returned `KnowledgeBaseChunk` corresponds to the same chunk's summary (i.e. `chunks[i].Embedding` is derived from `embeddings[i]`, which was generated from `chunkTexts[i]`'s summary) — index alignment between `chunkTexts`, the summaries list, and the batched `GenerateAsync` result must be preserved.
- Chunk summarization via `_summarizer.SummarizeAsync` remains sequential/awaited per chunk in a loop (per the brief: "sequential — must stay"), i.e. this fix does not attempt to parallelize or batch summarization — only embedding generation is batched.
- The output `IReadOnlyList<KnowledgeBaseChunk>` returned by `CreateChunksAsync` is unchanged in shape, content, and ordering compared to the current (unbatched) implementation for the same input — `Id`, `DocumentId`, `ChunkIndex`, `Content`, `Summary`, `DocumentType`, and `Embedding` values are equivalent to what the pre-fix implementation would have produced (given the same summarizer/embedding-generator responses).

### FR-2: Handle the zero-chunk case without an empty batch call
If `_chunker.Chunk(...)` produces zero chunks for the given `cleanText`, `CreateChunksAsync` must return an empty chunk list without calling `_embeddingGenerator.GenerateAsync` with an empty input list.

**Acceptance criteria:**
- When `chunkTexts.Count == 0`, `_embeddingGenerator.GenerateAsync` is not invoked, and `CreateChunksAsync` returns an empty list.
- This matches the guard already present in `ConversationIndexingStrategy` (`if (topics.Count == 0) return [];`).

### FR-3: Preserve existing method signature and behavior contract
The public shape of `KnowledgeBaseDocIndexingStrategy` (constructor, `Supports`, `CreateChunksAsync` signature) is unchanged. Only the internal implementation of `CreateChunksAsync` changes.

**Acceptance criteria:**
- `IIndexingStrategy.CreateChunksAsync(string cleanText, Guid documentId, CancellationToken ct)` signature is unchanged.
- No changes to `Supports(DocumentType)`, constructor parameters, or injected dependencies (`IWordWindowChunker`, `IChunkSummarizer`, `IEmbeddingGenerator<string, Embedding<float>>`, `KnowledgeBaseOptions`).
- `CancellationToken ct` continues to be passed through to both `_summarizer.SummarizeAsync` and the single `_embeddingGenerator.GenerateAsync` call.

## Non-Functional Requirements

### NFR-1: Performance
- Embedding API round trips per `CreateChunksAsync` invocation must drop from O(N) (one per chunk) to O(1) (one call for the whole document, or zero calls when there are no chunks). This is the entire purpose of the fix; it is both the performance requirement and the primary acceptance criterion, verified by call-count assertions in tests (see FR-1, FR-2).
- No new performance regression is introduced elsewhere (summarization remains sequential and is explicitly out of scope for optimization per the brief).

### NFR-2: Security
- No change. The fix touches only in-process control flow (loop restructuring); it does not change what data is sent to the embedding API (same summaries, same content), does not introduce new external calls, and does not touch authentication, authorization, or data persistence.

## Data Model
No data model changes. `KnowledgeBaseChunk` (existing entity in `Anela.Heblo.Domain.Features.KnowledgeBase`) is populated identically to today: `Id`, `DocumentId`, `ChunkIndex`, `Content` (raw chunk text), `Summary` (per-chunk summary), `DocumentType`, `Embedding` (float array). The only change is *how* `Embedding` values are obtained (one batched call vs. N individual calls) — not the resulting per-chunk data shape.

## API / Interface Design
Internal implementation change only; no public API, controller, or MediatR endpoint is affected. For reference, the target implementation shape (per the brief and consistent with `ConversationIndexingStrategy`'s existing pattern) is:

```csharp
public async Task<IReadOnlyList<KnowledgeBaseChunk>> CreateChunksAsync(
    string cleanText, Guid documentId, CancellationToken ct)
{
    var chunkTexts = _chunker.Chunk(cleanText, _options.ChunkSize, _options.ChunkOverlap);
    if (chunkTexts.Count == 0)
        return [];

    var summaries = new List<string>(chunkTexts.Count);
    foreach (var chunkText in chunkTexts)
    {
        summaries.Add(await _summarizer.SummarizeAsync(chunkText, ct));
    }

    var embeddings = await _embeddingGenerator.GenerateAsync(summaries, cancellationToken: ct);

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
            Embedding = embeddings[i].Vector.ToArray(),
        });
    }

    return chunks;
}
```

This is illustrative of the required control flow, not a mandate to match verbatim — the implementing engineer may adjust variable names/style to match surrounding conventions, provided all Functional Requirements above are met.

## Dependencies
- `Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>>` — existing dependency, no version or contract change. Its `GenerateAsync(IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken)` overload (already used by `ConversationIndexingStrategy`) is reused for the batched call.
- `IChunkSummarizer.SummarizeAsync` — existing dependency, unchanged, remains called once per chunk sequentially.
- `IWordWindowChunker.Chunk` — existing dependency, unchanged.
- No new NuGet packages, configuration, or infrastructure required.

## Out of Scope
- Parallelizing or batching the summarization step (`_summarizer.SummarizeAsync`) — the brief explicitly states this must remain sequential.
- Any change to `ConversationIndexingStrategy` (it already implements the correct pattern and is used only as a reference).
- Any change to chunking logic (`IWordWindowChunker`), summarization logic (`ChunkSummarizer`), or embedding provider configuration.
- Handling of partial-batch failures from the embedding API (e.g. retry/backoff on `GenerateAsync` failure) — existing error-handling behavior (propagate exception, no retry) is preserved as-is; this fix does not add or change error handling.
- Any change to `DocumentIndexingService` or other callers of `IIndexingStrategy.CreateChunksAsync` — the public contract is untouched, so no caller changes are needed.

## Open Questions
None.

## Status: COMPLETE
