# Knowledge Base: Document-Type-Aware Embedding Strategies

**Date:** 2026-03-30
**Issue:** #455
**Depends on:** PR #460 (KB transcript preprocessing & keyword-summarized embeddings)
**Branch:** to be created

---

## Problem

PR #460 introduces per-chunk keyword summarization — a fixed-window chunker splits the document into 512-word windows, and each chunk gets an LLM-extracted keyword summary that is embedded instead of raw text.

This works well for structured documents (PDFs, Word files). For chat transcripts it has a remaining flaw: fixed windows split mid-dialogue. A customer question ends chunk A; the Anela answer starts chunk B. The summarizer for chunk A sees an unanswered question and produces an incomplete summary. The summarizer for chunk B sees an answer with no context. Neither chunk captures a complete problem + solution signal.

The correct unit for a chat transcript is a **topic** — a cluster of exchanges about one subject. One LLM call on the full transcript can both detect topic boundaries and produce a keyword summary per topic. Each topic summary gets embedded separately; the full clean transcript is stored as `Content` for answer generation.

---

## Solution Overview

Introduce a `DocumentType` concept that controls which embedding strategy is applied during indexing. Users select the document type at upload time via a combobox (smart defaults by file extension). Two strategies ship initially:

| Document Type | Default for | Embedding Strategy |
|---|---|---|
| `KnowledgeBase` | `.pdf`, `.docx` | Fixed-window chunks → per-chunk keyword summary (PR #460 logic) |
| `Conversation` | `.txt` | Full transcript → one LLM call → N topic summaries → N embeddings |

For `Conversation` documents, all chunks share the same `Content` (full clean transcript). When a chunk is retrieved, the full conversation is passed to the answer-generation LLM, which focuses on the relevant topic.

---

## Architecture

```
ExtractTextAsync()
  → ChatTranscriptPreprocessor.Clean()       ← PR #460, unchanged
  → pick IIndexingStrategy by doc.DocumentType
      Conversation → ConversationIndexingStrategy
                       └─ IConversationTopicSummarizer.SummarizeTopicsAsync(fullText)
                            → parse N topic summaries (split on [TOPIC] delimiter)
                            → for each topic: embed summary → chunk(Content=fullCleanText)
      KnowledgeBase → KnowledgeBaseDocIndexingStrategy
                       └─ DocumentChunker.Chunk()
                            → for each chunk: IChunkSummarizer.SummarizeAsync(chunk)
                            → embed summary → chunk(Content=chunkText)
  → repository.AddChunksAsync(chunks)
  → document.Status = Indexed
```

`DocumentIndexingService` is a thin orchestrator. No chunking or summarization logic lives in it directly.

---

## Components

### 1. `DocumentType` Enum

New enum in the Domain layer:

```csharp
public enum DocumentType
{
    KnowledgeBase = 0,
    Conversation = 1
}
```

### 2. `KnowledgeBaseDocument` Changes

Add one property:

```csharp
public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;
```

### 3. `IIndexingStrategy`

New interface. Mirrors the existing `IDocumentTextExtractor.CanHandle()` pattern:

```csharp
public interface IIndexingStrategy
{
    bool Supports(DocumentType documentType);
    Task<IReadOnlyList<KnowledgeBaseChunk>> CreateChunksAsync(
        string cleanText, Guid documentId, CancellationToken ct);
}
```

### 4. `KnowledgeBaseDocIndexingStrategy`

Moves the PR #460 logic out of `DocumentIndexingService` into a dedicated strategy class:

- `Supports(DocumentType.KnowledgeBase)` → `true`
- `CreateChunksAsync`: runs `DocumentChunker.Chunk()`, then `IChunkSummarizer.SummarizeAsync()` per chunk, then embeds the summary. `chunk.Content` = chunk text.

Dependencies: `DocumentChunker`, `IChunkSummarizer`, `IEmbeddingGenerator`.

### 5. `IConversationTopicSummarizer` / `ConversationTopicSummarizer`

Single LLM call on the full clean transcript. The prompt instructs the model to segment by topic and return one keyword block per topic, delimited by `[TOPIC]`:

```
[TOPIC]
Produkty: Sérum ABC
Problém zákazníka: akné
Doporučení: nanášet 2x denně

[TOPIC]
Produkty: Krém XYZ
Problém zákazníka: popraskané nožky
Doporučení: aplikovat večer
```

Parsed by splitting on `[TOPIC]` → `IReadOnlyList<string>`. Empty blocks are discarded.

**Disabled mode:** when `SummarizationEnabled = false`, returns `[fullText]` (single-item list, no LLM call) — consistent with `ChunkSummarizer` behavior.

```csharp
public interface IConversationTopicSummarizer
{
    Task<IReadOnlyList<string>> SummarizeTopicsAsync(string fullText, CancellationToken ct = default);
}
```

### 6. `ConversationIndexingStrategy`

- `Supports(DocumentType.Conversation)` → `true`
- `CreateChunksAsync`: calls `SummarizeTopicsAsync(fullText)`, then for each topic summary embeds it and creates a chunk. **All chunks from the same document share the same `Content`** (full clean transcript). `ChunkIndex` = topic position (0, 1, 2...).

Dependencies: `IConversationTopicSummarizer`, `IEmbeddingGenerator`.

### 7. `DocumentIndexingService` (simplified)

Orchestrates only: extract → clean → pick strategy → create chunks → persist.

```csharp
var text = await extractor.ExtractTextAsync(content, ct);
text = _preprocessor.Clean(text);

var strategy = _strategies.FirstOrDefault(s => s.Supports(document.DocumentType))
    ?? throw new NotSupportedException($"No indexing strategy for {document.DocumentType}.");

var chunks = await strategy.CreateChunksAsync(text, document.Id, ct);
await _repository.AddChunksAsync(chunks, ct);

document.Status = DocumentStatus.Indexed;
document.IndexedAt = DateTime.UtcNow;
```

### 8. `KnowledgeBaseOptions` Additions

```csharp
/// <summary>
/// Prompt used by ConversationTopicSummarizer. Instructs the LLM to segment the
/// transcript by topic and return keyword blocks separated by TopicDelimiter.
/// </summary>
public string TopicSummarizationPrompt { get; set; } =
    """
    Jsi asistent analyzující zákaznický chat kosmetické firmy Anela.
    Rozděl konverzaci do tematických bloků. Pro každý blok vypiš klíčová data.
    Každý blok začni značkou [TOPIC] na samostatném řádku (vynech kategorie bez obsahu):

    [TOPIC]
    Produkty: <názvy produktů>
    Ingredience: <účinné látky, složky>
    Problém zákazníka: <kožní potíže, dotazy>
    Doporučení: <rady, způsob použití>

    Konverzace:
    """;

/// <summary>
/// Delimiter used to split the LLM response into individual topic summaries.
/// </summary>
public string TopicDelimiter { get; set; } = "[TOPIC]";
```

### 9. Upload API & Frontend

**`UploadDocumentRequest`** — add `DocumentType DocumentType` field.

**`UploadDocumentHandler`** — set `document.DocumentType = request.DocumentType` before saving.

**DB migration** — add `DocumentType` column (int, not null, default 0) to `KnowledgeBaseDocuments`.

**Frontend upload modal** — add document type combobox. Smart default based on selected file extension:
- `.txt` → `Conversation`
- `.pdf`, `.docx` → `KnowledgeBase`

User can override before uploading. No edit after upload — reupload to change.

---

## Testing

### `ConversationTopicSummarizerTests` (unit, mock `IChatClient`)
- Single-topic response → list with one summary
- Multi-topic response → list with N summaries parsed by `[TOPIC]` delimiter
- Empty blocks discarded
- Prompt contains full transcript text
- `SummarizationEnabled = false` → returns `[fullText]`, no LLM call

### `ConversationIndexingStrategyTests` (unit, mock summarizer + embedding generator)
- N topic summaries → N chunks created
- All chunks have identical `Content` = full clean text
- `ChunkIndex` matches topic position
- Embedding input = topic summary text, not `Content`

### `KnowledgeBaseDocIndexingStrategyTests` (unit)
- PR #460 `DocumentIndexingServiceTests` logic moves here
- Per-chunk summarization verified
- `chunk.Content` = chunk text (not summary)

### `DocumentIndexingServiceTests` (unit, simplified)
- Correct strategy selected per `DocumentType`
- Preprocessor called before strategy
- Chunks persisted, document status set to `Indexed`
- Unsupported `DocumentType` throws `NotSupportedException`

### Frontend
- Upload modal: combobox present
- `.txt` file selected → defaults to `Conversation`
- `.pdf` / `.docx` selected → defaults to `KnowledgeBase`
- Manual override works

---

## Trade-offs & Constraints

- **LLM call per document (not per chunk) for conversations** — more efficient than PR #460's per-chunk approach for transcripts; one call replaces N calls.
- **Full transcript in every chunk's `Content`** — answer generation always has full context but sends more tokens. Acceptable given typical transcript lengths (2–5k words).
- **Topic parsing is LLM-dependent** — if the model returns malformed output (no `[TOPIC]` markers), the whole transcript falls back to a single chunk. This is safe: retrieval still works, quality degrades gracefully.
- **No re-index on type change** — document type is set at upload. Reupload required to change strategy.

---

## Acceptance Criteria

- [ ] `DocumentType` enum added to Domain
- [ ] `KnowledgeBaseDocument.DocumentType` property added + DB migration
- [ ] `IIndexingStrategy` interface implemented by `ConversationIndexingStrategy` and `KnowledgeBaseDocIndexingStrategy`
- [ ] `IConversationTopicSummarizer` / `ConversationTopicSummarizer` implemented
- [ ] `DocumentIndexingService` refactored to thin orchestrator
- [ ] `KnowledgeBaseModule` registers new services
- [ ] `KnowledgeBaseOptions` extended with `TopicSummarizationPrompt` and `TopicDelimiter`
- [ ] `UploadDocumentRequest` accepts `DocumentType`
- [ ] Frontend upload modal has document type combobox with smart defaults
- [ ] All new unit tests pass
- [ ] `dotnet format` passes
