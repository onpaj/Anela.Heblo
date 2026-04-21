# Photo Bank: Iteration 1 — Infrastructure & Data Pipeline

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Set up the backend foundation: domain entities, database migration, Azure AI Vision adapter, OneDrive sync job, and photo indexing pipeline.

**Architecture:** New `PhotoBank` feature module following Clean Architecture vertical slice pattern. Domain entities in Domain layer, MediatR handlers + jobs in Application layer, EF Core configs + repository in Persistence layer, Azure AI Vision adapter in a new Adapters project. Hangfire recurring job syncs OneDrive, enqueues per-photo indexing jobs.

**Tech Stack:** .NET 8, EF Core, PostgreSQL + pgvector, Hangfire, Azure AI Vision 4.0 API, Microsoft Graph API, Azure Blob Storage

**GitHub Issue:** #611

---

## Task 1: Domain Entities

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/PhotoBank/TagSource.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/PhotoBank/PhotoAssetStatus.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/PhotoBank/PhotoTag.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/PhotoBank/PhotoAsset.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/PhotoBank/IPhotoAssetRepository.cs`

- [ ] **Step 1: Create TagSource enum**

```csharp
// backend/src/Anela.Heblo.Domain/Features/PhotoBank/TagSource.cs
namespace Anela.Heblo.Domain.Features.PhotoBank;

public enum TagSource
{
    Auto = 0,
    Manual = 1
}
```

- [ ] **Step 2: Create PhotoAssetStatus enum**

```csharp
// backend/src/Anela.Heblo.Domain/Features/PhotoBank/PhotoAssetStatus.cs
namespace Anela.Heblo.Domain.Features.PhotoBank;

public enum PhotoAssetStatus
{
    Pending = 0,
    Indexed = 1,
    Failed = 2,
    Deleted = 3
}
```

- [ ] **Step 3: Create PhotoTag entity**

```csharp
// backend/src/Anela.Heblo.Domain/Features/PhotoBank/PhotoTag.cs
namespace Anela.Heblo.Domain.Features.PhotoBank;

public class PhotoTag
{
    public Guid Id { get; set; }
    public Guid PhotoAssetId { get; set; }
    public string TagName { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public TagSource Source { get; set; } = TagSource.Auto;

    public PhotoAsset PhotoAsset { get; set; } = null!;
}
```

- [ ] **Step 4: Create PhotoAsset entity**

```csharp
// backend/src/Anela.Heblo.Domain/Features/PhotoBank/PhotoAsset.cs
namespace Anela.Heblo.Domain.Features.PhotoBank;

public class PhotoAsset
{
    public Guid Id { get; set; }
    public string OneDriveItemId { get; set; } = string.Empty;
    public string OneDrivePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public DateTimeOffset? TakenAt { get; set; }
    public DateTimeOffset? IndexedAt { get; set; }
    public string? ThumbnailBlobPath { get; set; }
    public float[]? Embedding { get; set; }
    public string? OcrText { get; set; }
    public PhotoAssetStatus Status { get; set; } = PhotoAssetStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }

    public ICollection<PhotoTag> Tags { get; set; } = new List<PhotoTag>();
}
```

- [ ] **Step 5: Create IPhotoAssetRepository interface**

```csharp
// backend/src/Anela.Heblo.Domain/Features/PhotoBank/IPhotoAssetRepository.cs
namespace Anela.Heblo.Domain.Features.PhotoBank;

public interface IPhotoAssetRepository
{
    Task<PhotoAsset?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PhotoAsset?> GetByOneDriveItemIdAsync(string oneDriveItemId, CancellationToken ct = default);
    Task AddAsync(PhotoAsset asset, CancellationToken ct = default);
    Task UpdateAsync(PhotoAsset asset, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task UpsertWithEmbeddingAsync(PhotoAsset asset, CancellationToken ct = default);
}
```

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/PhotoBank/
git commit -m "feat(photo-bank): add domain entities and repository interface"
```

---

## Task 2: EF Core Entity Configurations

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/PhotoBank/PhotoAssetConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/PhotoBank/PhotoTagConfiguration.cs`

- [ ] **Step 1: Create PhotoAssetConfiguration**

Follow the pattern from `KnowledgeBaseDocumentConfiguration` and `KnowledgeBaseChunkConfiguration`. The `Embedding` property is ignored by EF and managed via raw SQL (same as KnowledgeBase).

```csharp
// backend/src/Anela.Heblo.Persistence/PhotoBank/PhotoAssetConfiguration.cs
using Anela.Heblo.Domain.Features.PhotoBank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.PhotoBank;

public class PhotoAssetConfiguration : IEntityTypeConfiguration<PhotoAsset>
{
    public void Configure(EntityTypeBuilder<PhotoAsset> builder)
    {
        builder.ToTable("PhotoAssets", "dbo");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.OneDriveItemId)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.OneDrivePath)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(e => e.FileName)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.MimeType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.ThumbnailBlobPath)
            .HasMaxLength(500);

        builder.Property(e => e.OcrText);

        builder.Property(e => e.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        // Embedding managed via raw SQL (pgvector), same as KnowledgeBaseChunks
        builder.Ignore(e => e.Embedding);

        builder.HasIndex(e => e.OneDriveItemId)
            .IsUnique()
            .HasDatabaseName("IX_PhotoAssets_OneDriveItemId");

        builder.HasIndex(e => e.Status)
            .HasDatabaseName("IX_PhotoAssets_Status");

        builder.HasMany(e => e.Tags)
            .WithOne(e => e.PhotoAsset)
            .HasForeignKey(e => e.PhotoAssetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 2: Create PhotoTagConfiguration**

```csharp
// backend/src/Anela.Heblo.Persistence/PhotoBank/PhotoTagConfiguration.cs
using Anela.Heblo.Domain.Features.PhotoBank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.PhotoBank;

public class PhotoTagConfiguration : IEntityTypeConfiguration<PhotoTag>
{
    public void Configure(EntityTypeBuilder<PhotoTag> builder)
    {
        builder.ToTable("PhotoTags", "dbo");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.TagName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Confidence)
            .IsRequired();

        builder.Property(e => e.Source)
            .IsRequired()
            .HasConversion<int>();

        builder.HasIndex(e => e.PhotoAssetId)
            .HasDatabaseName("IX_PhotoTags_PhotoAssetId");

        builder.HasIndex(e => e.TagName)
            .HasDatabaseName("IX_PhotoTags_TagName");
    }
}
```

- [ ] **Step 3: Register DbSets in ApplicationDbContext**

Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`

Add two new `DbSet` properties following the existing pattern:

```csharp
public DbSet<PhotoAsset> PhotoAssets { get; set; } = null!;
public DbSet<PhotoTag> PhotoTags { get; set; } = null!;
```

- [ ] **Step 4: Verify build compiles**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/PhotoBank/ backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs
git commit -m "feat(photo-bank): add EF Core entity configurations"
```

---

## Task 3: Database Migration

**Files:**
- Create: SQL migration script (manual migration pattern used by this project)

- [ ] **Step 1: Create SQL migration script**

Create a migration SQL file. This project uses manual migrations (not EF Core auto-migrations).

```sql
-- Create PhotoAssets table
CREATE TABLE IF NOT EXISTS dbo."PhotoAssets" (
    "Id" uuid NOT NULL,
    "OneDriveItemId" character varying(500) NOT NULL,
    "OneDrivePath" character varying(1000) NOT NULL,
    "FileName" character varying(500) NOT NULL,
    "MimeType" character varying(100) NOT NULL,
    "FileSize" bigint NOT NULL DEFAULT 0,
    "Width" integer NULL,
    "Height" integer NULL,
    "TakenAt" timestamp with time zone NULL,
    "IndexedAt" timestamp with time zone NULL,
    "ThumbnailBlobPath" character varying(500) NULL,
    "Embedding" vector(1024) NULL,
    "OcrText" text NULL,
    "Status" integer NOT NULL DEFAULT 0,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ModifiedAt" timestamp with time zone NULL,
    CONSTRAINT "PK_PhotoAssets" PRIMARY KEY ("Id")
);

-- Create PhotoTags table
CREATE TABLE IF NOT EXISTS dbo."PhotoTags" (
    "Id" uuid NOT NULL,
    "PhotoAssetId" uuid NOT NULL,
    "TagName" character varying(200) NOT NULL,
    "Confidence" real NOT NULL,
    "Source" integer NOT NULL DEFAULT 0,
    CONSTRAINT "PK_PhotoTags" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_PhotoTags_PhotoAssets_PhotoAssetId" FOREIGN KEY ("PhotoAssetId")
        REFERENCES dbo."PhotoAssets" ("Id") ON DELETE CASCADE
);

-- Indexes for PhotoAssets
CREATE UNIQUE INDEX IF NOT EXISTS "IX_PhotoAssets_OneDriveItemId"
    ON dbo."PhotoAssets" ("OneDriveItemId");

CREATE INDEX IF NOT EXISTS "IX_PhotoAssets_Status"
    ON dbo."PhotoAssets" ("Status");

-- pgvector IVFFlat index for future similarity search
-- NOTE: IVFFlat requires rows to exist for training. Create after initial data load.
-- For now, use exact nearest-neighbor (no index = sequential scan, fine for <100K rows)

-- Trigram index for OCR text search
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE INDEX IF NOT EXISTS "IX_PhotoAssets_OcrText_Trgm"
    ON dbo."PhotoAssets" USING gin ("OcrText" gin_trgm_ops);

-- Indexes for PhotoTags
CREATE INDEX IF NOT EXISTS "IX_PhotoTags_PhotoAssetId"
    ON dbo."PhotoTags" ("PhotoAssetId");

CREATE INDEX IF NOT EXISTS "IX_PhotoTags_TagName"
    ON dbo."PhotoTags" ("TagName");
```

- [ ] **Step 2: Run migration against local database**

Run the SQL script against your local PostgreSQL database. The pgvector extension is already enabled (used by KnowledgeBase).

- [ ] **Step 3: Commit**

```bash
git add backend/migrations/
git commit -m "feat(photo-bank): add database migration for PhotoAssets and PhotoTags"
```

---

## Task 4: PhotoAssetRepository

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/PhotoBank/PhotoAssetRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`

- [ ] **Step 1: Create PhotoAssetRepository**

Follow the pattern from `KnowledgeBaseRepository` — use raw SQL for embedding operations, EF Core for everything else.

```csharp
// backend/src/Anela.Heblo.Persistence/PhotoBank/PhotoAssetRepository.cs
using Anela.Heblo.Domain.Features.PhotoBank;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;

namespace Anela.Heblo.Persistence.PhotoBank;

public class PhotoAssetRepository : IPhotoAssetRepository
{
    private readonly ApplicationDbContext _context;

    public PhotoAssetRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PhotoAsset?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.PhotoAssets
            .Include(a => a.Tags)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<PhotoAsset?> GetByOneDriveItemIdAsync(string oneDriveItemId, CancellationToken ct = default)
    {
        return await _context.PhotoAssets
            .FirstOrDefaultAsync(a => a.OneDriveItemId == oneDriveItemId, ct);
    }

    public Task AddAsync(PhotoAsset asset, CancellationToken ct = default)
    {
        _context.PhotoAssets.Add(asset);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(PhotoAsset asset, CancellationToken ct = default)
    {
        _context.PhotoAssets.Update(asset);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpsertWithEmbeddingAsync(PhotoAsset asset, CancellationToken ct = default)
    {
        // Save the entity first (without embedding, which is ignored by EF)
        var existing = await _context.PhotoAssets
            .FirstOrDefaultAsync(a => a.Id == asset.Id, ct);

        if (existing == null)
        {
            _context.PhotoAssets.Add(asset);
        }
        else
        {
            _context.Entry(existing).CurrentValues.SetValues(asset);
        }

        await _context.SaveChangesAsync(ct);

        // Now update the embedding via raw SQL (same pattern as KnowledgeBaseRepository)
        if (asset.Embedding is { Length: > 0 })
        {
            var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync(ct);

            var vector = new Vector(asset.Embedding);
            await using var cmd = new NpgsqlCommand(
                """
                UPDATE dbo."PhotoAssets"
                SET "Embedding" = @embedding
                WHERE "Id" = @id
                """,
                connection);

            cmd.Parameters.AddWithValue("id", asset.Id);
            cmd.Parameters.AddWithValue("embedding", vector);

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
```

- [ ] **Step 2: Register repository in PersistenceModule**

Modify `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`. Add the using and registration following the existing pattern:

```csharp
// Add using at top:
using Anela.Heblo.Domain.Features.PhotoBank;
using Anela.Heblo.Persistence.PhotoBank;

// Add in the AddPersistenceServices method, alongside other repository registrations:
services.AddScoped<IPhotoAssetRepository, PhotoAssetRepository>();
```

- [ ] **Step 3: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/PhotoBank/PhotoAssetRepository.cs backend/src/Anela.Heblo.Persistence/PersistenceModule.cs
git commit -m "feat(photo-bank): add PhotoAssetRepository with pgvector embedding support"
```

---

## Task 5: Azure AI Vision Adapter — Interface

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/Services/IAzureAiVisionService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/Services/AiVisionAnalysisResult.cs`

- [ ] **Step 1: Create result DTOs**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/Services/AiVisionAnalysisResult.cs
namespace Anela.Heblo.Application.Features.PhotoBank.Services;

public class AiVisionAnalysisResult
{
    public List<AiVisionTag> Tags { get; set; } = new();
    public string? OcrText { get; set; }
    public string? Caption { get; set; }
}

public class AiVisionTag
{
    public string Name { get; set; } = string.Empty;
    public float Confidence { get; set; }
}
```

- [ ] **Step 2: Create IAzureAiVisionService interface**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/Services/IAzureAiVisionService.cs
namespace Anela.Heblo.Application.Features.PhotoBank.Services;

public interface IAzureAiVisionService
{
    /// <summary>
    /// Analyze image: extract tags, OCR text, and dense captions using Azure AI Vision 4.0
    /// </summary>
    Task<AiVisionAnalysisResult> AnalyzeImageAsync(byte[] imageData, string contentType, CancellationToken ct = default);

    /// <summary>
    /// Generate 1024-dim multimodal embedding for image using Azure AI Vision vectorizeImage
    /// </summary>
    Task<float[]> GetImageEmbeddingAsync(byte[] imageData, string contentType, CancellationToken ct = default);
}
```

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PhotoBank/Services/
git commit -m "feat(photo-bank): add IAzureAiVisionService interface and result DTOs"
```

---

## Task 6: Azure AI Vision Adapter — Implementation

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.AzureAiVision/Anela.Heblo.Adapters.AzureAiVision.csproj`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.AzureAiVision/AzureAiVisionOptions.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.AzureAiVision/AzureAiVisionService.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.AzureAiVision/MockAzureAiVisionService.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.AzureAiVision/AzureAiVisionModule.cs`

- [ ] **Step 1: Create the adapter project**

```bash
cd backend/src/Adapters
dotnet new classlib -n Anela.Heblo.Adapters.AzureAiVision -f net8.0
```

Remove the auto-generated `Class1.cs`. Add project reference to Application layer:

```bash
cd Anela.Heblo.Adapters.AzureAiVision
dotnet add reference ../../Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Add the adapter project to the solution and reference it from the API project:

```bash
cd /path/to/backend
dotnet sln add src/Adapters/Anela.Heblo.Adapters.AzureAiVision/Anela.Heblo.Adapters.AzureAiVision.csproj
dotnet add src/Anela.Heblo.API/Anela.Heblo.API.csproj reference src/Adapters/Anela.Heblo.Adapters.AzureAiVision/Anela.Heblo.Adapters.AzureAiVision.csproj
```

- [ ] **Step 2: Create options class**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.AzureAiVision/AzureAiVisionOptions.cs
namespace Anela.Heblo.Adapters.AzureAiVision;

public class AzureAiVisionOptions
{
    public const string SettingsKey = "PhotoBank";

    public string AzureAiVisionEndpoint { get; set; } = string.Empty;
    public string AzureAiVisionKey { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Create AzureAiVisionService implementation**

Uses `HttpClient` to call Azure AI Vision Image Analysis 4.0 REST API (tags + OCR + captions) and multimodal embeddings endpoint.

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.AzureAiVision/AzureAiVisionService.cs
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Application.Features.PhotoBank.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.AzureAiVision;

public class AzureAiVisionService : IAzureAiVisionService
{
    private readonly HttpClient _httpClient;
    private readonly AzureAiVisionOptions _options;
    private readonly ILogger<AzureAiVisionService> _logger;

    public AzureAiVisionService(
        IHttpClientFactory httpClientFactory,
        IOptions<AzureAiVisionOptions> options,
        ILogger<AzureAiVisionService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("AzureAiVision");
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiVisionAnalysisResult> AnalyzeImageAsync(
        byte[] imageData, string contentType, CancellationToken ct = default)
    {
        var url = $"{_options.AzureAiVisionEndpoint}/computervision/imageanalysis:analyze" +
                  "?api-version=2024-02-01&features=tags,read,denseCaptions";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _options.AzureAiVisionKey);
        request.Content = new ByteArrayContent(imageData);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var result = new AiVisionAnalysisResult();

        // Parse tags
        if (root.TryGetProperty("tagsResult", out var tagsResult) &&
            tagsResult.TryGetProperty("values", out var tagsArray))
        {
            foreach (var tag in tagsArray.EnumerateArray())
            {
                result.Tags.Add(new AiVisionTag
                {
                    Name = tag.GetProperty("name").GetString() ?? string.Empty,
                    Confidence = tag.GetProperty("confidence").GetSingle()
                });
            }
        }

        // Parse OCR text
        if (root.TryGetProperty("readResult", out var readResult) &&
            readResult.TryGetProperty("blocks", out var blocks))
        {
            var textParts = new List<string>();
            foreach (var block in blocks.EnumerateArray())
            {
                if (block.TryGetProperty("lines", out var lines))
                {
                    foreach (var line in lines.EnumerateArray())
                    {
                        if (line.TryGetProperty("text", out var text))
                            textParts.Add(text.GetString() ?? string.Empty);
                    }
                }
            }
            result.OcrText = textParts.Count > 0 ? string.Join("\n", textParts) : null;
        }

        // Parse dense captions (take the top one)
        if (root.TryGetProperty("denseCaptionsResult", out var captionsResult) &&
            captionsResult.TryGetProperty("values", out var captionsArray))
        {
            var firstCaption = captionsArray.EnumerateArray().FirstOrDefault();
            if (firstCaption.ValueKind != JsonValueKind.Undefined &&
                firstCaption.TryGetProperty("text", out var captionText))
            {
                result.Caption = captionText.GetString();
            }
        }

        _logger.LogDebug("Analyzed image: {TagCount} tags, OCR: {HasOcr}, Caption: {HasCaption}",
            result.Tags.Count, result.OcrText != null, result.Caption != null);

        return result;
    }

    public async Task<float[]> GetImageEmbeddingAsync(
        byte[] imageData, string contentType, CancellationToken ct = default)
    {
        var url = $"{_options.AzureAiVisionEndpoint}/computervision/retrieval:vectorizeImage" +
                  "?api-version=2024-02-01&model-version=2023-04-15";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _options.AzureAiVisionKey);
        request.Content = new ByteArrayContent(imageData);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("vector", out var vectorArray))
        {
            return vectorArray.EnumerateArray()
                .Select(v => v.GetSingle())
                .ToArray();
        }

        throw new InvalidOperationException("Azure AI Vision did not return a vector embedding");
    }
}
```

- [ ] **Step 4: Create MockAzureAiVisionService**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.AzureAiVision/MockAzureAiVisionService.cs
using Anela.Heblo.Application.Features.PhotoBank.Services;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.AzureAiVision;

public class MockAzureAiVisionService : IAzureAiVisionService
{
    private readonly ILogger<MockAzureAiVisionService> _logger;

    public MockAzureAiVisionService(ILogger<MockAzureAiVisionService> logger)
    {
        _logger = logger;
    }

    public Task<AiVisionAnalysisResult> AnalyzeImageAsync(
        byte[] imageData, string contentType, CancellationToken ct = default)
    {
        _logger.LogInformation("MockAzureAiVisionService: AnalyzeImageAsync called ({Size} bytes)", imageData.Length);

        return Task.FromResult(new AiVisionAnalysisResult
        {
            Tags = new List<AiVisionTag>
            {
                new() { Name = "product", Confidence = 0.95f },
                new() { Name = "cosmetics", Confidence = 0.90f },
                new() { Name = "bottle", Confidence = 0.85f }
            },
            OcrText = "Mock OCR text — Bisabolol Serum 30ml",
            Caption = "A cosmetics product bottle on a white background"
        });
    }

    public Task<float[]> GetImageEmbeddingAsync(
        byte[] imageData, string contentType, CancellationToken ct = default)
    {
        _logger.LogInformation("MockAzureAiVisionService: GetImageEmbeddingAsync called ({Size} bytes)", imageData.Length);

        // Return a deterministic 1024-dim mock embedding
        var embedding = new float[1024];
        var random = new Random(imageData.Length); // deterministic seed
        for (int i = 0; i < 1024; i++)
            embedding[i] = (float)(random.NextDouble() * 2 - 1);

        return Task.FromResult(embedding);
    }
}
```

- [ ] **Step 5: Create AzureAiVisionModule**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.AzureAiVision/AzureAiVisionModule.cs
using Anela.Heblo.Application.Features.PhotoBank.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.AzureAiVision;

public static class AzureAiVisionModule
{
    public static IServiceCollection AddAzureAiVisionAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AzureAiVisionOptions>(configuration.GetSection(AzureAiVisionOptions.SettingsKey));

        var endpoint = configuration[$"{AzureAiVisionOptions.SettingsKey}:AzureAiVisionEndpoint"];
        var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);

        if (!string.IsNullOrWhiteSpace(endpoint) && !useMockAuth)
        {
            services.AddHttpClient("AzureAiVision");
            services.AddScoped<IAzureAiVisionService, AzureAiVisionService>();
        }
        else
        {
            services.AddScoped<IAzureAiVisionService, MockAzureAiVisionService>();
        }

        return services;
    }
}
```

- [ ] **Step 6: Verify build**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.AzureAiVision/`
Expected: Build succeeded

- [ ] **Step 7: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.AzureAiVision/ backend/Anela.Heblo.sln backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
git commit -m "feat(photo-bank): add Azure AI Vision adapter with mock for development"
```

---

## Task 7: PhotoBank Configuration and Module

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/PhotoBankOptions.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/PhotoBankModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`
- Modify: `backend/appsettings.json` (add PhotoBank section)

- [ ] **Step 1: Create PhotoBankOptions**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/PhotoBankOptions.cs
namespace Anela.Heblo.Application.Features.PhotoBank;

public class PhotoBankOptions
{
    public const string SettingsKey = "PhotoBank";

    public string[] OneDriveFolderIds { get; set; } = Array.Empty<string>();
    public string DriveId { get; set; } = string.Empty;
    public string SyncCronExpression { get; set; } = "*/15 * * * *";
    public float MinConfidenceThreshold { get; set; } = 0.7f;
    public int ThumbnailMaxWidth { get; set; } = 400;
    public int ThumbnailMaxHeight { get; set; } = 400;
    public string ThumbnailContainerName { get; set; } = "photo-thumbnails";
}
```

- [ ] **Step 2: Create PhotoBankModule**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/PhotoBankModule.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.PhotoBank;

public static class PhotoBankModule
{
    public static IServiceCollection AddPhotoBankModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PhotoBankOptions>(configuration.GetSection(PhotoBankOptions.SettingsKey));

        // IPhotoAssetRepository is registered in PersistenceModule
        // IAzureAiVisionService is registered in AzureAiVisionModule
        // Hangfire jobs are auto-discovered via IRecurringJob

        return services;
    }
}
```

- [ ] **Step 3: Register PhotoBankModule in ApplicationModule**

Modify `backend/src/Anela.Heblo.Application/ApplicationModule.cs`. Add the call alongside other feature modules:

```csharp
using Anela.Heblo.Application.Features.PhotoBank;

// In AddApplicationServices method:
services.AddPhotoBankModule(configuration);
```

- [ ] **Step 4: Register AzureAiVisionAdapter in Program.cs or API startup**

Find where other adapter modules are registered (e.g., `AddShoptetPlaywrightAdapter`, `AddAzurePrintQueueSink`) and add:

```csharp
using Anela.Heblo.Adapters.AzureAiVision;

// Alongside other adapter registrations:
services.AddAzureAiVisionAdapter(configuration);
```

- [ ] **Step 5: Add PhotoBank config section to appsettings.json**

```json
{
  "PhotoBank": {
    "OneDriveFolderIds": [],
    "DriveId": "",
    "SyncCronExpression": "*/15 * * * *",
    "MinConfidenceThreshold": 0.7,
    "ThumbnailMaxWidth": 400,
    "ThumbnailMaxHeight": 400,
    "ThumbnailContainerName": "photo-thumbnails",
    "AzureAiVisionEndpoint": "",
    "AzureAiVisionKey": ""
  }
}
```

- [ ] **Step 6: Verify full solution build**

Run: `dotnet build backend/`
Expected: Build succeeded

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PhotoBank/ backend/src/Anela.Heblo.Application/ApplicationModule.cs backend/appsettings.json
git commit -m "feat(photo-bank): add PhotoBankModule, options, and configuration"
```

---

## Task 8: OneDrive Photo Service Interface

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/Services/IPhotoOneDriveService.cs`

Rather than extending the existing `IOneDriveService` (which is KnowledgeBase-specific), create a separate interface for Photo Bank's OneDrive needs. This follows the Interface Segregation Principle.

- [ ] **Step 1: Create IPhotoOneDriveService**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/Services/IPhotoOneDriveService.cs
namespace Anela.Heblo.Application.Features.PhotoBank.Services;

public record OneDrivePhotoFile(string ItemId, string Name, string ContentType, string Path, long Size);

public record OneDriveDeltaResult(List<OneDrivePhotoFile> NewOrModified, List<string> DeletedItemIds, string? DeltaToken);

public interface IPhotoOneDriveService
{
    /// <summary>
    /// Get new/modified/deleted photos since the last delta token.
    /// Pass null deltaToken for initial full sync.
    /// </summary>
    Task<OneDriveDeltaResult> GetPhotosDeltaAsync(
        string driveId, string folderId, string? deltaToken, CancellationToken ct = default);

    /// <summary>
    /// Download photo content by item ID.
    /// </summary>
    Task<byte[]> DownloadPhotoAsync(string driveId, string itemId, CancellationToken ct = default);

    /// <summary>
    /// Get a web URL for opening the photo in OneDrive browser.
    /// </summary>
    Task<string?> GetWebUrlAsync(string driveId, string itemId, CancellationToken ct = default);
}
```

- [ ] **Step 2: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PhotoBank/Services/IPhotoOneDriveService.cs
git commit -m "feat(photo-bank): add IPhotoOneDriveService interface with delta sync support"
```

---

## Task 9: OneDrive Photo Service Implementation

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/Services/GraphPhotoOneDriveService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/Services/MockPhotoOneDriveService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/PhotoBank/PhotoBankModule.cs`

- [ ] **Step 1: Create GraphPhotoOneDriveService**

Follow the pattern from `GraphOneDriveService` — raw `HttpClient` + `ITokenAcquisition`.

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/Services/GraphPhotoOneDriveService.cs
using System.Text.Json;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;

namespace Anela.Heblo.Application.Features.PhotoBank.Services;

public class GraphPhotoOneDriveService : IPhotoOneDriveService
{
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GraphPhotoOneDriveService> _logger;

    private static readonly HashSet<string> ImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/heic", "image/heif",
        "image/gif", "image/bmp", "image/tiff"
    };

    public GraphPhotoOneDriveService(
        ITokenAcquisition tokenAcquisition,
        IHttpClientFactory httpClientFactory,
        ILogger<GraphPhotoOneDriveService> logger)
    {
        _tokenAcquisition = tokenAcquisition;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<OneDriveDeltaResult> GetPhotosDeltaAsync(
        string driveId, string folderId, string? deltaToken, CancellationToken ct = default)
    {
        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(
            "https://graph.microsoft.com/.default", cancellationToken: ct);

        using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

        var url = string.IsNullOrEmpty(deltaToken)
            ? $"https://graph.microsoft.com/v1.0/drives/{driveId}/items/{folderId}/delta"
            : deltaToken;

        var newOrModified = new List<OneDrivePhotoFile>();
        var deletedItemIds = new List<string>();
        string? nextDeltaToken = null;

        while (url != null)
        {
            var request = GraphApiHelpers.CreateRequest(HttpMethod.Get, url, token);
            var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("value", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    var itemId = item.GetProperty("id").GetString() ?? string.Empty;

                    // Check for deletion
                    if (item.TryGetProperty("deleted", out _))
                    {
                        deletedItemIds.Add(itemId);
                        continue;
                    }

                    // Skip folders
                    if (item.TryGetProperty("folder", out _))
                        continue;

                    // Check if it's an image by MIME type or file extension
                    var mimeType = item.TryGetProperty("file", out var fileProp) &&
                                   fileProp.TryGetProperty("mimeType", out var mime)
                        ? mime.GetString() ?? string.Empty
                        : string.Empty;

                    if (!ImageMimeTypes.Contains(mimeType))
                        continue;

                    var name = item.GetProperty("name").GetString() ?? string.Empty;
                    var path = item.TryGetProperty("parentReference", out var parentRef) &&
                               parentRef.TryGetProperty("path", out var pathProp)
                        ? $"{pathProp.GetString()}/{name}"
                        : name;
                    var size = item.TryGetProperty("size", out var sizeProp)
                        ? sizeProp.GetInt64()
                        : 0L;

                    newOrModified.Add(new OneDrivePhotoFile(itemId, name, mimeType, path, size));
                }
            }

            // Get next page or delta link
            if (root.TryGetProperty("@odata.nextLink", out var nextLink))
            {
                url = nextLink.GetString();
            }
            else
            {
                url = null;
                if (root.TryGetProperty("@odata.deltaLink", out var deltaLink))
                {
                    nextDeltaToken = deltaLink.GetString();
                }
            }
        }

        _logger.LogInformation("Delta sync: {NewCount} new/modified, {DeletedCount} deleted",
            newOrModified.Count, deletedItemIds.Count);

        return new OneDriveDeltaResult(newOrModified, deletedItemIds, nextDeltaToken);
    }

    public async Task<byte[]> DownloadPhotoAsync(string driveId, string itemId, CancellationToken ct = default)
    {
        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(
            "https://graph.microsoft.com/.default", cancellationToken: ct);

        using var client = _httpClientFactory.CreateClient("MicrosoftGraph");
        var url = $"https://graph.microsoft.com/v1.0/drives/{driveId}/items/{itemId}/content";
        var request = GraphApiHelpers.CreateRequest(HttpMethod.Get, url, token);
        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<string?> GetWebUrlAsync(string driveId, string itemId, CancellationToken ct = default)
    {
        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(
            "https://graph.microsoft.com/.default", cancellationToken: ct);

        using var client = _httpClientFactory.CreateClient("MicrosoftGraph");
        var url = $"https://graph.microsoft.com/v1.0/drives/{driveId}/items/{itemId}?$select=webUrl";
        var request = GraphApiHelpers.CreateRequest(HttpMethod.Get, url, token);
        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("webUrl", out var webUrl)
            ? webUrl.GetString()
            : null;
    }
}
```

- [ ] **Step 2: Create MockPhotoOneDriveService**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/Services/MockPhotoOneDriveService.cs
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PhotoBank.Services;

public class MockPhotoOneDriveService : IPhotoOneDriveService
{
    private readonly ILogger<MockPhotoOneDriveService> _logger;

    public MockPhotoOneDriveService(ILogger<MockPhotoOneDriveService> logger)
    {
        _logger = logger;
    }

    public Task<OneDriveDeltaResult> GetPhotosDeltaAsync(
        string driveId, string folderId, string? deltaToken, CancellationToken ct = default)
    {
        _logger.LogInformation("MockPhotoOneDriveService: GetPhotosDeltaAsync called");
        return Task.FromResult(new OneDriveDeltaResult(new List<OneDrivePhotoFile>(), new List<string>(), "mock-delta-token"));
    }

    public Task<byte[]> DownloadPhotoAsync(string driveId, string itemId, CancellationToken ct = default)
    {
        _logger.LogInformation("MockPhotoOneDriveService: DownloadPhotoAsync called");
        return Task.FromResult(Array.Empty<byte>());
    }

    public Task<string?> GetWebUrlAsync(string driveId, string itemId, CancellationToken ct = default)
    {
        return Task.FromResult<string?>("https://onedrive.live.com/mock");
    }
}
```

- [ ] **Step 3: Register in PhotoBankModule**

Update `PhotoBankModule.cs` to register the OneDrive service (same conditional pattern as KnowledgeBaseModule):

```csharp
// In PhotoBankModule.AddPhotoBankModule:
var pbOptions = new PhotoBankOptions();
configuration.GetSection(PhotoBankOptions.SettingsKey).Bind(pbOptions);
var driveConfigured = !string.IsNullOrWhiteSpace(pbOptions.DriveId);
var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);

if (driveConfigured && !useMockAuth)
{
    services.AddScoped<IPhotoOneDriveService, GraphPhotoOneDriveService>();
}
else
{
    services.AddScoped<IPhotoOneDriveService, MockPhotoOneDriveService>();
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build backend/`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PhotoBank/
git commit -m "feat(photo-bank): add GraphPhotoOneDriveService with delta sync"
```

---

## Task 10: Sync OneDrive Photos Job

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/Jobs/SyncOneDrivePhotosJob.cs`

- [ ] **Step 1: Create SyncOneDrivePhotosJob**

Follow the `KnowledgeBaseIngestionJob` pattern exactly — implements `IRecurringJob`.

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/Jobs/SyncOneDrivePhotosJob.cs
using Anela.Heblo.Application.Features.PhotoBank.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.PhotoBank;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.PhotoBank.Jobs;

public class SyncOneDrivePhotosJob : IRecurringJob
{
    private readonly IPhotoOneDriveService _oneDrive;
    private readonly IPhotoAssetRepository _repository;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly PhotoBankOptions _options;
    private readonly ILogger<SyncOneDrivePhotosJob> _logger;

    // In-memory delta token storage (persists across job runs within app lifetime).
    // For production, consider storing in DB.
    private static readonly Dictionary<string, string?> DeltaTokens = new();

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "photo-bank-sync",
        DisplayName = "Photo Bank OneDrive Sync",
        Description = "Polls OneDrive folders for new/modified/deleted photos and enqueues indexing jobs",
        CronExpression = "*/15 * * * *",
        DefaultIsEnabled = true
    };

    public SyncOneDrivePhotosJob(
        IPhotoOneDriveService oneDrive,
        IPhotoAssetRepository repository,
        IRecurringJobStatusChecker statusChecker,
        IOptions<PhotoBankOptions> options,
        ILogger<SyncOneDrivePhotosJob> logger)
    {
        _oneDrive = oneDrive;
        _repository = repository;
        _statusChecker = statusChecker;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        _logger.LogInformation("Starting {JobName}", Metadata.JobName);

        int enqueued = 0;
        int deleted = 0;

        foreach (var folderId in _options.OneDriveFolderIds)
        {
            var tokenKey = $"{_options.DriveId}:{folderId}";
            DeltaTokens.TryGetValue(tokenKey, out var deltaToken);

            var result = await _oneDrive.GetPhotosDeltaAsync(
                _options.DriveId, folderId, deltaToken, cancellationToken);

            // Handle new/modified photos
            foreach (var photo in result.NewOrModified)
            {
                var existing = await _repository.GetByOneDriveItemIdAsync(photo.ItemId, cancellationToken);
                if (existing == null)
                {
                    // New photo — create pending record and enqueue indexing
                    var asset = new PhotoAsset
                    {
                        Id = Guid.NewGuid(),
                        OneDriveItemId = photo.ItemId,
                        OneDrivePath = photo.Path,
                        FileName = photo.Name,
                        MimeType = photo.ContentType,
                        FileSize = photo.Size,
                        Status = PhotoAssetStatus.Pending,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    await _repository.AddAsync(asset, cancellationToken);
                    await _repository.SaveChangesAsync(cancellationToken);

                    BackgroundJob.Enqueue<IndexPhotoJob>(job =>
                        job.ExecuteAsync(asset.Id, CancellationToken.None));

                    enqueued++;
                }
                else if (existing.Status != PhotoAssetStatus.Pending)
                {
                    // Modified — re-enqueue for re-indexing
                    existing.Status = PhotoAssetStatus.Pending;
                    existing.ModifiedAt = DateTimeOffset.UtcNow;
                    await _repository.UpdateAsync(existing, cancellationToken);
                    await _repository.SaveChangesAsync(cancellationToken);

                    BackgroundJob.Enqueue<IndexPhotoJob>(job =>
                        job.ExecuteAsync(existing.Id, CancellationToken.None));

                    enqueued++;
                }
            }

            // Handle deletions (soft-delete)
            foreach (var deletedItemId in result.DeletedItemIds)
            {
                var existing = await _repository.GetByOneDriveItemIdAsync(deletedItemId, cancellationToken);
                if (existing != null && existing.Status != PhotoAssetStatus.Deleted)
                {
                    existing.Status = PhotoAssetStatus.Deleted;
                    existing.ModifiedAt = DateTimeOffset.UtcNow;
                    await _repository.UpdateAsync(existing, cancellationToken);
                    await _repository.SaveChangesAsync(cancellationToken);
                    deleted++;
                }
            }

            // Store delta token for next run
            if (result.DeltaToken != null)
            {
                DeltaTokens[tokenKey] = result.DeltaToken;
            }
        }

        _logger.LogInformation("{JobName} complete. Enqueued: {Enqueued}, Deleted: {Deleted}",
            Metadata.JobName, enqueued, deleted);
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.Application/`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PhotoBank/Jobs/SyncOneDrivePhotosJob.cs
git commit -m "feat(photo-bank): add SyncOneDrivePhotosJob with delta sync"
```

---

## Task 11: Index Photo Job

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/Jobs/IndexPhotoJob.cs`

- [ ] **Step 1: Create IndexPhotoJob**

This is a Hangfire background job (not a recurring job). It processes a single photo.

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/Jobs/IndexPhotoJob.cs
using Anela.Heblo.Application.Features.PhotoBank.Services;
using Anela.Heblo.Domain.Features.PhotoBank;
using Anela.Heblo.Xcc.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Anela.Heblo.Application.Features.PhotoBank.Jobs;

public class IndexPhotoJob
{
    private readonly IPhotoAssetRepository _repository;
    private readonly IPhotoOneDriveService _oneDrive;
    private readonly IAzureAiVisionService _aiVision;
    private readonly IBlobStorageService _blobStorage;
    private readonly PhotoBankOptions _options;
    private readonly ILogger<IndexPhotoJob> _logger;

    public IndexPhotoJob(
        IPhotoAssetRepository repository,
        IPhotoOneDriveService oneDrive,
        IAzureAiVisionService aiVision,
        IBlobStorageService blobStorage,
        IOptions<PhotoBankOptions> options,
        ILogger<IndexPhotoJob> logger)
    {
        _repository = repository;
        _oneDrive = oneDrive;
        _aiVision = aiVision;
        _blobStorage = blobStorage;
        _options = options.Value;
        _logger = logger;
    }

    [Hangfire.AutomaticRetry(Attempts = 3)]
    public async Task ExecuteAsync(Guid photoAssetId, CancellationToken ct = default)
    {
        var asset = await _repository.GetByIdAsync(photoAssetId, ct);
        if (asset == null)
        {
            _logger.LogWarning("PhotoAsset {Id} not found, skipping indexing", photoAssetId);
            return;
        }

        _logger.LogInformation("Indexing photo {FileName} ({Id})", asset.FileName, asset.Id);

        try
        {
            // 1. Download image from OneDrive
            var imageData = await _oneDrive.DownloadPhotoAsync(
                _options.DriveId, asset.OneDriveItemId, ct);

            if (imageData.Length == 0)
            {
                _logger.LogWarning("Empty image data for {FileName}, marking as failed", asset.FileName);
                asset.Status = PhotoAssetStatus.Failed;
                await _repository.UpdateAsync(asset, ct);
                await _repository.SaveChangesAsync(ct);
                return;
            }

            // 2. Extract image dimensions
            try
            {
                using var image = Image.Load(imageData);
                asset.Width = image.Width;
                asset.Height = image.Height;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read image dimensions for {FileName}", asset.FileName);
            }

            // 3. Generate thumbnail and upload to blob storage
            var thumbnailData = GenerateThumbnail(imageData);
            var thumbnailPath = $"photo-thumbnails/{asset.Id}.jpg";
            await _blobStorage.UploadAsync(
                _options.ThumbnailContainerName, $"{asset.Id}.jpg", thumbnailData, "image/jpeg", ct);
            asset.ThumbnailBlobPath = thumbnailPath;

            // 4. Analyze image with Azure AI Vision (tags + OCR + captions)
            var analysis = await _aiVision.AnalyzeImageAsync(imageData, asset.MimeType, ct);

            // 5. Get image embedding
            var embedding = await _aiVision.GetImageEmbeddingAsync(imageData, asset.MimeType, ct);
            asset.Embedding = embedding;

            // 6. Store OCR text
            asset.OcrText = analysis.OcrText;

            // 7. Create tags (filter by confidence threshold)
            asset.Tags.Clear();
            foreach (var tag in analysis.Tags.Where(t => t.Confidence >= _options.MinConfidenceThreshold))
            {
                asset.Tags.Add(new PhotoTag
                {
                    Id = Guid.NewGuid(),
                    PhotoAssetId = asset.Id,
                    TagName = tag.Name.ToLowerInvariant(),
                    Confidence = tag.Confidence,
                    Source = TagSource.Auto
                });
            }

            // Add caption as a tag if available
            if (!string.IsNullOrEmpty(analysis.Caption))
            {
                asset.Tags.Add(new PhotoTag
                {
                    Id = Guid.NewGuid(),
                    PhotoAssetId = asset.Id,
                    TagName = $"caption:{analysis.Caption}",
                    Confidence = 1.0f,
                    Source = TagSource.Auto
                });
            }

            // 8. Mark as indexed
            asset.Status = PhotoAssetStatus.Indexed;
            asset.IndexedAt = DateTimeOffset.UtcNow;
            asset.ModifiedAt = DateTimeOffset.UtcNow;

            await _repository.UpsertWithEmbeddingAsync(asset, ct);

            _logger.LogInformation("Indexed {FileName}: {TagCount} tags, OCR: {HasOcr}",
                asset.FileName, asset.Tags.Count, asset.OcrText != null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index photo {FileName} ({Id})", asset.FileName, asset.Id);
            asset.Status = PhotoAssetStatus.Failed;
            asset.ModifiedAt = DateTimeOffset.UtcNow;
            await _repository.UpdateAsync(asset, ct);
            await _repository.SaveChangesAsync(ct);
            throw; // Re-throw so Hangfire retries
        }
    }

    private byte[] GenerateThumbnail(byte[] imageData)
    {
        using var image = Image.Load(imageData);

        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(_options.ThumbnailMaxWidth, _options.ThumbnailMaxHeight),
            Mode = ResizeMode.Max
        }));

        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }
}
```

**Note:** This uses `SixLabors.ImageSharp` for thumbnail generation. Add the NuGet package:

```bash
dotnet add backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj package SixLabors.ImageSharp
```

- [ ] **Step 2: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.Application/`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PhotoBank/Jobs/IndexPhotoJob.cs backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
git commit -m "feat(photo-bank): add IndexPhotoJob with AI Vision tagging, OCR, and thumbnail generation"
```

---

## Task 12: Register Hangfire Job

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PhotoBank/PhotoBankModule.cs`

The `RecurringJobDiscoveryService` auto-discovers all `IRecurringJob` implementations from DI. Just register `SyncOneDrivePhotosJob`.

- [ ] **Step 1: Register the recurring job in PhotoBankModule**

Add to the `AddPhotoBankModule` method:

```csharp
using Anela.Heblo.Application.Features.PhotoBank.Jobs;
using Anela.Heblo.Domain.Features.BackgroundJobs;

// In AddPhotoBankModule:
services.AddScoped<IRecurringJob, SyncOneDrivePhotosJob>();
services.AddScoped<IndexPhotoJob>();
```

- [ ] **Step 2: Verify build**

Run: `dotnet build backend/`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PhotoBank/PhotoBankModule.cs
git commit -m "feat(photo-bank): register Hangfire sync job and indexing job"
```

---

## Task 13: Unit Tests for IndexPhotoJob

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/PhotoBank/Jobs/IndexPhotoJobTests.cs`

- [ ] **Step 1: Write tests**

```csharp
// backend/test/Anela.Heblo.Tests/PhotoBank/Jobs/IndexPhotoJobTests.cs
using Anela.Heblo.Application.Features.PhotoBank;
using Anela.Heblo.Application.Features.PhotoBank.Jobs;
using Anela.Heblo.Application.Features.PhotoBank.Services;
using Anela.Heblo.Domain.Features.PhotoBank;
using Anela.Heblo.Xcc.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.PhotoBank.Jobs;

public class IndexPhotoJobTests
{
    private readonly Mock<IPhotoAssetRepository> _repository;
    private readonly Mock<IPhotoOneDriveService> _oneDrive;
    private readonly Mock<IAzureAiVisionService> _aiVision;
    private readonly Mock<IBlobStorageService> _blobStorage;
    private readonly IndexPhotoJob _job;

    public IndexPhotoJobTests()
    {
        _repository = new Mock<IPhotoAssetRepository>();
        _oneDrive = new Mock<IPhotoOneDriveService>();
        _aiVision = new Mock<IAzureAiVisionService>();
        _blobStorage = new Mock<IBlobStorageService>();

        var options = Options.Create(new PhotoBankOptions
        {
            DriveId = "test-drive",
            MinConfidenceThreshold = 0.7f,
            ThumbnailMaxWidth = 400,
            ThumbnailMaxHeight = 400,
            ThumbnailContainerName = "photo-thumbnails"
        });

        _job = new IndexPhotoJob(
            _repository.Object,
            _oneDrive.Object,
            _aiVision.Object,
            _blobStorage.Object,
            options,
            Mock.Of<ILogger<IndexPhotoJob>>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenPhotoNotFound_SkipsProcessing()
    {
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((PhotoAsset?)null);

        await _job.ExecuteAsync(Guid.NewGuid());

        _oneDrive.Verify(o => o.DownloadPhotoAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEmptyImage_MarksAsFailed()
    {
        var assetId = Guid.NewGuid();
        var asset = new PhotoAsset
        {
            Id = assetId,
            OneDriveItemId = "item-1",
            FileName = "test.jpg",
            MimeType = "image/jpeg",
            Status = PhotoAssetStatus.Pending
        };

        _repository.Setup(r => r.GetByIdAsync(assetId, default)).ReturnsAsync(asset);
        _oneDrive.Setup(o => o.DownloadPhotoAsync("test-drive", "item-1", default))
            .ReturnsAsync(Array.Empty<byte>());

        await _job.ExecuteAsync(assetId);

        asset.Status.Should().Be(PhotoAssetStatus.Failed);
        _repository.Verify(r => r.UpdateAsync(asset, default), Times.Once);
        _repository.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_FiltersTagsBelowConfidenceThreshold()
    {
        var assetId = Guid.NewGuid();
        var asset = new PhotoAsset
        {
            Id = assetId,
            OneDriveItemId = "item-1",
            FileName = "test.jpg",
            MimeType = "image/jpeg",
            Status = PhotoAssetStatus.Pending
        };

        // Create a minimal valid JPEG (1x1 pixel)
        var testImage = CreateMinimalJpeg();

        _repository.Setup(r => r.GetByIdAsync(assetId, default)).ReturnsAsync(asset);
        _oneDrive.Setup(o => o.DownloadPhotoAsync("test-drive", "item-1", default))
            .ReturnsAsync(testImage);

        _aiVision.Setup(a => a.AnalyzeImageAsync(testImage, "image/jpeg", default))
            .ReturnsAsync(new AiVisionAnalysisResult
            {
                Tags = new List<AiVisionTag>
                {
                    new() { Name = "product", Confidence = 0.95f },     // above 0.7 threshold
                    new() { Name = "blurry", Confidence = 0.3f },       // below threshold
                    new() { Name = "cosmetics", Confidence = 0.85f }    // above threshold
                },
                OcrText = "Bisabolol Serum"
            });

        _aiVision.Setup(a => a.GetImageEmbeddingAsync(testImage, "image/jpeg", default))
            .ReturnsAsync(new float[1024]);

        await _job.ExecuteAsync(assetId);

        // Should have 2 auto tags (above threshold) — "blurry" excluded
        var autoTags = asset.Tags.Where(t => t.Source == TagSource.Auto && !t.TagName.StartsWith("caption:")).ToList();
        autoTags.Should().HaveCount(2);
        autoTags.Select(t => t.TagName).Should().Contain("product");
        autoTags.Select(t => t.TagName).Should().Contain("cosmetics");
        autoTags.Select(t => t.TagName).Should().NotContain("blurry");
    }

    [Fact]
    public async Task ExecuteAsync_StoresOcrText()
    {
        var assetId = Guid.NewGuid();
        var asset = new PhotoAsset
        {
            Id = assetId,
            OneDriveItemId = "item-1",
            FileName = "test.jpg",
            MimeType = "image/jpeg",
            Status = PhotoAssetStatus.Pending
        };

        var testImage = CreateMinimalJpeg();

        _repository.Setup(r => r.GetByIdAsync(assetId, default)).ReturnsAsync(asset);
        _oneDrive.Setup(o => o.DownloadPhotoAsync("test-drive", "item-1", default))
            .ReturnsAsync(testImage);

        _aiVision.Setup(a => a.AnalyzeImageAsync(testImage, "image/jpeg", default))
            .ReturnsAsync(new AiVisionAnalysisResult
            {
                Tags = new List<AiVisionTag>(),
                OcrText = "Bisabolol Serum 30ml"
            });

        _aiVision.Setup(a => a.GetImageEmbeddingAsync(testImage, "image/jpeg", default))
            .ReturnsAsync(new float[1024]);

        await _job.ExecuteAsync(assetId);

        asset.OcrText.Should().Be("Bisabolol Serum 30ml");
        asset.Status.Should().Be(PhotoAssetStatus.Indexed);
    }

    private static byte[] CreateMinimalJpeg()
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1, 1);
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~IndexPhotoJobTests" -v n`
Expected: All 4 tests pass

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/PhotoBank/
git commit -m "test(photo-bank): add IndexPhotoJob unit tests"
```

---

## Task 14: Unit Tests for SyncOneDrivePhotosJob

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/PhotoBank/Jobs/SyncOneDrivePhotosJobTests.cs`

- [ ] **Step 1: Write tests**

```csharp
// backend/test/Anela.Heblo.Tests/PhotoBank/Jobs/SyncOneDrivePhotosJobTests.cs
using Anela.Heblo.Application.Features.PhotoBank;
using Anela.Heblo.Application.Features.PhotoBank.Jobs;
using Anela.Heblo.Application.Features.PhotoBank.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.PhotoBank;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.PhotoBank.Jobs;

public class SyncOneDrivePhotosJobTests
{
    private readonly Mock<IPhotoOneDriveService> _oneDrive;
    private readonly Mock<IPhotoAssetRepository> _repository;
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker;
    private readonly SyncOneDrivePhotosJob _job;

    public SyncOneDrivePhotosJobTests()
    {
        _oneDrive = new Mock<IPhotoOneDriveService>();
        _repository = new Mock<IPhotoAssetRepository>();
        _statusChecker = new Mock<IRecurringJobStatusChecker>();

        _statusChecker.Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), default))
            .ReturnsAsync(true);

        var options = Options.Create(new PhotoBankOptions
        {
            DriveId = "test-drive",
            OneDriveFolderIds = new[] { "folder-1" }
        });

        _job = new SyncOneDrivePhotosJob(
            _oneDrive.Object,
            _repository.Object,
            _statusChecker.Object,
            options,
            Mock.Of<ILogger<SyncOneDrivePhotosJob>>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_SkipsExecution()
    {
        _statusChecker.Setup(s => s.IsJobEnabledAsync("photo-bank-sync", default))
            .ReturnsAsync(false);

        await _job.ExecuteAsync();

        _oneDrive.Verify(o => o.GetPhotosDeltaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), default), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNewPhotosFound_CreatesAssetsAndEnqueuesIndexing()
    {
        var deltaResult = new OneDriveDeltaResult(
            new List<OneDrivePhotoFile>
            {
                new("item-1", "photo1.jpg", "image/jpeg", "/photos/photo1.jpg", 1024)
            },
            new List<string>(),
            "delta-token-1");

        _oneDrive.Setup(o => o.GetPhotosDeltaAsync("test-drive", "folder-1", null, default))
            .ReturnsAsync(deltaResult);

        _repository.Setup(r => r.GetByOneDriveItemIdAsync("item-1", default))
            .ReturnsAsync((PhotoAsset?)null);

        await _job.ExecuteAsync();

        _repository.Verify(r => r.AddAsync(It.Is<PhotoAsset>(a =>
            a.OneDriveItemId == "item-1" &&
            a.FileName == "photo1.jpg" &&
            a.Status == PhotoAssetStatus.Pending), default), Times.Once);
        _repository.Verify(r => r.SaveChangesAsync(default), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDeletedPhotos_SoftDeletes()
    {
        var existing = new PhotoAsset
        {
            Id = Guid.NewGuid(),
            OneDriveItemId = "item-2",
            Status = PhotoAssetStatus.Indexed
        };

        var deltaResult = new OneDriveDeltaResult(
            new List<OneDrivePhotoFile>(),
            new List<string> { "item-2" },
            "delta-token-2");

        _oneDrive.Setup(o => o.GetPhotosDeltaAsync("test-drive", "folder-1", It.IsAny<string?>(), default))
            .ReturnsAsync(deltaResult);

        _repository.Setup(r => r.GetByOneDriveItemIdAsync("item-2", default))
            .ReturnsAsync(existing);

        await _job.ExecuteAsync();

        existing.Status.Should().Be(PhotoAssetStatus.Deleted);
        _repository.Verify(r => r.UpdateAsync(existing, default), Times.Once);
    }

    [Fact]
    public void Metadata_HasCorrectJobName()
    {
        _job.Metadata.JobName.Should().Be("photo-bank-sync");
        _job.Metadata.CronExpression.Should().Be("*/15 * * * *");
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~SyncOneDrivePhotosJobTests" -v n`
Expected: All 4 tests pass

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/PhotoBank/Jobs/SyncOneDrivePhotosJobTests.cs
git commit -m "test(photo-bank): add SyncOneDrivePhotosJob unit tests"
```

---

## Task 15: Final Build Validation and Format Check

- [ ] **Step 1: Run full build**

```bash
dotnet build backend/
```
Expected: Build succeeded

- [ ] **Step 2: Run format check**

```bash
dotnet format backend/ --verify-no-changes
```
Expected: No formatting issues (fix any that appear with `dotnet format backend/`)

- [ ] **Step 3: Run all tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ -v n
```
Expected: All tests pass (including new PhotoBank tests)

- [ ] **Step 4: Commit any formatting fixes**

```bash
git add -A
git commit -m "style(photo-bank): apply dotnet format"
```

---

## Verification Checklist

- [ ] Domain entities: `PhotoAsset`, `PhotoTag`, `TagSource`, `PhotoAssetStatus` in `Domain/Features/PhotoBank/`
- [ ] EF Core configs: `PhotoAssetConfiguration`, `PhotoTagConfiguration` in `Persistence/PhotoBank/`
- [ ] Database migration script creates `PhotoAssets` and `PhotoTags` tables with indexes
- [ ] Repository: `PhotoAssetRepository` handles CRUD + pgvector embedding via raw SQL
- [ ] Azure AI Vision adapter: `AzureAiVisionService` + `MockAzureAiVisionService` in new adapter project
- [ ] OneDrive service: `GraphPhotoOneDriveService` + `MockPhotoOneDriveService` with delta sync
- [ ] Sync job: `SyncOneDrivePhotosJob` (Hangfire recurring) polls OneDrive and enqueues indexing
- [ ] Index job: `IndexPhotoJob` downloads, thumbnails, AI tags, OCR, embeddings
- [ ] Configuration: `PhotoBankOptions` bound from `appsettings.json` `PhotoBank` section
- [ ] Module registration: `PhotoBankModule` + `AzureAiVisionModule` wired into DI
- [ ] Tests: `IndexPhotoJobTests` (4 tests), `SyncOneDrivePhotosJobTests` (4 tests)
- [ ] `dotnet build` passes
- [ ] `dotnet format` passes
- [ ] `dotnet test` passes
