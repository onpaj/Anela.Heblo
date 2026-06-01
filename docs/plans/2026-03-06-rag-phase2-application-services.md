# RAG Knowledge Base – Phase 2: Application Services

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix the ContentHash gap left by Phase 1, then implement all application-layer services needed by the ingestion pipeline and search (embedding, PDF extraction, chunking, Claude Q&A).

**Architecture:** Phase 1 was committed without `ContentHash` on `KnowledgeBaseDocument` — Task 1 patches this with a migration. Tasks 2–6 implement the application services in `Anela.Heblo.Application/Features/KnowledgeBase/Services/`. No use-case handlers yet (Phase 3).

**Tech Stack:**
- `OpenAI` v2.2.0 — `EmbeddingClient` for text-embedding-3-small
- `Anthropic` v1.0.0 — Claude API for Q&A generation (**note:** 1.0.0, not 0.11.0 as in the master plan)
- `PdfPig` v0.1.9 — PDF text extraction via `UglyToad.PdfPig`
- `System.Security.Cryptography.SHA256` (BCL, no extra package) — content hash

**Master plan reference:** `docs/plans/2026-03-02-rag-knowledge-base.md` (Tasks 3–4 fixup, Tasks 7–11)

---

## Task 1: Add ContentHash to domain, EF config, and migration

**Why:** `IKnowledgeBaseRepository` now has `GetDocumentByHashAsync` and `UpdateDocumentSourcePathAsync` (updated in master plan) but the domain entity and DB column are missing. This fixup bridges the gap before any application logic is written.

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseDocument.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/IKnowledgeBaseRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseDocumentConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddContentHashToKnowledgeBaseDocument.cs` (auto-generated)

**Step 1: Add ContentHash property to entity**

Open `KnowledgeBaseDocument.cs`. Add after `IndexedAt`:

```csharp
public string ContentHash { get; set; } = string.Empty; // SHA-256 hex, 64 chars
```

Full file after change:

```csharp
namespace Anela.Heblo.Domain.Features.KnowledgeBase;

public class KnowledgeBaseDocument
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty; // SHA-256 hex, 64 chars
    public string Status { get; set; } = DocumentStatus.Processing;
    public DateTime CreatedAt { get; set; }
    public DateTime? IndexedAt { get; set; }

    public ICollection<KnowledgeBaseChunk> Chunks { get; set; } = new List<KnowledgeBaseChunk>();
}

public static class DocumentStatus
{
    public const string Processing = "processing";
    public const string Indexed = "indexed";
    public const string Failed = "failed";
}
```

**Step 2: Update IKnowledgeBaseRepository**

Replace the entire interface in `IKnowledgeBaseRepository.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.KnowledgeBase;

public interface IKnowledgeBaseRepository
{
    Task AddDocumentAsync(KnowledgeBaseDocument document, CancellationToken ct = default);
    Task AddChunksAsync(IEnumerable<KnowledgeBaseChunk> chunks, CancellationToken ct = default);
    Task<List<KnowledgeBaseDocument>> GetAllDocumentsAsync(CancellationToken ct = default);
    Task<List<(KnowledgeBaseChunk Chunk, double Score)>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken ct = default);
    Task<KnowledgeBaseDocument?> GetDocumentByHashAsync(string contentHash, CancellationToken ct = default);
    Task UpdateDocumentSourcePathAsync(Guid documentId, string newSourcePath, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

**Step 3: Add ContentHash column config**

Open `KnowledgeBaseDocumentConfiguration.cs`. Add after the `IndexedAt` property block, before `HasMany`:

```csharp
builder.Property(e => e.ContentHash)
    .IsRequired()
    .HasMaxLength(64);

builder.HasIndex(e => e.ContentHash)
    .IsUnique();
```

**Step 4: Build to verify**

```bash
cd backend && dotnet build
```

Expected: build succeeds. The repository implementation doesn't exist yet so there are no errors from missing interface methods.

**Step 5: Create migration**

```bash
cd backend
dotnet ef migrations add AddContentHashToKnowledgeBaseDocument \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API
```

**Step 6: Review generated migration**

Open the generated file and verify:
- Adds `ContentHash` column: `character varying(64)`, not nullable
- Adds a unique index on `ContentHash`

If EF generates it as nullable (it shouldn't with `IsRequired()`), change `nullable: true` to `nullable: false` manually.

The column will default to `''` (empty string) for any existing rows — that is fine since no real documents exist yet.

**Step 7: Format and commit**

```bash
cd backend && dotnet format
git add backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseDocument.cs
git add backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/IKnowledgeBaseRepository.cs
git add backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseDocumentConfiguration.cs
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat: add ContentHash to KnowledgeBaseDocument for content-based deduplication"
```

---

## Task 2: KnowledgeBaseOptions

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

**Step 1: Create options class**

```csharp
namespace Anela.Heblo.Application.Features.KnowledgeBase;

public class KnowledgeBaseOptions
{
    public string OneDriveInboxPath { get; set; } = "/KnowledgeBase/Inbox";
    public string OneDriveArchivedPath { get; set; } = "/KnowledgeBase/Archived";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public int EmbeddingDimensions { get; set; } = 1536;
    public int ChunkSize { get; set; } = 512;
    public int ChunkOverlapTokens { get; set; } = 50;
    public int MaxRetrievedChunks { get; set; } = 5;
    public string ClaudeModel { get; set; } = "claude-sonnet-4-6";
    public int ClaudeMaxTokens { get; set; } = 1024;
}
```

**Step 2: Add to appsettings.json**

`appsettings.json` already has an `"OpenAI"` section. Add two new sections anywhere in the JSON (before the closing `}`):

```json
"KnowledgeBase": {
  "OneDriveInboxPath": "/KnowledgeBase/Inbox",
  "OneDriveArchivedPath": "/KnowledgeBase/Archived",
  "EmbeddingModel": "text-embedding-3-small",
  "EmbeddingDimensions": 1536,
  "ChunkSize": 512,
  "ChunkOverlapTokens": 50,
  "MaxRetrievedChunks": 5,
  "ClaudeModel": "claude-sonnet-4-6",
  "ClaudeMaxTokens": 1024
},
"Anthropic": {
  "ApiKey": ""
}
```

Do NOT add real API keys — those live in Azure App Settings or local user secrets.

**Step 3: Build**

```bash
cd backend && dotnet build
```

**Step 4: Format and commit**

```bash
cd backend && dotnet format
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs
git add backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat: add KnowledgeBaseOptions and appsettings defaults"
```

---

## Task 3: IEmbeddingService + OpenAI implementation

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IEmbeddingService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/OpenAiEmbeddingService.cs`

**Step 1: Create interface**

```csharp
namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
}
```

**Step 2: Create OpenAI implementation**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _client;
    private readonly ILogger<OpenAiEmbeddingService> _logger;

    public OpenAiEmbeddingService(IConfiguration configuration, ILogger<OpenAiEmbeddingService> logger)
    {
        var apiKey = configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
        var model = configuration["KnowledgeBase:EmbeddingModel"] ?? "text-embedding-3-small";
        _client = new EmbeddingClient(model, apiKey);
        _logger = logger;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        _logger.LogDebug("Generating embedding for {CharCount} characters", text.Length);
        var result = await _client.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return result.Value.ToFloats().ToArray();
    }
}
```

> **NOTE:** `result.Value.ToFloats()` returns `ReadOnlyMemory<float>` in OpenAI SDK v2.x. If the build fails, check the actual return type: it might be `result.Value.Vector` or similar. Verify against https://github.com/openai/openai-dotnet.

**Step 3: Build**

```bash
cd backend && dotnet build
```

Expected: builds with no errors. Fix any OpenAI SDK type mismatches before committing.

**Step 4: Format and commit**

```bash
cd backend && dotnet format
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/
git commit -m "feat: add IEmbeddingService and OpenAI text-embedding-3-small implementation"
```

---

## Task 4: IDocumentTextExtractor + PdfPig implementation

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IDocumentTextExtractor.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/PdfTextExtractor.cs`

**Step 1: Create interface**

```csharp
namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public interface IDocumentTextExtractor
{
    bool CanHandle(string contentType);
    Task<string> ExtractTextAsync(byte[] content, CancellationToken ct = default);
}
```

**Step 2: Create PDF extractor**

```csharp
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class PdfTextExtractor : IDocumentTextExtractor
{
    private readonly ILogger<PdfTextExtractor> _logger;

    public PdfTextExtractor(ILogger<PdfTextExtractor> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(string contentType) =>
        contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(byte[] content, CancellationToken ct = default)
    {
        _logger.LogDebug("Extracting text from PDF ({Bytes} bytes)", content.Length);

        using var document = PdfDocument.Open(content);
        var pages = document.GetPages().Select(p => p.Text);
        var text = string.Join("\n\n", pages);

        _logger.LogDebug("Extracted {CharCount} characters from {PageCount} pages",
            text.Length, document.NumberOfPages);

        return Task.FromResult(text);
    }
}
```

**Step 3: Build**

```bash
cd backend && dotnet build
```

**Step 4: Format and commit**

```bash
cd backend && dotnet format
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IDocumentTextExtractor.cs
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/PdfTextExtractor.cs
git commit -m "feat: add IDocumentTextExtractor and PdfPig PDF implementation"
```

---

## Task 5: DocumentChunker (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentChunkerTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentChunker.cs`

**Step 1: Write failing tests**

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class DocumentChunkerTests
{
    private static DocumentChunker CreateChunker(int chunkSize = 20, int overlapWords = 2)
    {
        var options = Options.Create(new KnowledgeBaseOptions
        {
            ChunkSize = chunkSize,
            ChunkOverlapTokens = overlapWords
        });
        return new DocumentChunker(options);
    }

    [Fact]
    public void Chunk_ShortText_ReturnsSingleChunk()
    {
        var chunker = CreateChunker(chunkSize: 200);
        var text = "This is a short text.";
        var chunks = chunker.Chunk(text);
        Assert.Single(chunks);
        Assert.Equal(text, chunks[0]);
    }

    [Fact]
    public void Chunk_EmptyText_ReturnsEmpty()
    {
        var chunker = CreateChunker();
        var chunks = chunker.Chunk(string.Empty);
        Assert.Empty(chunks);
    }

    [Fact]
    public void Chunk_WhitespaceOnly_ReturnsEmpty()
    {
        var chunker = CreateChunker();
        var chunks = chunker.Chunk("   \n\t  ");
        Assert.Empty(chunks);
    }

    [Fact]
    public void Chunk_LongText_ReturnsMultipleChunksWithOverlap()
    {
        var chunker = CreateChunker(chunkSize: 5, overlapWords: 1);
        // 15 words → chunkSize=5, overlap=1, step=4
        // chunk 0: words 0-4 ("one two three four five")
        // chunk 1: words 4-8 ("five six seven eight nine") ← "five" is the overlap
        var text = "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen";
        var chunks = chunker.Chunk(text);
        Assert.True(chunks.Count > 1);
        Assert.Contains("five", chunks[1]);
    }
}
```

**Step 2: Run to verify they fail**

```bash
cd backend && dotnet test --filter "DocumentChunkerTests" -v
```

Expected: FAIL — `DocumentChunker` does not exist.

**Step 3: Implement DocumentChunker**

```csharp
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class DocumentChunker
{
    private readonly KnowledgeBaseOptions _options;

    public DocumentChunker(IOptions<KnowledgeBaseOptions> options)
    {
        _options = options.Value;
    }

    public List<string> Chunk(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();
        var step = _options.ChunkSize - _options.ChunkOverlapTokens;

        for (int i = 0; i < words.Length; i += step)
        {
            var chunkWords = words.Skip(i).Take(_options.ChunkSize);
            chunks.Add(string.Join(" ", chunkWords));

            if (i + _options.ChunkSize >= words.Length)
            {
                break;
            }
        }

        return chunks;
    }
}
```

**Step 4: Run tests to verify they pass**

```bash
cd backend && dotnet test --filter "DocumentChunkerTests" -v
```

Expected: All 4 tests PASS.

**Step 5: Format and commit**

```bash
cd backend && dotnet format
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentChunker.cs
git add backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentChunkerTests.cs
git commit -m "feat: add DocumentChunker with sliding window chunking and tests"
```

---

## Task 6: IClaudeService + Anthropic implementation

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IClaudeService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/AnthropicClaudeService.cs`

**Step 1: Create interface**

```csharp
namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public interface IClaudeService
{
    Task<string> GenerateAnswerAsync(
        string question,
        IEnumerable<string> contextChunks,
        CancellationToken ct = default);
}
```

**Step 2: Check exact Anthropic SDK types before implementing**

The installed version is `Anthropic` v1.0.0. Run:

```bash
dotnet tool run dotnet-script -- -e "using Anthropic; Console.WriteLine(typeof(AnthropicClient).Assembly.GetName().Version);"
```

Or simply check the SDK README at https://github.com/anthropics/anthropic-sdk-dotnet — look at the `Messages.CreateAsync` example in the `1.0.0` tag. The implementation below follows the SDK's documented pattern; adjust type names if the build fails.

**Step 3: Create Anthropic implementation**

```csharp
using Anthropic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class AnthropicClaudeService : IClaudeService
{
    private readonly AnthropicClient _client;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly ILogger<AnthropicClaudeService> _logger;

    public AnthropicClaudeService(IConfiguration configuration, ILogger<AnthropicClaudeService> logger)
    {
        var apiKey = configuration["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException("Anthropic:ApiKey is not configured.");
        _model = configuration["KnowledgeBase:ClaudeModel"] ?? "claude-sonnet-4-6";
        _maxTokens = int.TryParse(configuration["KnowledgeBase:ClaudeMaxTokens"], out var t) ? t : 1024;
        _client = new AnthropicClient(apiKey);
        _logger = logger;
    }

    public async Task<string> GenerateAnswerAsync(
        string question,
        IEnumerable<string> contextChunks,
        CancellationToken ct = default)
    {
        var context = string.Join("\n\n---\n\n", contextChunks);

        var prompt = $"""
            You are an expert assistant for a cosmetics manufacturing company.
            Answer the following question based strictly on the provided context.
            If the answer cannot be found in the context, say so explicitly.
            Always be precise and cite specific details from the context.

            CONTEXT:
            {context}

            QUESTION:
            {question}

            ANSWER:
            """;

        _logger.LogDebug("Calling Claude {Model}, question length {Len}", _model, question.Length);

        var message = await _client.Messages.CreateAsync(new MessageCreateParams
        {
            Model = _model,
            MaxTokens = _maxTokens,
            Messages =
            [
                new InputMessage
                {
                    Role = MessageRole.User,
                    Content = prompt
                }
            ]
        }, cancellationToken: ct);

        return message.Content[0].Text;
    }
}
```

> **NOTE:** `MessageCreateParams`, `InputMessage`, `MessageRole`, `Content[0].Text` — these are based on common SDK conventions. If the build fails, check the SDK source at https://github.com/anthropics/anthropic-sdk-dotnet and adjust type names accordingly. The `1.0.0` release may use different names than pre-1.0.

**Step 4: Build**

```bash
cd backend && dotnet build
```

Expected: builds with no errors. Fix any Anthropic SDK type mismatches now — do not commit broken code.

**Step 5: Format and commit**

```bash
cd backend && dotnet format
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IClaudeService.cs
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/AnthropicClaudeService.cs
git commit -m "feat: add IClaudeService and Anthropic implementation for Q&A generation"
```

---

## Phase 2 complete — verify

Run the full test suite:

```bash
cd backend && dotnet test -v
```

Expected: all existing tests pass + 4 new `DocumentChunkerTests` pass.

**Next:** Phase 3 — Use Cases (TDD): `SearchDocuments`, `AskQuestion`, `IndexDocument` handlers.
See `docs/plans/2026-03-02-rag-knowledge-base.md`, Tasks 12–14.
