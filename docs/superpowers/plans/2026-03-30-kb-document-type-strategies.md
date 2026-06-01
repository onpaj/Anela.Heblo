# KB Document-Type-Aware Embedding Strategies — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce a `DocumentType` concept that routes each KB document through the right embedding strategy — `Conversation` (one LLM call → N topic summaries → N embeddings, full transcript as Content) or `KnowledgeBase` (fixed-window chunks → per-chunk keyword summaries, PR #460 logic).

**Architecture:** `IIndexingStrategy` interface with two implementations. `DocumentIndexingService` becomes a thin orchestrator: extract → clean → pick strategy by `document.DocumentType` → create chunks → persist. `DocumentType` is set once at upload and stored on `KnowledgeBaseDocument`.

**Tech Stack:** C# / .NET 8, PostgreSQL + EF Core (Npgsql), xUnit, Moq, `Microsoft.Extensions.AI` (`IChatClient`, `IEmbeddingGenerator`), React + TypeScript, React Testing Library

**Branch:** Create `feat/issue-455-kb-document-type-strategies` off `feat/issue-454-kb-preprocessing` (PR #460). All tasks assume PR #460 code is present: `ChatTranscriptPreprocessor`, `IChunkSummarizer`, `ChunkSummarizer`, updated `KnowledgeBaseOptions` (with `PreprocessorPatterns`, `SummarizationEnabled`, `SummarizationPrompt`), and the PR #460 version of `DocumentIndexingService` (which has preprocessor + summarizer wired in).

---

## Setup: Create branch

```bash
git fetch origin
git checkout feat/issue-454-kb-preprocessing
git checkout -b feat/issue-455-kb-document-type-strategies
```

---

## File Map

| Action | Path |
|--------|------|
| Modify | `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseDocument.cs` |
| Modify | `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseDocumentConfiguration.cs` |
| Create | `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddDocumentTypeToKnowledgeBaseDocument.cs` |
| Create | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IIndexingStrategy.cs` |
| Create | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs` |
| Create | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IConversationTopicSummarizer.cs` |
| Create | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationTopicSummarizer.cs` |
| Create | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationIndexingStrategy.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentIndexingService.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs` |
| Modify | `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs` |
| Create | `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategyTests.cs` |
| Create | `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ConversationTopicSummarizerTests.cs` |
| Create | `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ConversationIndexingStrategyTests.cs` |
| Modify | `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentIndexingServiceTests.cs` |
| Modify | `frontend/src/api/hooks/useKnowledgeBase.ts` |
| Modify | `frontend/src/components/knowledge-base/KnowledgeBaseUploadTab.tsx` |
| Modify | `frontend/src/components/knowledge-base/__tests__/KnowledgeBaseUploadTab.test.tsx` |

---

## Task 1: Domain + Persistence — DocumentType enum + migration

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseDocument.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseDocumentConfiguration.cs`
- Create migration via `dotnet ef`

- [ ] **Step 1: Add DocumentType enum and property to KnowledgeBaseDocument**

Replace the contents of `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseDocument.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.KnowledgeBase;

public class KnowledgeBaseDocument
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty; // SHA-256 hex, 64 chars
    public DocumentStatus Status { get; set; } = DocumentStatus.Processing;
    public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;
    public DateTime CreatedAt { get; set; }
    public DateTime? IndexedAt { get; set; }

    public ICollection<KnowledgeBaseChunk> Chunks { get; set; } = new List<KnowledgeBaseChunk>();
}

public enum DocumentStatus
{
    Processing,
    Indexed,
    Failed
}

public enum DocumentType
{
    KnowledgeBase = 0,
    Conversation = 1
}
```

- [ ] **Step 2: Map DocumentType in EF configuration**

Replace the contents of `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseDocumentConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.KnowledgeBase;

public class KnowledgeBaseDocumentConfiguration : IEntityTypeConfiguration<KnowledgeBaseDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeBaseDocument> builder)
    {
        builder.ToTable("KnowledgeBaseDocuments", "dbo");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Filename)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.SourcePath)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(e => e.ContentType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Status)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion(
                v => v.ToString().ToLowerInvariant(),
                v => Enum.Parse<DocumentStatus>(v, true));

        builder.Property(e => e.DocumentType)
            .IsRequired()
            .HasDefaultValue(DocumentType.KnowledgeBase)
            .HasConversion<int>();

        builder.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.IndexedAt)
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.ContentHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(e => e.ContentHash)
            .IsUnique();

        builder.HasMany(e => e.Chunks)
            .WithOne(e => e.Document)
            .HasForeignKey(e => e.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.SourcePath)
            .IsUnique();

        builder.HasIndex(e => e.Status);
    }
}
```

- [ ] **Step 3: Build to verify compilation**

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Generate the migration**

```bash
dotnet ef migrations add AddDocumentTypeToKnowledgeBaseDocument \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```

Expected: New migration file created in `backend/src/Anela.Heblo.Persistence/Migrations/`.

- [ ] **Step 5: Verify migration content**

Open the generated migration file. Confirm `Up` contains:

```csharp
migrationBuilder.AddColumn<int>(
    name: "DocumentType",
    schema: "dbo",
    table: "KnowledgeBaseDocuments",
    type: "integer",
    nullable: false,
    defaultValue: 0);
```

If the migration looks wrong (e.g., recreates the table), remove it with:
```bash
dotnet ef migrations remove --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API
```
Then fix the configuration and regenerate.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseDocument.cs \
        backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseDocumentConfiguration.cs \
        backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat(knowledge-base): add DocumentType enum and EF migration"
```

---

## Task 2: IIndexingStrategy + KnowledgeBaseDocIndexingStrategy

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IIndexingStrategy.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs`
- Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategyTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategyTests.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class KnowledgeBaseDocIndexingStrategyTests
{
    private readonly Mock<IChunkSummarizer> _summarizer;
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _embeddingGenerator;
    private readonly GeneratedEmbeddings<Embedding<float>> _generatedEmbeddings;
    private readonly KnowledgeBaseDocIndexingStrategy _strategy;

    public KnowledgeBaseDocIndexingStrategyTests()
    {
        _summarizer = new Mock<IChunkSummarizer>();
        _summarizer
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) => text);

        var floats = new float[] { 0.1f, 0.2f, 0.3f };
        _generatedEmbeddings = new GeneratedEmbeddings<Embedding<float>>(
            [new Embedding<float>(new ReadOnlyMemory<float>(floats))]);

        _embeddingGenerator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        _embeddingGenerator
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_generatedEmbeddings);

        var options = Options.Create(new KnowledgeBaseOptions { ChunkSize = 512, ChunkOverlapTokens = 50 });
        var chunker = new DocumentChunker(options);

        _strategy = new KnowledgeBaseDocIndexingStrategy(
            chunker,
            _summarizer.Object,
            _embeddingGenerator.Object);
    }

    [Fact]
    public void Supports_KnowledgeBase_ReturnsTrue()
    {
        Assert.True(_strategy.Supports(DocumentType.KnowledgeBase));
    }

    [Fact]
    public void Supports_Conversation_ReturnsFalse()
    {
        Assert.False(_strategy.Supports(DocumentType.Conversation));
    }

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

    [Fact]
    public async Task CreateChunksAsync_EmbeddingIsGeneratedFromSummary()
    {
        const string summary = "Problém zákazníka: akné";

        _summarizer
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        string? capturedEmbeddingInput = null;
        _embeddingGenerator
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>(
                (texts, _, _) => capturedEmbeddingInput = texts.First())
            .ReturnsAsync(_generatedEmbeddings);

        var documentId = Guid.NewGuid();
        await _strategy.CreateChunksAsync("word1 word2 word3", documentId, CancellationToken.None);

        Assert.Equal(summary, capturedEmbeddingInput);
    }

    [Fact]
    public async Task CreateChunksAsync_ChunkContentIsChunkText_NotSummary()
    {
        const string extractedText = "word1 word2 word3";
        const string summary = "Problém zákazníka: suchá pleť";

        _summarizer
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        var documentId = Guid.NewGuid();
        var chunks = await _strategy.CreateChunksAsync(extractedText, documentId, CancellationToken.None);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk =>
        {
            Assert.Equal(extractedText, chunk.Content);
            Assert.DoesNotContain(summary, chunk.Content);
        });
    }

    [Fact]
    public async Task CreateChunksAsync_ChunkIndexIsSequential()
    {
        var options = Options.Create(new KnowledgeBaseOptions { ChunkSize = 5, ChunkOverlapTokens = 1 });
        var chunker = new DocumentChunker(options);
        var strategy = new KnowledgeBaseDocIndexingStrategy(chunker, _summarizer.Object, _embeddingGenerator.Object);

        var words = string.Join(" ", Enumerable.Range(1, 20).Select(i => $"w{i}"));
        var chunks = await strategy.CreateChunksAsync(words, Guid.NewGuid(), CancellationToken.None);

        Assert.True(chunks.Count > 1);
        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~KnowledgeBaseDocIndexingStrategyTests" -v minimal
```

Expected: compile error — `IIndexingStrategy` and `KnowledgeBaseDocIndexingStrategy` do not exist.

- [ ] **Step 3: Create IIndexingStrategy interface**

Create `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IIndexingStrategy.cs`:

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public interface IIndexingStrategy
{
    bool Supports(DocumentType documentType);
    Task<IReadOnlyList<KnowledgeBaseChunk>> CreateChunksAsync(
        string cleanText, Guid documentId, CancellationToken ct);
}
```

- [ ] **Step 4: Implement KnowledgeBaseDocIndexingStrategy**

Create `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs`:

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.AI;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class KnowledgeBaseDocIndexingStrategy : IIndexingStrategy
{
    private readonly DocumentChunker _chunker;
    private readonly IChunkSummarizer _summarizer;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public KnowledgeBaseDocIndexingStrategy(
        DocumentChunker chunker,
        IChunkSummarizer summarizer,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _chunker = chunker;
        _summarizer = summarizer;
        _embeddingGenerator = embeddingGenerator;
    }

    public bool Supports(DocumentType documentType) =>
        documentType == DocumentType.KnowledgeBase;

    public async Task<IReadOnlyList<KnowledgeBaseChunk>> CreateChunksAsync(
        string cleanText, Guid documentId, CancellationToken ct)
    {
        var chunkTexts = _chunker.Chunk(cleanText);
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
                Embedding = embeddings[0].Vector.ToArray(),
            });
        }

        return chunks;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~KnowledgeBaseDocIndexingStrategyTests" -v minimal
```

Expected: 5 tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IIndexingStrategy.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategyTests.cs
git commit -m "feat(knowledge-base): add IIndexingStrategy interface and KnowledgeBaseDocIndexingStrategy"
```

---

## Task 3: IConversationTopicSummarizer + ConversationTopicSummarizer + KnowledgeBaseOptions

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IConversationTopicSummarizer.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationTopicSummarizer.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs`
- Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ConversationTopicSummarizerTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ConversationTopicSummarizerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class ConversationTopicSummarizerTests
{
    private readonly Mock<IChatClient> _chatClient = new();

    private ConversationTopicSummarizer Create(KnowledgeBaseOptions? options = null)
        => new(_chatClient.Object, Options.Create(options ?? new KnowledgeBaseOptions()));

    [Fact]
    public async Task SummarizeTopicsAsync_SingleTopicResponse_ReturnsOneItem()
    {
        const string llmResponse =
            "[TOPIC]\nProdukty: Sérum ABC\nProblém zákazníka: akné";

        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                [new ChatMessage(ChatRole.Assistant, llmResponse)]));

        var summarizer = Create();
        var result = await summarizer.SummarizeTopicsAsync("some transcript");

        Assert.Single(result);
        Assert.Contains("Sérum ABC", result[0]);
    }

    [Fact]
    public async Task SummarizeTopicsAsync_MultiTopicResponse_ReturnsMultipleItems()
    {
        const string llmResponse =
            "[TOPIC]\nProdukty: Sérum ABC\nProblém zákazníka: akné\n\n" +
            "[TOPIC]\nProdukty: Krém XYZ\nProblém zákazníka: popraskané nožky";

        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                [new ChatMessage(ChatRole.Assistant, llmResponse)]));

        var summarizer = Create();
        var result = await summarizer.SummarizeTopicsAsync("some transcript");

        Assert.Equal(2, result.Count);
        Assert.Contains("Sérum ABC", result[0]);
        Assert.Contains("Krém XYZ", result[1]);
    }

    [Fact]
    public async Task SummarizeTopicsAsync_EmptyBlocksDiscarded()
    {
        // LLM response starts with [TOPIC] — split produces empty string before first block
        const string llmResponse =
            "[TOPIC]\nProdukty: Sérum ABC\n\n[TOPIC]\n\n[TOPIC]\nProdukty: Krém XYZ";

        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                [new ChatMessage(ChatRole.Assistant, llmResponse)]));

        var summarizer = Create();
        var result = await summarizer.SummarizeTopicsAsync("transcript");

        // Empty block in the middle must be discarded
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task SummarizeTopicsAsync_PromptContainsFullText()
    {
        const string fullText = "Zákazník: Dobrý den, mám problém s akné";

        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.Is<IEnumerable<ChatMessage>>(m =>
                    m.First().Text!.Contains(fullText)),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                [new ChatMessage(ChatRole.Assistant, "[TOPIC]\nProblém zákazníka: akné")]));

        var summarizer = Create();
        await summarizer.SummarizeTopicsAsync(fullText);

        _chatClient.Verify(
            c => c.GetResponseAsync(
                It.Is<IEnumerable<ChatMessage>>(m => m.First().Text!.Contains(fullText)),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SummarizeTopicsAsync_WhenDisabled_ReturnsFullTextWithoutCallingLlm()
    {
        var options = new KnowledgeBaseOptions { SummarizationEnabled = false };
        var summarizer = Create(options);
        const string fullText = "Some conversation text";

        var result = await summarizer.SummarizeTopicsAsync(fullText);

        Assert.Single(result);
        Assert.Equal(fullText, result[0]);
        _chatClient.Verify(
            c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConversationTopicSummarizerTests" -v minimal
```

Expected: compile error — `IConversationTopicSummarizer`, `ConversationTopicSummarizer`, and `KnowledgeBaseOptions.TopicSummarizationPrompt` do not exist.

- [ ] **Step 3: Extend KnowledgeBaseOptions**

Append to `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs` (add the two new properties inside the class, after `SummarizationPrompt`):

The full file becomes:

```csharp
namespace Anela.Heblo.Application.Features.KnowledgeBase;

public class KnowledgeBaseOptions
{
    public string OneDriveInboxPath { get; set; } = "/KnowledgeBase/Inbox";
    public string OneDriveArchivedPath { get; set; } = "/KnowledgeBase/Archived";
    public int ChunkSize { get; set; } = 512;
    public int ChunkOverlapTokens { get; set; } = 50;
    public int MaxRetrievedChunks { get; set; } = 5;

    /// <summary>
    /// UPN or object ID of the OneDrive user account used for ingestion (app-only access).
    /// Example: "service@anela.cz" or a GUID object ID.
    /// </summary>
    public string OneDriveUserId { get; set; } = string.Empty;

    /// <summary>
    /// Regex patterns stripped from documents before chunking.
    /// Patterns are compiled with Singleline + Multiline flags.
    /// Defaults cover the standard Anela chat transcript boilerplate.
    /// </summary>
    public List<string> PreprocessorPatterns { get; set; } =
    [
        @"Vítejte ve světě Anela.*?Napište nám, jsme tu pro Vás!",
        @"(?m)^datum:\s+\S+\s+zákazník:\s+\S+",
        @"Zákazník-\d+:?\s*"
    ];

    /// <summary>
    /// When true, each chunk (or full conversation) is summarized by the LLM before embedding.
    /// Set to false to skip LLM calls (e.g. in tests or cost-free re-index runs).
    /// </summary>
    public bool SummarizationEnabled { get; set; } = true;

    /// <summary>
    /// Prompt used by ChunkSummarizer (KnowledgeBase strategy).
    /// The chunk text is appended after a newline.
    /// </summary>
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

    /// <summary>
    /// Prompt used by ConversationTopicSummarizer. Instructs the LLM to segment
    /// the full transcript by topic and return keyword blocks separated by TopicDelimiter.
    /// The full transcript text is appended after a newline.
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
}
```

- [ ] **Step 4: Create IConversationTopicSummarizer**

Create `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IConversationTopicSummarizer.cs`:

```csharp
namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public interface IConversationTopicSummarizer
{
    Task<IReadOnlyList<string>> SummarizeTopicsAsync(string fullText, CancellationToken ct = default);
}
```

- [ ] **Step 5: Implement ConversationTopicSummarizer**

Create `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationTopicSummarizer.cs`:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class ConversationTopicSummarizer : IConversationTopicSummarizer
{
    private readonly IChatClient _chatClient;
    private readonly KnowledgeBaseOptions _options;

    public ConversationTopicSummarizer(IChatClient chatClient, IOptions<KnowledgeBaseOptions> options)
    {
        _chatClient = chatClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<string>> SummarizeTopicsAsync(
        string fullText, CancellationToken ct = default)
    {
        if (!_options.SummarizationEnabled)
            return [fullText];

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, _options.TopicSummarizationPrompt + "\n" + fullText)
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        var responseText = response.Text ?? fullText;

        var topics = responseText
            .Split(_options.TopicDelimiter, StringSplitOptions.None)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        return topics.Count > 0 ? topics : [fullText];
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConversationTopicSummarizerTests" -v minimal
```

Expected: 5 tests pass.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IConversationTopicSummarizer.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationTopicSummarizer.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ConversationTopicSummarizerTests.cs
git commit -m "feat(knowledge-base): add ConversationTopicSummarizer for topic-segmented embeddings"
```

---

## Task 4: ConversationIndexingStrategy

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationIndexingStrategy.cs`
- Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ConversationIndexingStrategyTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ConversationIndexingStrategyTests.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class ConversationIndexingStrategyTests
{
    private readonly Mock<IConversationTopicSummarizer> _summarizer;
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _embeddingGenerator;
    private readonly GeneratedEmbeddings<Embedding<float>> _generatedEmbeddings;
    private readonly ConversationIndexingStrategy _strategy;

    public ConversationIndexingStrategyTests()
    {
        _summarizer = new Mock<IConversationTopicSummarizer>();

        var floats = new float[] { 0.1f, 0.2f, 0.3f };
        _generatedEmbeddings = new GeneratedEmbeddings<Embedding<float>>(
            [new Embedding<float>(new ReadOnlyMemory<float>(floats))]);

        _embeddingGenerator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        _embeddingGenerator
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_generatedEmbeddings);

        _strategy = new ConversationIndexingStrategy(
            _summarizer.Object,
            _embeddingGenerator.Object);
    }

    [Fact]
    public void Supports_Conversation_ReturnsTrue()
    {
        Assert.True(_strategy.Supports(DocumentType.Conversation));
    }

    [Fact]
    public void Supports_KnowledgeBase_ReturnsFalse()
    {
        Assert.False(_strategy.Supports(DocumentType.KnowledgeBase));
    }

    [Fact]
    public async Task CreateChunksAsync_NTopicSummaries_ProducesNChunks()
    {
        var topics = new List<string>
        {
            "Produkty: Sérum ABC\nProblém zákazníka: akné",
            "Produkty: Krém XYZ\nProblém zákazníka: popraskané nožky"
        };

        _summarizer
            .Setup(s => s.SummarizeTopicsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(topics);

        var documentId = Guid.NewGuid();
        var chunks = await _strategy.CreateChunksAsync("full transcript", documentId, CancellationToken.None);

        Assert.Equal(2, chunks.Count);
    }

    [Fact]
    public async Task CreateChunksAsync_AllChunksHaveFullTranscriptAsContent()
    {
        const string fullText = "Zákazník: Mám problém s akné\nAnela: Doporučuji Sérum ABC";
        var topics = new List<string>
        {
            "Problém zákazníka: akné",
            "Doporučení: Sérum ABC"
        };

        _summarizer
            .Setup(s => s.SummarizeTopicsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(topics);

        var chunks = await _strategy.CreateChunksAsync(fullText, Guid.NewGuid(), CancellationToken.None);

        Assert.All(chunks, chunk => Assert.Equal(fullText, chunk.Content));
    }

    [Fact]
    public async Task CreateChunksAsync_ChunkIndexMatchesTopicPosition()
    {
        var topics = new List<string> { "Topic 0", "Topic 1", "Topic 2" };

        _summarizer
            .Setup(s => s.SummarizeTopicsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(topics);

        var chunks = await _strategy.CreateChunksAsync("transcript", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(3, chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
        }
    }

    [Fact]
    public async Task CreateChunksAsync_EmbeddingInputIsTopicSummary_NotFullText()
    {
        const string fullText = "full transcript text";
        const string topicSummary = "Problém zákazníka: akné";

        _summarizer
            .Setup(s => s.SummarizeTopicsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { topicSummary });

        string? capturedEmbeddingInput = null;
        _embeddingGenerator
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>(
                (texts, _, _) => capturedEmbeddingInput = texts.First())
            .ReturnsAsync(_generatedEmbeddings);

        await _strategy.CreateChunksAsync(fullText, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(topicSummary, capturedEmbeddingInput);
        Assert.NotEqual(fullText, capturedEmbeddingInput);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConversationIndexingStrategyTests" -v minimal
```

Expected: compile error — `ConversationIndexingStrategy` does not exist.

- [ ] **Step 3: Implement ConversationIndexingStrategy**

Create `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationIndexingStrategy.cs`:

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.AI;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class ConversationIndexingStrategy : IIndexingStrategy
{
    private readonly IConversationTopicSummarizer _summarizer;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public ConversationIndexingStrategy(
        IConversationTopicSummarizer summarizer,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _summarizer = summarizer;
        _embeddingGenerator = embeddingGenerator;
    }

    public bool Supports(DocumentType documentType) =>
        documentType == DocumentType.Conversation;

    public async Task<IReadOnlyList<KnowledgeBaseChunk>> CreateChunksAsync(
        string cleanText, Guid documentId, CancellationToken ct)
    {
        var topics = await _summarizer.SummarizeTopicsAsync(cleanText, ct);
        var chunks = new List<KnowledgeBaseChunk>();

        for (var i = 0; i < topics.Count; i++)
        {
            var embeddings = await _embeddingGenerator.GenerateAsync([topics[i]], cancellationToken: ct);
            chunks.Add(new KnowledgeBaseChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                ChunkIndex = i,
                Content = cleanText,
                Embedding = embeddings[0].Vector.ToArray(),
            });
        }

        return chunks;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConversationIndexingStrategyTests" -v minimal
```

Expected: 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationIndexingStrategy.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ConversationIndexingStrategyTests.cs
git commit -m "feat(knowledge-base): add ConversationIndexingStrategy for topic-per-chunk embedding"
```

---

## Task 5: DocumentIndexingService refactor + KnowledgeBaseModule

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentIndexingService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`
- Modify: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentIndexingServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Replace the contents of `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentIndexingServiceTests.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class DocumentIndexingServiceTests
{
    private readonly Mock<IDocumentTextExtractor> _pdfExtractor;
    private readonly Mock<IKnowledgeBaseRepository> _repository;
    private readonly Mock<IIndexingStrategy> _kbStrategy;
    private readonly Mock<IIndexingStrategy> _conversationStrategy;
    private readonly DocumentIndexingService _service;
    private readonly KnowledgeBaseOptions _options;

    public DocumentIndexingServiceTests()
    {
        _pdfExtractor = new Mock<IDocumentTextExtractor>();
        _pdfExtractor.Setup(e => e.CanHandle("application/pdf")).Returns(true);
        _pdfExtractor
            .Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("word1 word2 word3");

        _repository = new Mock<IKnowledgeBaseRepository>();

        _kbStrategy = new Mock<IIndexingStrategy>();
        _kbStrategy.Setup(s => s.Supports(DocumentType.KnowledgeBase)).Returns(true);
        _kbStrategy.Setup(s => s.Supports(DocumentType.Conversation)).Returns(false);
        _kbStrategy
            .Setup(s => s.CreateChunksAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeBaseChunk>
            {
                new() { Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), ChunkIndex = 0, Content = "chunk", Embedding = [0.1f] }
            });

        _conversationStrategy = new Mock<IIndexingStrategy>();
        _conversationStrategy.Setup(s => s.Supports(DocumentType.Conversation)).Returns(true);
        _conversationStrategy.Setup(s => s.Supports(DocumentType.KnowledgeBase)).Returns(false);
        _conversationStrategy
            .Setup(s => s.CreateChunksAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeBaseChunk>
            {
                new() { Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), ChunkIndex = 0, Content = "chunk", Embedding = [0.1f] }
            });

        _options = new KnowledgeBaseOptions();
        var preprocessor = new ChatTranscriptPreprocessor(Options.Create(_options));

        _service = new DocumentIndexingService(
            new[] { _pdfExtractor.Object },
            _repository.Object,
            preprocessor,
            new[] { _kbStrategy.Object, _conversationStrategy.Object });
    }

    [Fact]
    public async Task IndexChunksAsync_KnowledgeBaseDocument_UsesKbStrategy()
    {
        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid(), DocumentType = DocumentType.KnowledgeBase };

        await _service.IndexChunksAsync([], "application/pdf", doc, CancellationToken.None);

        _kbStrategy.Verify(
            s => s.CreateChunksAsync(It.IsAny<string>(), doc.Id, It.IsAny<CancellationToken>()),
            Times.Once);
        _conversationStrategy.Verify(
            s => s.CreateChunksAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IndexChunksAsync_ConversationDocument_UsesConversationStrategy()
    {
        _pdfExtractor
            .Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("conversation text");

        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid(), DocumentType = DocumentType.Conversation };

        await _service.IndexChunksAsync([], "application/pdf", doc, CancellationToken.None);

        _conversationStrategy.Verify(
            s => s.CreateChunksAsync(It.IsAny<string>(), doc.Id, It.IsAny<CancellationToken>()),
            Times.Once);
        _kbStrategy.Verify(
            s => s.CreateChunksAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IndexChunksAsync_ChunksPersistedAndStatusSetToIndexed()
    {
        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid(), DocumentType = DocumentType.KnowledgeBase };

        await _service.IndexChunksAsync([], "application/pdf", doc, CancellationToken.None);

        _repository.Verify(
            r => r.AddChunksAsync(It.IsAny<IEnumerable<KnowledgeBaseChunk>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(DocumentStatus.Indexed, doc.Status);
        Assert.NotNull(doc.IndexedAt);
    }

    [Fact]
    public async Task IndexChunksAsync_UnsupportedContentType_ThrowsNotSupportedException()
    {
        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid() };

        await Assert.ThrowsAsync<NotSupportedException>(
            () => _service.IndexChunksAsync([], "image/png", doc, CancellationToken.None));
    }

    [Fact]
    public async Task IndexChunksAsync_PreprocessorStripsBoilerplateBeforeStrategy()
    {
        const string boilerplateText =
            "datum: 04.11.2025 zákazník: Zákazník-0001\nAnela: bisabolol je vhodný pro citlivou pleť";

        _pdfExtractor
            .Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(boilerplateText);

        string? capturedCleanText = null;
        _kbStrategy
            .Setup(s => s.CreateChunksAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, CancellationToken>((text, _, _) => capturedCleanText = text)
            .ReturnsAsync(new List<KnowledgeBaseChunk>());

        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid(), DocumentType = DocumentType.KnowledgeBase };
        await _service.IndexChunksAsync([], "application/pdf", doc, CancellationToken.None);

        Assert.NotNull(capturedCleanText);
        Assert.DoesNotContain("datum:", capturedCleanText);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DocumentIndexingServiceTests" -v minimal
```

Expected: compile error — `DocumentIndexingService` constructor does not match the new signature.

- [ ] **Step 3: Refactor DocumentIndexingService**

Replace the contents of `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentIndexingService.cs`:

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class DocumentIndexingService : IDocumentIndexingService
{
    private readonly IEnumerable<IDocumentTextExtractor> _extractors;
    private readonly IKnowledgeBaseRepository _repository;
    private readonly ChatTranscriptPreprocessor _preprocessor;
    private readonly IEnumerable<IIndexingStrategy> _strategies;

    public DocumentIndexingService(
        IEnumerable<IDocumentTextExtractor> extractors,
        IKnowledgeBaseRepository repository,
        ChatTranscriptPreprocessor preprocessor,
        IEnumerable<IIndexingStrategy> strategies)
    {
        _extractors = extractors;
        _repository = repository;
        _preprocessor = preprocessor;
        _strategies = strategies;
    }

    public async Task IndexChunksAsync(
        byte[] content,
        string contentType,
        KnowledgeBaseDocument document,
        CancellationToken ct = default)
    {
        var extractor = _extractors.FirstOrDefault(e => e.CanHandle(contentType))
            ?? throw new NotSupportedException($"Content type '{contentType}' is not supported.");

        var text = await extractor.ExtractTextAsync(content, ct);
        text = _preprocessor.Clean(text);

        var strategy = _strategies.FirstOrDefault(s => s.Supports(document.DocumentType))
            ?? throw new NotSupportedException($"No indexing strategy for DocumentType '{document.DocumentType}'.");

        var chunks = await strategy.CreateChunksAsync(text, document.Id, ct);
        await _repository.AddChunksAsync(chunks, ct);

        document.Status = DocumentStatus.Indexed;
        document.IndexedAt = DateTime.UtcNow;
    }
}
```

- [ ] **Step 4: Update KnowledgeBaseModule**

Replace the contents of `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.Services.DocumentExtractors;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Domain.Features.Configuration;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.KnowledgeBase;

public static class KnowledgeBaseModule
{
    public static IServiceCollection AddKnowledgeBaseModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind options
        services.Configure<KnowledgeBaseOptions>(configuration.GetSection("KnowledgeBase"));

        // Register application services
        services.AddScoped<IDocumentTextExtractor, PdfTextExtractor>();
        services.AddScoped<IDocumentTextExtractor, WordDocumentExtractor>();
        services.AddScoped<IDocumentTextExtractor, PlainTextExtractor>();
        services.AddScoped<DocumentChunker>();
        services.AddScoped<ChatTranscriptPreprocessor>();
        services.AddScoped<IChunkSummarizer, ChunkSummarizer>();
        services.AddScoped<IConversationTopicSummarizer, ConversationTopicSummarizer>();
        services.AddScoped<IIndexingStrategy, KnowledgeBaseDocIndexingStrategy>();
        services.AddScoped<IIndexingStrategy, ConversationIndexingStrategy>();
        services.AddScoped<IDocumentIndexingService, DocumentIndexingService>();

        // IKnowledgeBaseRepository is registered in PersistenceModule (real EF Core implementation)

        // OneDrive service — use mock in mock auth mode (no ITokenAcquisition available)
        var useMockAuth = configuration.GetValue<bool>(ConfigurationConstants.USE_MOCK_AUTH, defaultValue: false);
        var bypassJwtValidation = configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, defaultValue: false);

        if (useMockAuth || bypassJwtValidation)
        {
            services.AddScoped<IOneDriveService, MockOneDriveService>();
        }
        else
        {
            services.AddHttpClient("MicrosoftGraph");
            services.AddScoped<IOneDriveService, GraphOneDriveService>();
        }

        // Register QuestionLoggingBehavior scoped to KB (not global like ValidationBehavior)
        services.AddScoped<IPipelineBehavior<AskQuestionRequest, AskQuestionResponse>, QuestionLoggingBehavior>();

        // MediatR handlers are automatically registered by AddMediatR scan

        return services;
    }
}
```

- [ ] **Step 5: Run all KB tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~KnowledgeBase" -v minimal
```

Expected: all KB tests pass. If `DocumentIndexingServiceTests` from PR #460 (the old tests about embedding-from-summary, chunk-content-not-summary, etc.) still exist in the file, remove them — they have been superseded by `KnowledgeBaseDocIndexingStrategyTests`.

- [ ] **Step 6: Run full test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v minimal
```

Expected: all tests pass.

- [ ] **Step 7: Run dotnet format**

```bash
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --verify-no-changes
dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --verify-no-changes
```

If violations reported, run without `--verify-no-changes` to fix, then re-run with it.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentIndexingService.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentIndexingServiceTests.cs
git commit -m "feat(knowledge-base): refactor DocumentIndexingService to thin orchestrator using IIndexingStrategy"
```

---

## Task 6: Controller + UploadDocumentRequest

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs`

- [ ] **Step 1: Add DocumentType to UploadDocumentRequest**

Replace the contents of `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs`:

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;

public class UploadDocumentRequest : IRequest<UploadDocumentResponse>
{
    public Stream FileStream { get; set; } = default!;
    public string Filename { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long FileSizeBytes { get; set; }
    public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;
}
```

- [ ] **Step 2: Set DocumentType in UploadDocumentHandler**

In `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs`, find the document creation block and add `DocumentType = request.DocumentType`:

```csharp
var doc = new KnowledgeBaseDocument
{
    Id = Guid.NewGuid(),
    Filename = request.Filename,
    SourcePath = $"upload/{Guid.NewGuid()}/{request.Filename}",
    ContentType = contentType,
    ContentHash = hash,
    Status = DocumentStatus.Processing,
    DocumentType = request.DocumentType,
    CreatedAt = DateTime.UtcNow,
};
```

- [ ] **Step 3: Accept documentType in the controller**

In `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs`, update the `UploadDocument` action:

```csharp
[HttpPost("documents/upload")]
[Authorize(Policy = AuthorizationConstants.Policies.KnowledgeBaseUpload)]
public async Task<ActionResult<UploadDocumentResponse>> UploadDocument(
    IFormFile file,
    [FromForm] string documentType = "KnowledgeBase",
    CancellationToken ct = default)
{
    if (file is null)
        return BadRequest(new UploadDocumentResponse { Success = false });

    var parsedDocumentType = Enum.TryParse<DocumentType>(documentType, ignoreCase: true, out var dt)
        ? dt
        : DocumentType.KnowledgeBase;

    await using var stream = file.OpenReadStream();
    var request = new UploadDocumentRequest
    {
        FileStream = stream,
        Filename = file.FileName,
        ContentType = file.ContentType,
        FileSizeBytes = file.Length,
        DocumentType = parsedDocumentType,
    };
    var result = await _mediator.Send(request, ct);
    return HandleResponse(result);
}
```

- [ ] **Step 4: Build and run full test suite**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v minimal
```

Expected: build succeeds, all tests pass.

- [ ] **Step 5: Run dotnet format**

```bash
dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --verify-no-changes
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --verify-no-changes
```

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs \
        backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs
git commit -m "feat(knowledge-base): pass DocumentType through upload request and controller"
```

---

## Task 7: Frontend — hook + upload tab

**Files:**
- Modify: `frontend/src/api/hooks/useKnowledgeBase.ts`
- Modify: `frontend/src/components/knowledge-base/KnowledgeBaseUploadTab.tsx`
- Modify: `frontend/src/components/knowledge-base/__tests__/KnowledgeBaseUploadTab.test.tsx`

- [ ] **Step 1: Write failing tests**

The existing tests call `mockMutateAsync` with a `File` object. After this task, the mutation receives `{ file: File; documentType: DocumentType }`. We also need tests for the new combobox.

Replace the contents of `frontend/src/components/knowledge-base/__tests__/KnowledgeBaseUploadTab.test.tsx`:

```tsx
import React from 'react';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import '@testing-library/jest-dom';
import KnowledgeBaseUploadTab from '../KnowledgeBaseUploadTab';
import { useUploadKnowledgeBaseDocumentMutation } from '../../../api/hooks/useKnowledgeBase';

jest.mock('lucide-react', () => ({
  Upload: () => <svg data-testid="icon-upload" />,
  X: () => <svg data-testid="icon-x" />,
  FileText: () => <svg data-testid="icon-filetext" />,
}));

jest.mock('../../../api/hooks/useKnowledgeBase', () => ({
  useUploadKnowledgeBaseDocumentMutation: jest.fn(),
}));

const mockUseUploadKnowledgeBaseDocumentMutation =
  useUploadKnowledgeBaseDocumentMutation as jest.Mock;

const makeFile = (name: string, type = 'application/pdf'): File =>
  new File(['content'], name, { type });

const simulateDrop = (dropZone: HTMLElement, files: File[]) => {
  const dataTransfer = {
    files: Object.assign(files, {
      item: (i: number) => files[i],
      length: files.length,
    }),
  };
  fireEvent.drop(dropZone, { dataTransfer });
};

const getDropZone = () =>
  screen.getByText('Přetáhněte soubory sem').closest('div[class*="border-dashed"]') as HTMLElement;

describe('KnowledgeBaseUploadTab', () => {
  let mockMutateAsync: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockMutateAsync = jest.fn().mockResolvedValue({ success: true, document: null });
    mockUseUploadKnowledgeBaseDocumentMutation.mockReturnValue({
      mutateAsync: mockMutateAsync,
    });
  });

  it('drops 3 valid files and all appear in the list with "Čeká" status', () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    const files = [
      makeFile('document1.pdf'),
      makeFile('report.docx', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'),
      makeFile('notes.txt', 'text/plain'),
    ];
    act(() => { simulateDrop(dropZone, files); });
    expect(screen.getByText('document1.pdf')).toBeInTheDocument();
    expect(screen.getByText('report.docx')).toBeInTheDocument();
    expect(screen.getByText('notes.txt')).toBeInTheDocument();
    const waitingStatuses = screen.getAllByText('Čeká');
    expect(waitingStatuses).toHaveLength(3);
  });

  it('does not add a file with unsupported extension (.exe) to the queue', () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    act(() => { simulateDrop(dropZone, [makeFile('virus.exe', 'application/octet-stream')]); });
    expect(screen.queryByText('virus.exe')).not.toBeInTheDocument();
  });

  it('accepts a file with uppercase extension (.PDF)', () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    act(() => { simulateDrop(dropZone, [makeFile('DOCUMENT.PDF')]); });
    expect(screen.getByText('DOCUMENT.PDF')).toBeInTheDocument();
  });

  it('ignores a duplicate filename on second drop', () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    act(() => { simulateDrop(dropZone, [makeFile('file.pdf')]); });
    act(() => { simulateDrop(dropZone, [makeFile('file.pdf')]); });
    expect(screen.getAllByText('file.pdf')).toHaveLength(1);
  });

  it('removes a file from the queue when the X button is clicked', () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    act(() => { simulateDrop(dropZone, [makeFile('removeme.pdf'), makeFile('keepme.txt', 'text/plain')]); });
    const removeButtons = screen.getAllByLabelText('Odebrat');
    fireEvent.click(removeButtons[0]);
    expect(screen.queryByText('removeme.pdf')).not.toBeInTheDocument();
    expect(screen.getByText('keepme.txt')).toBeInTheDocument();
  });

  it('uploads with correct documentType passed to mutation', async () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    act(() => { simulateDrop(dropZone, [makeFile('notes.txt', 'text/plain')]); });

    const uploadButton = screen.getByRole('button', { name: /Nahrát vše/i });
    await act(async () => { fireEvent.click(uploadButton); });

    await waitFor(() => {
      expect(mockMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          documentType: 'Conversation',
        })
      );
    });
  });

  it('defaults .pdf to KnowledgeBase document type', async () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    act(() => { simulateDrop(dropZone, [makeFile('manual.pdf', 'application/pdf')]); });

    const uploadButton = screen.getByRole('button', { name: /Nahrát vše/i });
    await act(async () => { fireEvent.click(uploadButton); });

    await waitFor(() => {
      expect(mockMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          documentType: 'KnowledgeBase',
        })
      );
    });
  });

  it('shows document type selector per file', () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    act(() => { simulateDrop(dropZone, [makeFile('notes.txt', 'text/plain'), makeFile('doc.pdf')]); });
    const selects = screen.getAllByRole('combobox');
    expect(selects).toHaveLength(2);
  });

  it('allows overriding document type before upload', async () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    act(() => { simulateDrop(dropZone, [makeFile('manual.pdf', 'application/pdf')]); });

    // Default is KnowledgeBase for pdf — override to Conversation
    const select = screen.getByRole('combobox');
    fireEvent.change(select, { target: { value: 'Conversation' } });

    const uploadButton = screen.getByRole('button', { name: /Nahrát vše/i });
    await act(async () => { fireEvent.click(uploadButton); });

    await waitFor(() => {
      expect(mockMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ documentType: 'Conversation' })
      );
    });
  });

  it('uploads all files successfully and removes done files', async () => {
    mockMutateAsync.mockResolvedValue({ success: true });
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    act(() => { simulateDrop(dropZone, [makeFile('file1.pdf'), makeFile('file2.txt', 'text/plain')]); });
    const uploadButton = screen.getByRole('button', { name: /Nahrát vše/i });
    await act(async () => { fireEvent.click(uploadButton); });
    await waitFor(() => {
      expect(screen.queryByText('file1.pdf')).not.toBeInTheDocument();
      expect(screen.queryByText('file2.txt')).not.toBeInTheDocument();
    });
  });

  it('shows error status for failed file', async () => {
    mockMutateAsync
      .mockResolvedValueOnce({ success: true })
      .mockRejectedValueOnce(new Error('Upload failed'));
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    act(() => { simulateDrop(dropZone, [makeFile('success.pdf'), makeFile('failure.txt', 'text/plain')]); });
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: /Nahrát vše/i })); });
    await waitFor(() => { expect(screen.getByText('❌ Chyba')).toBeInTheDocument(); });
    expect(screen.getByText('failure.txt')).toBeInTheDocument();
  });

  it('clears the queue when "Zrušit vše" is clicked', () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    act(() => { simulateDrop(dropZone, [makeFile('file1.pdf')]); });
    fireEvent.click(screen.getByRole('button', { name: /Zrušit vše/i }));
    expect(screen.queryByText('file1.pdf')).not.toBeInTheDocument();
  });

  it('renders without any props', () => {
    expect(() => { render(<KnowledgeBaseUploadTab />); }).not.toThrow();
    expect(screen.getByText('Přetáhněte soubory sem')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd frontend && npm test -- --testPathPattern="KnowledgeBaseUploadTab" --watchAll=false
```

Expected: existing tests fail because mutation receives a plain `File`, not `{ file, documentType }`.

- [ ] **Step 3: Update useUploadKnowledgeBaseDocumentMutation in useKnowledgeBase.ts**

In `frontend/src/api/hooks/useKnowledgeBase.ts`, add the `DocumentType` type and update the mutation:

Add near the top of the types section (after `DeleteDocumentResponse`):

```typescript
export type DocumentType = 'KnowledgeBase' | 'Conversation';
```

Replace the `useUploadKnowledgeBaseDocumentMutation` function:

```typescript
export const useUploadKnowledgeBaseDocumentMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      file,
      documentType,
    }: {
      file: File;
      documentType: DocumentType;
    }): Promise<UploadDocumentResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/knowledgebase/documents/upload`;

      const formData = new FormData();
      formData.append('file', file);
      formData.append('documentType', documentType);

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        body: formData,
      });

      if (!response.ok) {
        throw new Error(`Upload failed: ${response.status}`);
      }

      return response.json();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: knowledgeBaseKeys.all });
    },
  });
};
```

- [ ] **Step 4: Update KnowledgeBaseUploadTab**

Replace the contents of `frontend/src/components/knowledge-base/KnowledgeBaseUploadTab.tsx`:

```tsx
import React, { useCallback, useRef, useState } from 'react';
import { Upload, X, FileText } from 'lucide-react';
import {
  DocumentType,
  useUploadKnowledgeBaseDocumentMutation,
} from '../../api/hooks/useKnowledgeBase';

const ACCEPTED_EXTENSIONS = ['.pdf', '.docx', '.txt', '.md'];
const ACCEPTED_ATTR = ACCEPTED_EXTENSIONS.join(',');

const isAcceptedFile = (file: File): boolean => {
  const lower = file.name.toLowerCase();
  return ACCEPTED_EXTENSIONS.some(ext => lower.endsWith(ext));
};

const defaultDocumentType = (file: File): DocumentType => {
  const lower = file.name.toLowerCase();
  if (lower.endsWith('.txt') || lower.endsWith('.md')) return 'Conversation';
  return 'KnowledgeBase';
};

type FileStatus = 'waiting' | 'uploading' | 'done' | 'error';

const KnowledgeBaseUploadTab: React.FC = () => {
  const [dragOver, setDragOver] = useState(false);
  const [queuedFiles, setQueuedFiles] = useState<File[]>([]);
  const [fileStatuses, setFileStatuses] = useState<Record<string, FileStatus>>({});
  const [fileDocumentTypes, setFileDocumentTypes] = useState<Record<string, DocumentType>>({});
  const [isUploading, setIsUploading] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const upload = useUploadKnowledgeBaseDocumentMutation();

  const addFiles = useCallback((incoming: FileList | null) => {
    if (!incoming) return;
    const accepted = Array.from(incoming).filter(isAcceptedFile);
    setQueuedFiles(prev => {
      const existingNames = new Set(prev.map(f => f.name));
      const newFiles = accepted.filter(f => !existingNames.has(f.name));
      return [...prev, ...newFiles];
    });
    setFileDocumentTypes(prev => {
      const next = { ...prev };
      for (const file of accepted) {
        if (!next[file.name]) {
          next[file.name] = defaultDocumentType(file);
        }
      }
      return next;
    });
  }, []);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setDragOver(false);
    addFiles(e.dataTransfer.files);
  }, [addFiles]);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    addFiles(e.target.files);
    e.target.value = '';
  };

  const handleRemoveFile = (fileName: string) => {
    setQueuedFiles(prev => prev.filter(f => f.name !== fileName));
    setFileStatuses(prev => {
      const next = { ...prev };
      delete next[fileName];
      return next;
    });
    setFileDocumentTypes(prev => {
      const next = { ...prev };
      delete next[fileName];
      return next;
    });
  };

  const handleDocumentTypeChange = (fileName: string, value: DocumentType) => {
    setFileDocumentTypes(prev => ({ ...prev, [fileName]: value }));
  };

  const handleUpload = async () => {
    setIsUploading(true);
    const filesToProcess = queuedFiles.filter(f => fileStatuses[f.name] !== 'done');
    const outcomes: Record<string, 'done' | 'error'> = {};

    for (const file of filesToProcess) {
      setFileStatuses(prev => ({ ...prev, [file.name]: 'uploading' }));
      try {
        await upload.mutateAsync({
          file,
          documentType: fileDocumentTypes[file.name] ?? 'KnowledgeBase',
        });
        outcomes[file.name] = 'done';
        setFileStatuses(prev => ({ ...prev, [file.name]: 'done' }));
      } catch {
        outcomes[file.name] = 'error';
        setFileStatuses(prev => ({ ...prev, [file.name]: 'error' }));
      }
    }

    setIsUploading(false);
    setQueuedFiles(prev => prev.filter(f => outcomes[f.name] !== 'done'));
  };

  const handleCancelAll = () => {
    setQueuedFiles([]);
    setFileStatuses({});
    setFileDocumentTypes({});
  };

  const pendingCount = queuedFiles.filter(f => fileStatuses[f.name] !== 'done').length;

  const statusLabel = (status: FileStatus | undefined): React.ReactNode => {
    switch (status) {
      case 'uploading':
        return <span className="text-xs text-blue-600">Nahrávám…</span>;
      case 'done':
        return <span className="text-xs text-green-600">✅ Hotovo</span>;
      case 'error':
        return <span className="text-xs text-red-600">❌ Chyba</span>;
      default:
        return <span className="text-xs text-gray-400">Čeká</span>;
    }
  };

  return (
    <div className="space-y-4 max-w-lg">
      <div
        onDragOver={(e) => { e.preventDefault(); setDragOver(true); }}
        onDragLeave={() => setDragOver(false)}
        onDrop={handleDrop}
        onClick={() => fileInputRef.current?.click()}
        className={`border-2 border-dashed rounded-xl p-12 text-center cursor-pointer transition-colors ${
          dragOver
            ? 'border-blue-400 bg-blue-50'
            : 'border-gray-300 hover:border-gray-400 bg-gray-50'
        }`}
      >
        <Upload className="w-10 h-10 text-gray-400 mx-auto mb-3" />
        <p className="text-sm font-medium text-gray-700">Přetáhněte soubory sem</p>
        <p className="text-xs text-gray-500 mt-1">nebo</p>
        <p className="text-sm text-blue-600 mt-1 font-medium">Vybrat soubory</p>
        <p className="text-xs text-gray-400 mt-3">Podporované formáty: PDF, DOCX, TXT, MD</p>
        <input
          ref={fileInputRef}
          type="file"
          accept={ACCEPTED_ATTR}
          multiple
          className="hidden"
          onChange={handleFileChange}
        />
      </div>

      {queuedFiles.length > 0 && (
        <div className="border border-gray-200 rounded-xl divide-y divide-gray-100">
          {queuedFiles.map(file => (
            <div key={file.name} className="flex items-center justify-between px-4 py-3 gap-2">
              <div className="flex items-center gap-2 min-w-0">
                <FileText className="w-4 h-4 text-gray-400 shrink-0" />
                <span className="text-sm text-gray-700 truncate">{file.name}</span>
              </div>
              <div className="flex items-center gap-2 shrink-0 ml-2">
                <select
                  value={fileDocumentTypes[file.name] ?? 'KnowledgeBase'}
                  onChange={(e) => handleDocumentTypeChange(file.name, e.target.value as DocumentType)}
                  disabled={isUploading}
                  className="text-xs border border-gray-200 rounded px-1 py-0.5 bg-white disabled:opacity-50"
                >
                  <option value="KnowledgeBase">Znalostní báze</option>
                  <option value="Conversation">Konverzace</option>
                </select>
                {statusLabel(fileStatuses[file.name])}
                <button
                  onClick={() => handleRemoveFile(file.name)}
                  disabled={isUploading}
                  className="text-gray-400 hover:text-gray-600 disabled:opacity-50"
                  aria-label="Odebrat"
                >
                  <X className="w-4 h-4" />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      {queuedFiles.length > 0 && (
        <div className="flex gap-2">
          <button
            onClick={handleUpload}
            disabled={pendingCount === 0 || isUploading}
            className="flex items-center gap-1 px-4 py-2 bg-blue-600 text-white text-sm rounded-lg hover:bg-blue-700 disabled:opacity-50"
          >
            <Upload className="w-4 h-4" />
            Nahrát vše ({pendingCount})
          </button>
          <button
            onClick={handleCancelAll}
            disabled={isUploading}
            className="px-4 py-2 border border-gray-300 text-sm rounded-lg hover:bg-gray-50 disabled:opacity-50"
          >
            Zrušit vše
          </button>
        </div>
      )}
    </div>
  );
};

export default KnowledgeBaseUploadTab;
```

- [ ] **Step 5: Run frontend tests**

```bash
cd frontend && npm test -- --testPathPattern="KnowledgeBaseUploadTab" --watchAll=false
```

Expected: all tests pass.

- [ ] **Step 6: Run full frontend test suite**

```bash
cd frontend && npm test -- --watchAll=false
```

Expected: all tests pass.

- [ ] **Step 7: Run lint**

```bash
cd frontend && npm run lint
```

Expected: no errors.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/api/hooks/useKnowledgeBase.ts \
        frontend/src/components/knowledge-base/KnowledgeBaseUploadTab.tsx \
        frontend/src/components/knowledge-base/__tests__/KnowledgeBaseUploadTab.test.tsx
git commit -m "feat(knowledge-base): add document type selector to upload tab and pass to API"
```
