# Implementation Plan: Batch embedding calls in KnowledgeBaseDocIndexingStrategy

## Feature Name
Batch embedding calls in KnowledgeBaseDocIndexingStrategy (feat-3590)

## Goal
`KnowledgeBaseDocIndexingStrategy.CreateChunksAsync` currently calls `IEmbeddingGenerator.GenerateAsync`
once per chunk inside its indexing loop — N embedding-API round trips for an N-chunk document. Restructure
the method to summarize all chunks sequentially first (unchanged behavior), then issue a single batched
`GenerateAsync` call for all summaries, mirroring the already-correct pattern in `ConversationIndexingStrategy`.
Add a guard so a zero-chunk document never issues an empty-batch call. Output shape, chunk ordering, and the
public method signature are unchanged — this is an internal control-flow change only, reducing embedding API
round trips per `CreateChunksAsync` call from O(N) to O(1).

## Architecture
No architectural change. `KnowledgeBaseDocIndexingStrategy` is one of two `IIndexingStrategy` implementations
resolved via `IEnumerable<IIndexingStrategy>` injection into `DocumentIndexingService`, which is unaffected.
The fix converges `KnowledgeBaseDocIndexingStrategy` onto the same two-pass pattern
(collect inputs → single batched `GenerateAsync` call → zip results by index) already used by its sibling
`ConversationIndexingStrategy`. No interface, DI registration, constructor signature, or caller changes.

```
DocumentIndexingService
   └─ IEnumerable<IIndexingStrategy>  (unchanged: resolved by DocumentType)
        ├─ ConversationIndexingStrategy      (reference pattern — untouched by this task)
        └─ KnowledgeBaseDocIndexingStrategy  (← CreateChunksAsync body changes)
                ├─ IWordWindowChunker                              (unchanged)
                ├─ IChunkSummarizer                                (unchanged; still one call per chunk, sequential)
                └─ IEmbeddingGenerator<string, Embedding<float>>   (now one call per document, not per chunk)
```

## Tech Stack
- .NET 8, C# (backend only — no frontend changes)
- `Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>>` (existing dependency, no version change)
- xUnit + Moq (existing test conventions used across the `KnowledgeBase.Services` test folder)

---

### task: batch-knowledgebase-doc-embedding-calls

## Goal
In `KnowledgeBaseDocIndexingStrategy.CreateChunksAsync`, replace the per-chunk `_embeddingGenerator.GenerateAsync`
call (called once per loop iteration, N times for N chunks) with a single batched call made once per method
invocation, after all chunk summaries have been collected. Add a guard that returns an empty list immediately
when there are zero chunks, without calling `GenerateAsync` on an empty list. Update the existing unit test
file so its embedding-generator mock and assertions are compatible with the batched call and cover the new
guard.

This task touches exactly two files:
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategyTests.cs`

Do not touch `ConversationIndexingStrategy.cs`, `ConversationIndexingStrategyTests.cs`, `IIndexingStrategy.cs`,
`DocumentIndexingService.cs`, or `KnowledgeBaseModule.cs` — none of them need to change for this fix.

## Why this matters
Embedding-generation APIs (Azure OpenAI / OpenAI embeddings endpoints) accept a list of inputs per call and
are materially more cost- and latency-efficient when batched, since each call carries fixed network/auth
overhead independent of batch size. For a typical 20-chunk document, the current code performs 20 sequential
embedding calls instead of 1. `ConversationIndexingStrategy` already batches correctly; this brings
`KnowledgeBaseDocIndexingStrategy` in line with that pattern.

## Step 1 — Change the implementation

Open `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs`.

Current `CreateChunksAsync` method (to be replaced in full):

```csharp
    public async Task<IReadOnlyList<KnowledgeBaseChunk>> CreateChunksAsync(
        string cleanText, Guid documentId, CancellationToken ct)
    {
        var chunkTexts = _chunker.Chunk(cleanText, _options.ChunkSize, _options.ChunkOverlap);
        var chunks = new List<KnowledgeBaseChunk>();

        for (var i = 0; i < chunkTexts.Count; i++)
        {
            var summary = await _summarizer.SummarizeAsync(chunkTexts[i], ct);
            var embeddings = await _embeddingGenerator.GenerateAsync([summary], cancellationToken: ct);
            chunks.Add(new KnowledgeBaseChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                ChunkIndex = i,
                Content = chunkTexts[i],
                Summary = summary,
                DocumentType = DocumentType.KnowledgeBase,
                Embedding = embeddings[0].Vector.ToArray(),
            });
        }

        return chunks;
    }
```

Replace it with:

```csharp
    public async Task<IReadOnlyList<KnowledgeBaseChunk>> CreateChunksAsync(
        string cleanText, Guid documentId, CancellationToken ct)
    {
        var chunkTexts = _chunker.Chunk(cleanText, _options.ChunkSize, _options.ChunkOverlap);
        if (chunkTexts.Count == 0)
            return [];

        var summaries = new List<string>(chunkTexts.Count);
        for (var i = 0; i < chunkTexts.Count; i++)
        {
            summaries.Add(await _summarizer.SummarizeAsync(chunkTexts[i], ct));
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

Nothing else in the file changes: constructor, fields, `Supports`, using directives, and namespace stay exactly
as they are today.

Key points preserved:
- `_summarizer.SummarizeAsync` is still called once per chunk, sequentially, awaited in a loop (not
  `Task.WhenAll`) — summarization is explicitly out of scope for this fix.
- `_embeddingGenerator.GenerateAsync` is called exactly once per `CreateChunksAsync` invocation when
  `chunkTexts.Count > 0`, and not called at all when `chunkTexts.Count == 0`.
- Index alignment `chunkTexts[i]` ↔ `summaries[i]` ↔ `embeddings[i]` ↔ `chunks[i]` is preserved — the batched
  `GenerateAsync` result preserves input order (same assumption `ConversationIndexingStrategy` already relies on).
- `CancellationToken ct` continues to flow into both `_summarizer.SummarizeAsync` and the single
  `_embeddingGenerator.GenerateAsync` call.

## Step 2 — Fix the test that will break under batching

Open `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategyTests.cs`.

**Why this step is required:** the shared `_embeddingGenerator` mock set up in the constructor always returns a
fixed `GeneratedEmbeddings` containing exactly **one** `Embedding<float>`, regardless of how many summaries are
passed in. Every test in this file that produces a single chunk (short text, default `ChunkSize = 512`)
continues to work fine against that 1-item mock after batching, because `chunkTexts.Count == 1` in those cases.
But `CreateChunksAsync_ChunkIndexIsSequential` uses `ChunkSize = 5, ChunkOverlap = 1` over 20 words, which
produces 5 chunks (and therefore 5 summaries). Once the implementation batches, that test will call
`GenerateAsync` with a 5-item summary list but the mock still returns a 1-item `GeneratedEmbeddings`, so
`embeddings[i]` for `i > 0` throws `IndexOutOfRangeException` (`ArgumentOutOfRangeException` from
`GeneratedEmbeddings`'s list indexer) inside the assembly loop. This test's own mock must be overridden to
return one embedding per input summary before that test will pass.

### Step 2a — Strengthen `CreateChunksAsync_ProducesChunksWithEmbeddings`

This test uses text `"word1 word2 word3"` with the default `ChunkSize = 512` (from the constructor's
`_strategy`), which always produces exactly 1 chunk, so the existing 1-item mock is compatible as-is — no
mock change needed here. Only tighten the call-count assertion from `Times.AtLeastOnce` (which would still
pass even if the bug were never fixed) to `Times.Exactly(1)`, which is the actual regression guard.

Find:

```csharp
    [Fact]
    public async Task CreateChunksAsync_ProducesChunksWithEmbeddings()
    {
        var documentId = Guid.NewGuid();
        var text = "word1 word2 word3";

        var chunks = await _strategy.CreateChunksAsync(text, documentId, CancellationToken.None);

        Assert.NotEmpty(chunks);
        _embeddingGenerator.Verify(
            e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        Assert.All(chunks, chunk =>
        {
            Assert.Equal(documentId, chunk.DocumentId);
            Assert.NotEmpty(chunk.Embedding);
        });
    }
```

Replace the `Times.AtLeastOnce` line so the method reads:

```csharp
    [Fact]
    public async Task CreateChunksAsync_ProducesChunksWithEmbeddings()
    {
        var documentId = Guid.NewGuid();
        var text = "word1 word2 word3";

        var chunks = await _strategy.CreateChunksAsync(text, documentId, CancellationToken.None);

        Assert.NotEmpty(chunks);
        _embeddingGenerator.Verify(
            e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(1));
        Assert.All(chunks, chunk =>
        {
            Assert.Equal(documentId, chunk.DocumentId);
            Assert.NotEmpty(chunk.Embedding);
        });
    }
```

(Only `Times.AtLeastOnce` → `Times.Exactly(1)` changes; the rest of the method body is unchanged.)

### Step 2b — Fix the multi-chunk test's mock and add the primary regression guard

Find:

```csharp
    [Fact]
    public async Task CreateChunksAsync_ChunkIndexIsSequential()
    {
        var options = Options.Create(new KnowledgeBaseOptions { ChunkSize = 5, ChunkOverlap = 1 });
        var chunker = new WordWindowChunker();
        var strategy = new KnowledgeBaseDocIndexingStrategy(
            chunker,
            _summarizer.Object,
            _embeddingGenerator.Object,
            options);

        var words = string.Join(" ", Enumerable.Range(1, 20).Select(i => $"w{i}"));
        var chunks = await strategy.CreateChunksAsync(words, Guid.NewGuid(), CancellationToken.None);

        Assert.True(chunks.Count > 1);
        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
        }
    }
```

Replace with:

```csharp
    [Fact]
    public async Task CreateChunksAsync_ChunkIndexIsSequential()
    {
        var options = Options.Create(new KnowledgeBaseOptions { ChunkSize = 5, ChunkOverlap = 1 });
        var chunker = new WordWindowChunker();

        var floats = new float[] { 0.1f, 0.2f, 0.3f };
        _embeddingGenerator
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> texts, EmbeddingGenerationOptions? options, CancellationToken ct) =>
                new GeneratedEmbeddings<Embedding<float>>(
                    texts.Select(t => new Embedding<float>(new ReadOnlyMemory<float>(floats))).ToList()));

        var strategy = new KnowledgeBaseDocIndexingStrategy(
            chunker,
            _summarizer.Object,
            _embeddingGenerator.Object,
            options);

        var words = string.Join(" ", Enumerable.Range(1, 20).Select(i => $"w{i}"));
        var chunks = await strategy.CreateChunksAsync(words, Guid.NewGuid(), CancellationToken.None);

        Assert.True(chunks.Count > 1);
        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
        }

        _embeddingGenerator.Verify(
            e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(1));
    }
```

Notes on this change:
- The new `_embeddingGenerator.Setup(...)` call overrides the constructor's default 1-item mock for this test
  only (Moq uses the most recently registered matching setup), returning one `Embedding<float>` per item in the
  actual `texts` argument — this is what makes the test correct regardless of how many chunks
  `WordWindowChunker` produces for this input, without hardcoding a chunk count.
  This mirrors the existing per-test mock override pattern already used by `CreateChunksAsync_EmbeddingIsGeneratedFromSummary`
  in this same file, and by the multi-topic tests in the sibling
  `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ConversationIndexingStrategyTests.cs`
  (e.g. `CreateChunksAsync_NTopicSummaries_ProducesNChunks`).
- The trailing `_embeddingGenerator.Verify(..., Times.Exactly(1))` is the primary regression guard for the N→1
  round-trip requirement: this test is the only one in the file guaranteed to produce more than one chunk
  (`Assert.True(chunks.Count > 1)`), so it is the correct place to assert the batched call happens exactly once
  even though multiple chunks/summaries were produced.

### Step 2c — Add the zero-chunk test case

Add a new test method at the end of the class, immediately after `CreateChunksAsync_ChunkIndexIsSequential`
(before the closing `}` of the class):

```csharp

    [Fact]
    public async Task CreateChunksAsync_NoChunksProduced_ReturnsEmptyListAndDoesNotCallEmbeddingGenerator()
    {
        var documentId = Guid.NewGuid();

        var chunks = await _strategy.CreateChunksAsync(string.Empty, documentId, CancellationToken.None);

        Assert.Empty(chunks);
        _embeddingGenerator.Verify(
            e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

This relies on `WordWindowChunker.Chunk` (`backend/src/Anela.Heblo.Application/Shared/Rag/WordWindowChunker.cs`)
returning `Array.Empty<string>()` when the input text is `string.IsNullOrWhiteSpace`, which is already true for
`string.Empty` today — no chunker change needed. This exercises the new `if (chunkTexts.Count == 0) return [];`
guard added in Step 1 and asserts `_embeddingGenerator.GenerateAsync` is never invoked for a zero-chunk
document, per spec FR-2.

## Step 3 — Build and run tests

Run these commands from the repository root (`/home/user/worktrees/feature-3590-Arch-Review-Knowledgebase-N-1-Embedding-Api-Calls`):

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded.` with 0 errors.

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~KnowledgeBaseDocIndexingStrategyTests"
```

Expected: all 8 tests pass (the 6 pre-existing tests — `Supports_KnowledgeBase_ReturnsTrue`,
`Supports_Conversation_ReturnsFalse`, `CreateChunksAsync_ProducesChunksWithEmbeddings`,
`CreateChunksAsync_EmbeddingIsGeneratedFromSummary`, `CreateChunksAsync_ChunkContentIsChunkText_NotSummary`,
`CreateChunksAsync_ChunkIndexIsSequential` — plus the new `CreateChunksAsync_NoChunksProduced_ReturnsEmptyListAndDoesNotCallEmbeddingGenerator`,
for 7 total... verify the actual count in the console output matches the number of `[Fact]` methods in the
file after Step 2), 0 failed.

Also run the sibling reference-pattern tests to confirm no unintended impact (this task must not modify
`ConversationIndexingStrategy.cs` or its tests, but running them confirms isolation):

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ConversationIndexingStrategyTests"
```

Expected: all pre-existing tests still pass, unchanged.

Finally, run the full backend test suite to catch any unrelated regression:

```bash
dotnet test backend/Anela.Heblo.sln
```

Expected: all tests pass (0 failed).

Then run formatting check per repo convention:

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

If this reports formatting differences, run `dotnet format backend/Anela.Heblo.sln` (without `--verify-no-changes`)
to apply them, then re-run `dotnet build` and the test commands above to confirm nothing broke.

## Step 4 — Commit

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategyTests.cs
git commit -m "Batch embedding calls in KnowledgeBaseDocIndexingStrategy"
```

## Acceptance criteria (map to spec FR-1/FR-2/FR-3, verified by the tests above)

- [ ] `CreateChunksAsync` calls `_embeddingGenerator.GenerateAsync` exactly once per invocation when
      `chunkTexts.Count > 0`, with a list containing all N chunk summaries in chunk order
      (verified by `CreateChunksAsync_ChunkIndexIsSequential`'s `Times.Exactly(1)` assertion over a 5-chunk
      input, and `CreateChunksAsync_ProducesChunksWithEmbeddings`'s `Times.Exactly(1)` assertion over a
      1-chunk input).
- [ ] `chunks[i].Embedding` is derived from `embeddings[i]`, which was generated from `chunkTexts[i]`'s summary
      — index alignment preserved (verified by `CreateChunksAsync_ChunkIndexIsSequential`'s per-index
      `ChunkIndex` assertions combined with the batched mock; no index-out-of-range failures).
- [ ] `_summarizer.SummarizeAsync` remains sequential/awaited per chunk in a loop — unchanged in the
      implementation (Step 1's `for` loop over `chunkTexts` calling `await _summarizer.SummarizeAsync(...)`
      with no `Task.WhenAll`).
- [ ] `_embeddingGenerator.GenerateAsync` is not invoked when `chunkTexts.Count == 0`, and `CreateChunksAsync`
      returns an empty list (verified by `CreateChunksAsync_NoChunksProduced_ReturnsEmptyListAndDoesNotCallEmbeddingGenerator`).
- [ ] `IIndexingStrategy.CreateChunksAsync(string, Guid, CancellationToken)` signature, `Supports`, constructor
      parameters, and injected dependencies are unchanged (verified by inspection — Step 1 only replaces the
      method body, not its signature or the class's other members; `Supports_KnowledgeBase_ReturnsTrue` and
      `Supports_Conversation_ReturnsFalse` continue to pass unmodified).
- [ ] `CancellationToken ct` continues to flow into both `_summarizer.SummarizeAsync` and the single
      `_embeddingGenerator.GenerateAsync` call (verified by inspection of Step 1's code — both calls pass `ct`).
