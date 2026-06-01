# RAG Knowledge Base Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a RAG Knowledge Base vertical slice that ingests documents from OneDrive, stores vector embeddings in PostgreSQL via pgvector, and exposes semantic search + AI-grounded Q&A through REST API and MCP tools.

**Architecture:** New `KnowledgeBase` vertical slice following the existing Clean Architecture pattern (Domain → Application → Persistence → API). pgvector extension on existing Azure PostgreSQL. Hangfire job auto-discovered from Application assembly polls OneDrive inbox via Microsoft Graph API. Two new MCP tools appended to the existing MCP server.

**Tech Stack:**
- `Pgvector` NuGet — `Vector` type for EF Core
- `OpenAI` NuGet (official, v2.x) — text-embedding-3-small embeddings
- `Anthropic` NuGet (official Anthropic .NET SDK) — Claude API for Q&A generation
- `PdfPig` NuGet — PDF text extraction
- `Microsoft.Graph` (already installed) — OneDrive file access
- Hangfire (already installed) — recurring job scheduling

**GitHub Issue:** #381

---

## Phase 1: Database Foundation

### Task 1: Add NuGet packages

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`
- Modify: `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`

**Step 1: Add Pgvector to Persistence project**

Open `Anela.Heblo.Persistence.csproj` and add inside `<ItemGroup>`:

```xml
<PackageReference Include="Pgvector" Version="0.3.2" />
```

**Step 2: Add OpenAI + Anthropic + PdfPig to Application project**

Open `Anela.Heblo.Application.csproj` and add inside `<ItemGroup>`:

```xml
<PackageReference Include="OpenAI" Version="2.2.0" />
<PackageReference Include="Anthropic" Version="0.11.0" />
<PackageReference Include="PdfPig" Version="0.1.9" />
```

**Step 3: Restore and verify build**

```bash
cd backend
dotnet restore
dotnet build
```

Expected: Build succeeds with no errors.

**Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
git add backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
git commit -m "chore: add pgvector, openai, anthropic, pdfpig nuget packages"
```

---

### Task 2: Enable pgvector on NpgsqlDataSource

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`

**Step 1: Read the current UseNpgsql setup**

Find where `options.UseNpgsql(connectionString)` is called in `PersistenceModule.cs`.

**Step 2: Replace with NpgsqlDataSourceBuilder + UseVector**

Replace the `options.UseNpgsql(connectionString)` call (inside the `else` branch that handles real Postgres) with:

```csharp
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UseVector();
var dataSource = dataSourceBuilder.Build();
options.UseNpgsql(dataSource);
```

Add the using at the top of the file:

```csharp
using Npgsql;
using Pgvector;
```

The `UseInMemoryDatabase` path stays unchanged — it's only used in tests.

**Step 3: Build to verify**

```bash
cd backend && dotnet build
```

Expected: Builds with no errors.

**Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/PersistenceModule.cs
git commit -m "feat: enable pgvector extension on NpgsqlDataSource"
```

---

### Task 3: Domain entities

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseDocument.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseChunk.cs`

**Step 1: Create KnowledgeBaseDocument entity**

```csharp
namespace Anela.Heblo.Domain.Features.KnowledgeBase;

public class KnowledgeBaseDocument
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
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

**Step 2: Create KnowledgeBaseChunk entity**

Note: The `Vector` type comes from `Pgvector` namespace. The Persistence project has a reference to it; the Domain project does NOT — keep Domain free of infrastructure concerns by using `float[]` in domain and mapping at the persistence boundary.

```csharp
namespace Anela.Heblo.Domain.Features.KnowledgeBase;

public class KnowledgeBaseChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = Array.Empty<float>();

    public KnowledgeBaseDocument Document { get; set; } = null!;
}
```

**Step 3: Build to verify**

```bash
cd backend && dotnet build
```

**Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/
git commit -m "feat: add KnowledgeBaseDocument and KnowledgeBaseChunk domain entities"
```

---

### Task 4: Repository interface

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/IKnowledgeBaseRepository.cs`

**Step 1: Create the interface**

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

**Step 2: Build**

```bash
cd backend && dotnet build
```

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/IKnowledgeBaseRepository.cs
git commit -m "feat: add IKnowledgeBaseRepository interface"
```

---

### Task 5: EF Core configuration + DbContext update

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseDocumentConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseChunkConfiguration.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`

**Step 1: Create document configuration**

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
            .HasMaxLength(50);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.IndexedAt)
            .HasColumnType("timestamp without time zone");

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

**Step 2: Create chunk configuration**

This is where `Vector` (from `Pgvector`) is used. The `Embedding` is stored as a pgvector column. EF Core reads/writes it as `Vector` type at the persistence boundary.

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgvector;

namespace Anela.Heblo.Persistence.KnowledgeBase;

public class KnowledgeBaseChunkConfiguration : IEntityTypeConfiguration<KnowledgeBaseChunk>
{
    public void Configure(EntityTypeBuilder<KnowledgeBaseChunk> builder)
    {
        builder.ToTable("KnowledgeBaseChunks", "dbo");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Content)
            .IsRequired();

        builder.Property(e => e.ChunkIndex)
            .IsRequired();

        // Map float[] in domain to Vector (pgvector) in database
        builder.Property(e => e.Embedding)
            .HasColumnType("vector(1536)")
            .HasConversion(
                v => new Vector(v),
                v => v.Memory.ToArray());

        builder.HasIndex(e => e.Embedding)
            .HasMethod("hnsw")
            .HasOperators("vector_cosine_ops")
            .HasStorageParameter("m", 16)
            .HasStorageParameter("ef_construction", 64);

        builder.HasIndex(e => e.DocumentId);
    }
}
```

**Step 3: Add DbSets to ApplicationDbContext**

Open `ApplicationDbContext.cs` and add two new DbSet properties alongside the existing ones:

```csharp
public DbSet<KnowledgeBaseDocument> KnowledgeBaseDocuments { get; set; }
public DbSet<KnowledgeBaseChunk> KnowledgeBaseChunks { get; set; }
```

Also add inside `OnModelCreating` (before or after existing extension setup) — **only if** `HasPostgresExtension` is not already called elsewhere:

```csharp
modelBuilder.HasPostgresExtension("vector");
```

Add the using:

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;
```

**Step 4: Build**

```bash
cd backend && dotnet build
```

**Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/KnowledgeBase/
git add backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs
git commit -m "feat: add KnowledgeBase EF Core configurations and DbSets"
```

---

### Task 6: Create EF Core migration

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddKnowledgeBase.cs` (auto-generated)

**Step 1: Create migration**

```bash
cd backend
dotnet ef migrations add AddKnowledgeBase \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API
```

**Step 2: Review the generated migration**

Open the generated migration file and verify it contains:
- `CREATE TABLE "dbo"."KnowledgeBaseDocuments"` with all columns
- `CREATE TABLE "dbo"."KnowledgeBaseChunks"` with `embedding vector(1536)` column
- HNSW index on the embedding column
- `CREATE EXTENSION IF NOT EXISTS vector` (or it may be in HasPostgresExtension)

If the HNSW index isn't generated automatically (EF Core support for HNSW operators varies), add it manually in the migration:

```csharp
migrationBuilder.Sql(
    "CREATE INDEX idx_knowledge_base_chunks_embedding ON dbo.\"KnowledgeBaseChunks\" " +
    "USING hnsw (\"Embedding\" vector_cosine_ops) WITH (m = 16, ef_construction = 64);");
```

**Step 3: Commit migration**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat: add EF Core migration for KnowledgeBase tables with pgvector"
```

> **NOTE:** Apply this migration to local dev DB before running tests:
> ```bash
> dotnet ef database update --project src/Anela.Heblo.Persistence --startup-project src/Anela.Heblo.API
> ```

---

## Phase 2: Application Services

### Task 7: Configuration options

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs`

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

**Step 2: Add to appsettings.json** (dev defaults only — secrets via Azure App Settings):

Open `backend/src/Anela.Heblo.API/appsettings.json` and add:

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
}
```

Also add the Anthropic key placeholder (actual value goes in Azure App Settings / `.env.local`):

```json
"Anthropic": {
  "ApiKey": ""
}
```

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs
git add backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat: add KnowledgeBaseOptions configuration class and appsettings defaults"
```

---

### Task 8: IEmbeddingService + OpenAI implementation

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
using Microsoft.Extensions.Options;
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

> **NOTE:** Verify exact OpenAI SDK API from https://github.com/openai/openai-dotnet — the `EmbeddingClient` constructor and `GenerateEmbeddingAsync` signature may differ slightly in newer versions.

**Step 3: Build**

```bash
cd backend && dotnet build
```

**Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/
git commit -m "feat: add IEmbeddingService and OpenAI implementation"
```

---

### Task 9: IDocumentTextExtractor + PDF implementation

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

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IDocumentTextExtractor.cs
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/PdfTextExtractor.cs
git commit -m "feat: add IDocumentTextExtractor and PdfPig PDF implementation"
```

---

### Task 10: DocumentChunker (sliding window)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentChunker.cs`

**Step 1: Write failing test**

Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentChunkerTests.cs`

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class DocumentChunkerTests
{
    private DocumentChunker CreateChunker(int chunkSize = 20, int overlapWords = 2)
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
    public void Chunk_LongText_ReturnsMultipleChunks()
    {
        var chunker = CreateChunker(chunkSize: 5, overlapWords: 1);
        // 15 words → expect 3+ chunks with overlap
        var text = "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen";
        var chunks = chunker.Chunk(text);
        Assert.True(chunks.Count > 1);
        // Last chunk of first window should appear at start of second window (overlap)
        Assert.Contains("five", chunks[1]);
    }
}
```

**Step 2: Run test to see it fail**

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

Expected: All 3 tests PASS.

**Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentChunker.cs
git add backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentChunkerTests.cs
git commit -m "feat: add DocumentChunker with sliding window chunking and tests"
```

---

### Task 11: IClaudeService + Anthropic implementation

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IClaudeService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/AnthropicClaudeService.cs`

**Step 1: Create interface**

```csharp
namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public interface IClaudeService
{
    Task<string> GenerateAnswerAsync(string question, IEnumerable<string> contextChunks, CancellationToken ct = default);
}
```

**Step 2: Create Anthropic implementation**

> **IMPORTANT:** The installed `Anthropic` v1.0.0 NuGet package is the **tryAGI/Anthropic** community SDK, NOT the official `Anthropic.SDK`. The API differs from what the official SDK docs show.

```csharp
using Anthropic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class AnthropicClaudeService : IClaudeService
{
    private readonly IConfiguration _configuration;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly ILogger<AnthropicClaudeService> _logger;

    public AnthropicClaudeService(IConfiguration configuration, ILogger<AnthropicClaudeService> logger)
    {
        _configuration = configuration;
        _model = configuration["KnowledgeBase:ClaudeModel"] ?? "claude-sonnet-4-6";
        _maxTokens = int.TryParse(configuration["KnowledgeBase:ClaudeMaxTokens"], out var t) ? t : 1024;
        _logger = logger;
    }

    public async Task<string> GenerateAnswerAsync(
        string question,
        IEnumerable<string> contextChunks,
        CancellationToken ct = default)
    {
        var apiKey = _configuration["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException("Anthropic:ApiKey is not configured.");

        using var api = new AnthropicApi();
        api.AuthorizeUsingApiKey(apiKey);

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

        _logger.LogDebug("Calling Claude {Model} for Q&A, question length {Len}", _model, question.Length);

        var response = await api.CreateMessageAsync(
            model: _model,
            messages: [prompt],
            maxTokens: _maxTokens,
            cancellationToken: ct);

        // response.Content is OneOf<string, IList<Block>>
        var blocks = response.Content.Value2;
        if (blocks is not null)
        {
            return blocks.OfType<TextBlock>().Select(b => b.Text).FirstOrDefault() ?? string.Empty;
        }

        return response.Content.Value1 ?? string.Empty;
    }
}
```

**Step 3: Build**

```bash
cd backend && dotnet build
```

**Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/
git commit -m "feat: add IClaudeService and Anthropic implementation for Q&A generation"
```

---

## Phase 3: Use Cases (TDD)

### Task 12: SearchDocuments use case

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

**Step 2: Run test to verify it fails**

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

**Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SearchDocuments/
git add backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/SearchDocumentsHandlerTests.cs
git commit -m "feat: add SearchDocuments use case with handler and tests"
```

---

### Task 13: AskQuestion use case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/AskQuestionHandlerTests.cs`

**Step 1: Write failing test**

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Domain.Features.KnowledgeBase;
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

**Step 2: Run test to verify it fails**

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

**Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/
git add backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/AskQuestionHandlerTests.cs
git commit -m "feat: add AskQuestion use case with handler and tests"
```

---

### Task 14: IndexDocument use case

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
            Content = pdfBytes
        }, default);

        Assert.NotNull(savedDoc);
        Assert.Equal("test.pdf", savedDoc!.Filename);
        _repository.Verify(r => r.AddChunksAsync(
            It.Is<IEnumerable<KnowledgeBaseChunk>>(chunks => chunks.Any()),
            default), Times.Once);
        _repository.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }
}
```

**Step 2: Run test to verify it fails**

```bash
cd backend && dotnet test --filter "IndexDocumentHandlerTests" -v
```

Expected: FAIL.

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

Expected: 1 test PASSES.

**Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/
git add backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/IndexDocumentHandlerTests.cs
git commit -m "feat: add IndexDocument use case with handler and tests"
```

---

## Phase 4: Repository + Ingestion

### Task 15: KnowledgeBaseRepository (Persistence)

> **IMPLEMENTED (Phase 4).** `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs` ✅

**Key deviation from original plan:** `Pgvector.EntityFrameworkCore` extension methods (`CosineDistance()`) are incompatible with Npgsql 8.0.4 (requires >= 9.0.1). Raw SQL with the `<=>` pgvector cosine distance operator is used instead for both `AddChunksAsync` and `SearchSimilarAsync`. EF Core is still used for document queries.

**Actual implementation:**
- `AddChunksAsync`: Raw `NpgsqlCommand` INSERT with `@embedding` pgvector parameter
- `SearchSimilarAsync`: Raw SQL `SELECT ... 1 - (c."Embedding" <=> @embedding) AS "Score" ... ORDER BY c."Embedding" <=> @embedding LIMIT @topK`; documents loaded via `_context.KnowledgeBaseDocuments.FindAsync`
- All other methods use EF Core LINQ as planned

**Step 2: Build**

```bash
cd backend && dotnet build
```

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs
git commit -m "feat: add KnowledgeBaseRepository with pgvector cosine similarity search"
```

---

### Task 16: Wire real repository into DI (replaces placeholder)

> **IMPLEMENTED (Phase 4).** ✅ Placeholder removed. `KnowledgeBaseRepository` registered in `PersistenceModule`. `KnowledgeBaseModule` updated with conditional `GraphOneDriveService`/`MockOneDriveService` registration.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` — remove placeholder registration
- Modify: `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` — add real repository

**Step 1: Remove placeholder from KnowledgeBaseModule**

In `KnowledgeBaseModule.cs`, remove:
```csharp
services.AddScoped<IKnowledgeBaseRepository, NotImplementedKnowledgeBaseRepository>();
```

Also delete the file: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/NotImplementedKnowledgeBaseRepository.cs`

**Step 2: Register real repository in PersistenceModule**

In `PersistenceModule.cs`, add alongside other repository registrations:

```csharp
services.AddScoped<IKnowledgeBaseRepository, KnowledgeBaseRepository>();
```

Add usings:

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Persistence.KnowledgeBase;
```

**Step 3: Build and run all tests**

```bash
cd backend && dotnet build && dotnet test
```

Expected: All tests pass.

**Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs
git add backend/src/Anela.Heblo.Persistence/PersistenceModule.cs
git rm backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/NotImplementedKnowledgeBaseRepository.cs
git commit -m "feat: register KnowledgeBase module and repository in DI"
```

---

### Task 17: IOneDriveService + Microsoft Graph implementation

> **IMPLEMENTED (Phase 4).** `IOneDriveService.cs` and `GraphOneDriveService.cs` ✅. Also added `MockOneDriveService.cs` for mock auth mode.

**Key deviation from original plan:** `GraphServiceClient` is NOT directly injectable in this codebase. The actual pattern uses `ITokenAcquisition` + raw `HttpClient` (matching existing codebase conventions). `MockOneDriveService` (returns empty list) is registered when `UseMockAuth=true` or `BypassJwtValidation=true` (matching `UserManagementModule` pattern).

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IOneDriveService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/MockOneDriveService.cs`

**Step 1: Create interface**

```csharp
namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public record OneDriveFile(string Id, string Name, string ContentType, string Path);

public interface IOneDriveService
{
    Task<List<OneDriveFile>> ListInboxFilesAsync(CancellationToken ct = default);
    Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default);
    Task MoveToArchivedAsync(string fileId, string filename, CancellationToken ct = default);
}
```

**Step 2: Create Graph implementation**

Uses `ITokenAcquisition` + raw `HttpClient` (NOT `GraphServiceClient` directly — it is not injectable in this codebase):

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class GraphOneDriveService : IOneDriveService
{
    private readonly GraphServiceClient _graphClient;
    private readonly KnowledgeBaseOptions _options;
    private readonly ILogger<GraphOneDriveService> _logger;

    public GraphOneDriveService(
        GraphServiceClient graphClient,
        IOptions<KnowledgeBaseOptions> options,
        ILogger<GraphOneDriveService> logger)
    {
        _graphClient = graphClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<OneDriveFile>> ListInboxFilesAsync(CancellationToken ct = default)
    {
        // List files in the inbox folder from the authenticated user's OneDrive
        // NOTE: For a service principal (daemon app), use Sites API or a specific user's drive.
        // Verify the correct Graph API path for your setup:
        // - User's drive: /me/drive/root:{path}:/children
        // - SharePoint: /sites/{siteId}/drives/{driveId}/root:{path}:/children

        _logger.LogDebug("Listing files in OneDrive inbox: {Path}", _options.OneDriveInboxPath);

        var driveItems = await _graphClient.Me.Drive
            .Root
            .ItemWithPath(_options.OneDriveInboxPath)
            .Children
            .GetAsync(cancellationToken: ct);

        return driveItems?.Value?
            .Where(i => i.File != null)
            .Select(i => new OneDriveFile(
                i.Id!,
                i.Name!,
                i.File!.MimeType ?? "application/octet-stream",
                i.WebUrl ?? string.Empty))
            .ToList() ?? [];
    }

    public async Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default)
    {
        _logger.LogDebug("Downloading file {FileId} from OneDrive", fileId);

        var stream = await _graphClient.Me.Drive
            .Items[fileId]
            .Content
            .GetAsync(cancellationToken: ct);

        using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    public async Task MoveToArchivedAsync(string fileId, string filename, CancellationToken ct = default)
    {
        _logger.LogDebug("Moving file {Filename} ({FileId}) to archived folder", filename, fileId);

        // Move by updating the parentReference
        var archivedFolderPath = _options.OneDriveArchivedPath.TrimStart('/');

        await _graphClient.Me.Drive
            .Items[fileId]
            .PatchAsync(new Microsoft.Graph.Models.DriveItem
            {
                ParentReference = new Microsoft.Graph.Models.ItemReference
                {
                    Path = $"/drive/root:/{archivedFolderPath}"
                }
            }, cancellationToken: ct);
    }
}
```

> **IMPORTANT:** The exact Graph API calls depend on whether you use `Me.Drive` (delegated user access) or a SharePoint/site drive (application permissions). Check how `GraphServiceClient` is configured in existing code (look for `GetResponsiblePersons` in `ManufactureOrderMcpTools`) and use the same pattern.

**Step 3: Register in KnowledgeBaseModule**

Add to `KnowledgeBaseModule.AddKnowledgeBaseModule()`:

```csharp
services.AddScoped<IOneDriveService, GraphOneDriveService>();
```

**Step 4: Build**

```bash
cd backend && dotnet build
```

**Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IOneDriveService.cs
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs
git commit -m "feat: add IOneDriveService and Microsoft Graph implementation for OneDrive access"
```

---

### Task 18: KnowledgeBaseIngestionJob (Hangfire)

> **IMPLEMENTED (Phase 4).** `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs` ✅

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs`

**Step 1: Create job**

The job is auto-discovered from the Application assembly via `AddRecurringJobs()`. Just implement `IRecurringJob` and it will be registered automatically.

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure.Jobs;

public class KnowledgeBaseIngestionJob : IRecurringJob
{
    private readonly IOneDriveService _oneDrive;
    private readonly IKnowledgeBaseRepository _repository;
    private readonly IMediator _mediator;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<KnowledgeBaseIngestionJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "knowledge-base-ingestion",
        DisplayName = "Knowledge Base Ingestion",
        Description = "Polls OneDrive inbox folder and ingests new documents into the knowledge base vector store",
        CronExpression = "*/15 * * * *", // Every 15 minutes
        DefaultIsEnabled = true
    };

    public KnowledgeBaseIngestionJob(
        IOneDriveService oneDrive,
        IKnowledgeBaseRepository repository,
        IMediator mediator,
        IRecurringJobStatusChecker statusChecker,
        ILogger<KnowledgeBaseIngestionJob> logger)
    {
        _oneDrive = oneDrive;
        _repository = repository;
        _mediator = mediator;
        _statusChecker = statusChecker;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        _logger.LogInformation("Starting {JobName}", Metadata.JobName);

        var files = await _oneDrive.ListInboxFilesAsync(cancellationToken);
        _logger.LogInformation("Found {Count} files in OneDrive inbox", files.Count);

        int indexed = 0;
        int skipped = 0;
        int failed = 0;

        foreach (var file in files)
        {
            try
            {
                var content = await _oneDrive.DownloadFileAsync(file.Id, cancellationToken);

                // Compute SHA-256 hash of file content for content-based deduplication.
                // This handles moves/renames correctly: same content at a new path → update path, skip re-embedding.
                var contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();

                var existingDocument = await _repository.GetDocumentByHashAsync(contentHash, cancellationToken);
                if (existingDocument is not null)
                {
                    if (existingDocument.SourcePath != file.Path)
                    {
                        _logger.LogInformation("File {Filename} moved from {OldPath} to {NewPath}, updating path",
                            file.Name, existingDocument.SourcePath, file.Path);
                        await _repository.UpdateDocumentSourcePathAsync(existingDocument.Id, file.Path, cancellationToken);
                    }
                    else
                    {
                        _logger.LogDebug("Skipping already-indexed file {Filename} (hash match)", file.Name);
                    }
                    skipped++;
                    continue;
                }

                await _mediator.Send(new IndexDocumentRequest
                {
                    Filename = file.Name,
                    SourcePath = file.Path,
                    ContentType = file.ContentType,
                    Content = content,
                    ContentHash = contentHash
                }, cancellationToken);

                await _oneDrive.MoveToArchivedAsync(file.Id, file.Name, cancellationToken);

                _logger.LogInformation("Indexed and archived {Filename}", file.Name);
                indexed++;
            }
            catch (NotSupportedException ex)
            {
                _logger.LogWarning("Skipping unsupported file {Filename}: {Message}", file.Name, ex.Message);
                skipped++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to index {Filename}", file.Name);
                failed++;
            }
        }

        _logger.LogInformation("{JobName} complete. Indexed: {Indexed}, Skipped: {Skipped}, Failed: {Failed}",
            Metadata.JobName, indexed, skipped, failed);
    }
}
```

**Step 2: Build**

```bash
cd backend && dotnet build
```

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs
git commit -m "feat: add KnowledgeBaseIngestionJob - OneDrive polling and document indexing"
```

---

## Phase 5: API Surface

### Task 19: KnowledgeBaseController

> **IMPLEMENTED (Phase 5).** `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs` ✅

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs`

**Step 1: Create controller**

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/knowledge-base")]
[Authorize]
public class KnowledgeBaseController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IKnowledgeBaseRepository _repository;

    public KnowledgeBaseController(IMediator mediator, IKnowledgeBaseRepository repository)
    {
        _mediator = mediator;
        _repository = repository;
    }

    [HttpGet("documents")]
    public async Task<IActionResult> GetDocuments(CancellationToken ct)
    {
        var docs = await _repository.GetAllDocumentsAsync(ct);
        return Ok(docs.Select(d => new
        {
            d.Id,
            d.Filename,
            d.Status,
            d.ContentType,
            d.CreatedAt,
            d.IndexedAt
        }));
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] SearchDocumentsRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(request, ct);
        return Ok(result);
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskQuestionRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(request, ct);
        return Ok(result);
    }
}
```

**Step 2: Build**

```bash
cd backend && dotnet build
```

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs
git commit -m "feat: add KnowledgeBaseController with /documents, /search, /ask endpoints"
```

---

### Task 20: KnowledgeBaseTools (MCP) with tests

> **IMPLEMENTED (Phase 5).** `KnowledgeBaseTools.cs`, `KnowledgeBaseToolsTests.cs` (3 tests), `McpModule.cs` updated ✅. Final test count: 1692 all passing.

**Files:**
- Create: `backend/src/Anela.Heblo.API/MCP/Tools/KnowledgeBaseTools.cs`
- Create: `backend/test/Anela.Heblo.Tests/MCP/Tools/KnowledgeBaseToolsTests.cs`
- Modify: `backend/src/Anela.Heblo.API/MCP/McpModule.cs`

**Step 1: Write failing test**

```csharp
using System.Text.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using MediatR;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class KnowledgeBaseToolsTests
{
    private readonly Mock<IMediator> _mediator = new();

    [Fact]
    public async Task SearchKnowledgeBase_ShouldMapParametersCorrectly()
    {
        var expected = new SearchDocumentsResponse
        {
            Chunks = [new ChunkResult { Content = "Test chunk", Score = 0.9, SourceFilename = "doc.pdf" }]
        };

        _mediator
            .Setup(m => m.Send(
                It.Is<SearchDocumentsRequest>(r => r.Query == "phenoxyethanol" && r.TopK == 3),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tools = new KnowledgeBaseTools(_mediator.Object);
        var result = await tools.SearchKnowledgeBase("phenoxyethanol", 3);

        var deserialized = JsonSerializer.Deserialize<SearchDocumentsResponse>(result);
        Assert.NotNull(deserialized);
        Assert.Single(deserialized!.Chunks);
        Assert.Equal("Test chunk", deserialized.Chunks[0].Content);
    }

    [Fact]
    public async Task AskKnowledgeBase_ShouldMapParametersCorrectly()
    {
        var expected = new AskQuestionResponse
        {
            Answer = "The max is 1.0%.",
            Sources = [new SourceReference { Filename = "EU_reg.pdf", Score = 0.95 }]
        };

        _mediator
            .Setup(m => m.Send(
                It.Is<AskQuestionRequest>(r => r.Question == "Max phenoxyethanol?"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tools = new KnowledgeBaseTools(_mediator.Object);
        var result = await tools.AskKnowledgeBase("Max phenoxyethanol?");

        var deserialized = JsonSerializer.Deserialize<AskQuestionResponse>(result);
        Assert.NotNull(deserialized);
        Assert.Equal("The max is 1.0%.", deserialized!.Answer);
        Assert.Single(deserialized.Sources);
    }

    [Fact]
    public async Task SearchKnowledgeBase_ShouldThrowMcpException_WhenMediatorThrows()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        var tools = new KnowledgeBaseTools(_mediator.Object);

        await Assert.ThrowsAsync<McpException>(() =>
            tools.SearchKnowledgeBase("query"));
    }
}
```

**Step 2: Run test to verify it fails**

```bash
cd backend && dotnet test --filter "KnowledgeBaseToolsTests" -v
```

Expected: FAIL.

**Step 3: Create KnowledgeBaseTools**

```csharp
using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using MediatR;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anela.Heblo.API.MCP.Tools;

[McpServerToolType]
public class KnowledgeBaseTools
{
    private readonly IMediator _mediator;

    public KnowledgeBaseTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    [McpServerTool]
    [Description("Search the knowledge base for relevant document chunks using semantic similarity. Returns raw chunks with source references.")]
    public async Task<string> SearchKnowledgeBase(
        [Description("Natural language search query")] string query,
        [Description("Number of chunks to return (default: 5)")] int topK = 5)
    {
        try
        {
            var result = await _mediator.Send(new SearchDocumentsRequest
            {
                Query = query,
                TopK = topK
            });
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            throw new McpException($"Failed to search knowledge base: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Ask a question and get an AI-generated answer grounded in company documents. Returns a prose answer with cited sources.")]
    public async Task<string> AskKnowledgeBase(
        [Description("Question to answer using the knowledge base")] string question,
        [Description("Number of context chunks to retrieve (default: 5)")] int topK = 5)
    {
        try
        {
            var result = await _mediator.Send(new AskQuestionRequest
            {
                Question = question,
                TopK = topK
            });
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            throw new McpException($"Failed to answer question: {ex.Message}");
        }
    }
}
```

**Step 4: Register in McpModule**

Open `backend/src/Anela.Heblo.API/MCP/McpModule.cs` and add `.WithTools<KnowledgeBaseTools>()`:

```csharp
services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<CatalogMcpTools>()
    .WithTools<ManufactureOrderMcpTools>()
    .WithTools<ManufactureBatchMcpTools>()
    .WithTools<KnowledgeBaseTools>();  // ← add this line
```

**Step 5: Run tests to verify they pass**

```bash
cd backend && dotnet test --filter "KnowledgeBaseToolsTests" -v
```

Expected: All 3 tests PASS.

**Step 6: Run all tests**

```bash
cd backend && dotnet test
```

Expected: All tests pass.

**Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/KnowledgeBaseTools.cs
git add backend/src/Anela.Heblo.API/MCP/McpModule.cs
git add backend/test/Anela.Heblo.Tests/MCP/Tools/KnowledgeBaseToolsTests.cs
git commit -m "feat: add KnowledgeBaseTools MCP tools with tests and register in McpModule"
```

---

## Phase 6: Final Validation & Configuration

### Task 21: Format, build, and documentation

**Step 1: Run dotnet format**

```bash
cd backend && dotnet format
```

Expected: No changes needed (all code already formatted correctly). If changes are made, stage them:

```bash
git add -A
git commit -m "style: apply dotnet format to KnowledgeBase feature"
```

**Step 2: Final build**

```bash
cd backend && dotnet build --configuration Release
```

Expected: Build succeeds with 0 errors, 0 warnings.

**Step 3: Run all backend tests**

```bash
cd backend && dotnet test
```

Expected: All tests pass.

**Step 4: Update architecture docs**

Update `docs/architecture/application_infrastructure.md`:
- Add `pgvector` extension to the PostgreSQL section
- Add `OpenAI` and `Anthropic` API keys to the Azure App Settings section

Update `docs/architecture/environments.md`:
- Add new environment variables: `OpenAI__ApiKey` (already exists), `Anthropic__ApiKey` (new), `KnowledgeBase__OneDriveInboxPath`

Update `CLAUDE.md` MCP Server section:
- Add `SearchKnowledgeBase` and `AskKnowledgeBase` to the MCP tools list under a new "Knowledge Base Tools (2):" subsection

**Step 5: Commit docs**

```bash
git add docs/
git add CLAUDE.md
git commit -m "docs: update architecture docs and CLAUDE.md for KnowledgeBase RAG feature"
```

**Step 6: Final commit — close the loop**

```bash
git log --oneline -15
```

Verify all commits are clean and descriptive.

---

## Azure Deployment Checklist

Before deploying, complete these steps manually in Azure Portal:

1. **Enable pgvector extension** on Azure Database for PostgreSQL Flexible Server:
   - Go to Azure Portal → PostgreSQL instance → Server parameters
   - Search for `azure.extensions` and add `vector` to the list
   - Save and restart

2. **Apply EF Core migration** after enabling the extension:
   ```bash
   dotnet ef database update --project src/Anela.Heblo.Persistence --startup-project src/Anela.Heblo.API
   ```

3. **Add Azure App Settings** (do NOT commit these values):
   - `Anthropic__ApiKey` — your Anthropic API key
   - `KnowledgeBase__OneDriveInboxPath` — actual OneDrive path
   - `KnowledgeBase__OneDriveArchivedPath` — actual archive path

4. **Grant Microsoft Graph permissions** on Entra ID app registration:
   - `Files.ReadWrite.All` (for user's OneDrive) OR `Sites.ReadWrite.All` (for SharePoint)
   - Grant admin consent

5. **Create OneDrive folders**:
   - `/KnowledgeBase/Inbox/` — drop files here for ingestion
   - `/KnowledgeBase/Archived/` — processed files moved here automatically

---

## Summary of Files Created

```
backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/
├── KnowledgeBaseDocument.cs
├── KnowledgeBaseChunk.cs
└── IKnowledgeBaseRepository.cs

backend/src/Anela.Heblo.Application/Features/KnowledgeBase/
├── KnowledgeBaseOptions.cs
├── KnowledgeBaseModule.cs
├── Services/
│   ├── IEmbeddingService.cs
│   ├── OpenAiEmbeddingService.cs
│   ├── IDocumentTextExtractor.cs
│   ├── PdfTextExtractor.cs
│   ├── DocumentChunker.cs
│   ├── IClaudeService.cs
│   ├── AnthropicClaudeService.cs
│   ├── IOneDriveService.cs
│   └── GraphOneDriveService.cs
├── UseCases/
│   ├── SearchDocuments/
│   │   ├── SearchDocumentsRequest.cs
│   │   └── SearchDocumentsHandler.cs
│   ├── AskQuestion/
│   │   ├── AskQuestionRequest.cs
│   │   └── AskQuestionHandler.cs
│   └── IndexDocument/
│       ├── IndexDocumentRequest.cs
│       └── IndexDocumentHandler.cs
└── Infrastructure/Jobs/
    └── KnowledgeBaseIngestionJob.cs

backend/src/Anela.Heblo.Persistence/KnowledgeBase/
├── KnowledgeBaseDocumentConfiguration.cs
├── KnowledgeBaseChunkConfiguration.cs
└── KnowledgeBaseRepository.cs

backend/src/Anela.Heblo.API/
├── Controllers/KnowledgeBaseController.cs
└── MCP/Tools/KnowledgeBaseTools.cs

backend/test/Anela.Heblo.Tests/KnowledgeBase/
├── Services/DocumentChunkerTests.cs
└── UseCases/
    ├── SearchDocumentsHandlerTests.cs
    ├── AskQuestionHandlerTests.cs
    ├── IndexDocumentHandlerTests.cs
    └── (in MCP/Tools/) KnowledgeBaseToolsTests.cs
```
