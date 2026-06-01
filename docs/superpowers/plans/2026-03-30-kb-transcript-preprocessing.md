# KB Transcript Preprocessing & Keyword-Summarized Embeddings — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Strip boilerplate from chat transcripts and embed an LLM-extracted keyword summary instead of raw text, while storing full clean text in the chunk for answer generation.

**Architecture:** Two new services injected into `DocumentIndexingService`: `ChatTranscriptPreprocessor` (regex-based strip) runs before chunking; `ChunkSummarizer` (LLM keyword extraction) runs per-chunk before embedding. Embedding is generated from the summary; `chunk.Content` holds the full clean text.

**Tech Stack:** C# / .NET 8, xUnit, Moq, `Microsoft.Extensions.AI` (`IChatClient`, `IEmbeddingGenerator`), `Microsoft.Extensions.Options`

---

## File Map

| Action | Path |
|--------|------|
| Modify | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs` |
| Create | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ChatTranscriptPreprocessor.cs` |
| Create | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IChunkSummarizer.cs` |
| Create | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ChunkSummarizer.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentIndexingService.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` |
| Create | `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ChatTranscriptPreprocessorTests.cs` |
| Create | `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ChunkSummarizerTests.cs` |
| Modify | `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentIndexingServiceTests.cs` |

---

## Task 1: Extend KnowledgeBaseOptions + implement ChatTranscriptPreprocessor

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ChatTranscriptPreprocessor.cs`
- Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ChatTranscriptPreprocessorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ChatTranscriptPreprocessorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class ChatTranscriptPreprocessorTests
{
    private static ChatTranscriptPreprocessor Create(KnowledgeBaseOptions? options = null)
        => new(Options.Create(options ?? new KnowledgeBaseOptions()));

    [Fact]
    public void Clean_RemovesGreeting()
    {
        var preprocessor = Create();
        var input =
            "Anela: Vítejte ve světě Anela 🌿🌿 Rádi Vám poradíme s péčí o pleť i s potížemi, které Vás trápí. Napište nám, jsme tu pro Vás!\n" +
            "Zákazník: mám problém s akné";

        var result = preprocessor.Clean(input);

        Assert.DoesNotContain("Vítejte ve světě Anela", result);
        Assert.Contains("mám problém s akné", result);
    }

    [Fact]
    public void Clean_RemovesMetadataHeader()
    {
        var preprocessor = Create();
        var input = "datum: 04.11.2025 zákazník: Zákazník-0364\nAnela: Jak Vám mohu pomoci?";

        var result = preprocessor.Clean(input);

        Assert.DoesNotContain("datum:", result);
        Assert.Contains("Jak Vám mohu pomoci?", result);
    }

    [Fact]
    public void Clean_RemovesAnonymizedCustomerId()
    {
        var preprocessor = Create();
        var input = "Zákazník-0042: Mám suchou pleť";

        var result = preprocessor.Clean(input);

        Assert.DoesNotContain("Zákazník-0042", result);
        Assert.Contains("Mám suchou pleť", result);
    }

    [Fact]
    public void Clean_RemovesAllPatternsCombined()
    {
        var preprocessor = Create();
        var input =
            "datum: 04.11.2025 zákazník: Zákazník-0364\n" +
            "Anela: Vítejte ve světě Anela 🌿🌿 Rádi Vám poradíme s péčí o pleť i s potížemi, které Vás trápí. Napište nám, jsme tu pro Vás!\n" +
            "Zákazník-0364: mám akné";

        var result = preprocessor.Clean(input);

        Assert.DoesNotContain("datum:", result);
        Assert.DoesNotContain("Vítejte ve světě Anela", result);
        Assert.DoesNotContain("Zákazník-0364", result);
        Assert.Contains("mám akné", result);
    }

    [Fact]
    public void Clean_NoMatchingPatterns_ReturnsTextUnchanged()
    {
        var preprocessor = Create();
        var input = "Anela: Bisabolol je vhodný pro citlivou pleť.";

        var result = preprocessor.Clean(input);

        Assert.Equal("Anela: Bisabolol je vhodný pro citlivou pleť.", result);
    }

    [Fact]
    public void Clean_CustomPattern_IsApplied()
    {
        var options = new KnowledgeBaseOptions
        {
            PreprocessorPatterns = [@"REMOVE_ME"]
        };
        var preprocessor = Create(options);
        var input = "Some text REMOVE_ME more text";

        var result = preprocessor.Clean(input);

        Assert.DoesNotContain("REMOVE_ME", result);
        Assert.Contains("Some text", result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ChatTranscriptPreprocessorTests" -v minimal
```

Expected: compile error — `ChatTranscriptPreprocessor` does not exist, `KnowledgeBaseOptions.PreprocessorPatterns` does not exist.

- [ ] **Step 3: Extend KnowledgeBaseOptions**

Replace the contents of `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs`:

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
    /// When true, each chunk is summarized by the LLM before embedding.
    /// Set to false to skip LLM calls (e.g. in tests or cost-free re-index runs).
    /// </summary>
    public bool SummarizationEnabled { get; set; } = true;

    /// <summary>
    /// System prompt prepended to each chunk when requesting a keyword summary.
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
}
```

- [ ] **Step 4: Implement ChatTranscriptPreprocessor**

Create `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ChatTranscriptPreprocessor.cs`:

```csharp
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class ChatTranscriptPreprocessor
{
    private readonly IReadOnlyList<Regex> _patterns;

    public ChatTranscriptPreprocessor(IOptions<KnowledgeBaseOptions> options)
    {
        _patterns = options.Value.PreprocessorPatterns
            .Select(p => new Regex(
                p,
                RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.Compiled))
            .ToList();
    }

    public string Clean(string rawText)
    {
        var text = rawText;

        foreach (var pattern in _patterns)
        {
            text = pattern.Replace(text, string.Empty);
        }

        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.Trim();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ChatTranscriptPreprocessorTests" -v minimal
```

Expected: 6 tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ChatTranscriptPreprocessor.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ChatTranscriptPreprocessorTests.cs
git commit -m "feat(knowledge-base): add ChatTranscriptPreprocessor for boilerplate stripping"
```

---

## Task 2: Implement IChunkSummarizer / ChunkSummarizer

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IChunkSummarizer.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ChunkSummarizer.cs`
- Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ChunkSummarizerTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ChunkSummarizerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class ChunkSummarizerTests
{
    private readonly Mock<IChatClient> _chatClient = new();

    private ChunkSummarizer Create(KnowledgeBaseOptions? options = null)
        => new(_chatClient.Object, Options.Create(options ?? new KnowledgeBaseOptions()));

    [Fact]
    public async Task SummarizeAsync_CallsChatClient_WithPromptContainingChunkText()
    {
        const string chunkText = "Zákazník: Mám problém s akné na čele";
        const string expectedSummary = "Problém zákazníka: akné";

        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.Is<IEnumerable<ChatMessage>>(m =>
                    m.First().Text!.Contains(chunkText)),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                [new ChatMessage(ChatRole.Assistant, expectedSummary)]));

        var summarizer = Create();
        var result = await summarizer.SummarizeAsync(chunkText);

        Assert.Equal(expectedSummary, result);
    }

    [Fact]
    public async Task SummarizeAsync_WhenDisabled_ReturnsChunkTextWithoutCallingLlm()
    {
        var options = new KnowledgeBaseOptions { SummarizationEnabled = false };
        var summarizer = Create(options);
        const string chunkText = "Some chunk content";

        var result = await summarizer.SummarizeAsync(chunkText);

        Assert.Equal(chunkText, result);
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
  --filter "FullyQualifiedName~ChunkSummarizerTests" -v minimal
```

Expected: compile error — `IChunkSummarizer` and `ChunkSummarizer` do not exist.

- [ ] **Step 3: Create the interface**

Create `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IChunkSummarizer.cs`:

```csharp
namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public interface IChunkSummarizer
{
    Task<string> SummarizeAsync(string chunkText, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement ChunkSummarizer**

Create `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ChunkSummarizer.cs`:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class ChunkSummarizer : IChunkSummarizer
{
    private readonly IChatClient _chatClient;
    private readonly KnowledgeBaseOptions _options;

    public ChunkSummarizer(IChatClient chatClient, IOptions<KnowledgeBaseOptions> options)
    {
        _chatClient = chatClient;
        _options = options.Value;
    }

    public async Task<string> SummarizeAsync(string chunkText, CancellationToken ct = default)
    {
        if (!_options.SummarizationEnabled)
            return chunkText;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, _options.SummarizationPrompt + "\n" + chunkText)
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        return response.Text ?? chunkText;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ChunkSummarizerTests" -v minimal
```

Expected: 2 tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IChunkSummarizer.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ChunkSummarizer.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ChunkSummarizerTests.cs
git commit -m "feat(knowledge-base): add ChunkSummarizer for keyword extraction before embedding"
```

---

## Task 3: Wire into DocumentIndexingService + KnowledgeBaseModule

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
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class DocumentIndexingServiceTests
{
    private readonly Mock<IDocumentTextExtractor> _pdfExtractor;
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _embeddingGenerator;
    private readonly Mock<IKnowledgeBaseRepository> _repository;
    private readonly Mock<IChunkSummarizer> _summarizer;
    private readonly GeneratedEmbeddings<Embedding<float>> _generatedEmbeddings;
    private readonly DocumentIndexingService _service;

    public DocumentIndexingServiceTests()
    {
        _pdfExtractor = new Mock<IDocumentTextExtractor>();
        _pdfExtractor.Setup(e => e.CanHandle("application/pdf")).Returns(true);
        _pdfExtractor.Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("word1 word2 word3");

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

        _repository = new Mock<IKnowledgeBaseRepository>();

        _summarizer = new Mock<IChunkSummarizer>();
        _summarizer
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) => text);

        var options = Options.Create(new KnowledgeBaseOptions { ChunkSize = 512, ChunkOverlapTokens = 50 });
        var chunker = new DocumentChunker(options);
        var preprocessor = new ChatTranscriptPreprocessor(options);

        _service = new DocumentIndexingService(
            new[] { _pdfExtractor.Object },
            _embeddingGenerator.Object,
            chunker,
            _repository.Object,
            preprocessor,
            _summarizer.Object);
    }

    [Fact]
    public async Task IndexChunksAsync_CallsExtractorAndEmbedder_AndAddsChunks()
    {
        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid() };
        var content = new byte[] { 0x25, 0x50, 0x44, 0x46 };

        await _service.IndexChunksAsync(content, "application/pdf", doc, CancellationToken.None);

        _pdfExtractor.Verify(e => e.ExtractTextAsync(content, It.IsAny<CancellationToken>()), Times.Once);
        _embeddingGenerator.Verify(
            e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
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
    public async Task IndexChunksAsync_EmbeddingIsGeneratedFromSummary()
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

        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid() };
        await _service.IndexChunksAsync([], "application/pdf", doc, CancellationToken.None);

        Assert.Equal(summary, capturedEmbeddingInput);
    }

    [Fact]
    public async Task IndexChunksAsync_ChunkContentIsFullCleanText_NotSummary()
    {
        const string extractedText = "word1 word2 word3";
        const string summary = "Problém zákazníka: suchá pleť";

        _pdfExtractor
            .Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractedText);
        _summarizer
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        IEnumerable<KnowledgeBaseChunk>? savedChunks = null;
        _repository
            .Setup(r => r.AddChunksAsync(
                It.IsAny<IEnumerable<KnowledgeBaseChunk>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<KnowledgeBaseChunk>, CancellationToken>(
                (chunks, _) => savedChunks = chunks.ToList());

        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid() };
        await _service.IndexChunksAsync([], "application/pdf", doc, CancellationToken.None);

        Assert.NotNull(savedChunks);
        Assert.All(savedChunks!, chunk =>
        {
            Assert.Equal(extractedText, chunk.Content);
            Assert.DoesNotContain(summary, chunk.Content);
        });
    }

    [Fact]
    public async Task IndexChunksAsync_StripsBoilerplateBeforeChunking()
    {
        const string boilerplateText =
            "datum: 04.11.2025 zákazník: Zákazník-0001\nAnela: bisabolol je vhodný pro citlivou pleť";

        _pdfExtractor
            .Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(boilerplateText);

        IEnumerable<KnowledgeBaseChunk>? savedChunks = null;
        _repository
            .Setup(r => r.AddChunksAsync(
                It.IsAny<IEnumerable<KnowledgeBaseChunk>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<KnowledgeBaseChunk>, CancellationToken>(
                (chunks, _) => savedChunks = chunks.ToList());

        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid() };
        await _service.IndexChunksAsync([], "application/pdf", doc, CancellationToken.None);

        Assert.NotNull(savedChunks);
        Assert.All(savedChunks!, chunk => Assert.DoesNotContain("datum:", chunk.Content));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DocumentIndexingServiceTests" -v minimal
```

Expected: compile error — `DocumentIndexingService` constructor does not accept `ChatTranscriptPreprocessor` and `IChunkSummarizer`.

- [ ] **Step 3: Update DocumentIndexingService**

Replace the contents of `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentIndexingService.cs`:

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.AI;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class DocumentIndexingService : IDocumentIndexingService
{
    private readonly IEnumerable<IDocumentTextExtractor> _extractors;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly DocumentChunker _chunker;
    private readonly IKnowledgeBaseRepository _repository;
    private readonly ChatTranscriptPreprocessor _preprocessor;
    private readonly IChunkSummarizer _summarizer;

    public DocumentIndexingService(
        IEnumerable<IDocumentTextExtractor> extractors,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        DocumentChunker chunker,
        IKnowledgeBaseRepository repository,
        ChatTranscriptPreprocessor preprocessor,
        IChunkSummarizer summarizer)
    {
        _extractors = extractors;
        _embeddingGenerator = embeddingGenerator;
        _chunker = chunker;
        _repository = repository;
        _preprocessor = preprocessor;
        _summarizer = summarizer;
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
        var chunkTexts = _chunker.Chunk(text);

        var chunks = new List<KnowledgeBaseChunk>();
        for (var i = 0; i < chunkTexts.Count; i++)
        {
            var summary = await _summarizer.SummarizeAsync(chunkTexts[i], ct);
            var embeddings = await _embeddingGenerator.GenerateAsync(
                [summary],
                cancellationToken: ct);
            chunks.Add(new KnowledgeBaseChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                ChunkIndex = i,
                Content = chunkTexts[i],
                Embedding = embeddings[0].Vector.ToArray(),
            });
        }

        await _repository.AddChunksAsync(chunks, ct);

        document.Status = DocumentStatus.Indexed;
        document.IndexedAt = DateTime.UtcNow;
    }
}
```

- [ ] **Step 4: Register new services in KnowledgeBaseModule**

In `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`, add two lines after `services.AddScoped<DocumentChunker>();`:

```csharp
services.AddScoped<ChatTranscriptPreprocessor>();
services.AddScoped<IChunkSummarizer, ChunkSummarizer>();
```

The full `AddKnowledgeBaseModule` method becomes:

```csharp
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
```

- [ ] **Step 5: Run all KB tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~KnowledgeBase" -v minimal
```

Expected: all KB tests pass (no failures).

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

If format violations are reported, run without `--verify-no-changes` to auto-fix, then re-run with it to confirm clean.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentIndexingService.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentIndexingServiceTests.cs
git commit -m "feat(knowledge-base): wire preprocessor and summarizer into DocumentIndexingService"
```
