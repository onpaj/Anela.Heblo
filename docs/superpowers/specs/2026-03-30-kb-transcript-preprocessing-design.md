# Knowledge Base: Chat Transcript Preprocessing & Keyword-Summarized Embeddings

**Date:** 2026-03-30
**Issue:** #454 (extended)
**Branch:** to be created

---

## Problem

Customer-service chat transcripts ingested into the knowledge base contain two types of noise that destroy embedding quality:

1. **Structural boilerplate** — identical greeting opener and metadata header in every file
2. **Conversational filler** — social turns that carry no domain signal

When boilerplate is included in every chunk's embedding, all documents cluster around the same "generic customer-service" vector. Queries about specific products or skin conditions weakly match boilerplate text rather than the relevant advice chunks. The result: top search results are generic conversation openers ranked 54–61%, not actionable content.

---

## Solution Overview

Introduce two sequential preprocessing steps in `DocumentIndexingService.IndexChunksAsync`, between text extraction and embedding:

1. **`ChatTranscriptPreprocessor`** — regex-based, strips known boilerplate patterns
2. **`ChunkSummarizer`** — LLM-based, extracts keywords from each chunk (products, ingredients, customer issues, recommendations)

The embedding is generated from the **keyword summary**, but `chunk.Content` stores the **full clean transcript text**. This gives dense, targeted retrieval while preserving rich context for answer generation.

```
ExtractTextAsync()
    → ChatTranscriptPreprocessor.Clean()     # regex strip
    → DocumentChunker.Chunk()
    → ChunkSummarizer.SummarizeAsync()       # LLM keyword extraction
    → EmbeddingGenerator([summary])          # embed summary
    → chunk.Content = clean full text
    → chunk.Embedding = summary vector
```

---

## Components

### 1. `ChatTranscriptPreprocessor`

A pure, stateless service. No LLM, no async, fully deterministic.

**Responsibility:** strip known noise patterns before chunking.

**Patterns (initial set):**

| Pattern | Regex |
|---------|-------|
| Greeting opener | `Vítejte ve světě Anela.*?Napište nám, jsme tu pro Vás!` (single-line, dotall) |
| Metadata header | `^datum:\s+\S+\s+zákazník:\s+\S+` (multiline) |
| Anonymized customer ID | `Zákazník-\d+:?\s*` |

**Interface:**
```csharp
public string Clean(string rawText)
```

**Configuration:** patterns are loaded from `KnowledgeBaseOptions.PreprocessorPatterns` (list of regex strings). The three above are defaults; new patterns can be added via config without code changes.

After stripping, consecutive blank lines are collapsed to a single blank line.

---

### 2. `IChunkSummarizer` / `ChunkSummarizer`

Calls the LLM (`IChatClient` from `Microsoft.Extensions.AI`) with a structured extraction prompt.

**Responsibility:** produce a keyword-dense representation of each chunk for embedding. Not a prose summary — a structured keyword list.

**Output format:**
```
Produkty: Sérum ABC, Krém XYZ
Ingredience: niacinamid, kyselina hyaluronová
Problém zákazníka: akné, mastná pleť
Doporučení: nanášet 2x denně, kombinovat s SPF
```

If a category has no relevant content in the chunk, it is omitted from the output.

**Interface:**
```csharp
public Task<string> SummarizeAsync(string chunkText, CancellationToken ct = default)
```

**Prompt:** configurable via `KnowledgeBaseOptions.SummarizationPrompt`. Default instructs the model to extract products, ingredients, customer issues, and recommendations from a Czech customer-service chat excerpt, responding in Czech with the structured format above.

**Disabled mode:** when `KnowledgeBaseOptions.SummarizationEnabled = false`, `SummarizeAsync` returns the chunk text unchanged with no LLM call. Used for testing and cost-free re-indexing runs.

---

### 3. `DocumentIndexingService` Changes

Inject `ChatTranscriptPreprocessor` and `IChunkSummarizer`. Extend `IndexChunksAsync`:

```csharp
var text = await extractor.ExtractTextAsync(content, ct);
text = _preprocessor.Clean(text);                          // new: regex strip
var chunkTexts = _chunker.Chunk(text);

for (var i = 0; i < chunkTexts.Count; i++)
{
    var summary = await _summarizer.SummarizeAsync(chunkTexts[i], ct);  // new: keyword extraction
    var embeddings = await _embeddingGenerator.GenerateAsync([summary], cancellationToken: ct);
    chunks.Add(new KnowledgeBaseChunk
    {
        Id = Guid.NewGuid(),
        DocumentId = document.Id,
        ChunkIndex = i,
        Content = chunkTexts[i],          // full clean text
        Embedding = embeddings[0].Vector.ToArray(),  // summary vector
    });
}
```

No structural change to the class — two new injected dependencies, two new lines in the loop.

---

### 4. `KnowledgeBaseOptions` Extensions

```csharp
// Preprocessing — defaults are the three patterns from the table above
public List<string> PreprocessorPatterns { get; set; } =
[
    @"Vítejte ve světě Anela.*?Napište nám, jsme tu pro Vás!",
    @"(?m)^datum:\s+\S+\s+zákazník:\s+\S+",
    @"Zákazník-\d+:?\s*"
];

// Summarization
public bool SummarizationEnabled { get; set; } = true;
public string SummarizationPrompt { get; set; } =
    """
    Jsi asistent extrahující klíčová data z úryvku zákaznického chatu kosmetické firmy Anela.
    Z textu vypiš POUZE relevantní položky v tomto formátu (vynech kategorie bez obsahu):

    Produkty: <názvy produktů>
    Ingredience: <účinné látky, složky>
    Problém zákazníka: <kožní potíže, dotazy>
    Doporučení: <rady, způsob použití>

    Text:
    """;
```

---

## Testing

### `ChatTranscriptPreprocessorTests`
Unit tests, no mocks:
- Greeting removed
- Metadata header removed
- `Zákazník-\d+` inline IDs removed
- All three combined
- Passthrough (no match → text unchanged)
- Custom pattern added via options

### `ChunkSummarizerTests`
Mock `IChatClient`:
- Prompt contains chunk text
- Returns LLM response string
- When `SummarizationEnabled = false` → returns chunk text, no LLM call made

### `DocumentIndexingServiceTests` (extended)
- Preprocessor called before chunking
- Summarizer output is what gets passed to embedding generator
- `chunk.Content` holds full clean text (not the summary)

---

## Trade-offs & Constraints

- **Latency:** ~1 LLM call per chunk per document. At 200 docs/month with typical chunk counts, cost and latency are negligible. Ingestion is already a background job.
- **Determinism:** summarization output varies slightly between runs. This is acceptable — re-indexing a document will produce slightly different embeddings, which is fine.
- **Fallback:** `SummarizationEnabled = false` gives full Option A behavior (regex clean only) for cost-free testing or emergency rollback.
- **Non-transcript documents:** `ChatTranscriptPreprocessor` is safe to run on any text — if no patterns match, text passes through unchanged. `ChunkSummarizer` is also content-agnostic; the prompt is tailored for chat transcripts but keyword extraction degrades gracefully on other content types.

---

## Acceptance Criteria

- [ ] `ChatTranscriptPreprocessor` implemented and unit-tested (all cases above)
- [ ] `IChunkSummarizer` / `ChunkSummarizer` implemented and unit-tested
- [ ] Both wired into `DocumentIndexingService`
- [ ] `KnowledgeBaseOptions` extended with new fields
- [ ] `SummarizationEnabled = false` disables LLM call (verified by test)
- [ ] `dotnet format` passes
