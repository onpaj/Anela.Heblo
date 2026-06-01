# Photo Bank: Iteration 2 — Search API & Frontend UI

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build REST API endpoints for photo search/browse and React frontend with grid view, tag filters, and detail panel.

**Architecture:** MediatR handlers for search/CRUD operations exposed via MVC controller. React frontend using React Query hooks, URL-based filter state, and Tailwind CSS grid layout.

**Tech Stack:** .NET 8, MediatR, EF Core, React 18, TypeScript, React Query, Tailwind CSS

**GitHub Issue:** #612

---

## Task 1: Extend IPhotoAssetRepository & PhotoAssetRepository with Search Methods

**Files:**
- Edit: `backend/src/Anela.Heblo.Domain/Features/PhotoBank/IPhotoAssetRepository.cs`
- Edit: `backend/src/Anela.Heblo.Persistence/PhotoBank/PhotoAssetRepository.cs`

**Depends on:** Iteration 1 (domain entities, repository, EF configs must exist)

- [ ] **Step 1: Add search methods to IPhotoAssetRepository**

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

    // New methods for Iteration 2
    Task<(List<PhotoAsset> Items, int TotalCount)> SearchAsync(
        List<string>? tags,
        string? ocrText,
        string? searchTerm,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? sortBy,
        bool sortDescending,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<List<TagCount>> GetAllTagsWithCountsAsync(CancellationToken ct = default);

    Task<PhotoAsset?> GetByIdWithTagsAsync(Guid id, CancellationToken ct = default);

    Task AddTagAsync(PhotoTag tag, CancellationToken ct = default);

    Task<PhotoTag?> GetTagByIdAsync(Guid tagId, CancellationToken ct = default);

    Task RemoveTagAsync(PhotoTag tag, CancellationToken ct = default);
}

public class TagCount
{
    public string TagName { get; set; } = string.Empty;
    public int Count { get; set; }
}
```

- [ ] **Step 2: Implement search methods in PhotoAssetRepository**

Add these methods to the existing `PhotoAssetRepository` class. The `SearchAsync` method uses EF Core query composition with tag AND filtering and `EF.Functions.ILike` for OCR trigram search. The `GetAllTagsWithCountsAsync` method uses GroupBy on tags.

```csharp
// Add to backend/src/Anela.Heblo.Persistence/PhotoBank/PhotoAssetRepository.cs
// These methods are added to the existing PhotoAssetRepository class

    public async Task<(List<PhotoAsset> Items, int TotalCount)> SearchAsync(
        List<string>? tags,
        string? ocrText,
        string? searchTerm,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? sortBy,
        bool sortDescending,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _context.Set<PhotoAsset>()
            .Include(p => p.Tags)
            .Where(p => p.Status == PhotoAssetStatus.Indexed)
            .AsQueryable();

        // Tag AND filtering: photo must have ALL specified tags
        if (tags != null && tags.Count > 0)
        {
            foreach (var tag in tags)
            {
                var normalizedTag = tag.Trim().ToLowerInvariant();
                query = query.Where(p => p.Tags.Any(t => t.TagName.ToLower() == normalizedTag));
            }
        }

        // OCR text search using trigram similarity (pg_trgm)
        if (!string.IsNullOrWhiteSpace(ocrText))
        {
            var searchText = $"%{ocrText.Trim()}%";
            query = query.Where(p => p.OcrText != null && EF.Functions.ILike(p.OcrText, searchText));
        }

        // General search term: searches both tag names and OCR text
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLowerInvariant();
            var likeTerm = $"%{term}%";
            query = query.Where(p =>
                p.Tags.Any(t => t.TagName.ToLower().Contains(term)) ||
                (p.OcrText != null && EF.Functions.ILike(p.OcrText, likeTerm)) ||
                p.FileName.ToLower().Contains(term));
        }

        // Date range filter on TakenAt
        if (from.HasValue)
        {
            query = query.Where(p => p.TakenAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(p => p.TakenAt <= to.Value);
        }

        // Count before paging
        var totalCount = await query.CountAsync(ct);

        // Apply sorting
        query = sortBy?.ToLowerInvariant() switch
        {
            "filename" => sortDescending ? query.OrderByDescending(p => p.FileName) : query.OrderBy(p => p.FileName),
            "takenat" => sortDescending ? query.OrderByDescending(p => p.TakenAt) : query.OrderBy(p => p.TakenAt),
            "filesize" => sortDescending ? query.OrderByDescending(p => p.FileSize) : query.OrderBy(p => p.FileSize),
            "indexedat" => sortDescending ? query.OrderByDescending(p => p.IndexedAt) : query.OrderBy(p => p.IndexedAt),
            _ => query.OrderByDescending(p => p.TakenAt ?? p.CreatedAt) // Default: newest first
        };

        // Apply paging
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<List<TagCount>> GetAllTagsWithCountsAsync(CancellationToken ct = default)
    {
        return await _context.Set<PhotoTag>()
            .Where(t => t.PhotoAsset.Status == PhotoAssetStatus.Indexed)
            .GroupBy(t => t.TagName.ToLower())
            .Select(g => new TagCount
            {
                TagName = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(tc => tc.Count)
            .ToListAsync(ct);
    }

    public async Task<PhotoAsset?> GetByIdWithTagsAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Set<PhotoAsset>()
            .Include(p => p.Tags)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task AddTagAsync(PhotoTag tag, CancellationToken ct = default)
    {
        await _context.Set<PhotoTag>().AddAsync(tag, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<PhotoTag?> GetTagByIdAsync(Guid tagId, CancellationToken ct = default)
    {
        return await _context.Set<PhotoTag>()
            .FirstOrDefaultAsync(t => t.Id == tagId, ct);
    }

    public async Task RemoveTagAsync(PhotoTag tag, CancellationToken ct = default)
    {
        _context.Set<PhotoTag>().Remove(tag);
        await _context.SaveChangesAsync(ct);
    }
```

- [ ] **Step 3: Verify compilation**

```bash
cd backend && dotnet build src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

---

## Task 2: SearchPhotosHandler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/SearchPhotos/SearchPhotosRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/SearchPhotos/SearchPhotosResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/SearchPhotos/PhotoAssetDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/SearchPhotos/SearchPhotosHandler.cs`

- [ ] **Step 1: Create SearchPhotosRequest**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/SearchPhotos/SearchPhotosRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.SearchPhotos;

public class SearchPhotosRequest : IRequest<SearchPhotosResponse>
{
    public string? Tags { get; set; }
    public string? OcrText { get; set; }
    public string? SearchTerm { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = true;
}
```

- [ ] **Step 2: Create PhotoAssetDto**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/SearchPhotos/PhotoAssetDto.cs
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.SearchPhotos;

public class PhotoAssetDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = string.Empty;

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("takenAt")]
    public DateTimeOffset? TakenAt { get; set; }

    [JsonPropertyName("indexedAt")]
    public DateTimeOffset? IndexedAt { get; set; }

    [JsonPropertyName("thumbnailUrl")]
    public string? ThumbnailUrl { get; set; }

    [JsonPropertyName("ocrTextExcerpt")]
    public string? OcrTextExcerpt { get; set; }

    [JsonPropertyName("tags")]
    public List<PhotoTagDto> Tags { get; set; } = new();
}

public class PhotoTagDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("tagName")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public float Confidence { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Create SearchPhotosResponse**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/SearchPhotos/SearchPhotosResponse.cs
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.SearchPhotos;

public class SearchPhotosResponse : BaseResponse
{
    public List<PhotoAssetDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
}
```

- [ ] **Step 4: Create SearchPhotosHandler**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/SearchPhotos/SearchPhotosHandler.cs
using Anela.Heblo.Domain.Features.PhotoBank;
using MediatR;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.SearchPhotos;

public class SearchPhotosHandler : IRequestHandler<SearchPhotosRequest, SearchPhotosResponse>
{
    private readonly IPhotoAssetRepository _repository;

    public SearchPhotosHandler(IPhotoAssetRepository repository)
    {
        _repository = repository;
    }

    public async Task<SearchPhotosResponse> Handle(SearchPhotosRequest request, CancellationToken cancellationToken)
    {
        // Parse comma-separated tags
        List<string>? tagList = null;
        if (!string.IsNullOrWhiteSpace(request.Tags))
        {
            tagList = request.Tags
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        var (items, totalCount) = await _repository.SearchAsync(
            tags: tagList,
            ocrText: request.OcrText,
            searchTerm: request.SearchTerm,
            from: request.From,
            to: request.To,
            sortBy: request.SortBy,
            sortDescending: request.SortDescending,
            page: request.PageNumber,
            pageSize: request.PageSize,
            ct: cancellationToken);

        var dtos = items.Select(MapToDto).ToList();

        return new SearchPhotosResponse
        {
            Items = dtos,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }

    private static PhotoAssetDto MapToDto(PhotoAsset asset)
    {
        return new PhotoAssetDto
        {
            Id = asset.Id,
            FileName = asset.FileName,
            MimeType = asset.MimeType,
            FileSize = asset.FileSize,
            Width = asset.Width,
            Height = asset.Height,
            TakenAt = asset.TakenAt,
            IndexedAt = asset.IndexedAt,
            ThumbnailUrl = asset.ThumbnailBlobPath != null
                ? $"/api/photo-bank/{asset.Id}/thumbnail"
                : null,
            OcrTextExcerpt = asset.OcrText != null
                ? (asset.OcrText.Length > 200 ? asset.OcrText[..200] + "..." : asset.OcrText)
                : null,
            Tags = asset.Tags.Select(t => new PhotoTagDto
            {
                Id = t.Id,
                TagName = t.TagName,
                Confidence = t.Confidence,
                Source = t.Source.ToString()
            }).OrderByDescending(t => t.Confidence).ToList()
        };
    }
}
```

- [ ] **Step 5: Verify compilation**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

---

## Task 3: GetPhotoDetailHandler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/GetPhotoDetail/GetPhotoDetailRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/GetPhotoDetail/GetPhotoDetailResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/GetPhotoDetail/GetPhotoDetailHandler.cs`

- [ ] **Step 1: Create GetPhotoDetailRequest**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/GetPhotoDetail/GetPhotoDetailRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.GetPhotoDetail;

public class GetPhotoDetailRequest : IRequest<GetPhotoDetailResponse>
{
    public Guid Id { get; set; }
}
```

- [ ] **Step 2: Create GetPhotoDetailResponse**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/GetPhotoDetail/GetPhotoDetailResponse.cs
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.PhotoBank.UseCases.SearchPhotos;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.GetPhotoDetail;

public class GetPhotoDetailResponse : BaseResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("oneDrivePath")]
    public string OneDrivePath { get; set; } = string.Empty;

    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = string.Empty;

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("takenAt")]
    public DateTimeOffset? TakenAt { get; set; }

    [JsonPropertyName("indexedAt")]
    public DateTimeOffset? IndexedAt { get; set; }

    [JsonPropertyName("thumbnailUrl")]
    public string? ThumbnailUrl { get; set; }

    [JsonPropertyName("ocrText")]
    public string? OcrText { get; set; }

    [JsonPropertyName("tags")]
    public List<PhotoTagDto> Tags { get; set; } = new();
}
```

- [ ] **Step 3: Create GetPhotoDetailHandler**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/GetPhotoDetail/GetPhotoDetailHandler.cs
using Anela.Heblo.Application.Features.PhotoBank.UseCases.SearchPhotos;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PhotoBank;
using MediatR;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.GetPhotoDetail;

public class GetPhotoDetailHandler : IRequestHandler<GetPhotoDetailRequest, GetPhotoDetailResponse>
{
    private readonly IPhotoAssetRepository _repository;

    public GetPhotoDetailHandler(IPhotoAssetRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetPhotoDetailResponse> Handle(GetPhotoDetailRequest request, CancellationToken cancellationToken)
    {
        var asset = await _repository.GetByIdWithTagsAsync(request.Id, cancellationToken);

        if (asset == null)
        {
            return new GetPhotoDetailResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ResourceNotFound,
                Params = new Dictionary<string, string>
                {
                    { "resource", "PhotoAsset" },
                    { "id", request.Id.ToString() }
                }
            };
        }

        return new GetPhotoDetailResponse
        {
            Id = asset.Id,
            FileName = asset.FileName,
            OneDrivePath = asset.OneDrivePath,
            MimeType = asset.MimeType,
            FileSize = asset.FileSize,
            Width = asset.Width,
            Height = asset.Height,
            TakenAt = asset.TakenAt,
            IndexedAt = asset.IndexedAt,
            ThumbnailUrl = asset.ThumbnailBlobPath != null
                ? $"/api/photo-bank/{asset.Id}/thumbnail"
                : null,
            OcrText = asset.OcrText,
            Tags = asset.Tags.Select(t => new PhotoTagDto
            {
                Id = t.Id,
                TagName = t.TagName,
                Confidence = t.Confidence,
                Source = t.Source.ToString()
            }).OrderByDescending(t => t.Confidence).ToList()
        };
    }
}
```

- [ ] **Step 4: Verify compilation**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

---

## Task 4: GetAllTagsHandler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/GetAllTags/GetAllTagsRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/GetAllTags/GetAllTagsResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/GetAllTags/GetAllTagsHandler.cs`

- [ ] **Step 1: Create GetAllTagsRequest**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/GetAllTags/GetAllTagsRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.GetAllTags;

public class GetAllTagsRequest : IRequest<GetAllTagsResponse>
{
}
```

- [ ] **Step 2: Create GetAllTagsResponse**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/GetAllTags/GetAllTagsResponse.cs
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.GetAllTags;

public class GetAllTagsResponse : BaseResponse
{
    [JsonPropertyName("tags")]
    public List<TagCountDto> Tags { get; set; } = new();
}

public class TagCountDto
{
    [JsonPropertyName("tagName")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; set; }
}
```

- [ ] **Step 3: Create GetAllTagsHandler**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/GetAllTags/GetAllTagsHandler.cs
using Anela.Heblo.Domain.Features.PhotoBank;
using MediatR;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.GetAllTags;

public class GetAllTagsHandler : IRequestHandler<GetAllTagsRequest, GetAllTagsResponse>
{
    private readonly IPhotoAssetRepository _repository;

    public GetAllTagsHandler(IPhotoAssetRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetAllTagsResponse> Handle(GetAllTagsRequest request, CancellationToken cancellationToken)
    {
        var tagCounts = await _repository.GetAllTagsWithCountsAsync(cancellationToken);

        return new GetAllTagsResponse
        {
            Tags = tagCounts.Select(tc => new TagCountDto
            {
                TagName = tc.TagName,
                Count = tc.Count
            }).ToList()
        };
    }
}
```

- [ ] **Step 4: Verify compilation**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

---

## Task 5: AddManualTagHandler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/AddManualTag/AddManualTagRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/AddManualTag/AddManualTagResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/AddManualTag/AddManualTagHandler.cs`

- [ ] **Step 1: Create AddManualTagRequest**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/AddManualTag/AddManualTagRequest.cs
using System.Text.Json.Serialization;
using MediatR;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.AddManualTag;

public class AddManualTagRequest : IRequest<AddManualTagResponse>
{
    [JsonPropertyName("photoAssetId")]
    public Guid PhotoAssetId { get; set; }

    [JsonPropertyName("tagName")]
    public string TagName { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Create AddManualTagResponse**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/AddManualTag/AddManualTagResponse.cs
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.AddManualTag;

public class AddManualTagResponse : BaseResponse
{
    [JsonPropertyName("tagId")]
    public Guid TagId { get; set; }

    [JsonPropertyName("tagName")]
    public string TagName { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Create AddManualTagHandler**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/AddManualTag/AddManualTagHandler.cs
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PhotoBank;
using MediatR;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.AddManualTag;

public class AddManualTagHandler : IRequestHandler<AddManualTagRequest, AddManualTagResponse>
{
    private readonly IPhotoAssetRepository _repository;

    public AddManualTagHandler(IPhotoAssetRepository repository)
    {
        _repository = repository;
    }

    public async Task<AddManualTagResponse> Handle(AddManualTagRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TagName))
        {
            return new AddManualTagResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ValidationError,
                Params = new Dictionary<string, string>
                {
                    { "field", "tagName" },
                    { "detail", "Tag name cannot be empty" }
                }
            };
        }

        var asset = await _repository.GetByIdWithTagsAsync(request.PhotoAssetId, cancellationToken);

        if (asset == null)
        {
            return new AddManualTagResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ResourceNotFound,
                Params = new Dictionary<string, string>
                {
                    { "resource", "PhotoAsset" },
                    { "id", request.PhotoAssetId.ToString() }
                }
            };
        }

        var normalizedTagName = request.TagName.Trim().ToLowerInvariant();

        // Check if tag already exists on this photo
        var existingTag = asset.Tags.FirstOrDefault(t =>
            t.TagName.ToLowerInvariant() == normalizedTagName);

        if (existingTag != null)
        {
            return new AddManualTagResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.DuplicateEntry,
                Params = new Dictionary<string, string>
                {
                    { "detail", $"Tag '{normalizedTagName}' already exists on this photo" }
                }
            };
        }

        var tag = new PhotoTag
        {
            Id = Guid.NewGuid(),
            PhotoAssetId = request.PhotoAssetId,
            TagName = normalizedTagName,
            Confidence = 1.0f,
            Source = TagSource.Manual
        };

        await _repository.AddTagAsync(tag, cancellationToken);

        return new AddManualTagResponse
        {
            TagId = tag.Id,
            TagName = tag.TagName
        };
    }
}
```

- [ ] **Step 4: Verify compilation**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

---

## Task 6: RemoveTagHandler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/RemoveTag/RemoveTagRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/RemoveTag/RemoveTagResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/RemoveTag/RemoveTagHandler.cs`

- [ ] **Step 1: Create RemoveTagRequest**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/RemoveTag/RemoveTagRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.RemoveTag;

public class RemoveTagRequest : IRequest<RemoveTagResponse>
{
    public Guid PhotoAssetId { get; set; }
    public Guid TagId { get; set; }
}
```

- [ ] **Step 2: Create RemoveTagResponse**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/RemoveTag/RemoveTagResponse.cs
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.RemoveTag;

public class RemoveTagResponse : BaseResponse
{
}
```

- [ ] **Step 3: Create RemoveTagHandler**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/RemoveTag/RemoveTagHandler.cs
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PhotoBank;
using MediatR;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.RemoveTag;

public class RemoveTagHandler : IRequestHandler<RemoveTagRequest, RemoveTagResponse>
{
    private readonly IPhotoAssetRepository _repository;

    public RemoveTagHandler(IPhotoAssetRepository repository)
    {
        _repository = repository;
    }

    public async Task<RemoveTagResponse> Handle(RemoveTagRequest request, CancellationToken cancellationToken)
    {
        var tag = await _repository.GetTagByIdAsync(request.TagId, cancellationToken);

        if (tag == null)
        {
            return new RemoveTagResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ResourceNotFound,
                Params = new Dictionary<string, string>
                {
                    { "resource", "PhotoTag" },
                    { "id", request.TagId.ToString() }
                }
            };
        }

        // Verify tag belongs to the specified photo
        if (tag.PhotoAssetId != request.PhotoAssetId)
        {
            return new RemoveTagResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ValidationError,
                Params = new Dictionary<string, string>
                {
                    { "detail", "Tag does not belong to the specified photo" }
                }
            };
        }

        // Only allow removing manual tags
        if (tag.Source != TagSource.Manual)
        {
            return new RemoveTagResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InvalidOperation,
                Params = new Dictionary<string, string>
                {
                    { "detail", "Only manually added tags can be removed" }
                }
            };
        }

        await _repository.RemoveTagAsync(tag, cancellationToken);

        return new RemoveTagResponse();
    }
}
```

- [ ] **Step 4: Verify compilation**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

---

## Task 7: TriggerSyncHandler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/TriggerSync/TriggerSyncRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/TriggerSync/TriggerSyncResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/TriggerSync/TriggerSyncHandler.cs`

- [ ] **Step 1: Create TriggerSyncRequest**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/TriggerSync/TriggerSyncRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.TriggerSync;

public class TriggerSyncRequest : IRequest<TriggerSyncResponse>
{
}
```

- [ ] **Step 2: Create TriggerSyncResponse**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/TriggerSync/TriggerSyncResponse.cs
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.TriggerSync;

public class TriggerSyncResponse : BaseResponse
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Create TriggerSyncHandler**

This handler enqueues a Hangfire background job for OneDrive sync. It uses `IBackgroundJobClient` from Hangfire to trigger the `SyncOneDrivePhotosJob` (created in Iteration 1).

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/TriggerSync/TriggerSyncHandler.cs
using Hangfire;
using Anela.Heblo.Application.Features.PhotoBank.Jobs;
using MediatR;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.TriggerSync;

public class TriggerSyncHandler : IRequestHandler<TriggerSyncRequest, TriggerSyncResponse>
{
    private readonly IBackgroundJobClient _backgroundJobClient;

    public TriggerSyncHandler(IBackgroundJobClient backgroundJobClient)
    {
        _backgroundJobClient = backgroundJobClient;
    }

    public Task<TriggerSyncResponse> Handle(TriggerSyncRequest request, CancellationToken cancellationToken)
    {
        _backgroundJobClient.Enqueue<SyncOneDrivePhotosJob>(job => job.ExecuteAsync(CancellationToken.None));

        return Task.FromResult(new TriggerSyncResponse
        {
            Message = "OneDrive photo sync has been enqueued"
        });
    }
}
```

- [ ] **Step 4: Verify compilation**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

---

## Task 8: PhotoBankController

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/PhotoBankController.cs`

- [ ] **Step 1: Create PhotoBankController**

Follows the exact pattern from `CatalogController`: extends `BaseApiController`, `[Authorize]`, `[ApiController]`, `[Route("api/[controller]")]`, uses `IMediator`.

```csharp
// backend/src/Anela.Heblo.API/Controllers/PhotoBankController.cs
using Anela.Heblo.Application.Features.PhotoBank.UseCases.AddManualTag;
using Anela.Heblo.Application.Features.PhotoBank.UseCases.GetAllTags;
using Anela.Heblo.Application.Features.PhotoBank.UseCases.GetPhotoDetail;
using Anela.Heblo.Application.Features.PhotoBank.UseCases.RemoveTag;
using Anela.Heblo.Application.Features.PhotoBank.UseCases.SearchPhotos;
using Anela.Heblo.Application.Features.PhotoBank.UseCases.TriggerSync;
using Anela.Heblo.API.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class PhotoBankController : BaseApiController
{
    private readonly IMediator _mediator;

    public PhotoBankController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Search and filter photos with pagination
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<SearchPhotosResponse>> SearchPhotos([FromQuery] SearchPhotosRequest request)
    {
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    /// <summary>
    /// Get full photo detail with all tags and metadata
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetPhotoDetailResponse>> GetPhotoDetail(Guid id)
    {
        var request = new GetPhotoDetailRequest { Id = id };
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    /// <summary>
    /// List all unique tags with counts (for filter UI)
    /// </summary>
    [HttpGet("tags")]
    public async Task<ActionResult<GetAllTagsResponse>> GetAllTags()
    {
        var request = new GetAllTagsRequest();
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    /// <summary>
    /// Add a manual tag to a photo
    /// </summary>
    [HttpPost("{id:guid}/tags")]
    public async Task<ActionResult<AddManualTagResponse>> AddManualTag(Guid id, [FromBody] AddManualTagRequest request)
    {
        request.PhotoAssetId = id;
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    /// <summary>
    /// Remove a manual tag from a photo
    /// </summary>
    [HttpDelete("{id:guid}/tags/{tagId:guid}")]
    public async Task<ActionResult<RemoveTagResponse>> RemoveTag(Guid id, Guid tagId)
    {
        var request = new RemoveTagRequest { PhotoAssetId = id, TagId = tagId };
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    /// <summary>
    /// Manually trigger OneDrive photo sync (admin)
    /// </summary>
    [HttpPost("sync")]
    public async Task<ActionResult<TriggerSyncResponse>> TriggerSync()
    {
        var request = new TriggerSyncRequest();
        var response = await _mediator.Send(request);
        return Ok(response);
    }
}
```

- [ ] **Step 2: Verify compilation**

```bash
cd backend && dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

---

## Task 9: Add PhotoBank Error Codes

**Files:**
- Edit: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`

- [ ] **Step 1: Add PhotoBank error codes**

Add the following block after the ShoptetOrders module errors (21XX) section, before the External Service errors (90XX):

```csharp
    // PhotoBank module errors (22XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    PhotoAssetNotFound = 2201,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    PhotoTagNotFound = 2202,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    PhotoTagAlreadyExists = 2203,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    PhotoTagRemovalNotAllowed = 2204,
```

> **Note:** The handlers currently use generic `ErrorCodes.ResourceNotFound`, `ErrorCodes.DuplicateEntry`, `ErrorCodes.ValidationError`, and `ErrorCodes.InvalidOperation`. These PhotoBank-specific codes are reserved for future use if more granular error handling is needed.

- [ ] **Step 2: Verify compilation**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

---

## Task 10: Backend Unit Tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/PhotoBank/SearchPhotosHandlerTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/PhotoBank/AddManualTagHandlerTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/PhotoBank/RemoveTagHandlerTests.cs`

- [ ] **Step 1: Create SearchPhotosHandlerTests**

```csharp
// backend/test/Anela.Heblo.Tests/Features/PhotoBank/SearchPhotosHandlerTests.cs
using Anela.Heblo.Application.Features.PhotoBank.UseCases.SearchPhotos;
using Anela.Heblo.Domain.Features.PhotoBank;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.PhotoBank;

public class SearchPhotosHandlerTests
{
    private readonly Mock<IPhotoAssetRepository> _repositoryMock;
    private readonly SearchPhotosHandler _handler;

    public SearchPhotosHandlerTests()
    {
        _repositoryMock = new Mock<IPhotoAssetRepository>();
        _handler = new SearchPhotosHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_NoFilters_ReturnsAllIndexedPhotos()
    {
        // Arrange
        var photos = new List<PhotoAsset>
        {
            CreateTestPhoto("photo1.jpg", new[] { "bottle", "outdoor" }),
            CreateTestPhoto("photo2.jpg", new[] { "person" }),
        };

        _repositoryMock
            .Setup(r => r.SearchAsync(
                null, null, null, null, null,
                null, true, 1, 50,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((photos, 2));

        var request = new SearchPhotosRequest { PageNumber = 1, PageSize = 50 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(50, result.PageSize);
    }

    [Fact]
    public async Task Handle_WithCommaSeparatedTags_ParsesCorrectly()
    {
        // Arrange
        var photos = new List<PhotoAsset>
        {
            CreateTestPhoto("photo1.jpg", new[] { "bottle", "outdoor" }),
        };

        _repositoryMock
            .Setup(r => r.SearchAsync(
                It.Is<List<string>>(tags => tags.Count == 2 && tags.Contains("bottle") && tags.Contains("outdoor")),
                null, null, null, null,
                null, true, 1, 50,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((photos, 1));

        var request = new SearchPhotosRequest { Tags = "bottle,outdoor", PageNumber = 1, PageSize = 50 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Items);
        _repositoryMock.Verify(r => r.SearchAsync(
            It.Is<List<string>>(tags => tags.Count == 2),
            null, null, null, null,
            null, true, 1, 50,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MapsOcrTextExcerpt_TruncatesLongText()
    {
        // Arrange
        var longOcrText = new string('A', 300);
        var photo = CreateTestPhoto("photo1.jpg", new[] { "label" });
        photo.OcrText = longOcrText;

        _repositoryMock
            .Setup(r => r.SearchAsync(
                null, null, null, null, null,
                null, true, 1, 50,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PhotoAsset> { photo }, 1));

        var request = new SearchPhotosRequest();

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Items[0].OcrTextExcerpt);
        Assert.Equal(203, result.Items[0].OcrTextExcerpt!.Length); // 200 chars + "..."
        Assert.EndsWith("...", result.Items[0].OcrTextExcerpt);
    }

    [Fact]
    public async Task Handle_MapsThumbnailUrl_UsesApiEndpoint()
    {
        // Arrange
        var photo = CreateTestPhoto("photo1.jpg", new[] { "bottle" });
        photo.ThumbnailBlobPath = "photo-thumbnails/abc.jpg";

        _repositoryMock
            .Setup(r => r.SearchAsync(
                null, null, null, null, null,
                null, true, 1, 50,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PhotoAsset> { photo }, 1));

        var request = new SearchPhotosRequest();

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal($"/api/photo-bank/{photo.Id}/thumbnail", result.Items[0].ThumbnailUrl);
    }

    [Fact]
    public async Task Handle_PaginationValues_PassedCorrectly()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.SearchAsync(
                null, null, null, null, null,
                "filename", false, 3, 25,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PhotoAsset>(), 100));

        var request = new SearchPhotosRequest
        {
            PageNumber = 3,
            PageSize = 25,
            SortBy = "filename",
            SortDescending = false
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.PageNumber);
        Assert.Equal(25, result.PageSize);
        Assert.Equal(100, result.TotalCount);
        Assert.Equal(4, result.TotalPages); // ceil(100/25)
    }

    private static PhotoAsset CreateTestPhoto(string fileName, string[] tagNames)
    {
        var assetId = Guid.NewGuid();
        return new PhotoAsset
        {
            Id = assetId,
            FileName = fileName,
            OneDriveItemId = $"onedrive-{Guid.NewGuid()}",
            OneDrivePath = $"/Photos/{fileName}",
            MimeType = "image/jpeg",
            FileSize = 1024 * 100,
            Width = 1920,
            Height = 1080,
            TakenAt = DateTimeOffset.UtcNow.AddDays(-7),
            IndexedAt = DateTimeOffset.UtcNow,
            Status = PhotoAssetStatus.Indexed,
            CreatedAt = DateTimeOffset.UtcNow,
            Tags = tagNames.Select(t => new PhotoTag
            {
                Id = Guid.NewGuid(),
                PhotoAssetId = assetId,
                TagName = t,
                Confidence = 0.95f,
                Source = TagSource.Auto
            }).ToList()
        };
    }
}
```

- [ ] **Step 2: Create AddManualTagHandlerTests**

```csharp
// backend/test/Anela.Heblo.Tests/Features/PhotoBank/AddManualTagHandlerTests.cs
using Anela.Heblo.Application.Features.PhotoBank.UseCases.AddManualTag;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PhotoBank;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.PhotoBank;

public class AddManualTagHandlerTests
{
    private readonly Mock<IPhotoAssetRepository> _repositoryMock;
    private readonly AddManualTagHandler _handler;

    public AddManualTagHandlerTests()
    {
        _repositoryMock = new Mock<IPhotoAssetRepository>();
        _handler = new AddManualTagHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ValidTag_AddsTagSuccessfully()
    {
        // Arrange
        var assetId = Guid.NewGuid();
        var asset = new PhotoAsset
        {
            Id = assetId,
            FileName = "photo.jpg",
            Tags = new List<PhotoTag>()
        };

        _repositoryMock
            .Setup(r => r.GetByIdWithTagsAsync(assetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset);

        var request = new AddManualTagRequest
        {
            PhotoAssetId = assetId,
            TagName = "Rose Water Toner"
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("rose water toner", result.TagName); // normalized to lowercase
        Assert.NotEqual(Guid.Empty, result.TagId);
        _repositoryMock.Verify(r => r.AddTagAsync(
            It.Is<PhotoTag>(t =>
                t.TagName == "rose water toner" &&
                t.Source == TagSource.Manual &&
                t.Confidence == 1.0f),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EmptyTagName_ReturnsValidationError()
    {
        // Arrange
        var request = new AddManualTagRequest
        {
            PhotoAssetId = Guid.NewGuid(),
            TagName = "   "
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.ValidationError, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_PhotoNotFound_ReturnsNotFound()
    {
        // Arrange
        var assetId = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.GetByIdWithTagsAsync(assetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PhotoAsset?)null);

        var request = new AddManualTagRequest
        {
            PhotoAssetId = assetId,
            TagName = "product"
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.ResourceNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_DuplicateTag_ReturnsDuplicateError()
    {
        // Arrange
        var assetId = Guid.NewGuid();
        var asset = new PhotoAsset
        {
            Id = assetId,
            FileName = "photo.jpg",
            Tags = new List<PhotoTag>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    PhotoAssetId = assetId,
                    TagName = "bottle",
                    Confidence = 0.95f,
                    Source = TagSource.Auto
                }
            }
        };

        _repositoryMock
            .Setup(r => r.GetByIdWithTagsAsync(assetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset);

        var request = new AddManualTagRequest
        {
            PhotoAssetId = assetId,
            TagName = "Bottle" // case-insensitive duplicate
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.DuplicateEntry, result.ErrorCode);
    }
}
```

- [ ] **Step 3: Create RemoveTagHandlerTests**

```csharp
// backend/test/Anela.Heblo.Tests/Features/PhotoBank/RemoveTagHandlerTests.cs
using Anela.Heblo.Application.Features.PhotoBank.UseCases.RemoveTag;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PhotoBank;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.PhotoBank;

public class RemoveTagHandlerTests
{
    private readonly Mock<IPhotoAssetRepository> _repositoryMock;
    private readonly RemoveTagHandler _handler;

    public RemoveTagHandlerTests()
    {
        _repositoryMock = new Mock<IPhotoAssetRepository>();
        _handler = new RemoveTagHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ManualTag_RemovesSuccessfully()
    {
        // Arrange
        var assetId = Guid.NewGuid();
        var tagId = Guid.NewGuid();
        var tag = new PhotoTag
        {
            Id = tagId,
            PhotoAssetId = assetId,
            TagName = "custom-tag",
            Source = TagSource.Manual,
            Confidence = 1.0f
        };

        _repositoryMock
            .Setup(r => r.GetTagByIdAsync(tagId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);

        var request = new RemoveTagRequest { PhotoAssetId = assetId, TagId = tagId };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        _repositoryMock.Verify(r => r.RemoveTagAsync(tag, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AutoTag_ReturnsInvalidOperation()
    {
        // Arrange
        var assetId = Guid.NewGuid();
        var tagId = Guid.NewGuid();
        var tag = new PhotoTag
        {
            Id = tagId,
            PhotoAssetId = assetId,
            TagName = "bottle",
            Source = TagSource.Auto,
            Confidence = 0.95f
        };

        _repositoryMock
            .Setup(r => r.GetTagByIdAsync(tagId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);

        var request = new RemoveTagRequest { PhotoAssetId = assetId, TagId = tagId };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidOperation, result.ErrorCode);
        _repositoryMock.Verify(r => r.RemoveTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TagNotFound_ReturnsNotFound()
    {
        // Arrange
        var tagId = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.GetTagByIdAsync(tagId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PhotoTag?)null);

        var request = new RemoveTagRequest { PhotoAssetId = Guid.NewGuid(), TagId = tagId };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.ResourceNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_TagBelongsToDifferentPhoto_ReturnsValidationError()
    {
        // Arrange
        var tagId = Guid.NewGuid();
        var tag = new PhotoTag
        {
            Id = tagId,
            PhotoAssetId = Guid.NewGuid(), // different photo
            TagName = "manual-tag",
            Source = TagSource.Manual,
            Confidence = 1.0f
        };

        _repositoryMock
            .Setup(r => r.GetTagByIdAsync(tagId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);

        var request = new RemoveTagRequest
        {
            PhotoAssetId = Guid.NewGuid(), // does not match tag's photo
            TagId = tagId
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.ValidationError, result.ErrorCode);
    }
}
```

- [ ] **Step 4: Run tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotoBank"
```

---

## Task 11: React Query Hooks

**Files:**
- Create: `frontend/src/api/hooks/usePhotoBank.ts`
- Edit: `frontend/src/api/client.ts`

- [ ] **Step 1: Register photoBank query key in QUERY_KEYS**

Add to the `QUERY_KEYS` object in `frontend/src/api/client.ts`, after the `expeditionListArchive` entry and before the comment line:

```typescript
  photoBank: ["photo-bank"] as const,
```

The block should look like:

```typescript
  expeditionListArchive: ["expedition-list-archive"] as const,
  photoBank: ["photo-bank"] as const,
  // Add more query keys as needed
```

- [ ] **Step 2: Create usePhotoBank.ts**

```typescript
// frontend/src/api/hooks/usePhotoBank.ts
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

// --- Types ---

export interface PhotoTagDto {
  id: string;
  tagName: string;
  confidence: number;
  source: string;
}

export interface PhotoAssetDto {
  id: string;
  fileName: string;
  mimeType: string;
  fileSize: number;
  width: number | null;
  height: number | null;
  takenAt: string | null;
  indexedAt: string | null;
  thumbnailUrl: string | null;
  ocrTextExcerpt: string | null;
  tags: PhotoTagDto[];
}

export interface SearchPhotosParams {
  tags?: string;
  ocrText?: string;
  searchTerm?: string;
  from?: string;
  to?: string;
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
}

export interface SearchPhotosResponse {
  items: PhotoAssetDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  success: boolean;
}

export interface PhotoDetailResponse {
  id: string;
  fileName: string;
  oneDrivePath: string;
  mimeType: string;
  fileSize: number;
  width: number | null;
  height: number | null;
  takenAt: string | null;
  indexedAt: string | null;
  thumbnailUrl: string | null;
  ocrText: string | null;
  tags: PhotoTagDto[];
  success: boolean;
}

export interface TagCountDto {
  tagName: string;
  count: number;
}

export interface GetAllTagsResponse {
  tags: TagCountDto[];
  success: boolean;
}

export interface AddManualTagResponse {
  tagId: string;
  tagName: string;
  success: boolean;
}

// --- Fetch functions ---

const fetchPhotos = async (params: SearchPhotosParams = {}): Promise<SearchPhotosResponse> => {
  const apiClient = getAuthenticatedApiClient();
  const searchParams = new URLSearchParams();

  if (params.tags) {
    searchParams.append('tags', params.tags);
  }
  if (params.ocrText) {
    searchParams.append('ocrText', params.ocrText);
  }
  if (params.searchTerm) {
    searchParams.append('searchTerm', params.searchTerm);
  }
  if (params.from) {
    searchParams.append('from', params.from);
  }
  if (params.to) {
    searchParams.append('to', params.to);
  }
  if (params.pageNumber !== undefined) {
    searchParams.append('pageNumber', params.pageNumber.toString());
  }
  if (params.pageSize !== undefined) {
    searchParams.append('pageSize', params.pageSize.toString());
  }
  if (params.sortBy) {
    searchParams.append('sortBy', params.sortBy);
  }
  if (params.sortDescending !== undefined) {
    searchParams.append('sortDescending', params.sortDescending.toString());
  }

  const queryString = searchParams.toString();
  const relativeUrl = `/api/photo-bank${queryString ? `?${queryString}` : ''}`;
  const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'GET',
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch photos: ${response.status} ${response.statusText}`);
  }

  return response.json();
};

const fetchPhotoDetail = async (id: string): Promise<PhotoDetailResponse> => {
  const apiClient = getAuthenticatedApiClient();
  const relativeUrl = `/api/photo-bank/${encodeURIComponent(id)}`;
  const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'GET',
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch photo detail: ${response.status} ${response.statusText}`);
  }

  return response.json();
};

const fetchAllTags = async (): Promise<GetAllTagsResponse> => {
  const apiClient = getAuthenticatedApiClient();
  const relativeUrl = '/api/photo-bank/tags';
  const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'GET',
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch tags: ${response.status} ${response.statusText}`);
  }

  return response.json();
};

const addManualTag = async (photoId: string, tagName: string): Promise<AddManualTagResponse> => {
  const apiClient = getAuthenticatedApiClient();
  const relativeUrl = `/api/photo-bank/${encodeURIComponent(photoId)}/tags`;
  const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ tagName }),
  });

  if (!response.ok) {
    throw new Error(`Failed to add tag: ${response.status} ${response.statusText}`);
  }

  return response.json();
};

const removeTag = async (photoId: string, tagId: string): Promise<void> => {
  const apiClient = getAuthenticatedApiClient();
  const relativeUrl = `/api/photo-bank/${encodeURIComponent(photoId)}/tags/${encodeURIComponent(tagId)}`;
  const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'DELETE',
  });

  if (!response.ok) {
    throw new Error(`Failed to remove tag: ${response.status} ${response.statusText}`);
  }
};

const triggerSync = async (): Promise<{ message: string }> => {
  const apiClient = getAuthenticatedApiClient();
  const relativeUrl = '/api/photo-bank/sync';
  const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'POST',
  });

  if (!response.ok) {
    throw new Error(`Failed to trigger sync: ${response.status} ${response.statusText}`);
  }

  return response.json();
};

// --- Hooks ---

export const usePhotoSearch = (params: SearchPhotosParams = {}) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.photoBank, 'search', params],
    queryFn: () => fetchPhotos(params),
    staleTime: 2 * 60 * 1000, // 2 minutes
    gcTime: 5 * 60 * 1000,
  });
};

export const usePhotoDetail = (id: string | null) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.photoBank, 'detail', id],
    queryFn: () => fetchPhotoDetail(id!),
    enabled: !!id,
    staleTime: 5 * 60 * 1000,
    gcTime: 10 * 60 * 1000,
  });
};

export const usePhotoTags = () => {
  return useQuery({
    queryKey: [...QUERY_KEYS.photoBank, 'tags'],
    queryFn: () => fetchAllTags(),
    staleTime: 5 * 60 * 1000,
    gcTime: 10 * 60 * 1000,
  });
};

export const useAddManualTag = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ photoId, tagName }: { photoId: string; tagName: string }) =>
      addManualTag(photoId, tagName),
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.photoBank, 'detail', variables.photoId] });
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.photoBank, 'search'] });
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.photoBank, 'tags'] });
    },
  });
};

export const useRemoveTag = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ photoId, tagId }: { photoId: string; tagId: string }) =>
      removeTag(photoId, tagId),
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.photoBank, 'detail', variables.photoId] });
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.photoBank, 'search'] });
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.photoBank, 'tags'] });
    },
  });
};

export const useTriggerSync = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => triggerSync(),
    onSuccess: () => {
      // Invalidate photo queries after sync trigger (data will change after sync completes)
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.photoBank });
    },
  });
};
```

- [ ] **Step 3: Verify build**

```bash
cd frontend && npm run build
```

---

## Task 12: PhotoBankPage Component

**Files:**
- Create: `frontend/src/components/photo-bank/PhotoBankPage.tsx`

- [ ] **Step 1: Create PhotoBankPage**

Main page component with URL-based filter state, tag filter chips, search input, and grid layout. Follows CatalogList pattern with `useSearchParams`, separate input vs applied state, and `PAGE_CONTAINER_HEIGHT`.

```tsx
// frontend/src/components/photo-bank/PhotoBankPage.tsx
import React, { useState, useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Search, Filter, X, RefreshCw, Loader2, Image } from 'lucide-react';
import { PAGE_CONTAINER_HEIGHT } from '../../constants/layout';
import {
  usePhotoSearch,
  usePhotoTags,
  useTriggerSync,
  SearchPhotosParams,
} from '../../api/hooks/usePhotoBank';
import PhotoGrid from './PhotoGrid';
import PhotoDetailPanel from './PhotoDetailPanel';
import Pagination from '../common/Pagination';

const PhotoBankPage: React.FC = () => {
  const [searchParams, setSearchParams] = useSearchParams();

  // Parse URL params
  const getParam = (key: string) => searchParams.get(key) || '';
  const getNumParam = (key: string, defaultVal: number) => {
    const val = searchParams.get(key);
    return val ? parseInt(val, 10) : defaultVal;
  };

  // Filter input state (separate from applied filters for "Apply" button pattern)
  const [searchTermInput, setSearchTermInput] = useState(getParam('searchTerm'));
  const [ocrTextInput, setOcrTextInput] = useState(getParam('ocrText'));
  const [dateFromInput, setDateFromInput] = useState(getParam('from'));
  const [dateToInput, setDateToInput] = useState(getParam('to'));

  // Applied filter state from URL
  const appliedSearchTerm = getParam('searchTerm');
  const appliedOcrText = getParam('ocrText');
  const appliedTags = getParam('tags');
  const appliedFrom = getParam('from');
  const appliedTo = getParam('to');
  const pageNumber = getNumParam('page', 1);
  const pageSize = getNumParam('pageSize', 50);
  const sortBy = getParam('sortBy') || undefined;
  const sortDescending = searchParams.get('sortDesc') !== 'false';

  // Selected photo detail
  const [selectedPhotoId, setSelectedPhotoId] = useState<string | null>(null);

  // Build search params
  const queryParams: SearchPhotosParams = {
    searchTerm: appliedSearchTerm || undefined,
    ocrText: appliedOcrText || undefined,
    tags: appliedTags || undefined,
    from: appliedFrom || undefined,
    to: appliedTo || undefined,
    pageNumber,
    pageSize,
    sortBy,
    sortDescending,
  };

  const { data, isLoading, isError, error } = usePhotoSearch(queryParams);
  const { data: tagsData } = usePhotoTags();
  const syncMutation = useTriggerSync();

  // URL update helper
  const updateUrl = useCallback(
    (updates: Record<string, string | undefined>) => {
      const newParams = new URLSearchParams(searchParams);
      Object.entries(updates).forEach(([key, value]) => {
        if (value) {
          newParams.set(key, value);
        } else {
          newParams.delete(key);
        }
      });
      // Reset to page 1 when filters change (unless only page is changing)
      if (!('page' in updates)) {
        newParams.set('page', '1');
      }
      setSearchParams(newParams);
    },
    [searchParams, setSearchParams],
  );

  // Apply filters
  const handleApplyFilters = () => {
    updateUrl({
      searchTerm: searchTermInput || undefined,
      ocrText: ocrTextInput || undefined,
      from: dateFromInput || undefined,
      to: dateToInput || undefined,
    });
  };

  // Handle Enter key in search inputs
  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') {
      handleApplyFilters();
    }
  };

  // Toggle tag filter
  const handleTagToggle = (tagName: string) => {
    const currentTags = appliedTags ? appliedTags.split(',') : [];
    const isActive = currentTags.includes(tagName);
    const newTags = isActive
      ? currentTags.filter((t) => t !== tagName)
      : [...currentTags, tagName];
    updateUrl({ tags: newTags.length > 0 ? newTags.join(',') : undefined });
  };

  // Clear all filters
  const handleClearFilters = () => {
    setSearchTermInput('');
    setOcrTextInput('');
    setDateFromInput('');
    setDateToInput('');
    setSearchParams(new URLSearchParams());
  };

  // Page change
  const handlePageChange = (page: number) => {
    updateUrl({ page: page.toString() });
  };

  const handlePageSizeChange = (size: number) => {
    updateUrl({ pageSize: size.toString(), page: '1' });
  };

  const hasActiveFilters =
    !!appliedSearchTerm || !!appliedOcrText || !!appliedTags || !!appliedFrom || !!appliedTo;

  const activeTags = appliedTags ? appliedTags.split(',') : [];

  return (
    <div className="flex flex-col" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      {/* Header */}
      <div className="flex-shrink-0 bg-white border-b border-gray-200 px-4 py-3">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Image className="h-5 w-5 text-gray-600" />
            <h1 className="text-lg font-semibold text-gray-900">Fotobank</h1>
            {data && (
              <span className="text-sm text-gray-500">
                ({data.totalCount} fotek)
              </span>
            )}
          </div>
          <button
            onClick={() => syncMutation.mutate()}
            disabled={syncMutation.isPending}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 disabled:opacity-50"
          >
            <RefreshCw className={`h-3.5 w-3.5 ${syncMutation.isPending ? 'animate-spin' : ''}`} />
            Sync
          </button>
        </div>
      </div>

      {/* Filters */}
      <div className="flex-shrink-0 bg-white border-b border-gray-200 px-4 py-2 space-y-2">
        {/* Search row */}
        <div className="flex items-center gap-2">
          <div className="relative flex-1">
            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-400" />
            <input
              type="text"
              value={searchTermInput}
              onChange={(e) => setSearchTermInput(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Hledat ve fotkách (tagy, OCR text, název souboru)..."
              className="w-full pl-9 pr-3 py-1.5 text-sm border border-gray-300 rounded-md focus:ring-1 focus:ring-blue-500 focus:border-blue-500"
            />
          </div>
          <div className="relative">
            <input
              type="text"
              value={ocrTextInput}
              onChange={(e) => setOcrTextInput(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="OCR text..."
              className="w-48 px-3 py-1.5 text-sm border border-gray-300 rounded-md focus:ring-1 focus:ring-blue-500 focus:border-blue-500"
            />
          </div>
          <input
            type="date"
            value={dateFromInput}
            onChange={(e) => setDateFromInput(e.target.value)}
            className="w-36 px-2 py-1.5 text-sm border border-gray-300 rounded-md focus:ring-1 focus:ring-blue-500 focus:border-blue-500"
          />
          <span className="text-sm text-gray-400">-</span>
          <input
            type="date"
            value={dateToInput}
            onChange={(e) => setDateToInput(e.target.value)}
            className="w-36 px-2 py-1.5 text-sm border border-gray-300 rounded-md focus:ring-1 focus:ring-blue-500 focus:border-blue-500"
          />
          <button
            onClick={handleApplyFilters}
            className="inline-flex items-center gap-1 px-3 py-1.5 text-sm font-medium text-white bg-blue-600 rounded-md hover:bg-blue-700"
          >
            <Filter className="h-3.5 w-3.5" />
            Filtrovat
          </button>
          {hasActiveFilters && (
            <button
              onClick={handleClearFilters}
              className="inline-flex items-center gap-1 px-3 py-1.5 text-sm font-medium text-gray-600 bg-gray-100 rounded-md hover:bg-gray-200"
            >
              <X className="h-3.5 w-3.5" />
              Vymazat
            </button>
          )}
        </div>

        {/* Tag filter bar */}
        {tagsData && tagsData.tags.length > 0 && (
          <div className="flex items-center gap-1.5 overflow-x-auto pb-1">
            <span className="text-xs text-gray-500 flex-shrink-0">Tagy:</span>
            {tagsData.tags.slice(0, 30).map((tag) => (
              <button
                key={tag.tagName}
                onClick={() => handleTagToggle(tag.tagName)}
                className={`inline-flex items-center gap-1 px-2 py-0.5 text-xs rounded-full whitespace-nowrap transition-colors ${
                  activeTags.includes(tag.tagName)
                    ? 'bg-blue-100 text-blue-700 border border-blue-300'
                    : 'bg-gray-100 text-gray-600 border border-gray-200 hover:bg-gray-200'
                }`}
              >
                {tag.tagName}
                <span className="text-gray-400">({tag.count})</span>
              </button>
            ))}
          </div>
        )}
      </div>

      {/* Content area */}
      <div className="flex-1 overflow-hidden flex">
        {/* Main grid */}
        <div className="flex-1 flex flex-col overflow-hidden">
          {isLoading ? (
            <div className="flex-1 flex items-center justify-center">
              <Loader2 className="h-8 w-8 animate-spin text-gray-400" />
            </div>
          ) : isError ? (
            <div className="flex-1 flex items-center justify-center">
              <div className="text-center text-red-500">
                <p className="font-medium">Chyba pri nacitani fotek</p>
                <p className="text-sm">{(error as Error)?.message || 'Neznama chyba'}</p>
              </div>
            </div>
          ) : data && data.items.length === 0 ? (
            <div className="flex-1 flex items-center justify-center">
              <div className="text-center text-gray-500">
                <Image className="h-12 w-12 mx-auto mb-2 text-gray-300" />
                <p className="font-medium">Zadne fotky nenalezeny</p>
                {hasActiveFilters && (
                  <p className="text-sm mt-1">Zkuste zmenit filtry</p>
                )}
              </div>
            </div>
          ) : data ? (
            <>
              <div className="flex-1 overflow-y-auto p-4">
                <PhotoGrid
                  photos={data.items}
                  onPhotoClick={(id) => setSelectedPhotoId(id)}
                  selectedPhotoId={selectedPhotoId}
                />
              </div>
              <Pagination
                totalCount={data.totalCount}
                pageNumber={data.pageNumber}
                pageSize={data.pageSize}
                totalPages={data.totalPages}
                onPageChange={handlePageChange}
                onPageSizeChange={handlePageSizeChange}
                isFiltered={hasActiveFilters}
              />
            </>
          ) : null}
        </div>

        {/* Detail panel */}
        {selectedPhotoId && (
          <PhotoDetailPanel
            photoId={selectedPhotoId}
            onClose={() => setSelectedPhotoId(null)}
          />
        )}
      </div>
    </div>
  );
};

export default PhotoBankPage;
```

- [ ] **Step 2: Verify build**

```bash
cd frontend && npm run build
```

---

## Task 13: PhotoGrid Component

**Files:**
- Create: `frontend/src/components/photo-bank/PhotoGrid.tsx`

- [ ] **Step 1: Create PhotoGrid**

Responsive grid of photo thumbnails with lazy loading. Shows filename, tag chips, and selection highlight.

```tsx
// frontend/src/components/photo-bank/PhotoGrid.tsx
import React from 'react';
import { PhotoAssetDto } from '../../api/hooks/usePhotoBank';
import { Image as ImageIcon } from 'lucide-react';

interface PhotoGridProps {
  photos: PhotoAssetDto[];
  onPhotoClick: (id: string) => void;
  selectedPhotoId: string | null;
}

const formatFileSize = (bytes: number): string => {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
};

const PhotoGrid: React.FC<PhotoGridProps> = ({ photos, onPhotoClick, selectedPhotoId }) => {
  return (
    <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-3">
      {photos.map((photo) => (
        <div
          key={photo.id}
          onClick={() => onPhotoClick(photo.id)}
          className={`group cursor-pointer rounded-lg border overflow-hidden transition-all hover:shadow-md ${
            selectedPhotoId === photo.id
              ? 'border-blue-500 ring-2 ring-blue-200'
              : 'border-gray-200 hover:border-gray-300'
          }`}
        >
          {/* Thumbnail */}
          <div className="aspect-square bg-gray-100 relative">
            {photo.thumbnailUrl ? (
              <img
                src={photo.thumbnailUrl}
                alt={photo.fileName}
                loading="lazy"
                className="w-full h-full object-cover"
                onError={(e) => {
                  (e.target as HTMLImageElement).style.display = 'none';
                  (e.target as HTMLImageElement).nextElementSibling?.classList.remove('hidden');
                }}
              />
            ) : null}
            <div className={`absolute inset-0 flex items-center justify-center ${photo.thumbnailUrl ? 'hidden' : ''}`}>
              <ImageIcon className="h-8 w-8 text-gray-300" />
            </div>
          </div>

          {/* Info */}
          <div className="p-2">
            <p className="text-xs font-medium text-gray-800 truncate" title={photo.fileName}>
              {photo.fileName}
            </p>
            <p className="text-xs text-gray-400 mt-0.5">
              {formatFileSize(photo.fileSize)}
              {photo.width && photo.height && ` \u00B7 ${photo.width}\u00D7${photo.height}`}
            </p>
            {/* Top tags */}
            {photo.tags.length > 0 && (
              <div className="flex flex-wrap gap-1 mt-1">
                {photo.tags.slice(0, 3).map((tag) => (
                  <span
                    key={tag.id}
                    className={`inline-block px-1.5 py-0 text-[10px] rounded-full ${
                      tag.source === 'Manual'
                        ? 'bg-green-100 text-green-700'
                        : 'bg-gray-100 text-gray-600'
                    }`}
                  >
                    {tag.tagName}
                  </span>
                ))}
                {photo.tags.length > 3 && (
                  <span className="text-[10px] text-gray-400">
                    +{photo.tags.length - 3}
                  </span>
                )}
              </div>
            )}
          </div>
        </div>
      ))}
    </div>
  );
};

export default PhotoGrid;
```

- [ ] **Step 2: Verify build**

```bash
cd frontend && npm run build
```

---

## Task 14: PhotoDetailPanel Component

**Files:**
- Create: `frontend/src/components/photo-bank/PhotoDetailPanel.tsx`

- [ ] **Step 1: Create PhotoDetailPanel**

Slide-out panel (same pattern as Catalog detail) showing larger preview, all tags (AI + manual) as chips with add/remove, OCR text, metadata, and "Open in OneDrive" link.

```tsx
// frontend/src/components/photo-bank/PhotoDetailPanel.tsx
import React, { useState } from 'react';
import {
  X,
  Plus,
  Trash2,
  ExternalLink,
  FileText,
  Tag,
  Calendar,
  HardDrive,
  Loader2,
} from 'lucide-react';
import {
  usePhotoDetail,
  useAddManualTag,
  useRemoveTag,
  PhotoTagDto,
} from '../../api/hooks/usePhotoBank';

interface PhotoDetailPanelProps {
  photoId: string;
  onClose: () => void;
}

const formatFileSize = (bytes: number): string => {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
};

const formatDate = (dateStr: string | null): string => {
  if (!dateStr) return '-';
  return new Date(dateStr).toLocaleDateString('cs-CZ', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
};

const PhotoDetailPanel: React.FC<PhotoDetailPanelProps> = ({ photoId, onClose }) => {
  const { data, isLoading, isError } = usePhotoDetail(photoId);
  const addTagMutation = useAddManualTag();
  const removeTagMutation = useRemoveTag();
  const [newTagInput, setNewTagInput] = useState('');
  const [showTagInput, setShowTagInput] = useState(false);

  const handleAddTag = () => {
    if (!newTagInput.trim()) return;
    addTagMutation.mutate(
      { photoId, tagName: newTagInput.trim() },
      {
        onSuccess: () => {
          setNewTagInput('');
          setShowTagInput(false);
        },
      },
    );
  };

  const handleRemoveTag = (tag: PhotoTagDto) => {
    if (tag.source !== 'Manual') return;
    removeTagMutation.mutate({ photoId, tagId: tag.id });
  };

  const handleTagKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') {
      handleAddTag();
    }
    if (e.key === 'Escape') {
      setShowTagInput(false);
      setNewTagInput('');
    }
  };

  return (
    <div className="w-96 flex-shrink-0 border-l border-gray-200 bg-white overflow-y-auto">
      {/* Panel header */}
      <div className="sticky top-0 bg-white border-b border-gray-200 px-4 py-3 flex items-center justify-between z-10">
        <h2 className="text-sm font-semibold text-gray-900">Detail fotky</h2>
        <button
          onClick={onClose}
          className="p-1 rounded-md hover:bg-gray-100 text-gray-400 hover:text-gray-600"
        >
          <X className="h-4 w-4" />
        </button>
      </div>

      {isLoading ? (
        <div className="flex items-center justify-center py-12">
          <Loader2 className="h-6 w-6 animate-spin text-gray-400" />
        </div>
      ) : isError || !data ? (
        <div className="px-4 py-8 text-center text-red-500 text-sm">
          Chyba pri nacitani detailu
        </div>
      ) : (
        <div className="p-4 space-y-4">
          {/* Thumbnail preview */}
          <div className="bg-gray-100 rounded-lg overflow-hidden">
            {data.thumbnailUrl ? (
              <img
                src={data.thumbnailUrl}
                alt={data.fileName}
                className="w-full h-auto"
              />
            ) : (
              <div className="h-48 flex items-center justify-center text-gray-300">
                <FileText className="h-12 w-12" />
              </div>
            )}
          </div>

          {/* File info */}
          <div>
            <h3 className="text-sm font-medium text-gray-900 break-all">
              {data.fileName}
            </h3>
            <div className="mt-2 space-y-1 text-xs text-gray-500">
              <div className="flex items-center gap-1.5">
                <HardDrive className="h-3.5 w-3.5" />
                <span>{formatFileSize(data.fileSize)}</span>
                {data.width && data.height && (
                  <span className="ml-1">{data.width} x {data.height} px</span>
                )}
              </div>
              <div className="flex items-center gap-1.5">
                <Calendar className="h-3.5 w-3.5" />
                <span>Porizeno: {formatDate(data.takenAt)}</span>
              </div>
              <div className="flex items-center gap-1.5">
                <Calendar className="h-3.5 w-3.5" />
                <span>Indexovano: {formatDate(data.indexedAt)}</span>
              </div>
            </div>
          </div>

          {/* Tags */}
          <div>
            <div className="flex items-center justify-between mb-2">
              <h4 className="text-xs font-semibold text-gray-700 uppercase tracking-wide flex items-center gap-1">
                <Tag className="h-3.5 w-3.5" />
                Tagy ({data.tags.length})
              </h4>
              <button
                onClick={() => setShowTagInput(!showTagInput)}
                className="p-1 rounded-md hover:bg-gray-100 text-gray-400 hover:text-gray-600"
                title="Pridat tag"
              >
                <Plus className="h-3.5 w-3.5" />
              </button>
            </div>

            {/* Add tag input */}
            {showTagInput && (
              <div className="flex items-center gap-1 mb-2">
                <input
                  type="text"
                  value={newTagInput}
                  onChange={(e) => setNewTagInput(e.target.value)}
                  onKeyDown={handleTagKeyDown}
                  placeholder="Nazev tagu..."
                  className="flex-1 px-2 py-1 text-xs border border-gray-300 rounded focus:ring-1 focus:ring-blue-500 focus:border-blue-500"
                  autoFocus
                />
                <button
                  onClick={handleAddTag}
                  disabled={!newTagInput.trim() || addTagMutation.isPending}
                  className="px-2 py-1 text-xs font-medium text-white bg-blue-600 rounded hover:bg-blue-700 disabled:opacity-50"
                >
                  {addTagMutation.isPending ? '...' : 'Pridat'}
                </button>
              </div>
            )}

            {/* Tag list */}
            <div className="flex flex-wrap gap-1.5">
              {data.tags.map((tag) => (
                <span
                  key={tag.id}
                  className={`inline-flex items-center gap-1 px-2 py-0.5 text-xs rounded-full ${
                    tag.source === 'Manual'
                      ? 'bg-green-100 text-green-700 border border-green-200'
                      : 'bg-gray-100 text-gray-600 border border-gray-200'
                  }`}
                >
                  {tag.tagName}
                  {tag.source === 'Auto' && (
                    <span className="text-gray-400 text-[10px]">
                      {(tag.confidence * 100).toFixed(0)}%
                    </span>
                  )}
                  {tag.source === 'Manual' && (
                    <button
                      onClick={(e) => {
                        e.stopPropagation();
                        handleRemoveTag(tag);
                      }}
                      className="ml-0.5 p-0.5 rounded-full hover:bg-green-200 text-green-600"
                      title="Odebrat tag"
                    >
                      <Trash2 className="h-2.5 w-2.5" />
                    </button>
                  )}
                </span>
              ))}
              {data.tags.length === 0 && (
                <span className="text-xs text-gray-400 italic">Zadne tagy</span>
              )}
            </div>
          </div>

          {/* OCR Text */}
          {data.ocrText && (
            <div>
              <h4 className="text-xs font-semibold text-gray-700 uppercase tracking-wide mb-1 flex items-center gap-1">
                <FileText className="h-3.5 w-3.5" />
                OCR Text
              </h4>
              <div className="bg-gray-50 rounded-md p-2 text-xs text-gray-600 max-h-40 overflow-y-auto whitespace-pre-wrap">
                {data.ocrText}
              </div>
            </div>
          )}

          {/* OneDrive link */}
          <div className="pt-2 border-t border-gray-200">
            <p className="text-xs text-gray-400 truncate" title={data.oneDrivePath}>
              <ExternalLink className="h-3 w-3 inline mr-1" />
              {data.oneDrivePath}
            </p>
          </div>
        </div>
      )}
    </div>
  );
};

export default PhotoDetailPanel;
```

- [ ] **Step 2: Verify build**

```bash
cd frontend && npm run build
```

---

## Task 15: Sidebar Navigation & Route Registration

**Files:**
- Edit: `frontend/src/components/Layout/Sidebar.tsx`
- Edit: `frontend/src/App.tsx`

- [ ] **Step 1: Add "Image" icon import to Sidebar.tsx**

In `frontend/src/components/Layout/Sidebar.tsx`, add `ImageIcon` (aliased to avoid conflict with HTML Image) to the lucide-react import on line 1-21. Replace the existing import block:

Find the `lucide-react` import and add `Image as ImageIcon` to it. The import block at the top of the file should include:

```typescript
import {
  LayoutDashboard,
  Package,
  ShoppingCart,
  ChevronDown,
  ChevronRight,
  PanelLeftClose,
  PanelLeftOpen,
  Menu,
  DollarSign,
  Cog,
  Truck,
  Bot,
  Newspaper,
  Users,
  ExternalLink,
  FileText,
  Database,
  Image as ImageIcon,
} from "lucide-react";
```

- [ ] **Step 2: Add Photo Bank navigation section to Sidebar.tsx**

Add a new "Media" section to the `navigationSections` array in `frontend/src/components/Layout/Sidebar.tsx`, after the Knowledgebase section (line 298, before the closing `];`):

```typescript
    {
      id: 'media',
      name: 'Media',
      icon: ImageIcon,
      type: 'section' as const,
      items: [
        {
          id: 'photo-bank',
          name: 'Fotobank',
          href: '/photo-bank',
        },
      ],
    },
```

Insert this block between the knowledgebase section closing `},` (line 298) and the `];` (line 299).

- [ ] **Step 3: Add PhotoBankPage import to App.tsx**

Add the import at the top of `frontend/src/App.tsx`, after the other page imports (around line 38):

```typescript
import PhotoBankPage from './components/photo-bank/PhotoBankPage';
```

- [ ] **Step 4: Add Route to App.tsx**

Add the route inside the `<Routes>` block, before the closing `</Routes>` tag (around line 461):

```tsx
                        <Route
                          path="/photo-bank"
                          element={<PhotoBankPage />}
                        />
```

Insert this after the Knowledge Base Feedback route and before the `</Routes>` closing tag.

- [ ] **Step 5: Verify build**

```bash
cd frontend && npm run build
```

---

## Task 16: Frontend Component Tests

**Files:**
- Create: `frontend/src/components/photo-bank/__tests__/PhotoGrid.test.tsx`
- Create: `frontend/src/components/photo-bank/__tests__/PhotoDetailPanel.test.tsx`

- [ ] **Step 1: Create PhotoGrid.test.tsx**

```tsx
// frontend/src/components/photo-bank/__tests__/PhotoGrid.test.tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom';
import PhotoGrid from '../PhotoGrid';
import { PhotoAssetDto } from '../../../api/hooks/usePhotoBank';

const mockPhotos: PhotoAssetDto[] = [
  {
    id: '1',
    fileName: 'rose-water-toner.jpg',
    mimeType: 'image/jpeg',
    fileSize: 1048576, // 1 MB
    width: 1920,
    height: 1080,
    takenAt: '2026-01-15T10:00:00Z',
    indexedAt: '2026-01-16T10:00:00Z',
    thumbnailUrl: '/api/photo-bank/1/thumbnail',
    ocrTextExcerpt: 'Rose Water Toner 100ml',
    tags: [
      { id: 't1', tagName: 'bottle', confidence: 0.95, source: 'Auto' },
      { id: 't2', tagName: 'label', confidence: 0.88, source: 'Auto' },
      { id: 't3', tagName: 'product', confidence: 0.82, source: 'Auto' },
      { id: 't4', tagName: 'rose water', confidence: 1.0, source: 'Manual' },
    ],
  },
  {
    id: '2',
    fileName: 'lifestyle-photo.jpg',
    mimeType: 'image/jpeg',
    fileSize: 2097152, // 2 MB
    width: 3840,
    height: 2160,
    takenAt: '2026-02-01T12:00:00Z',
    indexedAt: '2026-02-02T12:00:00Z',
    thumbnailUrl: '/api/photo-bank/2/thumbnail',
    ocrTextExcerpt: null,
    tags: [
      { id: 't5', tagName: 'person', confidence: 0.91, source: 'Auto' },
    ],
  },
];

describe('PhotoGrid', () => {
  it('renders all photos', () => {
    render(
      <PhotoGrid
        photos={mockPhotos}
        onPhotoClick={jest.fn()}
        selectedPhotoId={null}
      />,
    );

    expect(screen.getByText('rose-water-toner.jpg')).toBeInTheDocument();
    expect(screen.getByText('lifestyle-photo.jpg')).toBeInTheDocument();
  });

  it('shows file size and dimensions', () => {
    render(
      <PhotoGrid
        photos={mockPhotos}
        onPhotoClick={jest.fn()}
        selectedPhotoId={null}
      />,
    );

    expect(screen.getByText(/1\.0 MB/)).toBeInTheDocument();
    expect(screen.getByText(/1920.*1080/)).toBeInTheDocument();
  });

  it('shows up to 3 tags with overflow count', () => {
    render(
      <PhotoGrid
        photos={mockPhotos}
        onPhotoClick={jest.fn()}
        selectedPhotoId={null}
      />,
    );

    // First photo has 4 tags, shows 3 + "+1"
    expect(screen.getByText('bottle')).toBeInTheDocument();
    expect(screen.getByText('label')).toBeInTheDocument();
    expect(screen.getByText('product')).toBeInTheDocument();
    expect(screen.getByText('+1')).toBeInTheDocument();
  });

  it('calls onPhotoClick when photo is clicked', () => {
    const onPhotoClick = jest.fn();
    render(
      <PhotoGrid
        photos={mockPhotos}
        onPhotoClick={onPhotoClick}
        selectedPhotoId={null}
      />,
    );

    fireEvent.click(screen.getByText('rose-water-toner.jpg'));
    expect(onPhotoClick).toHaveBeenCalledWith('1');
  });

  it('highlights selected photo', () => {
    const { container } = render(
      <PhotoGrid
        photos={mockPhotos}
        onPhotoClick={jest.fn()}
        selectedPhotoId="1"
      />,
    );

    const selectedCard = container.querySelector('.border-blue-500');
    expect(selectedCard).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Create PhotoDetailPanel.test.tsx**

```tsx
// frontend/src/components/photo-bank/__tests__/PhotoDetailPanel.test.tsx
import React from 'react';
import { render, screen } from '@testing-library/react';
import '@testing-library/jest-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import PhotoDetailPanel from '../PhotoDetailPanel';

// Mock the hooks
jest.mock('../../../api/hooks/usePhotoBank', () => ({
  usePhotoDetail: jest.fn(),
  useAddManualTag: jest.fn(() => ({
    mutate: jest.fn(),
    isPending: false,
  })),
  useRemoveTag: jest.fn(() => ({
    mutate: jest.fn(),
    isPending: false,
  })),
}));

import { usePhotoDetail } from '../../../api/hooks/usePhotoBank';

const mockUsePhotoDetail = usePhotoDetail as jest.MockedFunction<typeof usePhotoDetail>;

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { retry: false },
  },
});

const renderWithProviders = (ui: React.ReactElement) => {
  return render(
    <QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>,
  );
};

describe('PhotoDetailPanel', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('shows loading state', () => {
    mockUsePhotoDetail.mockReturnValue({
      data: undefined,
      isLoading: true,
      isError: false,
    } as any);

    renderWithProviders(
      <PhotoDetailPanel photoId="1" onClose={jest.fn()} />,
    );

    expect(screen.getByText('Detail fotky')).toBeInTheDocument();
  });

  it('shows photo detail when loaded', () => {
    mockUsePhotoDetail.mockReturnValue({
      data: {
        id: '1',
        fileName: 'rose-water.jpg',
        oneDrivePath: '/Photos/rose-water.jpg',
        mimeType: 'image/jpeg',
        fileSize: 1048576,
        width: 1920,
        height: 1080,
        takenAt: '2026-01-15T10:00:00Z',
        indexedAt: '2026-01-16T10:00:00Z',
        thumbnailUrl: '/api/photo-bank/1/thumbnail',
        ocrText: 'Rose Water Toner 100ml Natural',
        tags: [
          { id: 't1', tagName: 'bottle', confidence: 0.95, source: 'Auto' },
          { id: 't2', tagName: 'custom-tag', confidence: 1.0, source: 'Manual' },
        ],
        success: true,
      },
      isLoading: false,
      isError: false,
    } as any);

    renderWithProviders(
      <PhotoDetailPanel photoId="1" onClose={jest.fn()} />,
    );

    expect(screen.getByText('rose-water.jpg')).toBeInTheDocument();
    expect(screen.getByText('bottle')).toBeInTheDocument();
    expect(screen.getByText('custom-tag')).toBeInTheDocument();
    expect(screen.getByText(/Rose Water Toner/)).toBeInTheDocument();
    expect(screen.getByText(/Photos\/rose-water.jpg/)).toBeInTheDocument();
  });

  it('shows error state', () => {
    mockUsePhotoDetail.mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
    } as any);

    renderWithProviders(
      <PhotoDetailPanel photoId="1" onClose={jest.fn()} />,
    );

    expect(screen.getByText('Chyba pri nacitani detailu')).toBeInTheDocument();
  });
});
```

- [ ] **Step 3: Run frontend tests**

```bash
cd frontend && npx jest --testPathPattern="photo-bank" --passWithNoTests
```

---

## Task 17: Build Validation

- [ ] **Step 1: Backend build**

```bash
cd backend && dotnet build
```

- [ ] **Step 2: Backend format check**

```bash
cd backend && dotnet format --verify-no-changes
```

If formatting issues exist:

```bash
cd backend && dotnet format
```

- [ ] **Step 3: Backend tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotoBank"
```

- [ ] **Step 4: Frontend build**

```bash
cd frontend && npm run build
```

- [ ] **Step 5: Frontend lint**

```bash
cd frontend && npm run lint
```

Fix any lint issues that arise.

- [ ] **Step 6: Frontend tests**

```bash
cd frontend && npx jest --testPathPattern="photo-bank" --passWithNoTests
```

- [ ] **Step 7: Full test suite verification**

```bash
cd backend && dotnet test
cd frontend && npm test -- --watchAll=false
```
