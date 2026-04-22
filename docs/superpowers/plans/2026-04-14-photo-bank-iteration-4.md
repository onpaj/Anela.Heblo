# Photo Bank: Iteration 4 — Semantic & Visual Similarity Search

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable natural language photo search and "find similar" using vector embeddings stored since Iteration 1.

**Architecture:** Azure AI Vision text vectorization for query embedding, pgvector cosine similarity search in PostgreSQL, React frontend search mode toggle.

**Tech Stack:** .NET 8, pgvector, Azure AI Vision vectorizeText API, React 18, TypeScript

**GitHub Issue:** #614

---

## Task 1: Add VectorizeTextAsync to IAzureAiVisionService

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PhotoBank/Services/IAzureAiVisionService.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.AzureAiVision/AzureAiVisionService.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.AzureAiVision/MockAzureAiVisionService.cs`

- [ ] **Step 1: Add VectorizeTextAsync to interface**

Add the new method to the existing `IAzureAiVisionService` interface:

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

    /// <summary>
    /// Generate 1024-dim multimodal embedding for text query using Azure AI Vision vectorizeText.
    /// The resulting vector is in the same embedding space as image embeddings,
    /// enabling cross-modal similarity search.
    /// </summary>
    Task<float[]> VectorizeTextAsync(string text, CancellationToken ct = default);
}
```

- [ ] **Step 2: Implement VectorizeTextAsync in AzureAiVisionService**

Add the method to the real implementation. Uses the same Azure AI Vision multimodal embeddings endpoint but with text input instead of image.

```csharp
// Add to: backend/src/Adapters/Anela.Heblo.Adapters.AzureAiVision/AzureAiVisionService.cs
// Add this method to the existing AzureAiVisionService class:

public async Task<float[]> VectorizeTextAsync(string text, CancellationToken ct = default)
{
    var url = $"{_options.AzureAiVisionEndpoint}/computervision/retrieval:vectorizeText" +
              "?api-version=2024-02-01&model-version=2023-04-15";

    var requestBody = new { text };
    var json = JsonSerializer.Serialize(requestBody);

    using var request = new HttpRequestMessage(HttpMethod.Post, url);
    request.Headers.Add("Ocp-Apim-Subscription-Key", _options.AzureAiVisionKey);
    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

    var response = await _httpClient.SendAsync(request, ct);
    response.EnsureSuccessStatusCode();

    var responseJson = await response.Content.ReadAsStringAsync(ct);
    var doc = JsonDocument.Parse(responseJson);

    if (doc.RootElement.TryGetProperty("vector", out var vectorArray))
    {
        return vectorArray.EnumerateArray()
            .Select(v => v.GetSingle())
            .ToArray();
    }

    throw new InvalidOperationException("Azure AI Vision did not return a vector embedding for text");
}
```

- [ ] **Step 3: Implement VectorizeTextAsync in MockAzureAiVisionService**

```csharp
// Add to: backend/src/Adapters/Anela.Heblo.Adapters.AzureAiVision/MockAzureAiVisionService.cs
// Add this method to the existing MockAzureAiVisionService class:

public Task<float[]> VectorizeTextAsync(string text, CancellationToken ct = default)
{
    _logger.LogInformation("MockAzureAiVisionService: VectorizeTextAsync called for '{Text}'", text);

    // Return a deterministic 1024-dim mock embedding based on text hash
    var embedding = new float[1024];
    var random = new Random(text.GetHashCode());
    for (int i = 0; i < 1024; i++)
        embedding[i] = (float)(random.NextDouble() * 2 - 1);

    return Task.FromResult(embedding);
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.AzureAiVision/`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PhotoBank/Services/IAzureAiVisionService.cs backend/src/Adapters/Anela.Heblo.Adapters.AzureAiVision/AzureAiVisionService.cs backend/src/Adapters/Anela.Heblo.Adapters.AzureAiVision/MockAzureAiVisionService.cs
git commit -m "feat(photo-bank): add VectorizeTextAsync to IAzureAiVisionService for semantic search"
```

---

## Task 2: Add SearchSimilarAsync to IPhotoAssetRepository

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/PhotoBank/IPhotoAssetRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/PhotoBank/PhotoAssetRepository.cs`

- [ ] **Step 1: Add SearchSimilarAsync and FindSimilarByIdAsync to interface**

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

    /// <summary>
    /// Search photos by cosine similarity against a query embedding.
    /// Returns photos ranked by similarity score (1 = identical, 0 = orthogonal).
    /// Only returns photos with Status = Indexed.
    /// </summary>
    Task<List<(PhotoAsset Photo, double Score)>> SearchSimilarAsync(
        float[] queryEmbedding, int topK, CancellationToken ct = default);

    /// <summary>
    /// Find photos visually similar to a given photo by its stored embedding.
    /// Excludes the source photo from results.
    /// </summary>
    Task<List<(PhotoAsset Photo, double Score)>> FindSimilarByIdAsync(
        Guid photoId, int topK, CancellationToken ct = default);
}
```

- [ ] **Step 2: Implement SearchSimilarAsync in PhotoAssetRepository**

Follow the exact pattern from `KnowledgeBaseRepository.SearchSimilarAsync` — raw SQL with pgvector cosine distance operator.

```csharp
// Add to: backend/src/Anela.Heblo.Persistence/PhotoBank/PhotoAssetRepository.cs
// Add these methods to the existing PhotoAssetRepository class:

public async Task<List<(PhotoAsset Photo, double Score)>> SearchSimilarAsync(
    float[] queryEmbedding, int topK, CancellationToken ct = default)
{
    var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
        await connection.OpenAsync(ct);

    await using var cmd = new NpgsqlCommand(
        """
        SELECT "Id", "OneDriveItemId", "OneDrivePath", "FileName", "MimeType",
               "FileSize", "Width", "Height", "TakenAt", "IndexedAt",
               "ThumbnailBlobPath", "OcrText", "Status", "CreatedAt", "ModifiedAt",
               1 - ("Embedding" <=> @embedding) AS "Score"
        FROM dbo."PhotoAssets"
        WHERE "Status" = 1
          AND "Embedding" IS NOT NULL
        ORDER BY "Embedding" <=> @embedding
        LIMIT @topK
        """,
        connection);

    cmd.Parameters.AddWithValue("embedding", new Vector(queryEmbedding));
    cmd.Parameters.AddWithValue("topK", topK);

    var results = new List<(PhotoAsset Photo, double Score)>();

    await using var reader = await cmd.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
    {
        var photo = ReadPhotoAssetFromReader(reader);
        var score = reader.GetDouble(reader.GetOrdinal("Score"));
        results.Add((photo, score));
    }

    // Load tags for all returned photos via EF
    var photoIds = results.Select(r => r.Photo.Id).ToList();
    var tags = await _context.PhotoTags
        .Where(t => photoIds.Contains(t.PhotoAssetId))
        .ToListAsync(ct);

    foreach (var (photo, _) in results)
    {
        photo.Tags = tags.Where(t => t.PhotoAssetId == photo.Id).ToList();
    }

    return results;
}

public async Task<List<(PhotoAsset Photo, double Score)>> FindSimilarByIdAsync(
    Guid photoId, int topK, CancellationToken ct = default)
{
    var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
        await connection.OpenAsync(ct);

    // Use a subquery to get the source photo's embedding, then find similar
    await using var cmd = new NpgsqlCommand(
        """
        WITH source AS (
            SELECT "Embedding"
            FROM dbo."PhotoAssets"
            WHERE "Id" = @photoId
              AND "Embedding" IS NOT NULL
        )
        SELECT p."Id", p."OneDriveItemId", p."OneDrivePath", p."FileName", p."MimeType",
               p."FileSize", p."Width", p."Height", p."TakenAt", p."IndexedAt",
               p."ThumbnailBlobPath", p."OcrText", p."Status", p."CreatedAt", p."ModifiedAt",
               1 - (p."Embedding" <=> source."Embedding") AS "Score"
        FROM dbo."PhotoAssets" p, source
        WHERE p."Status" = 1
          AND p."Embedding" IS NOT NULL
          AND p."Id" != @photoId
        ORDER BY p."Embedding" <=> source."Embedding"
        LIMIT @topK
        """,
        connection);

    cmd.Parameters.AddWithValue("photoId", photoId);
    cmd.Parameters.AddWithValue("topK", topK);

    var results = new List<(PhotoAsset Photo, double Score)>();

    await using var reader = await cmd.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
    {
        var photo = ReadPhotoAssetFromReader(reader);
        var score = reader.GetDouble(reader.GetOrdinal("Score"));
        results.Add((photo, score));
    }

    // Load tags for all returned photos via EF
    var photoIds = results.Select(r => r.Photo.Id).ToList();
    var tags = await _context.PhotoTags
        .Where(t => photoIds.Contains(t.PhotoAssetId))
        .ToListAsync(ct);

    foreach (var (photo, _) in results)
    {
        photo.Tags = tags.Where(t => t.PhotoAssetId == photo.Id).ToList();
    }

    return results;
}

private static PhotoAsset ReadPhotoAssetFromReader(NpgsqlDataReader reader)
{
    return new PhotoAsset
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")),
        OneDriveItemId = reader.GetString(reader.GetOrdinal("OneDriveItemId")),
        OneDrivePath = reader.GetString(reader.GetOrdinal("OneDrivePath")),
        FileName = reader.GetString(reader.GetOrdinal("FileName")),
        MimeType = reader.GetString(reader.GetOrdinal("MimeType")),
        FileSize = reader.GetInt64(reader.GetOrdinal("FileSize")),
        Width = reader.IsDBNull(reader.GetOrdinal("Width")) ? null : reader.GetInt32(reader.GetOrdinal("Width")),
        Height = reader.IsDBNull(reader.GetOrdinal("Height")) ? null : reader.GetInt32(reader.GetOrdinal("Height")),
        TakenAt = reader.IsDBNull(reader.GetOrdinal("TakenAt")) ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("TakenAt")),
        IndexedAt = reader.IsDBNull(reader.GetOrdinal("IndexedAt")) ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("IndexedAt")),
        ThumbnailBlobPath = reader.IsDBNull(reader.GetOrdinal("ThumbnailBlobPath")) ? null : reader.GetString(reader.GetOrdinal("ThumbnailBlobPath")),
        OcrText = reader.IsDBNull(reader.GetOrdinal("OcrText")) ? null : reader.GetString(reader.GetOrdinal("OcrText")),
        Status = (PhotoAssetStatus)reader.GetInt32(reader.GetOrdinal("Status")),
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("CreatedAt")),
        ModifiedAt = reader.IsDBNull(reader.GetOrdinal("ModifiedAt")) ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("ModifiedAt"))
    };
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/PhotoBank/IPhotoAssetRepository.cs backend/src/Anela.Heblo.Persistence/PhotoBank/PhotoAssetRepository.cs
git commit -m "feat(photo-bank): add SearchSimilarAsync and FindSimilarByIdAsync to repository"
```

---

## Task 3: SemanticSearchPhotosHandler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/SemanticSearchPhotos/SemanticSearchPhotosRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/SemanticSearchPhotos/SemanticSearchPhotosHandler.cs`

- [ ] **Step 1: Create request and response classes**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/SemanticSearchPhotos/SemanticSearchPhotosRequest.cs
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.SemanticSearchPhotos;

public class SemanticSearchPhotosRequest : IRequest<SemanticSearchPhotosResponse>
{
    [Required, MinLength(1), MaxLength(500)]
    public string Query { get; set; } = string.Empty;

    [Range(1, 100)]
    public int TopK { get; set; } = 20;

    public float MinScore { get; set; } = 0.0f;
}

public class SemanticSearchPhotosResponse : BaseResponse
{
    public List<SemanticPhotoResult> Photos { get; set; } = [];
    public int BelowThresholdCount { get; set; }
}

public class SemanticPhotoResult
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string OneDrivePath { get; set; } = string.Empty;
    public string? ThumbnailBlobPath { get; set; }
    public string? OcrText { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public DateTimeOffset? TakenAt { get; set; }
    public double Score { get; set; }
    public List<string> Tags { get; set; } = [];
}
```

- [ ] **Step 2: Create handler**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/SemanticSearchPhotos/SemanticSearchPhotosHandler.cs
using Anela.Heblo.Application.Features.PhotoBank.Services;
using Anela.Heblo.Domain.Features.PhotoBank;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.SemanticSearchPhotos;

public class SemanticSearchPhotosHandler : IRequestHandler<SemanticSearchPhotosRequest, SemanticSearchPhotosResponse>
{
    private readonly IAzureAiVisionService _aiVisionService;
    private readonly IPhotoAssetRepository _repository;
    private readonly ILogger<SemanticSearchPhotosHandler> _logger;

    public SemanticSearchPhotosHandler(
        IAzureAiVisionService aiVisionService,
        IPhotoAssetRepository repository,
        ILogger<SemanticSearchPhotosHandler> logger)
    {
        _aiVisionService = aiVisionService;
        _repository = repository;
        _logger = logger;
    }

    public async Task<SemanticSearchPhotosResponse> Handle(
        SemanticSearchPhotosRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Semantic search for photos: '{Query}', TopK={TopK}",
            request.Query, request.TopK);

        // Convert query text to embedding using Azure AI Vision vectorizeText
        var queryEmbedding = await _aiVisionService.VectorizeTextAsync(
            request.Query, cancellationToken);

        // Search by cosine similarity in pgvector
        var results = await _repository.SearchSimilarAsync(
            queryEmbedding, request.TopK, cancellationToken);

        // Filter by minimum score
        var aboveThreshold = results
            .Where(r => r.Score >= request.MinScore)
            .ToList();
        var belowCount = results.Count - aboveThreshold.Count;

        return new SemanticSearchPhotosResponse
        {
            BelowThresholdCount = belowCount,
            Photos = aboveThreshold.Select(r => new SemanticPhotoResult
            {
                Id = r.Photo.Id,
                FileName = r.Photo.FileName,
                OneDrivePath = r.Photo.OneDrivePath,
                ThumbnailBlobPath = r.Photo.ThumbnailBlobPath,
                OcrText = r.Photo.OcrText,
                Width = r.Photo.Width,
                Height = r.Photo.Height,
                TakenAt = r.Photo.TakenAt,
                Score = r.Score,
                Tags = r.Photo.Tags.Select(t => t.TagName).ToList()
            }).ToList()
        };
    }
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.Application/`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/SemanticSearchPhotos/
git commit -m "feat(photo-bank): add SemanticSearchPhotosHandler with text-to-image vector search"
```

---

## Task 4: FindSimilarPhotosHandler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/FindSimilarPhotos/FindSimilarPhotosRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/FindSimilarPhotos/FindSimilarPhotosHandler.cs`

- [ ] **Step 1: Create request and response classes**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/FindSimilarPhotos/FindSimilarPhotosRequest.cs
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.FindSimilarPhotos;

public class FindSimilarPhotosRequest : IRequest<FindSimilarPhotosResponse>
{
    [Required]
    public Guid PhotoId { get; set; }

    [Range(1, 100)]
    public int TopK { get; set; } = 12;
}

public class FindSimilarPhotosResponse : BaseResponse
{
    public Guid SourcePhotoId { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
    public List<SimilarPhotoResult> SimilarPhotos { get; set; } = [];
}

public class SimilarPhotoResult
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string OneDrivePath { get; set; } = string.Empty;
    public string? ThumbnailBlobPath { get; set; }
    public string? OcrText { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public DateTimeOffset? TakenAt { get; set; }
    public double Score { get; set; }
    public List<string> Tags { get; set; } = [];
}
```

- [ ] **Step 2: Create handler**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/FindSimilarPhotos/FindSimilarPhotosHandler.cs
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PhotoBank;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.FindSimilarPhotos;

public class FindSimilarPhotosHandler : IRequestHandler<FindSimilarPhotosRequest, FindSimilarPhotosResponse>
{
    private readonly IPhotoAssetRepository _repository;
    private readonly ILogger<FindSimilarPhotosHandler> _logger;

    public FindSimilarPhotosHandler(
        IPhotoAssetRepository repository,
        ILogger<FindSimilarPhotosHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<FindSimilarPhotosResponse> Handle(
        FindSimilarPhotosRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Finding similar photos for {PhotoId}, TopK={TopK}",
            request.PhotoId, request.TopK);

        // Verify source photo exists
        var sourcePhoto = await _repository.GetByIdAsync(request.PhotoId, cancellationToken);
        if (sourcePhoto == null)
        {
            return new FindSimilarPhotosResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.NotFound,
                Params = new Dictionary<string, string>
                {
                    { "Entity", "PhotoAsset" },
                    { "Id", request.PhotoId.ToString() }
                }
            };
        }

        // Use repository to find similar photos by the source photo's embedding
        var results = await _repository.FindSimilarByIdAsync(
            request.PhotoId, request.TopK, cancellationToken);

        return new FindSimilarPhotosResponse
        {
            SourcePhotoId = sourcePhoto.Id,
            SourceFileName = sourcePhoto.FileName,
            SimilarPhotos = results.Select(r => new SimilarPhotoResult
            {
                Id = r.Photo.Id,
                FileName = r.Photo.FileName,
                OneDrivePath = r.Photo.OneDrivePath,
                ThumbnailBlobPath = r.Photo.ThumbnailBlobPath,
                OcrText = r.Photo.OcrText,
                Width = r.Photo.Width,
                Height = r.Photo.Height,
                TakenAt = r.Photo.TakenAt,
                Score = r.Score,
                Tags = r.Photo.Tags.Select(t => t.TagName).ToList()
            }).ToList()
        };
    }
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.Application/`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/FindSimilarPhotos/
git commit -m "feat(photo-bank): add FindSimilarPhotosHandler for visual similarity search"
```

---

## Task 5: API Endpoints on PhotoBankController

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/PhotoBankController.cs`

- [ ] **Step 1: Add semantic search and find-similar endpoints**

Add two new endpoints to the existing `PhotoBankController`. The controller already has the standard search/detail/tag endpoints from Iteration 1.

```csharp
// Add to: backend/src/Anela.Heblo.API/Controllers/PhotoBankController.cs
// Add these using statements at the top:
using Anela.Heblo.Application.Features.PhotoBank.UseCases.SemanticSearchPhotos;
using Anela.Heblo.Application.Features.PhotoBank.UseCases.FindSimilarPhotos;

// Add these action methods to the existing PhotoBankController class:

[HttpGet("semantic-search")]
public async Task<ActionResult<SemanticSearchPhotosResponse>> SemanticSearch(
    [FromQuery] string query,
    [FromQuery] int topK = 20,
    [FromQuery] float minScore = 0.0f,
    CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(query))
        return BadRequest("Query parameter is required");

    var result = await _mediator.Send(new SemanticSearchPhotosRequest
    {
        Query = query,
        TopK = topK,
        MinScore = minScore
    }, ct);

    if (!result.Success)
        return StatusCode(500, result);

    return Ok(result);
}

[HttpGet("{id:guid}/similar")]
public async Task<ActionResult<FindSimilarPhotosResponse>> FindSimilar(
    Guid id,
    [FromQuery] int topK = 12,
    CancellationToken ct = default)
{
    var result = await _mediator.Send(new FindSimilarPhotosRequest
    {
        PhotoId = id,
        TopK = topK
    }, ct);

    if (!result.Success)
        return NotFound(result);

    return Ok(result);
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.API/`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/PhotoBankController.cs
git commit -m "feat(photo-bank): add semantic-search and find-similar API endpoints"
```

---

## Task 6: MCP Tools for Semantic Search

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/Tools/PhotoBankTools.cs`

- [ ] **Step 1: Add SemanticSearchPhotoBank tool**

Add a new MCP tool to the existing `PhotoBankTools` class. Follows the same pattern as `KnowledgeBaseTools.SearchKnowledgeBase`.

```csharp
// Add to: backend/src/Anela.Heblo.API/MCP/Tools/PhotoBankTools.cs
// Add these using statements at the top:
using Anela.Heblo.Application.Features.PhotoBank.UseCases.SemanticSearchPhotos;
using Anela.Heblo.Application.Features.PhotoBank.UseCases.FindSimilarPhotos;

// Add these methods to the existing PhotoBankTools class:

[McpServerTool]
[Description("Search photos using natural language. Converts query text to a vector embedding and finds visually/semantically similar photos using AI. Example queries: 'product on white background', 'person holding bottle', 'outdoor lifestyle shot'.")]
public async Task<string> SemanticSearchPhotoBank(
    [Description("Natural language search query describing the photos you want to find")] string query,
    [Description("Number of results to return (default: 20, max: 100)")] int topK = 20)
{
    try
    {
        var result = await _mediator.Send(new SemanticSearchPhotosRequest
        {
            Query = query,
            TopK = topK
        });
        return JsonSerializer.Serialize(result);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "MCP SemanticSearchPhotoBank failed for query '{Query}'", query);
        throw new McpException($"Failed to search photos semantically: {ex.Message}");
    }
}

[McpServerTool]
[Description("Find photos that are visually similar to a given photo. Uses the photo's AI-generated embedding to find nearest neighbors.")]
public async Task<string> FindSimilarPhotos(
    [Description("The ID (GUID) of the source photo to find similar photos for")] string photoId,
    [Description("Number of similar photos to return (default: 12, max: 100)")] int topK = 12)
{
    try
    {
        if (!Guid.TryParse(photoId, out var parsedId))
            throw new McpException($"Invalid photo ID format: {photoId}");

        var result = await _mediator.Send(new FindSimilarPhotosRequest
        {
            PhotoId = parsedId,
            TopK = topK
        });
        return JsonSerializer.Serialize(result);
    }
    catch (McpException)
    {
        throw;
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "MCP FindSimilarPhotos failed for photo '{PhotoId}'", photoId);
        throw new McpException($"Failed to find similar photos: {ex.Message}");
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.API/`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/PhotoBankTools.cs
git commit -m "feat(photo-bank): add MCP tools for semantic search and find-similar"
```

---

## Task 7: Frontend — Search Mode Toggle and Semantic Search Hook

**Files:**
- Create: `frontend/src/api/hooks/usePhotoBank.ts` (add semantic search hooks to existing file, or create if not yet present)

- [ ] **Step 1: Add semantic search types and hooks**

Add to the existing `usePhotoBank.ts` hook file (created in Iteration 1). If the file already exists, add these new types and hooks. If not, create it with all the types.

```typescript
// Add to: frontend/src/api/hooks/usePhotoBank.ts
// Add these types alongside existing photo bank types:

export type PhotoSearchMode = 'tags' | 'semantic';

export interface SemanticSearchParams {
  query: string;
  topK?: number;
  minScore?: number;
}

export interface SemanticPhotoResult {
  id: string;
  fileName: string;
  oneDrivePath: string;
  thumbnailBlobPath: string | null;
  ocrText: string | null;
  width: number | null;
  height: number | null;
  takenAt: string | null;
  score: number;
  tags: string[];
}

export interface SemanticSearchResponse {
  success: boolean;
  photos: SemanticPhotoResult[];
  belowThresholdCount: number;
}

export interface SimilarPhotoResult {
  id: string;
  fileName: string;
  oneDrivePath: string;
  thumbnailBlobPath: string | null;
  ocrText: string | null;
  width: number | null;
  height: number | null;
  takenAt: string | null;
  score: number;
  tags: string[];
}

export interface FindSimilarResponse {
  success: boolean;
  sourcePhotoId: string;
  sourceFileName: string;
  similarPhotos: SimilarPhotoResult[];
}

// Add these query key entries to the existing photoBankKeys object:
// semanticSearch: (params: SemanticSearchParams) =>
//   [...QUERY_KEYS.photoBank, 'semantic-search', params] as const,
// similar: (photoId: string) =>
//   [...QUERY_KEYS.photoBank, 'similar', photoId] as const,

// Add these hooks:

/**
 * Semantic search for photos using natural language query.
 * Only runs when query is non-empty.
 */
export const useSemanticPhotoSearch = (params: SemanticSearchParams) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.photoBank, 'semantic-search', params],
    queryFn: async (): Promise<SemanticSearchResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const searchParams = new URLSearchParams();

      searchParams.append('query', params.query);
      if (params.topK !== undefined)
        searchParams.append('topK', params.topK.toString());
      if (params.minScore !== undefined)
        searchParams.append('minScore', params.minScore.toString());

      const relativeUrl = `/api/photo-bank/semantic-search?${searchParams.toString()}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });

      if (!response.ok) {
        throw new Error(`Semantic search failed: ${response.status}`);
      }

      return response.json();
    },
    enabled: params.query.trim().length > 0,
    staleTime: 2 * 60 * 1000,
    gcTime: 5 * 60 * 1000,
  });
};

/**
 * Find photos visually similar to a given photo.
 * Only runs when photoId is provided.
 */
export const useFindSimilarPhotos = (photoId: string | null, topK: number = 12) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.photoBank, 'similar', photoId],
    queryFn: async (): Promise<FindSimilarResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const searchParams = new URLSearchParams();
      searchParams.append('topK', topK.toString());

      const relativeUrl = `/api/photo-bank/${photoId}/similar?${searchParams.toString()}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });

      if (!response.ok) {
        throw new Error(`Find similar failed: ${response.status}`);
      }

      return response.json();
    },
    enabled: !!photoId,
    staleTime: 2 * 60 * 1000,
    gcTime: 5 * 60 * 1000,
  });
};
```

- [ ] **Step 2: Commit**

```bash
git add frontend/src/api/hooks/usePhotoBank.ts
git commit -m "feat(photo-bank): add frontend hooks for semantic search and find-similar"
```

---

## Task 8: Frontend — Search Mode Toggle and Find Similar Button

**Files:**
- Modify: `frontend/src/pages/PhotoBank/components/PhotoSearchBar.tsx`
- Modify: `frontend/src/pages/PhotoBank/components/PhotoDetailPanel.tsx`
- Modify: `frontend/src/pages/PhotoBank/PhotoBankPage.tsx`

- [ ] **Step 1: Add search mode toggle to PhotoSearchBar**

Add a toggle button group that switches between "Tags" and "Semantic" search modes. When in semantic mode, the search input sends the query to the semantic search endpoint instead of the tag-based one.

```tsx
// Modify: frontend/src/pages/PhotoBank/components/PhotoSearchBar.tsx
// Add search mode toggle. The component receives searchMode and onSearchModeChange props.

import React from 'react';
import {
  TextField,
  ToggleButtonGroup,
  ToggleButton,
  InputAdornment,
  Box,
  Tooltip,
} from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import LabelIcon from '@mui/icons-material/Label';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import { PhotoSearchMode } from '../../../api/hooks/usePhotoBank';

interface PhotoSearchBarProps {
  searchTerm: string;
  onSearchTermChange: (value: string) => void;
  searchMode: PhotoSearchMode;
  onSearchModeChange: (mode: PhotoSearchMode) => void;
  onSubmit: () => void;
  /** Existing tag filter props — keep as-is from Iteration 1 */
  selectedTags?: string[];
  onTagsChange?: (tags: string[]) => void;
  availableTags?: string[];
}

export const PhotoSearchBar: React.FC<PhotoSearchBarProps> = ({
  searchTerm,
  onSearchTermChange,
  searchMode,
  onSearchModeChange,
  onSubmit,
  selectedTags,
  onTagsChange,
  availableTags,
}) => {
  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') {
      onSubmit();
    }
  };

  return (
    <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
      <ToggleButtonGroup
        value={searchMode}
        exclusive
        onChange={(_, value) => {
          if (value !== null) onSearchModeChange(value);
        }}
        size="small"
        aria-label="search mode"
      >
        <ToggleButton value="tags" aria-label="tag search">
          <Tooltip title="Search by tags and OCR text">
            <LabelIcon sx={{ mr: 0.5 }} />
          </Tooltip>
          Tags
        </ToggleButton>
        <ToggleButton value="semantic" aria-label="semantic search">
          <Tooltip title="Search by natural language description (AI)">
            <AutoAwesomeIcon sx={{ mr: 0.5 }} />
          </Tooltip>
          Semantic
        </ToggleButton>
      </ToggleButtonGroup>

      <TextField
        value={searchTerm}
        onChange={(e) => onSearchTermChange(e.target.value)}
        onKeyDown={handleKeyDown}
        placeholder={
          searchMode === 'semantic'
            ? 'Describe what you\'re looking for... (e.g. "product on white background")'
            : 'Search tags and OCR text...'
        }
        size="small"
        sx={{ flexGrow: 1, minWidth: 300 }}
        InputProps={{
          startAdornment: (
            <InputAdornment position="start">
              {searchMode === 'semantic' ? <AutoAwesomeIcon color="primary" /> : <SearchIcon />}
            </InputAdornment>
          ),
        }}
      />

      {/* Tag filter chips — only shown in tags mode */}
      {searchMode === 'tags' && selectedTags && onTagsChange && availableTags && (
        <Box sx={{ display: 'flex', gap: 0.5, flexWrap: 'wrap' }}>
          {/* Existing tag filter chip rendering from Iteration 1 */}
        </Box>
      )}
    </Box>
  );
};
```

- [ ] **Step 2: Add "Find Similar" button to PhotoDetailPanel**

```tsx
// Modify: frontend/src/pages/PhotoBank/components/PhotoDetailPanel.tsx
// Add a "Find Similar" button and a similar photos grid section.
// Add these imports and the button to the existing component:

import React from 'react';
import {
  Box,
  Button,
  Typography,
  CircularProgress,
  ImageList,
  ImageListItem,
  ImageListItemBar,
  Chip,
} from '@mui/material';
import ImageSearchIcon from '@mui/icons-material/ImageSearch';
import { useFindSimilarPhotos } from '../../../api/hooks/usePhotoBank';

interface FindSimilarSectionProps {
  photoId: string;
  showSimilar: boolean;
  onToggleSimilar: () => void;
  onPhotoClick: (photoId: string) => void;
}

export const FindSimilarSection: React.FC<FindSimilarSectionProps> = ({
  photoId,
  showSimilar,
  onToggleSimilar,
  onPhotoClick,
}) => {
  const { data, isLoading } = useFindSimilarPhotos(
    showSimilar ? photoId : null,
    12
  );

  return (
    <Box sx={{ mt: 2 }}>
      <Button
        variant="outlined"
        startIcon={<ImageSearchIcon />}
        onClick={onToggleSimilar}
        fullWidth
        size="small"
      >
        {showSimilar ? 'Hide Similar Photos' : 'Find Similar Photos'}
      </Button>

      {showSimilar && (
        <Box sx={{ mt: 2 }}>
          {isLoading && (
            <Box sx={{ display: 'flex', justifyContent: 'center', py: 2 }}>
              <CircularProgress size={24} />
            </Box>
          )}

          {data && data.similarPhotos.length > 0 && (
            <>
              <Typography variant="subtitle2" sx={{ mb: 1 }}>
                Similar Photos ({data.similarPhotos.length})
              </Typography>
              <ImageList cols={2} gap={8}>
                {data.similarPhotos.map((photo) => (
                  <ImageListItem
                    key={photo.id}
                    sx={{ cursor: 'pointer' }}
                    onClick={() => onPhotoClick(photo.id)}
                  >
                    <img
                      src={photo.thumbnailBlobPath || ''}
                      alt={photo.fileName}
                      loading="lazy"
                      style={{ borderRadius: 4 }}
                    />
                    <ImageListItemBar
                      title={photo.fileName}
                      subtitle={
                        <Box sx={{ display: 'flex', gap: 0.5, flexWrap: 'wrap', mt: 0.5 }}>
                          <Chip
                            label={`${(photo.score * 100).toFixed(0)}% match`}
                            size="small"
                            color="primary"
                            variant="outlined"
                          />
                        </Box>
                      }
                    />
                  </ImageListItem>
                ))}
              </ImageList>
            </>
          )}

          {data && data.similarPhotos.length === 0 && (
            <Typography variant="body2" color="text.secondary" sx={{ mt: 1, textAlign: 'center' }}>
              No similar photos found.
            </Typography>
          )}
        </Box>
      )}
    </Box>
  );
};
```

- [ ] **Step 3: Update PhotoBankPage to handle search mode state**

```tsx
// Modify: frontend/src/pages/PhotoBank/PhotoBankPage.tsx
// Add state management for search mode. Add these changes to the existing page component:

// Add import:
import { PhotoSearchMode, useSemanticPhotoSearch } from '../../api/hooks/usePhotoBank';

// Add state inside the component:
const [searchMode, setSearchMode] = React.useState<PhotoSearchMode>('tags');
const [semanticQuery, setSemanticQuery] = React.useState('');
const [submittedSemanticQuery, setSubmittedSemanticQuery] = React.useState('');

// Add semantic search hook:
const semanticResults = useSemanticPhotoSearch({
  query: submittedSemanticQuery,
  topK: 40,
});

// Add handler for submitting semantic search:
const handleSearchSubmit = () => {
  if (searchMode === 'semantic') {
    setSubmittedSemanticQuery(semanticQuery);
  }
  // Tag-based search updates happen via existing filter state
};

// In the render, conditionally show semantic results or tag-based results:
// When searchMode === 'semantic' && semanticResults.data:
//   Map semanticResults.data.photos to the PhotoGrid component
// When searchMode === 'tags':
//   Use existing tag-based search results (from Iteration 1)

// Pass searchMode props to PhotoSearchBar:
// <PhotoSearchBar
//   searchTerm={searchMode === 'semantic' ? semanticQuery : existingSearchTerm}
//   onSearchTermChange={searchMode === 'semantic' ? setSemanticQuery : setExistingSearchTerm}
//   searchMode={searchMode}
//   onSearchModeChange={setSearchMode}
//   onSubmit={handleSearchSubmit}
//   selectedTags={selectedTags}
//   onTagsChange={setSelectedTags}
//   availableTags={availableTagsData}
// />
```

- [ ] **Step 4: Verify frontend build**

Run: `cd frontend && npm run build`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/PhotoBank/ frontend/src/api/hooks/usePhotoBank.ts
git commit -m "feat(photo-bank): add search mode toggle and find-similar UI"
```

---

## Task 9: Tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/PhotoBank/SemanticSearchPhotosHandlerTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/PhotoBank/FindSimilarPhotosHandlerTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/MCP/Tools/PhotoBankToolsSemanticTests.cs`

- [ ] **Step 1: Create SemanticSearchPhotosHandlerTests**

```csharp
// backend/test/Anela.Heblo.Tests/Features/PhotoBank/SemanticSearchPhotosHandlerTests.cs
using Anela.Heblo.Application.Features.PhotoBank.Services;
using Anela.Heblo.Application.Features.PhotoBank.UseCases.SemanticSearchPhotos;
using Anela.Heblo.Domain.Features.PhotoBank;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.PhotoBank;

public class SemanticSearchPhotosHandlerTests
{
    private readonly Mock<IAzureAiVisionService> _aiVision;
    private readonly Mock<IPhotoAssetRepository> _repository;
    private readonly SemanticSearchPhotosHandler _handler;

    public SemanticSearchPhotosHandlerTests()
    {
        _aiVision = new Mock<IAzureAiVisionService>();
        _repository = new Mock<IPhotoAssetRepository>();

        _handler = new SemanticSearchPhotosHandler(
            _aiVision.Object,
            _repository.Object,
            Mock.Of<ILogger<SemanticSearchPhotosHandler>>());
    }

    [Fact]
    public async Task Handle_CallsVectorizeTextWithQuery()
    {
        var queryEmbedding = new float[1024];
        _aiVision.Setup(a => a.VectorizeTextAsync("cosmetics bottle", default))
            .ReturnsAsync(queryEmbedding);
        _repository.Setup(r => r.SearchSimilarAsync(queryEmbedding, 20, default))
            .ReturnsAsync(new List<(PhotoAsset, double)>());

        await _handler.Handle(new SemanticSearchPhotosRequest
        {
            Query = "cosmetics bottle",
            TopK = 20
        }, default);

        _aiVision.Verify(a => a.VectorizeTextAsync("cosmetics bottle", default), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsPhotosAboveMinScore()
    {
        var queryEmbedding = new float[1024];
        _aiVision.Setup(a => a.VectorizeTextAsync(It.IsAny<string>(), default))
            .ReturnsAsync(queryEmbedding);

        var photo1 = CreatePhoto("photo1.jpg");
        var photo2 = CreatePhoto("photo2.jpg");
        var photo3 = CreatePhoto("photo3.jpg");

        _repository.Setup(r => r.SearchSimilarAsync(queryEmbedding, 20, default))
            .ReturnsAsync(new List<(PhotoAsset, double)>
            {
                (photo1, 0.92),
                (photo2, 0.75),
                (photo3, 0.30)
            });

        var result = await _handler.Handle(new SemanticSearchPhotosRequest
        {
            Query = "test",
            TopK = 20,
            MinScore = 0.5f
        }, default);

        result.Photos.Should().HaveCount(2);
        result.BelowThresholdCount.Should().Be(1);
        result.Photos[0].Score.Should().Be(0.92);
        result.Photos[1].Score.Should().Be(0.75);
    }

    [Fact]
    public async Task Handle_MapsPhotoFieldsCorrectly()
    {
        var queryEmbedding = new float[1024];
        _aiVision.Setup(a => a.VectorizeTextAsync(It.IsAny<string>(), default))
            .ReturnsAsync(queryEmbedding);

        var photo = new PhotoAsset
        {
            Id = Guid.NewGuid(),
            FileName = "rose-water.jpg",
            OneDrivePath = "/Photos/Products/rose-water.jpg",
            ThumbnailBlobPath = "photo-thumbnails/abc.jpg",
            OcrText = "Rose Water Toner 100ml",
            Width = 1920,
            Height = 1080,
            TakenAt = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero),
            Status = PhotoAssetStatus.Indexed,
            Tags = new List<PhotoTag>
            {
                new() { TagName = "bottle", Confidence = 0.95f, Source = TagSource.Auto },
                new() { TagName = "cosmetics", Confidence = 0.90f, Source = TagSource.Auto }
            }
        };

        _repository.Setup(r => r.SearchSimilarAsync(queryEmbedding, 10, default))
            .ReturnsAsync(new List<(PhotoAsset, double)> { (photo, 0.88) });

        var result = await _handler.Handle(new SemanticSearchPhotosRequest
        {
            Query = "rose water",
            TopK = 10
        }, default);

        result.Success.Should().BeTrue();
        result.Photos.Should().HaveCount(1);
        var p = result.Photos[0];
        p.FileName.Should().Be("rose-water.jpg");
        p.OcrText.Should().Be("Rose Water Toner 100ml");
        p.Tags.Should().Contain("bottle");
        p.Tags.Should().Contain("cosmetics");
        p.Score.Should().Be(0.88);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenNoResults()
    {
        _aiVision.Setup(a => a.VectorizeTextAsync(It.IsAny<string>(), default))
            .ReturnsAsync(new float[1024]);
        _repository.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), default))
            .ReturnsAsync(new List<(PhotoAsset, double)>());

        var result = await _handler.Handle(new SemanticSearchPhotosRequest
        {
            Query = "unicorn rainbow",
            TopK = 20
        }, default);

        result.Success.Should().BeTrue();
        result.Photos.Should().BeEmpty();
    }

    private static PhotoAsset CreatePhoto(string fileName)
    {
        return new PhotoAsset
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            OneDriveItemId = Guid.NewGuid().ToString(),
            OneDrivePath = $"/Photos/{fileName}",
            MimeType = "image/jpeg",
            Status = PhotoAssetStatus.Indexed,
            Tags = new List<PhotoTag>()
        };
    }
}
```

- [ ] **Step 2: Create FindSimilarPhotosHandlerTests**

```csharp
// backend/test/Anela.Heblo.Tests/Features/PhotoBank/FindSimilarPhotosHandlerTests.cs
using Anela.Heblo.Application.Features.PhotoBank.UseCases.FindSimilarPhotos;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PhotoBank;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.PhotoBank;

public class FindSimilarPhotosHandlerTests
{
    private readonly Mock<IPhotoAssetRepository> _repository;
    private readonly FindSimilarPhotosHandler _handler;

    public FindSimilarPhotosHandlerTests()
    {
        _repository = new Mock<IPhotoAssetRepository>();

        _handler = new FindSimilarPhotosHandler(
            _repository.Object,
            Mock.Of<ILogger<FindSimilarPhotosHandler>>());
    }

    [Fact]
    public async Task Handle_WhenPhotoNotFound_ReturnsNotFoundError()
    {
        var photoId = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(photoId, default))
            .ReturnsAsync((PhotoAsset?)null);

        var result = await _handler.Handle(new FindSimilarPhotosRequest
        {
            PhotoId = photoId,
            TopK = 12
        }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_ReturnsSimilarPhotos_WithSourceInfo()
    {
        var sourceId = Guid.NewGuid();
        var sourcePhoto = new PhotoAsset
        {
            Id = sourceId,
            FileName = "source-photo.jpg",
            OneDriveItemId = "item-source",
            OneDrivePath = "/Photos/source-photo.jpg",
            MimeType = "image/jpeg",
            Status = PhotoAssetStatus.Indexed,
            Tags = new List<PhotoTag>()
        };

        var similar1 = CreatePhoto("similar1.jpg");
        var similar2 = CreatePhoto("similar2.jpg");

        _repository.Setup(r => r.GetByIdAsync(sourceId, default))
            .ReturnsAsync(sourcePhoto);

        _repository.Setup(r => r.FindSimilarByIdAsync(sourceId, 12, default))
            .ReturnsAsync(new List<(PhotoAsset, double)>
            {
                (similar1, 0.95),
                (similar2, 0.82)
            });

        var result = await _handler.Handle(new FindSimilarPhotosRequest
        {
            PhotoId = sourceId,
            TopK = 12
        }, default);

        result.Success.Should().BeTrue();
        result.SourcePhotoId.Should().Be(sourceId);
        result.SourceFileName.Should().Be("source-photo.jpg");
        result.SimilarPhotos.Should().HaveCount(2);
        result.SimilarPhotos[0].Score.Should().Be(0.95);
        result.SimilarPhotos[1].Score.Should().Be(0.82);
    }

    [Fact]
    public async Task Handle_WhenNoSimilarPhotos_ReturnsEmptyList()
    {
        var sourceId = Guid.NewGuid();
        var sourcePhoto = new PhotoAsset
        {
            Id = sourceId,
            FileName = "unique-photo.jpg",
            OneDriveItemId = "item-unique",
            OneDrivePath = "/Photos/unique-photo.jpg",
            MimeType = "image/jpeg",
            Status = PhotoAssetStatus.Indexed,
            Tags = new List<PhotoTag>()
        };

        _repository.Setup(r => r.GetByIdAsync(sourceId, default))
            .ReturnsAsync(sourcePhoto);

        _repository.Setup(r => r.FindSimilarByIdAsync(sourceId, 12, default))
            .ReturnsAsync(new List<(PhotoAsset, double)>());

        var result = await _handler.Handle(new FindSimilarPhotosRequest
        {
            PhotoId = sourceId,
            TopK = 12
        }, default);

        result.Success.Should().BeTrue();
        result.SimilarPhotos.Should().BeEmpty();
    }

    private static PhotoAsset CreatePhoto(string fileName)
    {
        return new PhotoAsset
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            OneDriveItemId = Guid.NewGuid().ToString(),
            OneDrivePath = $"/Photos/{fileName}",
            MimeType = "image/jpeg",
            Status = PhotoAssetStatus.Indexed,
            Tags = new List<PhotoTag>()
        };
    }
}
```

- [ ] **Step 3: Create PhotoBankToolsSemanticTests**

```csharp
// backend/test/Anela.Heblo.Tests/MCP/Tools/PhotoBankToolsSemanticTests.cs
using System.Text.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.PhotoBank.UseCases.FindSimilarPhotos;
using Anela.Heblo.Application.Features.PhotoBank.UseCases.SemanticSearchPhotos;
using MediatR;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class PhotoBankToolsSemanticTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<ILogger<PhotoBankTools>> _logger = new();

    private PhotoBankTools CreateTools() => new(_mediator.Object, _logger.Object);

    [Fact]
    public async Task SemanticSearchPhotoBank_ShouldMapParametersCorrectly()
    {
        var expected = new SemanticSearchPhotosResponse
        {
            Photos =
            [
                new SemanticPhotoResult
                {
                    FileName = "test.jpg", Score = 0.9, Tags = ["bottle"]
                }
            ]
        };

        _mediator
            .Setup(m => m.Send(
                It.Is<SemanticSearchPhotosRequest>(r => r.Query == "cosmetics bottle" && r.TopK == 10),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await CreateTools().SemanticSearchPhotoBank("cosmetics bottle", 10);

        var deserialized = JsonSerializer.Deserialize<SemanticSearchPhotosResponse>(result);
        Assert.NotNull(deserialized);
        Assert.Single(deserialized!.Photos);
        Assert.Equal("test.jpg", deserialized.Photos[0].FileName);
    }

    [Fact]
    public async Task SemanticSearchPhotoBank_ShouldThrowMcpException_WhenMediatorThrows()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<SemanticSearchPhotosRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("AI Vision error"));

        await Assert.ThrowsAsync<McpException>(() =>
            CreateTools().SemanticSearchPhotoBank("query"));
    }

    [Fact]
    public async Task FindSimilarPhotos_ShouldMapParametersCorrectly()
    {
        var photoId = Guid.NewGuid();
        var expected = new FindSimilarPhotosResponse
        {
            SourcePhotoId = photoId,
            SourceFileName = "source.jpg",
            SimilarPhotos =
            [
                new SimilarPhotoResult
                {
                    FileName = "similar.jpg", Score = 0.88
                }
            ]
        };

        _mediator
            .Setup(m => m.Send(
                It.Is<FindSimilarPhotosRequest>(r => r.PhotoId == photoId && r.TopK == 6),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await CreateTools().FindSimilarPhotos(photoId.ToString(), 6);

        var deserialized = JsonSerializer.Deserialize<FindSimilarPhotosResponse>(result);
        Assert.NotNull(deserialized);
        Assert.Equal("source.jpg", deserialized!.SourceFileName);
        Assert.Single(deserialized.SimilarPhotos);
    }

    [Fact]
    public async Task FindSimilarPhotos_ShouldThrowMcpException_ForInvalidGuid()
    {
        await Assert.ThrowsAsync<McpException>(() =>
            CreateTools().FindSimilarPhotos("not-a-guid"));
    }

    [Fact]
    public async Task FindSimilarPhotos_ShouldThrowMcpException_WhenMediatorThrows()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<FindSimilarPhotosRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        await Assert.ThrowsAsync<McpException>(() =>
            CreateTools().FindSimilarPhotos(Guid.NewGuid().ToString()));
    }

    [Fact]
    public async Task SemanticSearchPhotoBank_ShouldLogWarning_WhenMediatorThrows()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<SemanticSearchPhotosRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("timeout"));

        await Assert.ThrowsAsync<McpException>(() =>
            CreateTools().SemanticSearchPhotoBank("query"));

        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("SemanticSearchPhotoBank")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~PhotoBank" -v n
```
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/PhotoBank/ backend/test/Anela.Heblo.Tests/MCP/Tools/PhotoBankToolsSemanticTests.cs
git commit -m "test(photo-bank): add tests for semantic search and find-similar handlers and MCP tools"
```

---

## Task 10: Final Build Validation and Format Check

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
Expected: All tests pass (including new PhotoBank semantic search tests)

- [ ] **Step 4: Run frontend build**

```bash
cd frontend && npm run build
```
Expected: Build succeeded

- [ ] **Step 5: Run frontend lint**

```bash
cd frontend && npm run lint
```
Expected: No lint errors

- [ ] **Step 6: Commit any formatting fixes**

```bash
git add -A
git commit -m "style(photo-bank): apply dotnet format and lint fixes"
```

---

## Verification Checklist

- [ ] `IAzureAiVisionService.VectorizeTextAsync` added with real + mock implementations
- [ ] `IPhotoAssetRepository.SearchSimilarAsync` performs pgvector cosine similarity on PhotoAssets
- [ ] `IPhotoAssetRepository.FindSimilarByIdAsync` finds similar photos by source photo embedding
- [ ] `SemanticSearchPhotosHandler` converts text query to embedding and searches
- [ ] `FindSimilarPhotosHandler` finds similar photos with proper error handling for missing source
- [ ] API endpoint: `GET /api/photo-bank/semantic-search?query=X` returns ranked photo results
- [ ] API endpoint: `GET /api/photo-bank/{id}/similar` returns visually similar photos
- [ ] MCP tools: `SemanticSearchPhotoBank` and `FindSimilarPhotos` exposed for AI assistants
- [ ] Frontend: search mode toggle (Tags / Semantic) on PhotoSearchBar
- [ ] Frontend: "Find Similar" button on PhotoDetailPanel
- [ ] Frontend: `useSemanticPhotoSearch` and `useFindSimilarPhotos` hooks
- [ ] Tests: `SemanticSearchPhotosHandlerTests` (4 tests)
- [ ] Tests: `FindSimilarPhotosHandlerTests` (3 tests)
- [ ] Tests: `PhotoBankToolsSemanticTests` (6 tests)
- [ ] `dotnet build` passes
- [ ] `dotnet format` passes
- [ ] `dotnet test` passes
- [ ] `npm run build` passes
- [ ] `npm run lint` passes
