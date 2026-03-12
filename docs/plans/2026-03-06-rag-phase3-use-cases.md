# RAG Knowledge Base – Phase 3: Use Cases (TDD)

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement the three MediatR use-case handlers for the knowledge base: semantic search, Q&A with Claude, and document indexing. All handlers are tested first (red → green).

**Architecture:** Three vertical slices under `Application/Features/KnowledgeBase/UseCases/`. Each handler depends only on interfaces (`IEmbeddingService`, `IKnowledgeBaseRepository`, `IClaudeService`, `IDocumentTextExtractor`, `DocumentChunker`) — all already implemented in Phase 2. No new infrastructure needed.

**Master plan reference:** `docs/plans/2026-03-02-rag-knowledge-base.md`, Tasks 12–14

---

## Task 1: SearchDocuments use case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/SearchDocumentsHandlerTests.cs`

**Step 1: Write failing test**

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class SearchDocumentsHandlerTests
{
    private readonly Mock<IEmbeddingService> _embeddingService = new();
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();

    [Fact]
    public async Task Handle_ReturnsChunksOrderedByScore()
    {
        var queryEmbedding = new float[] { 0.1f, 0.2f, 0.3f };
        _embeddingService
            .Setup(s => s.GenerateEmbeddingAsync("phenoxyethanol concentration", default))
            .ReturnsAsync(queryEmbedding);

        var chunk = new KnowledgeBaseChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            ChunkIndex = 0,
            Content = "Phenoxyethanol max 1.0% in Annex V",
            Embedding = queryEmbedding,
            Document = new KnowledgeBaseDocument { Filename = "EU_reg.pdf", SourcePath = "/inbox/EU_reg.pdf" }
        };

        _repository
            .Setup(r => r.SearchSimilarAsync(queryEmbedding, 5, default))
            .ReturnsAsync([(chunk, 0.95)]);

        var handler = new SearchDocumentsHandler(_embeddingService.Object, _repository.Object);
        var result = await handler.Handle(
            new SearchDocumentsRequest { Query = "phenoxyethanol concentration", TopK = 5 },
            default);

        Assert.Single(result.Chunks);
        Assert.Equal("Phenoxyethanol max 1.0% in Annex V", result.Chunks[0].Content);
        Assert.Equal(0.95, result.Chunks[0].Score);
        Assert.Equal("EU_reg.pdf", result.Chunks[0].SourceFilename);
    }
}
```

**Step 2: Run to verify it fails**

```bash
cd backend && dotnet test --filter "SearchDocumentsHandlerTests" -v
```

Expected: FAIL — types don't exist yet.

**Step 3: Create request/response DTOs**

```csharp
// SearchDocumentsRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;

public class SearchDocumentsRequest : IRequest<SearchDocumentsResponse>
{
    public string Query { get; set; } = string.Empty;
    public int TopK { get; set; } = 5;
}

public class SearchDocumentsResponse
{
    public List<ChunkResult> Chunks { get; set; } = [];
}

public class ChunkResult
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
    public string SourceFilename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
}
```

**Step 4: Create handler**

```csharp
// SearchDocumentsHandler.cs
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;

public class SearchDocumentsHandler : IRequestHandler<SearchDocumentsRequest, SearchDocumentsResponse>
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IKnowledgeBaseRepository _repository;

    public SearchDocumentsHandler(IEmbeddingService embeddingService, IKnowledgeBaseRepository repository)
    {
        _embeddingService = embeddingService;
        _repository = repository;
    }

    public async Task<SearchDocumentsResponse> Handle(
        SearchDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(request.Query, cancellationToken);
        var results = await _repository.SearchSimilarAsync(queryEmbedding, request.TopK, cancellationToken);

        return new SearchDocumentsResponse
        {
            Chunks = results.Select(r => new ChunkResult
            {
                ChunkId = r.Chunk.Id,
                DocumentId = r.Chunk.DocumentId,
                Content = r.Chunk.Content,
                Score = r.Score,
                SourceFilename = r.Chunk.Document.Filename,
                SourcePath = r.Chunk.Document.SourcePath
            }).ToList()
        };
    }
}
```

**Step 5: Run tests to verify they pass**

```bash
cd backend && dotnet test --filter "SearchDocumentsHandlerTests" -v
```

Expected: 1 test PASSES.

**Step 6: Format and commit**

```bash
cd backend && dotnet format
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SearchDocuments/
git add backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/SearchDocumentsHandlerTests.cs
git commit -m "feat: add SearchDocuments use case with handler and tests"
```

---

## Task 2: AskQuestion use case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/AskQuestionHandlerTests.cs`

**Step 1: Write failing test**

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class AskQuestionHandlerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IClaudeService> _claude = new();

    [Fact]
    public async Task Handle_ReturnsAnswerWithSources()
    {
        var searchResponse = new SearchDocumentsResponse
        {
            Chunks =
            [
                new ChunkResult
                {
                    ChunkId = Guid.NewGuid(),
                    DocumentId = Guid.NewGuid(),
                    Content = "Max phenoxyethanol 1.0% per EU regulation",
                    Score = 0.95,
                    SourceFilename = "EU_reg.pdf",
                    SourcePath = "/archived/EU_reg.pdf"
                }
            ]
        };

        _mediator
            .Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), default))
            .ReturnsAsync(searchResponse);

        _claude
            .Setup(c => c.GenerateAnswerAsync(
                "Max phenoxyethanol?",
                It.IsAny<IEnumerable<string>>(),
                default))
            .ReturnsAsync("The maximum allowed concentration is 1.0%.");

        var handler = new AskQuestionHandler(_mediator.Object, _claude.Object);
        var result = await handler.Handle(
            new AskQuestionRequest { Question = "Max phenoxyethanol?", TopK = 5 },
            default);

        Assert.Equal("The maximum allowed concentration is 1.0%.", result.Answer);
        Assert.Single(result.Sources);
        Assert.Equal("EU_reg.pdf", result.Sources[0].Filename);
    }
}
```

**Step 2: Run to verify it fails**

```bash
cd backend && dotnet test --filter "AskQuestionHandlerTests" -v
```

Expected: FAIL.

**Step 3: Create request/response DTOs**

```csharp
// AskQuestionRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;

public class AskQuestionRequest : IRequest<AskQuestionResponse>
{
    public string Question { get; set; } = string.Empty;
    public int TopK { get; set; } = 5;
}

public class AskQuestionResponse
{
    public string Answer { get; set; } = string.Empty;
    public List<SourceReference> Sources { get; set; } = [];
}

public class SourceReference
{
    public Guid DocumentId { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public double Score { get; set; }
}
```

**Step 4: Create handler**

```csharp
// AskQuestionHandler.cs
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;

public class AskQuestionHandler : IRequestHandler<AskQuestionRequest, AskQuestionResponse>
{
    private readonly IMediator _mediator;
    private readonly IClaudeService _claude;

    public AskQuestionHandler(IMediator mediator, IClaudeService claude)
    {
        _mediator = mediator;
        _claude = claude;
    }

    public async Task<AskQuestionResponse> Handle(
        AskQuestionRequest request,
        CancellationToken cancellationToken)
    {
        var searchResult = await _mediator.Send(
            new SearchDocumentsRequest { Query = request.Question, TopK = request.TopK },
            cancellationToken);

        var contextChunks = searchResult.Chunks.Select(c => c.Content);

        var answer = await _claude.GenerateAnswerAsync(
            request.Question,
            contextChunks,
            cancellationToken);

        return new AskQuestionResponse
        {
            Answer = answer,
            Sources = searchResult.Chunks.Select(c => new SourceReference
            {
                DocumentId = c.DocumentId,
                Filename = c.SourceFilename,
                Excerpt = c.Content[..Math.Min(200, c.Content.Length)],
                Score = c.Score
            }).ToList()
        };
    }
}
```

**Step 5: Run tests to verify they pass**

```bash
cd backend && dotnet test --filter "AskQuestionHandlerTests" -v
```

Expected: 1 test PASSES.

**Step 6: Format and commit**

```bash
cd backend && dotnet format
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/
git add backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/AskQuestionHandlerTests.cs
git commit -m "feat: add AskQuestion use case with handler and tests"
```

---

## Task 3: IndexDocument use case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/IndexDocumentHandlerTests.cs`

**Step 1: Write failing test**

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class IndexDocumentHandlerTests
{
    private readonly Mock<IDocumentTextExtractor> _extractor = new();
    private readonly Mock<IEmbeddingService> _embedding = new();
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();
    private readonly DocumentChunker _chunker;

    public IndexDocumentHandlerTests()
    {
        _chunker = new DocumentChunker(Options.Create(new KnowledgeBaseOptions
        {
            ChunkSize = 5,
            ChunkOverlapTokens = 1
        }));
    }

    [Fact]
    public async Task Handle_StoresDocumentAndChunksWithEmbeddings()
    {
        var pdfBytes = new byte[] { 1, 2, 3 };
        var extractedText = "word1 word2 word3 word4 word5 word6 word7 word8 word9 word10";

        _extractor.Setup(e => e.CanHandle("application/pdf")).Returns(true);
        _extractor.Setup(e => e.ExtractTextAsync(pdfBytes, default)).ReturnsAsync(extractedText);
        _embedding.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), default))
            .ReturnsAsync(new float[] { 0.1f, 0.2f });

        KnowledgeBaseDocument? savedDoc = null;
        _repository.Setup(r => r.AddDocumentAsync(It.IsAny<KnowledgeBaseDocument>(), default))
            .Callback<KnowledgeBaseDocument, CancellationToken>((doc, _) => savedDoc = doc);

        var handler = new IndexDocumentHandler(_extractor.Object, _embedding.Object, _chunker, _repository.Object);

        await handler.Handle(new IndexDocumentRequest
        {
            Filename = "test.pdf",
            SourcePath = "/inbox/test.pdf",
            ContentType = "application/pdf",
            Content = pdfBytes,
            ContentHash = "abc123def456"
        }, default);

        Assert.NotNull(savedDoc);
        Assert.Equal("test.pdf", savedDoc!.Filename);
        Assert.Equal("abc123def456", savedDoc.ContentHash);
        _repository.Verify(r => r.AddChunksAsync(
            It.Is<IEnumerable<KnowledgeBaseChunk>>(chunks => chunks.Any()),
            default), Times.Once);
        _repository.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_ThrowsForUnsupportedContentType()
    {
        _extractor.Setup(e => e.CanHandle("image/png")).Returns(false);

        var handler = new IndexDocumentHandler(_extractor.Object, _embedding.Object, _chunker, _repository.Object);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            handler.Handle(new IndexDocumentRequest
            {
                Filename = "photo.png",
                SourcePath = "/inbox/photo.png",
                ContentType = "image/png",
                Content = [1, 2, 3],
                ContentHash = "abc"
            }, default));
    }
}
```

**Step 2: Run to verify they fail**

```bash
cd backend && dotnet test --filter "IndexDocumentHandlerTests" -v
```

Expected: FAIL — types don't exist yet.

**Step 3: Create request DTO**

```csharp
// IndexDocumentRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;

public class IndexDocumentRequest : IRequest
{
    public string Filename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    /// <summary>SHA-256 hex digest of Content bytes. Computed by the caller (ingestion job) before sending.</summary>
    public string ContentHash { get; set; } = string.Empty;
}
```

**Step 4: Create handler**

```csharp
// IndexDocumentHandler.cs
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;

public class IndexDocumentHandler : IRequestHandler<IndexDocumentRequest>
{
    private readonly IDocumentTextExtractor _extractor;
    private readonly IEmbeddingService _embeddingService;
    private readonly DocumentChunker _chunker;
    private readonly IKnowledgeBaseRepository _repository;
    private readonly ILogger<IndexDocumentHandler> _logger;

    public IndexDocumentHandler(
        IDocumentTextExtractor extractor,
        IEmbeddingService embeddingService,
        DocumentChunker chunker,
        IKnowledgeBaseRepository repository,
        ILogger<IndexDocumentHandler> logger = null!)
    {
        _extractor = extractor;
        _embeddingService = embeddingService;
        _chunker = chunker;
        _repository = repository;
        _logger = logger;
    }

    public async Task Handle(IndexDocumentRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Indexing document {Filename} from {SourcePath}", request.Filename, request.SourcePath);

        if (!_extractor.CanHandle(request.ContentType))
        {
            throw new NotSupportedException($"Content type '{request.ContentType}' is not supported.");
        }

        var document = new KnowledgeBaseDocument
        {
            Id = Guid.NewGuid(),
            Filename = request.Filename,
            SourcePath = request.SourcePath,
            ContentType = request.ContentType,
            ContentHash = request.ContentHash,
            Status = DocumentStatus.Processing,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddDocumentAsync(document, cancellationToken);

        var text = await _extractor.ExtractTextAsync(request.Content, cancellationToken);
        var chunkTexts = _chunker.Chunk(text);

        var chunks = new List<KnowledgeBaseChunk>();

        for (int i = 0; i < chunkTexts.Count; i++)
        {
            var embedding = await _embeddingService.GenerateEmbeddingAsync(chunkTexts[i], cancellationToken);
            chunks.Add(new KnowledgeBaseChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                ChunkIndex = i,
                Content = chunkTexts[i],
                Embedding = embedding
            });
        }

        await _repository.AddChunksAsync(chunks, cancellationToken);

        document.Status = DocumentStatus.Indexed;
        document.IndexedAt = DateTime.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Indexed {ChunkCount} chunks for {Filename}", chunks.Count, request.Filename);
    }
}
```

**Step 5: Run tests to verify they pass**

```bash
cd backend && dotnet test --filter "IndexDocumentHandlerTests" -v
```

Expected: 2 tests PASS.

**Step 6: Run full suite to confirm nothing broken**

```bash
cd backend && dotnet test -v
```

Expected: all existing tests pass + 3 new handler tests pass.

**Step 7: Format and commit**

```bash
cd backend && dotnet format
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/
git add backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/IndexDocumentHandlerTests.cs
git commit -m "feat: add IndexDocument use case with handler and tests"
```

---

## Phase 3 complete

All three use-case handlers are implemented and tested. Next: Phase 4 — Repository implementation + DI registration + Ingestion job.
See `docs/plans/2026-03-02-rag-knowledge-base.md`, Tasks 15–18.
