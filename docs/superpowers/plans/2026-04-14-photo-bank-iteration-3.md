# Photo Bank: Iteration 3 — MCP Tools & Polish

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add MCP tools for AI assistant access to Photo Bank, plus UX polish (sync status, loading states, error handling).

**Architecture:** MCP tools as thin wrappers around existing MediatR handlers. UX improvements to existing Photo Bank frontend.

**Tech Stack:** .NET 8, MCP SDK (ModelContextProtocol.AspNetCore), React 18, TypeScript

**GitHub Issue:** #613

---

## Task 1: PhotoBankMcpTools Class

**Files:**
- Create: `backend/src/Anela.Heblo.API/MCP/Tools/PhotoBankMcpTools.cs`

**Depends on:** Iteration 2 (SearchPhotosHandler, GetPhotoDetailHandler must exist)

- [ ] **Step 1: Create PhotoBankMcpTools with SearchPhotoBank and GetPhotoDetail tools**

```csharp
// backend/src/Anela.Heblo.API/MCP/Tools/PhotoBankMcpTools.cs
using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.Application.Features.PhotoBank.UseCases.SearchPhotos;
using Anela.Heblo.Application.Features.PhotoBank.UseCases.GetPhotoDetail;
using MediatR;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anela.Heblo.API.MCP.Tools;

/// <summary>
/// MCP tools for Photo Bank operations.
/// Thin wrappers around MediatR handlers that expose photo search and detail to MCP clients.
/// </summary>
[McpServerToolType]
public class PhotoBankMcpTools
{
    private readonly IMediator _mediator;

    public PhotoBankMcpTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    [McpServerTool]
    [Description("Search photos in the Photo Bank by tags, OCR text, or date range. Returns paginated list of photos with metadata, tags, and thumbnail URLs.")]
    public async Task<string> SearchPhotoBank(
        [Description("Free text search across tag names and OCR-extracted text")]
        string? searchTerm = null,
        [Description("Comma-separated tag names to filter by (AND logic — photo must have all specified tags)")]
        string? tags = null,
        [Description("Search within OCR-extracted text from photo labels/packaging")]
        string? ocrText = null,
        [Description("Filter photos taken on or after this date (ISO 8601 format, e.g. '2026-01-01')")]
        DateTimeOffset? from = null,
        [Description("Filter photos taken on or before this date (ISO 8601 format, e.g. '2026-04-01')")]
        DateTimeOffset? to = null,
        [Description("Page number for pagination (default: 1)")]
        int pageNumber = 1,
        [Description("Page size for pagination (default: 50, max: 100)")]
        int pageSize = 50)
    {
        var request = new SearchPhotosRequest
        {
            SearchTerm = searchTerm,
            Tags = !string.IsNullOrWhiteSpace(tags)
                ? tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : null,
            OcrText = ocrText,
            From = from,
            To = to,
            PageNumber = pageNumber,
            PageSize = Math.Min(pageSize, 100)
        };

        var response = await _mediator.Send(request);
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool]
    [Description("Get full details of a single photo including all tags (AI + manual), OCR text, metadata, and OneDrive path.")]
    public async Task<string> GetPhotoDetail(
        [Description("The unique identifier (GUID) of the photo")]
        Guid photoId)
    {
        var request = new GetPhotoDetailRequest { Id = photoId };
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            throw new McpException($"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] {response.FullError()}");
        }

        return JsonSerializer.Serialize(response);
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build backend/src/Anela.Heblo.API/
```

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/PhotoBankMcpTools.cs
git commit -m "feat(photo-bank): add MCP tools for SearchPhotoBank and GetPhotoDetail"
```

---

## Task 2: Register PhotoBankMcpTools in McpModule

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/McpModule.cs`

- [ ] **Step 1: Add PhotoBankMcpTools registration**

In `backend/src/Anela.Heblo.API/MCP/McpModule.cs`, add `.WithTools<PhotoBankMcpTools>()` to the MCP server builder chain:

```csharp
// backend/src/Anela.Heblo.API/MCP/McpModule.cs
using Anela.Heblo.API.MCP.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;

namespace Anela.Heblo.API.MCP;

/// <summary>
/// Dependency injection module for MCP server configuration.
/// Registers MCP server with official ModelContextProtocol.AspNetCore SDK and all tool classes.
/// </summary>
public static class McpModule
{
    public static IServiceCollection AddMcpServices(this IServiceCollection services)
    {
        services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<CatalogMcpTools>()
            .WithTools<ManufactureOrderMcpTools>()
            .WithTools<ManufactureBatchMcpTools>()
            .WithTools<KnowledgeBaseTools>()
            .WithTools<PhotoBankMcpTools>();

        return services;
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build backend/src/Anela.Heblo.API/
```

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/McpModule.cs
git commit -m "feat(photo-bank): register PhotoBankMcpTools in McpModule"
```

---

## Task 3: MCP Tool Unit Tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/MCP/Tools/PhotoBankMcpToolsTests.cs`

**Pattern:** Same as `CatalogMcpToolsTests.cs` — xUnit + Moq, mock `IMediator`, verify request mapping and JSON serialization.

- [ ] **Step 1: Create PhotoBankMcpToolsTests**

```csharp
// backend/test/Anela.Heblo.Tests/MCP/Tools/PhotoBankMcpToolsTests.cs
using System.Text.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.PhotoBank.UseCases.GetPhotoDetail;
using Anela.Heblo.Application.Features.PhotoBank.UseCases.SearchPhotos;
using Anela.Heblo.Application.Shared;
using MediatR;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class PhotoBankMcpToolsTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly PhotoBankMcpTools _tools;

    public PhotoBankMcpToolsTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _tools = new PhotoBankMcpTools(_mediatorMock.Object);
    }

    [Fact]
    public async Task SearchPhotoBank_ReturnsSerializedResponse()
    {
        // Arrange
        var expectedResponse = new SearchPhotosResponse
        {
            Items = new List<PhotoAssetDto>
            {
                new PhotoAssetDto
                {
                    Id = Guid.NewGuid(),
                    FileName = "product-shot.jpg",
                    Tags = new List<PhotoTagDto>
                    {
                        new PhotoTagDto { TagName = "bottle", Confidence = 0.95f }
                    }
                }
            },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 50
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<SearchPhotosRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var jsonResult = await _tools.SearchPhotoBank(searchTerm: "bottle");

        // Assert
        var deserialized = JsonSerializer.Deserialize<SearchPhotosResponse>(jsonResult);
        Assert.NotNull(deserialized);
        Assert.Equal(1, deserialized.TotalCount);
        Assert.Single(deserialized.Items);
        Assert.Equal("product-shot.jpg", deserialized.Items[0].FileName);
    }

    [Fact]
    public async Task SearchPhotoBank_MapsParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new SearchPhotosResponse
        {
            Items = new List<PhotoAssetDto>(),
            TotalCount = 0,
            PageNumber = 2,
            PageSize = 25
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<SearchPhotosRequest>(), default))
            .ReturnsAsync(expectedResponse);

        var fromDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var toDate = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

        // Act
        var jsonResult = await _tools.SearchPhotoBank(
            searchTerm: "bisabolol",
            tags: "bottle, outdoor",
            ocrText: "serum",
            from: fromDate,
            to: toDate,
            pageNumber: 2,
            pageSize: 25
        );

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<SearchPhotosRequest>(req =>
                req.SearchTerm == "bisabolol" &&
                req.Tags != null &&
                req.Tags.Length == 2 &&
                req.Tags[0] == "bottle" &&
                req.Tags[1] == "outdoor" &&
                req.OcrText == "serum" &&
                req.From == fromDate &&
                req.To == toDate &&
                req.PageNumber == 2 &&
                req.PageSize == 25
            ),
            default
        ), Times.Once);

        var deserialized = JsonSerializer.Deserialize<SearchPhotosResponse>(jsonResult);
        Assert.NotNull(deserialized);
        Assert.Equal(0, deserialized.TotalCount);
        Assert.Equal(2, deserialized.PageNumber);
        Assert.Equal(25, deserialized.PageSize);
    }

    [Fact]
    public async Task SearchPhotoBank_CapsPageSizeAt100()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<SearchPhotosRequest>(), default))
            .ReturnsAsync(new SearchPhotosResponse());

        // Act
        await _tools.SearchPhotoBank(pageSize: 500);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<SearchPhotosRequest>(req => req.PageSize == 100),
            default
        ), Times.Once);
    }

    [Fact]
    public async Task SearchPhotoBank_NullTags_SetsTagsToNull()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<SearchPhotosRequest>(), default))
            .ReturnsAsync(new SearchPhotosResponse());

        // Act
        await _tools.SearchPhotoBank(tags: null);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<SearchPhotosRequest>(req => req.Tags == null),
            default
        ), Times.Once);
    }

    [Fact]
    public async Task GetPhotoDetail_ReturnsSerializedResponse()
    {
        // Arrange
        var photoId = Guid.NewGuid();
        var expectedResponse = new GetPhotoDetailResponse
        {
            Success = true,
            Photo = new PhotoAssetDetailDto
            {
                Id = photoId,
                FileName = "lifestyle-photo.jpg",
                OneDrivePath = "/Photos/Marketing/lifestyle-photo.jpg",
                OcrText = "Bisabolol Serum 30ml",
                Tags = new List<PhotoTagDto>
                {
                    new PhotoTagDto { TagName = "product", Confidence = 0.95f },
                    new PhotoTagDto { TagName = "cosmetics", Confidence = 0.90f }
                }
            }
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetPhotoDetailRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var jsonResult = await _tools.GetPhotoDetail(photoId);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<GetPhotoDetailRequest>(req => req.Id == photoId),
            default
        ), Times.Once);

        var deserialized = JsonSerializer.Deserialize<GetPhotoDetailResponse>(jsonResult);
        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
        Assert.Equal(photoId, deserialized.Photo?.Id);
        Assert.Equal("lifestyle-photo.jpg", deserialized.Photo?.FileName);
        Assert.Equal("Bisabolol Serum 30ml", deserialized.Photo?.OcrText);
    }

    [Fact]
    public async Task GetPhotoDetail_WhenNotFound_ThrowsMcpException()
    {
        // Arrange
        var photoId = Guid.NewGuid();
        var errorResponse = new GetPhotoDetailResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.ResourceNotFound,
            Params = new Dictionary<string, string>
            {
                { "PhotoId", photoId.ToString() }
            }
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetPhotoDetailRequest>(), default))
            .ReturnsAsync(errorResponse);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpException>(
            () => _tools.GetPhotoDetail(photoId)
        );

        Assert.Contains("ResourceNotFound", exception.Message);
        Assert.Contains(photoId.ToString(), exception.Message);
    }
}
```

**Note on DTO types:** The tests reference `SearchPhotosResponse`, `PhotoAssetDto`, `PhotoTagDto`, `GetPhotoDetailResponse`, and `PhotoAssetDetailDto`. These are created in Iteration 2 as part of the SearchPhotos and GetPhotoDetail use cases. If the DTO names differ slightly from what Iteration 2 produces, adjust the imports and type references accordingly. The key contract is:

- `SearchPhotosResponse` has `Items` (list), `TotalCount`, `PageNumber`, `PageSize`
- `SearchPhotosRequest` has `SearchTerm`, `Tags` (string[]), `OcrText`, `From`, `To`, `PageNumber`, `PageSize`
- `GetPhotoDetailResponse` extends `BaseResponse` with a `Photo` property containing full photo details
- `GetPhotoDetailRequest` has `Id` (Guid)
- `PhotoAssetDto` has `Id`, `FileName`, `Tags`
- `PhotoAssetDetailDto` extends `PhotoAssetDto` with `OneDrivePath`, `OcrText`, full metadata
- `PhotoTagDto` has `TagName`, `Confidence`

- [ ] **Step 2: Run tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~PhotoBankMcpToolsTests"
```

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/MCP/Tools/PhotoBankMcpToolsTests.cs
git commit -m "test(photo-bank): add MCP tool unit tests for PhotoBankMcpTools"
```

---

## Task 4: Sync Status Endpoint

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/GetSyncStatus/GetSyncStatusRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/GetSyncStatus/GetSyncStatusResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/GetSyncStatus/GetSyncStatusHandler.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/PhotoBank/IPhotoAssetRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/PhotoBank/PhotoAssetRepository.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/PhotoBankController.cs`

- [ ] **Step 1: Add repository method for sync statistics**

Add a new method to `IPhotoAssetRepository`:

```csharp
// Add to backend/src/Anela.Heblo.Domain/Features/PhotoBank/IPhotoAssetRepository.cs
// (append to the existing interface)

Task<PhotoSyncStatistics> GetSyncStatisticsAsync(CancellationToken ct = default);
```

Add the statistics DTO in the Domain layer:

```csharp
// Add to bottom of IPhotoAssetRepository.cs or create a new file:
// backend/src/Anela.Heblo.Domain/Features/PhotoBank/PhotoSyncStatistics.cs
namespace Anela.Heblo.Domain.Features.PhotoBank;

public class PhotoSyncStatistics
{
    public int TotalPhotos { get; set; }
    public int IndexedPhotos { get; set; }
    public int PendingPhotos { get; set; }
    public int FailedPhotos { get; set; }
    public DateTimeOffset? LastSyncTime { get; set; }
}
```

- [ ] **Step 2: Implement repository method**

Add to `backend/src/Anela.Heblo.Persistence/PhotoBank/PhotoAssetRepository.cs`:

```csharp
// Add this method to the PhotoAssetRepository class
public async Task<PhotoSyncStatistics> GetSyncStatisticsAsync(CancellationToken ct = default)
{
    var statusCounts = await _context.PhotoAssets
        .Where(a => a.Status != PhotoAssetStatus.Deleted)
        .GroupBy(a => a.Status)
        .Select(g => new { Status = g.Key, Count = g.Count() })
        .ToListAsync(ct);

    var lastSyncTime = await _context.PhotoAssets
        .Where(a => a.IndexedAt != null)
        .OrderByDescending(a => a.IndexedAt)
        .Select(a => a.IndexedAt)
        .FirstOrDefaultAsync(ct);

    return new PhotoSyncStatistics
    {
        TotalPhotos = statusCounts.Sum(s => s.Count),
        IndexedPhotos = statusCounts
            .Where(s => s.Status == PhotoAssetStatus.Indexed)
            .Sum(s => s.Count),
        PendingPhotos = statusCounts
            .Where(s => s.Status == PhotoAssetStatus.Pending)
            .Sum(s => s.Count),
        FailedPhotos = statusCounts
            .Where(s => s.Status == PhotoAssetStatus.Failed)
            .Sum(s => s.Count),
        LastSyncTime = lastSyncTime
    };
}
```

Add the required using at the top of the repository file:

```csharp
using Microsoft.EntityFrameworkCore;
```

- [ ] **Step 3: Create GetSyncStatusRequest**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/GetSyncStatus/GetSyncStatusRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.GetSyncStatus;

public class GetSyncStatusRequest : IRequest<GetSyncStatusResponse>
{
}
```

- [ ] **Step 4: Create GetSyncStatusResponse**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/GetSyncStatus/GetSyncStatusResponse.cs
namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.GetSyncStatus;

public class GetSyncStatusResponse
{
    public int TotalPhotos { get; set; }
    public int IndexedPhotos { get; set; }
    public int PendingPhotos { get; set; }
    public int FailedPhotos { get; set; }
    public DateTimeOffset? LastSyncTime { get; set; }
}
```

- [ ] **Step 5: Create GetSyncStatusHandler**

```csharp
// backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/GetSyncStatus/GetSyncStatusHandler.cs
using Anela.Heblo.Domain.Features.PhotoBank;
using MediatR;

namespace Anela.Heblo.Application.Features.PhotoBank.UseCases.GetSyncStatus;

public class GetSyncStatusHandler : IRequestHandler<GetSyncStatusRequest, GetSyncStatusResponse>
{
    private readonly IPhotoAssetRepository _repository;

    public GetSyncStatusHandler(IPhotoAssetRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetSyncStatusResponse> Handle(
        GetSyncStatusRequest request,
        CancellationToken cancellationToken)
    {
        var stats = await _repository.GetSyncStatisticsAsync(cancellationToken);

        return new GetSyncStatusResponse
        {
            TotalPhotos = stats.TotalPhotos,
            IndexedPhotos = stats.IndexedPhotos,
            PendingPhotos = stats.PendingPhotos,
            FailedPhotos = stats.FailedPhotos,
            LastSyncTime = stats.LastSyncTime
        };
    }
}
```

- [ ] **Step 6: Add endpoint to PhotoBankController**

Add to the existing `PhotoBankController.cs` (created in Iteration 2):

```csharp
// Add this using at the top:
using Anela.Heblo.Application.Features.PhotoBank.UseCases.GetSyncStatus;

// Add this action method to the controller class:
[HttpGet("status")]
public async Task<ActionResult<GetSyncStatusResponse>> GetSyncStatus()
{
    var request = new GetSyncStatusRequest();
    var response = await _mediator.Send(request);
    return Ok(response);
}
```

- [ ] **Step 7: Verify build**

```bash
dotnet build backend/src/Anela.Heblo.API/
```

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/PhotoBank/PhotoSyncStatistics.cs \
      backend/src/Anela.Heblo.Domain/Features/PhotoBank/IPhotoAssetRepository.cs \
      backend/src/Anela.Heblo.Persistence/PhotoBank/PhotoAssetRepository.cs \
      backend/src/Anela.Heblo.Application/Features/PhotoBank/UseCases/GetSyncStatus/ \
      backend/src/Anela.Heblo.API/Controllers/PhotoBankController.cs
git commit -m "feat(photo-bank): add GET /api/photo-bank/status sync status endpoint"
```

---

## Task 5: Frontend Sync Status Hook and Indicator

**Files:**
- Create: `frontend/src/api/hooks/usePhotoBankStatus.ts`
- Modify: `frontend/src/api/client.ts` (add photoBank to QUERY_KEYS)
- Modify: `frontend/src/pages/PhotoBank/PhotoBankPage.tsx` (add sync status indicator to header)

**Depends on:** Iteration 2 (PhotoBankPage must exist)

- [ ] **Step 1: Add photoBank query key**

In `frontend/src/api/client.ts`, add a new entry to `QUERY_KEYS`:

```typescript
// Add to the QUERY_KEYS object, before the closing `} as const`:
  photoBank: ["photo-bank"] as const,
```

- [ ] **Step 2: Create usePhotoBankStatus hook**

```typescript
// frontend/src/api/hooks/usePhotoBankStatus.ts
import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

export interface PhotoBankSyncStatus {
  totalPhotos: number;
  indexedPhotos: number;
  pendingPhotos: number;
  failedPhotos: number;
  lastSyncTime: string | null;
}

const fetchPhotoBankStatus = async (): Promise<PhotoBankSyncStatus> => {
  const apiClient = getAuthenticatedApiClient();
  const relativeUrl = '/api/photo-bank/status';
  const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'GET',
  });

  if (!response.ok) {
    throw new Error(
      `Failed to fetch photo bank status: ${response.status} ${response.statusText}`,
    );
  }

  return response.json();
};

export const usePhotoBankStatus = () => {
  return useQuery({
    queryKey: [...QUERY_KEYS.photoBank, 'status'],
    queryFn: fetchPhotoBankStatus,
    staleTime: 30 * 1000, // 30 seconds — status changes frequently during sync
    gcTime: 60 * 1000, // 1 minute
    refetchInterval: 60 * 1000, // auto-refresh every 60 seconds
  });
};
```

- [ ] **Step 3: Add SyncStatusIndicator component**

```typescript
// frontend/src/pages/PhotoBank/components/SyncStatusIndicator.tsx
import React from 'react';
import {
  CheckCircle2,
  Clock,
  AlertTriangle,
  Loader2,
  Camera,
} from 'lucide-react';
import {
  usePhotoBankStatus,
  PhotoBankSyncStatus,
} from '../../../api/hooks/usePhotoBankStatus';

const formatRelativeTime = (isoString: string | null): string => {
  if (!isoString) return 'nikdy';

  const date = new Date(isoString);
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffMinutes = Math.floor(diffMs / 60000);
  const diffHours = Math.floor(diffMinutes / 60);
  const diffDays = Math.floor(diffHours / 24);

  if (diffMinutes < 1) return 'právě teď';
  if (diffMinutes < 60) return `před ${diffMinutes} min`;
  if (diffHours < 24) return `před ${diffHours} hod`;
  return `před ${diffDays} dny`;
};

export const SyncStatusIndicator: React.FC = () => {
  const { data: status, isLoading, error } = usePhotoBankStatus();

  if (isLoading) {
    return (
      <div className="flex items-center gap-1.5 text-xs text-gray-400">
        <Loader2 className="h-3.5 w-3.5 animate-spin" />
        <span>Načítání stavu...</span>
      </div>
    );
  }

  if (error || !status) {
    return null; // Silently hide if status unavailable
  }

  const hasPending = status.pendingPhotos > 0;
  const hasFailed = status.failedPhotos > 0;

  return (
    <div className="flex items-center gap-3 text-xs text-gray-500">
      {/* Total indexed count */}
      <div className="flex items-center gap-1">
        <Camera className="h-3.5 w-3.5" />
        <span>
          {status.indexedPhotos.toLocaleString('cs-CZ')}{' '}
          {status.indexedPhotos === 1
            ? 'fotka'
            : status.indexedPhotos < 5
              ? 'fotky'
              : 'fotek'}
        </span>
      </div>

      {/* Pending indicator */}
      {hasPending && (
        <div className="flex items-center gap-1 text-amber-600">
          <Clock className="h-3.5 w-3.5" />
          <span>{status.pendingPhotos} čeká na zpracování</span>
        </div>
      )}

      {/* Failed indicator */}
      {hasFailed && (
        <div className="flex items-center gap-1 text-red-500">
          <AlertTriangle className="h-3.5 w-3.5" />
          <span>{status.failedPhotos} selhalo</span>
        </div>
      )}

      {/* Last sync time */}
      <div className="flex items-center gap-1">
        <CheckCircle2 className="h-3.5 w-3.5 text-green-500" />
        <span>
          Poslední sync: {formatRelativeTime(status.lastSyncTime)}
        </span>
      </div>
    </div>
  );
};
```

- [ ] **Step 4: Integrate SyncStatusIndicator into PhotoBankPage header**

Modify `frontend/src/pages/PhotoBank/PhotoBankPage.tsx`. Add the sync status indicator to the page header, below the title:

```typescript
// Add import at top of PhotoBankPage.tsx:
import { SyncStatusIndicator } from './components/SyncStatusIndicator';

// In the JSX, add SyncStatusIndicator below the page title:
// Find the header section (typically <h1>Fotobanky</h1> or similar) and add:
<div className="flex-shrink-0 mb-3">
  <div className="flex items-center justify-between">
    <h1 className="text-lg font-semibold text-gray-900">Fotobanka</h1>
  </div>
  <div className="mt-1">
    <SyncStatusIndicator />
  </div>
</div>
```

- [ ] **Step 5: Verify frontend build**

```bash
cd frontend && npm run build && npm run lint
```

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/hooks/usePhotoBankStatus.ts \
      frontend/src/api/client.ts \
      frontend/src/pages/PhotoBank/components/SyncStatusIndicator.tsx \
      frontend/src/pages/PhotoBank/PhotoBankPage.tsx
git commit -m "feat(photo-bank): add sync status indicator to Photo Bank page header"
```

---

## Task 6: Frontend Loading, Empty, and Error States

**Files:**
- Modify: `frontend/src/pages/PhotoBank/components/PhotoGrid.tsx` (created in Iteration 2)

**Depends on:** Iteration 2 (PhotoGrid component must exist)

- [ ] **Step 1: Add loading, empty, and error states to PhotoGrid**

Modify `frontend/src/pages/PhotoBank/components/PhotoGrid.tsx` to handle all three states. The component should already accept `isLoading`, `error`, and have a `data` prop or similar. Add these state renders before the main grid render:

```typescript
// Add imports at top of PhotoGrid.tsx:
import { Loader2, AlertCircle, ImageOff, Search } from 'lucide-react';

// Add these as helper components inside the file (or inline in the main component):

const PhotoGridLoading: React.FC = () => (
  <div className="flex flex-col items-center justify-center h-64 text-gray-400">
    <Loader2 className="h-8 w-8 animate-spin text-indigo-500 mb-3" />
    <p className="text-sm text-gray-500">Načítání fotografií...</p>
  </div>
);

const PhotoGridError: React.FC<{ message: string }> = ({ message }) => (
  <div className="flex flex-col items-center justify-center h-64 text-red-500">
    <AlertCircle className="h-8 w-8 mb-3" />
    <p className="text-sm font-medium">Nepodařilo se načíst fotografie</p>
    <p className="text-xs text-red-400 mt-1">{message}</p>
  </div>
);

interface PhotoGridEmptyProps {
  hasActiveFilters: boolean;
}

const PhotoGridEmpty: React.FC<PhotoGridEmptyProps> = ({ hasActiveFilters }) => (
  <div className="flex flex-col items-center justify-center h-64 text-gray-400">
    {hasActiveFilters ? (
      <>
        <Search className="h-10 w-10 mb-3" />
        <p className="text-sm font-medium text-gray-500">
          Žádné fotografie neodpovídají filtrům
        </p>
        <p className="text-xs text-gray-400 mt-1">
          Zkuste upravit vyhledávání nebo odebrat filtry
        </p>
      </>
    ) : (
      <>
        <ImageOff className="h-10 w-10 mb-3" />
        <p className="text-sm font-medium text-gray-500">
          Fotobanka je prázdná
        </p>
        <p className="text-xs text-gray-400 mt-1">
          Fotografie se automaticky synchronizují z OneDrive
        </p>
      </>
    )}
  </div>
);
```

Then in the main PhotoGrid component render logic, add early returns:

```typescript
// Inside the PhotoGrid component, before the main grid render:

if (isLoading) {
  return <PhotoGridLoading />;
}

if (error) {
  return <PhotoGridError message={error.message} />;
}

if (!photos || photos.length === 0) {
  return <PhotoGridEmpty hasActiveFilters={hasActiveFilters} />;
}

// ... existing grid render continues here
```

The `hasActiveFilters` prop should be derived from whether any search/filter params are active. Pass it from the parent component or derive from the search state:

```typescript
// In PhotoGrid props interface, add:
interface PhotoGridProps {
  photos: PhotoAssetDto[];
  isLoading: boolean;
  error: Error | null;
  hasActiveFilters: boolean;
  onPhotoClick: (photoId: string) => void;
}
```

- [ ] **Step 2: Verify frontend build**

```bash
cd frontend && npm run build && npm run lint
```

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/PhotoBank/components/PhotoGrid.tsx
git commit -m "feat(photo-bank): add loading, empty, and error states to PhotoGrid"
```

---

## Task 7: Thumbnail Error Fallback

**Files:**
- Create: `frontend/src/pages/PhotoBank/components/PhotoThumbnail.tsx`
- Modify: `frontend/src/pages/PhotoBank/components/PhotoGrid.tsx` (use PhotoThumbnail)

- [ ] **Step 1: Create PhotoThumbnail component with error fallback**

```typescript
// frontend/src/pages/PhotoBank/components/PhotoThumbnail.tsx
import React, { useState } from 'react';
import { ImageOff } from 'lucide-react';

interface PhotoThumbnailProps {
  src: string;
  alt: string;
  className?: string;
  onClick?: () => void;
}

export const PhotoThumbnail: React.FC<PhotoThumbnailProps> = ({
  src,
  alt,
  className = '',
  onClick,
}) => {
  const [hasError, setHasError] = useState(false);
  const [isLoaded, setIsLoaded] = useState(false);

  if (hasError) {
    return (
      <div
        className={`flex items-center justify-center bg-gray-100 text-gray-300 ${className}`}
        onClick={onClick}
        role={onClick ? 'button' : undefined}
        tabIndex={onClick ? 0 : undefined}
        onKeyDown={
          onClick
            ? (e) => {
                if (e.key === 'Enter' || e.key === ' ') onClick();
              }
            : undefined
        }
      >
        <div className="flex flex-col items-center gap-1">
          <ImageOff className="h-8 w-8" />
          <span className="text-xs">Náhled nedostupný</span>
        </div>
      </div>
    );
  }

  return (
    <div className={`relative ${className}`}>
      {/* Skeleton placeholder while loading */}
      {!isLoaded && (
        <div className="absolute inset-0 bg-gray-100 animate-pulse rounded" />
      )}
      <img
        src={src}
        alt={alt}
        className={`w-full h-full object-cover rounded transition-opacity duration-200 ${
          isLoaded ? 'opacity-100' : 'opacity-0'
        }`}
        onError={() => setHasError(true)}
        onLoad={() => setIsLoaded(true)}
        onClick={onClick}
        loading="lazy"
      />
    </div>
  );
};
```

- [ ] **Step 2: Use PhotoThumbnail in PhotoGrid**

In `frontend/src/pages/PhotoBank/components/PhotoGrid.tsx`, replace direct `<img>` tags for thumbnails with the `<PhotoThumbnail>` component:

```typescript
// Add import at top:
import { PhotoThumbnail } from './PhotoThumbnail';

// Replace the thumbnail <img> in the grid item render with:
<PhotoThumbnail
  src={photo.thumbnailUrl}
  alt={photo.fileName}
  className="aspect-square w-full"
  onClick={() => onPhotoClick(photo.id)}
/>
```

- [ ] **Step 3: Verify frontend build**

```bash
cd frontend && npm run build && npm run lint
```

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/PhotoBank/components/PhotoThumbnail.tsx \
      frontend/src/pages/PhotoBank/components/PhotoGrid.tsx
git commit -m "feat(photo-bank): add thumbnail error fallback with placeholder"
```

---

## Task 8: Update CLAUDE.md MCP Tool Documentation

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add Photo Bank tools to the MCP Server section**

In the root `CLAUDE.md`, add a new section under "Available Tools" after Knowledge Base Tools:

```markdown
**Photo Bank Tools (2):**
- `SearchPhotoBank` - Search photos by tags, OCR text, or date range
- `GetPhotoDetail` - Get full photo details including tags, OCR text, and OneDrive path
```

Update the tool count in the MCP Server description line from "15 tools" to "17 tools across Catalog, Manufacturing, Batch Planning, Knowledge Base, and Photo Bank".

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: add Photo Bank MCP tools to CLAUDE.md"
```

---

## Task 9: Build Validation

- [ ] **Step 1: Backend build**

```bash
dotnet build backend/Anela.Heblo.sln
```

- [ ] **Step 2: Backend format check**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

If formatting issues found, run:

```bash
dotnet format backend/Anela.Heblo.sln
```

- [ ] **Step 3: Backend tests**

```bash
dotnet test backend/Anela.Heblo.sln
```

Verify specifically the new MCP tests pass:

```bash
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~PhotoBankMcpToolsTests"
```

- [ ] **Step 4: Frontend build**

```bash
cd frontend && npm run build
```

- [ ] **Step 5: Frontend lint**

```bash
cd frontend && npm run lint
```

- [ ] **Step 6: Final commit (if any formatting fixes)**

```bash
git add -A
git commit -m "chore: fix formatting from build validation"
```

---

## Verification Checklist

After completing all tasks, verify:

- [ ] `GET /api/photo-bank/status` returns `{ totalPhotos, indexedPhotos, pendingPhotos, failedPhotos, lastSyncTime }`
- [ ] MCP tool `SearchPhotoBank` is callable via `/mcp` endpoint and returns paginated photo results
- [ ] MCP tool `GetPhotoDetail` is callable via `/mcp` endpoint and returns full photo details
- [ ] MCP tool `GetPhotoDetail` throws `McpException` when photo not found
- [ ] Photo Bank page shows sync status indicator in header (total photos, pending, failed, last sync time)
- [ ] Photo Bank page shows loading spinner while photos are loading
- [ ] Photo Bank page shows empty state with appropriate message when no photos match filters
- [ ] Photo Bank page shows error state when API request fails
- [ ] Broken thumbnails show fallback placeholder instead of broken image icon
- [ ] All 6 new MCP tool tests pass
- [ ] `dotnet build` succeeds
- [ ] `dotnet format --verify-no-changes` succeeds
- [ ] `npm run build` succeeds
- [ ] `npm run lint` succeeds
