# Expedition List Archive Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an Expedition List Archive page that lets users browse, open, and reprint PDF picking lists previously archived to Azure Blob Storage under `expedition-lists/{yyyy-MM-dd}/{filename}.pdf`.

**Architecture:** New `ExpeditionListArchive` vertical slice in the Application layer with 4 MediatR handlers; `IBlobStorageService` extended with `ListBlobsAsync` + `DownloadAsync` methods implemented in `AzureBlobStorageService`; single `ExpeditionListArchiveController` exposes 4 REST endpoints; React page with date picker, table, and reprint confirmation.

**Tech Stack:** .NET 8 / ASP.NET Core, MediatR, Azure.Storage.Blobs, React 18, React Query (TanStack), Tailwind CSS, lucide-react icons, Moq + xUnit for backend tests.

---

## File Map

### Backend – New/Modified Files

| Action   | File                                                                                                                                               | Responsibility                                                          |
|----------|----------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------|
| Modify   | `backend/src/Anela.Heblo.Domain/Features/FileStorage/IBlobStorageService.cs`                                                                      | Add `BlobItemInfo` class + `ListBlobsAsync` + `DownloadAsync` methods   |
| Modify   | `backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs`                                                    | Implement new blob listing and download methods                         |
| Create   | `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/ExpeditionListItemDto.cs`                                           | DTO for a single archived PDF item                                      |
| Create   | `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesRequest.cs`                     | MediatR request (page, pageSize)                                        |
| Create   | `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesResponse.cs`                    | Paginated response with date strings                                    |
| Create   | `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs`                     | Lists unique date prefixes from blob container                          |
| Create   | `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateRequest.cs`         | MediatR request (date)                                                  |
| Create   | `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateResponse.cs`        | List of ExpeditionListItemDto                                           |
| Create   | `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs`         | Lists PDFs for a given date prefix                                      |
| Create   | `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListRequest.cs`             | MediatR request (blobPath)                                              |
| Create   | `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListResponse.cs`            | Contains Stream + ContentType + FileName                                |
| Create   | `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs`             | Validates blobPath and returns blob stream                              |
| Create   | `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListRequest.cs`               | MediatR request (blobPath)                                              |
| Create   | `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListResponse.cs`              | Success/error result                                                    |
| Create   | `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs`               | Downloads blob → temp file → calls keyed cups sink → deletes temp file  |
| Create   | `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs`                                               | DI registration                                                         |
| Create   | `backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs`                                                                      | 4 endpoints: dates, items-by-date, download, reprint                    |
| Modify   | `backend/src/Anela.Heblo.Application/ApplicationModule.cs`                                                                                        | Register `AddExpeditionListArchiveModule()`                             |
| Modify   | `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`                                                                           | Also register keyed cups sink for "Cups" mode (not just "Combined")     |

### Backend – Test Files

| Action | File                                                                                                                     |
|--------|--------------------------------------------------------------------------------------------------------------------------|
| Create | `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs`                                |
| Create | `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs`                          |
| Create | `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/DownloadExpeditionListHandlerTests.cs`                            |
| Create | `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs`                             |

### Frontend – New/Modified Files

| Action | File                                                                          | Responsibility                                             |
|--------|-------------------------------------------------------------------------------|------------------------------------------------------------|
| Modify | `frontend/src/api/client.ts`                                                  | Add `expeditionListArchive` key to `QUERY_KEYS`            |
| Create | `frontend/src/api/hooks/useExpeditionListArchive.ts`                          | React Query hooks for dates, items, and reprint mutation   |
| Create | `frontend/src/pages/ExpeditionListArchivePage.tsx`                            | Page component with date picker, table, open/reprint       |
| Modify | `frontend/src/App.tsx`                                                        | Add `/logistics/expedition-archive` route                  |
| Modify | `frontend/src/components/Layout/Sidebar.tsx`                                  | Add "Archiv expedic" item to Sklad section                 |

---

## Task 1: Extend IBlobStorageService – ListBlobsAsync & DownloadAsync

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/FileStorage/IBlobStorageService.cs`

- [ ] **Step 1: Add BlobItemInfo class and new methods to the interface**

Open `backend/src/Anela.Heblo.Domain/Features/FileStorage/IBlobStorageService.cs` and replace the entire file content with:

```csharp
namespace Anela.Heblo.Domain.Features.FileStorage;

/// <summary>
/// Metadata about a blob item in storage
/// </summary>
public class BlobItemInfo
{
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTimeOffset? CreatedOn { get; set; }
    public long? ContentLength { get; set; }
}

/// <summary>
/// Service interface for blob storage operations
/// </summary>
public interface IBlobStorageService
{
    /// <summary>
    /// Downloads a file from the specified URL and uploads it to blob storage
    /// </summary>
    Task<string> DownloadFromUrlAsync(string fileUrl, string containerName, string? blobName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a file stream to blob storage
    /// </summary>
    Task<string> UploadAsync(Stream stream, string containerName, string blobName, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a blob from storage
    /// </summary>
    Task<bool> DeleteAsync(string containerName, string blobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the URL of a blob
    /// </summary>
    string GetBlobUrl(string containerName, string blobName);

    /// <summary>
    /// Checks if a blob exists
    /// </summary>
    Task<bool> ExistsAsync(string containerName, string blobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists blobs in a container with optional prefix filter
    /// </summary>
    Task<IReadOnlyList<BlobItemInfo>> ListBlobsAsync(string containerName, string? prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a blob as a stream
    /// </summary>
    Task<Stream> DownloadAsync(string containerName, string blobName, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Verify the project builds (shows compile errors for unimplemented interface)**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj 2>&1 | grep -E "error|warning" | head -20
```

Expected: Error about `AzureBlobStorageService` not implementing `ListBlobsAsync` and `DownloadAsync`.

---

## Task 2: Implement ListBlobsAsync & DownloadAsync in AzureBlobStorageService

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs`

- [ ] **Step 1: Add the two implementations to AzureBlobStorageService**

Add these two methods after the `ExistsAsync` method (before `GetOrCreateContainerAsync`):

```csharp
/// <inheritdoc />
public async Task<IReadOnlyList<BlobItemInfo>> ListBlobsAsync(string containerName, string? prefix, CancellationToken cancellationToken = default)
{
    try
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var items = new List<BlobItemInfo>();

        await foreach (var blob in containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            items.Add(new BlobItemInfo
            {
                Name = blob.Name,
                FileName = Path.GetFileName(blob.Name),
                CreatedOn = blob.Properties.CreatedOn,
                ContentLength = blob.Properties.ContentLength
            });
        }

        return items.AsReadOnly();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error listing blobs in container {ContainerName} with prefix {Prefix}", containerName, prefix);
        throw;
    }
}

/// <inheritdoc />
public async Task<Stream> DownloadAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
{
    try
    {
        _logger.LogInformation("Downloading blob {BlobName} from container {ContainerName}", blobName, containerName);
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        var download = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return download.Value.Content;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error downloading blob {BlobName} from container {ContainerName}", blobName, containerName);
        throw;
    }
}
```

- [ ] **Step 2: Build to verify no compile errors**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feature-expedition-archive
git add backend/src/Anela.Heblo.Domain/Features/FileStorage/IBlobStorageService.cs
git add backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs
git commit -m "feat: extend IBlobStorageService with ListBlobsAsync and DownloadAsync"
```

---

## Task 3: GetExpeditionDates – Use Case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs`

The handler lists all "virtual directories" (date-prefix folders) from the `expedition-lists` container, paginates them descending by date.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionDates;
using Anela.Heblo.Domain.Features.FileStorage;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.ExpeditionListArchive;

public class GetExpeditionDatesHandlerTests
{
    private readonly Mock<IBlobStorageService> _blobStorageServiceMock;
    private readonly GetExpeditionDatesHandler _handler;
    private const string ContainerName = "expedition-lists";

    public GetExpeditionDatesHandlerTests()
    {
        _blobStorageServiceMock = new Mock<IBlobStorageService>();
        _handler = new GetExpeditionDatesHandler(_blobStorageServiceMock.Object);
    }

    [Fact]
    public async Task Handle_ReturnsDatesSortedDescending()
    {
        // Arrange
        var blobs = new List<BlobItemInfo>
        {
            new() { Name = "2026-03-24/list-001.pdf", FileName = "list-001.pdf", CreatedOn = DateTimeOffset.UtcNow.AddDays(-1) },
            new() { Name = "2026-03-25/list-002.pdf", FileName = "list-002.pdf", CreatedOn = DateTimeOffset.UtcNow },
            new() { Name = "2026-03-24/list-003.pdf", FileName = "list-003.pdf", CreatedOn = DateTimeOffset.UtcNow.AddDays(-1) },
            new() { Name = "2026-03-23/list-004.pdf", FileName = "list-004.pdf", CreatedOn = DateTimeOffset.UtcNow.AddDays(-2) },
        };

        _blobStorageServiceMock
            .Setup(s => s.ListBlobsAsync(ContainerName, null, default))
            .ReturnsAsync(blobs.AsReadOnly());

        var request = new GetExpeditionDatesRequest { Page = 1, PageSize = 20 };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Dates.Count);
        Assert.Equal("2026-03-25", result.Dates[0]);
        Assert.Equal("2026-03-24", result.Dates[1]);
        Assert.Equal("2026-03-23", result.Dates[2]);
    }

    [Fact]
    public async Task Handle_PaginatesCorrectly()
    {
        // Arrange
        var blobs = new List<BlobItemInfo>();
        for (int i = 1; i <= 25; i++)
        {
            var dateStr = $"2026-01-{i:D2}";
            blobs.Add(new() { Name = $"{dateStr}/list.pdf", FileName = "list.pdf" });
        }

        _blobStorageServiceMock
            .Setup(s => s.ListBlobsAsync(ContainerName, null, default))
            .ReturnsAsync(blobs.AsReadOnly());

        var request = new GetExpeditionDatesRequest { Page = 2, PageSize = 20 };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.Equal(25, result.TotalCount);
        Assert.Equal(5, result.Dates.Count); // page 2 of 20: items 21-25
    }

    [Fact]
    public async Task Handle_EmptyContainer_ReturnsEmptyList()
    {
        // Arrange
        _blobStorageServiceMock
            .Setup(s => s.ListBlobsAsync(ContainerName, null, default))
            .ReturnsAsync(new List<BlobItemInfo>().AsReadOnly());

        var request = new GetExpeditionDatesRequest { Page = 1, PageSize = 20 };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Dates);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feature-expedition-archive
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "GetExpeditionDatesHandlerTests" 2>&1 | tail -10
```

Expected: FAIL — namespace or type not found.

- [ ] **Step 3: Create the request class**

Create `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionDates;

public class GetExpeditionDatesRequest : IRequest<GetExpeditionDatesResponse>
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
```

- [ ] **Step 4: Create the response class**

Create `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesResponse.cs`:

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionDates;

public class GetExpeditionDatesResponse
{
    public List<string> Dates { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
```

- [ ] **Step 5: Create the handler**

Create `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs`:

```csharp
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionDates;

public class GetExpeditionDatesHandler : IRequestHandler<GetExpeditionDatesRequest, GetExpeditionDatesResponse>
{
    private const string ContainerName = "expedition-lists";
    private readonly IBlobStorageService _blobStorageService;

    public GetExpeditionDatesHandler(IBlobStorageService blobStorageService)
    {
        _blobStorageService = blobStorageService;
    }

    public async Task<GetExpeditionDatesResponse> Handle(GetExpeditionDatesRequest request, CancellationToken cancellationToken)
    {
        var blobs = await _blobStorageService.ListBlobsAsync(ContainerName, null, cancellationToken);

        var dates = blobs
            .Select(b => b.Name.Split('/')[0])
            .Where(d => IsValidDatePrefix(d))
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();

        var totalCount = dates.Count;
        var pagedDates = dates
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new GetExpeditionDatesResponse
        {
            Dates = pagedDates,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    private static bool IsValidDatePrefix(string prefix)
    {
        return DateOnly.TryParseExact(prefix, "yyyy-MM-dd", out _);
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "GetExpeditionDatesHandlerTests" 2>&1 | tail -10
```

Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/
git add backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs
git commit -m "feat: add GetExpeditionDates use case with tests"
```

---

## Task 4: GetExpeditionListsByDate – Use Case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/ExpeditionListItemDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;
using Anela.Heblo.Domain.Features.FileStorage;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.ExpeditionListArchive;

public class GetExpeditionListsByDateHandlerTests
{
    private readonly Mock<IBlobStorageService> _blobStorageServiceMock;
    private readonly GetExpeditionListsByDateHandler _handler;
    private const string ContainerName = "expedition-lists";

    public GetExpeditionListsByDateHandlerTests()
    {
        _blobStorageServiceMock = new Mock<IBlobStorageService>();
        _handler = new GetExpeditionListsByDateHandler(_blobStorageServiceMock.Object);
    }

    [Fact]
    public async Task Handle_ReturnsItemsForDate()
    {
        // Arrange
        var date = "2026-03-25";
        var blobs = new List<BlobItemInfo>
        {
            new() { Name = $"{date}/picking-list-001.pdf", FileName = "picking-list-001.pdf", CreatedOn = new DateTimeOffset(2026, 3, 25, 10, 0, 0, TimeSpan.Zero), ContentLength = 512000 },
            new() { Name = $"{date}/picking-list-002.pdf", FileName = "picking-list-002.pdf", CreatedOn = new DateTimeOffset(2026, 3, 25, 14, 0, 0, TimeSpan.Zero), ContentLength = 256000 },
        };

        _blobStorageServiceMock
            .Setup(s => s.ListBlobsAsync(ContainerName, date, default))
            .ReturnsAsync(blobs.AsReadOnly());

        var request = new GetExpeditionListsByDateRequest { Date = date };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("picking-list-001.pdf", result.Items[0].FileName);
        Assert.Equal($"{date}/picking-list-001.pdf", result.Items[0].BlobPath);
        Assert.Equal(512000, result.Items[0].ContentLength);
    }

    [Fact]
    public async Task Handle_FiltersPdfFilesOnly()
    {
        // Arrange
        var date = "2026-03-25";
        var blobs = new List<BlobItemInfo>
        {
            new() { Name = $"{date}/picking-list-001.pdf", FileName = "picking-list-001.pdf" },
            new() { Name = $"{date}/picking-list-002.txt", FileName = "picking-list-002.txt" },
        };

        _blobStorageServiceMock
            .Setup(s => s.ListBlobsAsync(ContainerName, date, default))
            .ReturnsAsync(blobs.AsReadOnly());

        var request = new GetExpeditionListsByDateRequest { Date = date };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("picking-list-001.pdf", result.Items[0].FileName);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "GetExpeditionListsByDateHandlerTests" 2>&1 | tail -5
```

Expected: FAIL — types not found.

- [ ] **Step 3: Create the DTO**

Create `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/ExpeditionListItemDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;

public class ExpeditionListItemDto
{
    public string BlobPath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTimeOffset? CreatedOn { get; set; }
    public long? ContentLength { get; set; }
}
```

- [ ] **Step 4: Create request/response classes**

Create `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;

public class GetExpeditionListsByDateRequest : IRequest<GetExpeditionListsByDateResponse>
{
    public string Date { get; set; } = string.Empty;
}
```

Create `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;

public class GetExpeditionListsByDateResponse
{
    public List<ExpeditionListItemDto> Items { get; set; } = new();
}
```

- [ ] **Step 5: Create the handler**

Create `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;

public class GetExpeditionListsByDateHandler : IRequestHandler<GetExpeditionListsByDateRequest, GetExpeditionListsByDateResponse>
{
    private const string ContainerName = "expedition-lists";
    private readonly IBlobStorageService _blobStorageService;

    public GetExpeditionListsByDateHandler(IBlobStorageService blobStorageService)
    {
        _blobStorageService = blobStorageService;
    }

    public async Task<GetExpeditionListsByDateResponse> Handle(GetExpeditionListsByDateRequest request, CancellationToken cancellationToken)
    {
        var blobs = await _blobStorageService.ListBlobsAsync(ContainerName, request.Date, cancellationToken);

        var items = blobs
            .Where(b => b.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .Select(b => new ExpeditionListItemDto
            {
                BlobPath = b.Name,
                FileName = b.FileName,
                CreatedOn = b.CreatedOn,
                ContentLength = b.ContentLength
            })
            .ToList();

        return new GetExpeditionListsByDateResponse { Items = items };
    }
}
```

- [ ] **Step 6: Run tests to verify pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "GetExpeditionListsByDateHandlerTests" 2>&1 | tail -5
```

Expected: `Passed! - Failed: 0, Passed: 2`

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/
git add backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs
git commit -m "feat: add GetExpeditionListsByDate use case with tests"
```

---

## Task 5: DownloadExpeditionList – Use Case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/DownloadExpeditionListHandlerTests.cs`

The handler validates `blobPath` (must match `yyyy-MM-dd/...pdf`), then returns the blob stream. Path traversal protection: reject any path with `..` segments, and verify the path matches the expected format.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/DownloadExpeditionListHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;
using Anela.Heblo.Domain.Features.FileStorage;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.ExpeditionListArchive;

public class DownloadExpeditionListHandlerTests
{
    private readonly Mock<IBlobStorageService> _blobStorageServiceMock;
    private readonly DownloadExpeditionListHandler _handler;
    private const string ContainerName = "expedition-lists";

    public DownloadExpeditionListHandlerTests()
    {
        _blobStorageServiceMock = new Mock<IBlobStorageService>();
        _handler = new DownloadExpeditionListHandler(_blobStorageServiceMock.Object);
    }

    [Fact]
    public async Task Handle_ValidBlobPath_ReturnsBlobStream()
    {
        // Arrange
        var blobPath = "2026-03-25/picking-list-001.pdf";
        var expectedStream = new MemoryStream(new byte[] { 1, 2, 3 });

        _blobStorageServiceMock
            .Setup(s => s.DownloadAsync(ContainerName, blobPath, default))
            .ReturnsAsync(expectedStream);

        var request = new DownloadExpeditionListRequest { BlobPath = blobPath };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(expectedStream, result.Stream);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal("picking-list-001.pdf", result.FileName);
    }

    [Theory]
    [InlineData("../secret/file.pdf")]
    [InlineData("2026-03-25/../../../etc/passwd")]
    [InlineData("invalid-date/file.pdf")]
    [InlineData("2026-03-25/file.exe")]
    [InlineData("")]
    public async Task Handle_InvalidBlobPath_ReturnsFailure(string blobPath)
    {
        // Arrange
        var request = new DownloadExpeditionListRequest { BlobPath = blobPath };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.Stream);

        _blobStorageServiceMock.Verify(
            s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "DownloadExpeditionListHandlerTests" 2>&1 | tail -5
```

Expected: FAIL — types not found.

- [ ] **Step 3: Create request class**

Create `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;

public class DownloadExpeditionListRequest : IRequest<DownloadExpeditionListResponse>
{
    public string BlobPath { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Create response class**

Create `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListResponse.cs`:

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;

public class DownloadExpeditionListResponse
{
    public bool Success { get; set; }
    public Stream? Stream { get; set; }
    public string ContentType { get; set; } = "application/pdf";
    public string FileName { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }

    public static DownloadExpeditionListResponse Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
```

- [ ] **Step 5: Create handler with path traversal protection**

Create `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs`:

```csharp
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using System.Text.RegularExpressions;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;

public class DownloadExpeditionListHandler : IRequestHandler<DownloadExpeditionListRequest, DownloadExpeditionListResponse>
{
    private const string ContainerName = "expedition-lists";
    private static readonly Regex ValidBlobPathPattern = new(@"^\d{4}-\d{2}-\d{2}/[^/]+\.pdf$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly IBlobStorageService _blobStorageService;

    public DownloadExpeditionListHandler(IBlobStorageService blobStorageService)
    {
        _blobStorageService = blobStorageService;
    }

    public async Task<DownloadExpeditionListResponse> Handle(DownloadExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidBlobPath(request.BlobPath))
        {
            return DownloadExpeditionListResponse.Fail("Invalid blob path.");
        }

        var stream = await _blobStorageService.DownloadAsync(ContainerName, request.BlobPath, cancellationToken);
        var fileName = Path.GetFileName(request.BlobPath);

        return new DownloadExpeditionListResponse
        {
            Success = true,
            Stream = stream,
            ContentType = "application/pdf",
            FileName = fileName
        };
    }

    private static bool IsValidBlobPath(string blobPath)
    {
        if (string.IsNullOrWhiteSpace(blobPath))
            return false;

        if (blobPath.Contains(".."))
            return false;

        if (!ValidBlobPathPattern.IsMatch(blobPath))
            return false;

        var datePart = blobPath.Split('/')[0];
        return DateOnly.TryParseExact(datePart, "yyyy-MM-dd", out _);
    }
}
```

- [ ] **Step 6: Run tests to verify pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "DownloadExpeditionListHandlerTests" 2>&1 | tail -5
```

Expected: `Passed! - Failed: 0, Passed: 2`

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/
git add backend/test/Anela.Heblo.Tests/ExpeditionListArchive/DownloadExpeditionListHandlerTests.cs
git commit -m "feat: add DownloadExpeditionList use case with path traversal protection"
```

---

## Task 6: ReprintExpeditionList – Use Case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs`
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`

The handler downloads the blob to a temp file, sends it to the keyed "cups" IPrintQueueSink, then deletes the temp file.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Domain.Features.FileStorage;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.ExpeditionListArchive;

public class ReprintExpeditionListHandlerTests
{
    private readonly Mock<IBlobStorageService> _blobStorageServiceMock;
    private readonly Mock<IPrintQueueSink> _cupsSinkMock;
    private readonly ReprintExpeditionListHandler _handler;
    private const string ContainerName = "expedition-lists";

    public ReprintExpeditionListHandlerTests()
    {
        _blobStorageServiceMock = new Mock<IBlobStorageService>();
        _cupsSinkMock = new Mock<IPrintQueueSink>();
        _handler = new ReprintExpeditionListHandler(_blobStorageServiceMock.Object, _cupsSinkMock.Object);
    }

    [Fact]
    public async Task Handle_ValidBlobPath_DownloadsAndSendsToCupsSink()
    {
        // Arrange
        var blobPath = "2026-03-25/picking-list-001.pdf";
        var pdfContent = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // PDF magic bytes
        var blobStream = new MemoryStream(pdfContent);

        _blobStorageServiceMock
            .Setup(s => s.DownloadAsync(ContainerName, blobPath, default))
            .ReturnsAsync(blobStream);

        _cupsSinkMock
            .Setup(s => s.SendAsync(It.IsAny<IEnumerable<string>>(), default))
            .Returns(Task.CompletedTask);

        var request = new ReprintExpeditionListRequest { BlobPath = blobPath };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        _blobStorageServiceMock.Verify(s => s.DownloadAsync(ContainerName, blobPath, default), Times.Once);
        _cupsSinkMock.Verify(
            s => s.SendAsync(It.Is<IEnumerable<string>>(paths => paths.Any()), default),
            Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidBlobPath_ReturnsFailureWithoutCallingBlob()
    {
        // Arrange
        var request = new ReprintExpeditionListRequest { BlobPath = "../malicious/path.pdf" };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        _blobStorageServiceMock.Verify(
            s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _cupsSinkMock.Verify(
            s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "ReprintExpeditionListHandlerTests" 2>&1 | tail -5
```

Expected: FAIL — types not found.

- [ ] **Step 3: Create request class**

Create `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;

public class ReprintExpeditionListRequest : IRequest<ReprintExpeditionListResponse>
{
    public string BlobPath { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Create response class**

Create `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListResponse.cs`:

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;

public class ReprintExpeditionListResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public static ReprintExpeditionListResponse Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
```

- [ ] **Step 5: Create the handler**

Create `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using System.Text.RegularExpressions;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;

public class ReprintExpeditionListHandler : IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>
{
    private const string ContainerName = "expedition-lists";
    private static readonly Regex ValidBlobPathPattern = new(@"^\d{4}-\d{2}-\d{2}/[^/]+\.pdf$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly IBlobStorageService _blobStorageService;
    private readonly IPrintQueueSink _cupsSink;

    public ReprintExpeditionListHandler(IBlobStorageService blobStorageService, IPrintQueueSink cupsSink)
    {
        _blobStorageService = blobStorageService;
        _cupsSink = cupsSink;
    }

    public async Task<ReprintExpeditionListResponse> Handle(ReprintExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidBlobPath(request.BlobPath))
        {
            return ReprintExpeditionListResponse.Fail("Invalid blob path.");
        }

        var tempFile = Path.GetTempFileName() + ".pdf";
        try
        {
            await using var blobStream = await _blobStorageService.DownloadAsync(ContainerName, request.BlobPath, cancellationToken);
            await using var fileStream = File.OpenWrite(tempFile);
            await blobStream.CopyToAsync(fileStream, cancellationToken);
        }
        catch
        {
            DeleteTempFile(tempFile);
            throw;
        }

        try
        {
            await _cupsSink.SendAsync(new[] { tempFile }, cancellationToken);
            return new ReprintExpeditionListResponse { Success = true };
        }
        finally
        {
            DeleteTempFile(tempFile);
        }
    }

    private static bool IsValidBlobPath(string blobPath)
    {
        if (string.IsNullOrWhiteSpace(blobPath))
            return false;

        if (blobPath.Contains(".."))
            return false;

        if (!ValidBlobPathPattern.IsMatch(blobPath))
            return false;

        var datePart = blobPath.Split('/')[0];
        return DateOnly.TryParseExact(datePart, "yyyy-MM-dd", out _);
    }

    private static void DeleteTempFile(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
```

- [ ] **Step 6: Run tests to verify pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "ReprintExpeditionListHandlerTests" 2>&1 | tail -5
```

Expected: `Passed! - Failed: 0, Passed: 2`

- [ ] **Step 7: Register keyed cups sink for "Cups" mode in ServiceCollectionExtensions**

In `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, update the `AddPrintQueueSink` method — change the `"Cups"` case to also register the keyed sink:

```csharp
case "Cups":
    services.AddCupsAdapter(configuration);
    services.AddKeyedScoped<IPrintQueueSink, CupsPrintQueueSink>("cups");
    break;
```

The full updated switch block becomes:

```csharp
var printSink = configuration["ExpeditionList:PrintSink"];
switch (printSink)
{
    case "AzureBlob":
        services.AddAzurePrintQueueSink(configuration);
        break;
    case "Cups":
        services.AddCupsAdapter(configuration);
        services.AddKeyedScoped<IPrintQueueSink, CupsPrintQueueSink>("cups");
        break;
    case "Combined":
        services.AddAzurePrintQueueSink(configuration);
        services.AddCupsAdapter(configuration);
        services.AddKeyedScoped<IPrintQueueSink, AzureBlobPrintQueueSink>("azure");
        services.AddKeyedScoped<IPrintQueueSink, CupsPrintQueueSink>("cups");
        services.AddScoped<IPrintQueueSink, CombinedPrintQueueSink>();
        break;
    default: // "FileSystem" or unset
        services.AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>();
        break;
}
```

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/
git add backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs
git add backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat: add ReprintExpeditionList use case with keyed cups sink support"
```

---

## Task 7: ExpeditionListArchiveModule + Controller

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs`
- Create: `backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`

The controller's `Reprint` endpoint uses `[FromKeyedServices("cups")] IPrintQueueSink cupsSink` injected via constructor, and passes it to the handler via a factory approach — OR simpler: inject `IPrintQueueSink` with keyed attribute in the constructor and create a handler there.

Actually the cleanest approach: the controller creates a `ReprintExpeditionListHandler` inline using the keyed service, OR inject a factory. But per the existing pattern, controllers delegate to MediatR. So the handler needs to receive the keyed sink from DI.

Since `IRequestHandler<>` instances are resolved by MediatR from DI, and we want the handler to receive the keyed "cups" sink — we need to register the handler with a keyed parameter. This is complex.

**Simpler approach**: Register a named factory or register `ReprintExpeditionListHandler` with explicit constructor resolution. OR: make the controller inject the keyed sink and pass it to a handler factory.

**Simplest approach that works cleanly**: The Reprint endpoint on the controller directly handles the validation and calls MediatR, but the handler gets the cups sink injected as a NON-keyed IPrintQueueSink (meaning: register the cups sink also as a non-keyed `AddKeyedScoped` when the action is "Cups" or "Combined").

Wait, this gets circular. Let me use a different approach:

**Final approach**: Create a `IReprintExpeditionListService` interface in the Application layer that the controller calls directly (bypassing MediatR for this one endpoint since it requires keyed DI). No — that breaks the pattern.

**Cleanest final approach**: Register `ReprintExpeditionListHandler` explicitly in DI with the keyed parameter resolved. Use a lambda registration:

```csharp
services.AddTransient<ReprintExpeditionListHandler>(provider =>
    new ReprintExpeditionListHandler(
        provider.GetRequiredService<IBlobStorageService>(),
        provider.GetRequiredKeyedService<IPrintQueueSink>("cups")));
```

And in the controller, inject `ReprintExpeditionListHandler` directly instead of using MediatR for the reprint endpoint, OR wrap it in an adapter.

Actually, looking at this more carefully, MediatR resolves handlers from the DI container. If we register `ReprintExpeditionListHandler` explicitly as above, MediatR should use it when resolving. This should work since MediatR calls `ServiceProvider.GetService<IRequestHandler<...>>()` and our lambda registration maps to `ReprintExpeditionListHandler` which implements `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>`.

We just need to also register it as `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>`:

```csharp
services.AddTransient<IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>>(provider =>
    new ReprintExpeditionListHandler(
        provider.GetRequiredService<IBlobStorageService>(),
        provider.GetRequiredKeyedService<IPrintQueueSink>("cups")));
```

This will work.

- [ ] **Step 1: Create ExpeditionListArchiveModule**

Create `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive;

public static class ExpeditionListArchiveModule
{
    public static IServiceCollection AddExpeditionListArchiveModule(this IServiceCollection services)
    {
        // ReprintExpeditionListHandler requires the keyed "cups" IPrintQueueSink.
        // We register it explicitly so MediatR uses the correct instance.
        services.AddTransient<IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>>(provider =>
            new ReprintExpeditionListHandler(
                provider.GetRequiredService<IBlobStorageService>(),
                provider.GetRequiredKeyedService<IPrintQueueSink>("cups")));

        return services;
    }
}
```

- [ ] **Step 2: Register in ApplicationModule**

In `backend/src/Anela.Heblo.Application/ApplicationModule.cs`, add the import and the call after `AddExpeditionListModule(configuration)`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive;
```

And in `AddApplicationServices`:

```csharp
services.AddExpeditionListModule(configuration);
services.AddExpeditionListArchiveModule();
```

- [ ] **Step 3: Create the controller**

Create `backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionDates;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/expedition-list-archive")]
public class ExpeditionListArchiveController : BaseApiController
{
    private readonly IMediator _mediator;

    public ExpeditionListArchiveController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("dates")]
    public async Task<ActionResult<GetExpeditionDatesResponse>> GetDates(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var request = new GetExpeditionDatesRequest { Page = page, PageSize = pageSize };
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpGet("{date}")]
    public async Task<ActionResult<GetExpeditionListsByDateResponse>> GetByDate(string date)
    {
        var request = new GetExpeditionListsByDateRequest { Date = date };
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpGet("download/{*blobPath}")]
    public async Task<ActionResult> Download(string blobPath)
    {
        var request = new DownloadExpeditionListRequest { BlobPath = blobPath };
        var response = await _mediator.Send(request);

        if (!response.Success || response.Stream == null)
        {
            return BadRequest(response.ErrorMessage);
        }

        return File(response.Stream, response.ContentType, response.FileName);
    }

    [HttpPost("reprint")]
    public async Task<ActionResult<ReprintExpeditionListResponse>> Reprint([FromBody] ReprintExpeditionListRequest request)
    {
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }
}
```

- [ ] **Step 4: Build the API project to verify no errors**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 5: Run all tests to verify nothing broke**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -5
```

Expected: Same as baseline (1825 passed, 3 Docker failures).

- [ ] **Step 6: Run dotnet format**

```bash
dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --verify-no-changes 2>&1 | tail -5
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --verify-no-changes 2>&1 | tail -5
dotnet format backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj --verify-no-changes 2>&1 | tail -5
```

If formatting issues reported, run without `--verify-no-changes` to auto-fix:
```bash
dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet format backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs
git add backend/src/Anela.Heblo.Application/ApplicationModule.cs
git add backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs
git commit -m "feat: add ExpeditionListArchiveModule and controller with 4 endpoints"
```

---

## Task 8: Frontend – QUERY_KEYS and API Hooks

**Files:**
- Modify: `frontend/src/api/client.ts`
- Create: `frontend/src/api/hooks/useExpeditionListArchive.ts`

- [ ] **Step 1: Add expeditionListArchive key to QUERY_KEYS**

In `frontend/src/api/client.ts`, in the `QUERY_KEYS` object (around line 399), add after `knowledgeBase`:

```typescript
  expeditionListArchive: ["expedition-list-archive"] as const,
```

- [ ] **Step 2: Create the hook file**

Create `frontend/src/api/hooks/useExpeditionListArchive.ts`:

```typescript
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";

// --- Types ---

export interface ExpeditionListItemDto {
  blobPath: string;
  fileName: string;
  createdOn: string | null;
  contentLength: number | null;
}

export interface GetExpeditionDatesResponse {
  dates: string[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface GetExpeditionListsByDateResponse {
  items: ExpeditionListItemDto[];
}

export interface ReprintExpeditionListRequest {
  blobPath: string;
}

export interface ReprintExpeditionListResponse {
  success: boolean;
  errorMessage: string | null;
}

// --- Query Keys ---

const expeditionArchiveKeys = {
  all: QUERY_KEYS.expeditionListArchive,
  dates: (page: number, pageSize: number) =>
    [...QUERY_KEYS.expeditionListArchive, "dates", page, pageSize] as const,
  itemsByDate: (date: string) =>
    [...QUERY_KEYS.expeditionListArchive, "items", date] as const,
};

// --- Hooks ---

export const useExpeditionDates = (page: number = 1, pageSize: number = 20) => {
  return useQuery<GetExpeditionDatesResponse>({
    queryKey: expeditionArchiveKeys.dates(page, pageSize),
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/expedition-list-archive/dates`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const params = new URLSearchParams();
      params.append("page", page.toString());
      params.append("pageSize", pageSize.toString());

      const response = await (apiClient as any).http.fetch(
        `${fullUrl}?${params.toString()}`,
        {
          method: "GET",
          headers: { "Content-Type": "application/json" },
        }
      );

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return await response.json();
    },
    staleTime: 1000 * 60 * 5,
  });
};

export const useExpeditionListsByDate = (date: string) => {
  return useQuery<GetExpeditionListsByDateResponse>({
    queryKey: expeditionArchiveKeys.itemsByDate(date),
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/expedition-list-archive/${encodeURIComponent(date)}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "GET",
        headers: { "Content-Type": "application/json" },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return await response.json();
    },
    enabled: !!date,
    staleTime: 1000 * 60 * 5,
  });
};

export const useReprintExpeditionList = () => {
  const queryClient = useQueryClient();

  return useMutation<ReprintExpeditionListResponse, Error, ReprintExpeditionListRequest>({
    mutationFn: async (request: ReprintExpeditionListRequest) => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/expedition-list-archive/reprint`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ blobPath: request.blobPath }),
      });

      if (!response.ok) {
        const errorData = await response.json().catch(() => null);
        throw new Error(
          errorData?.errorMessage ?? `HTTP error! status: ${response.status}`
        );
      }

      return await response.json();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.expeditionListArchive });
    },
  });
};

export const getExpeditionListDownloadUrl = (blobPath: string): string => {
  const apiClient = getAuthenticatedApiClient();
  const baseUrl = (apiClient as any).baseUrl;
  return `${baseUrl}/api/expedition-list-archive/download/${encodeURIComponent(blobPath)}`;
};
```

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/client.ts
git add frontend/src/api/hooks/useExpeditionListArchive.ts
git commit -m "feat: add expedition list archive API hooks"
```

---

## Task 9: Frontend – ExpeditionListArchivePage

**Files:**
- Create: `frontend/src/pages/ExpeditionListArchivePage.tsx`

The page:
1. On load: fetches paginated date list, auto-selects the most recent date
2. Date picker: shows all available dates, user can select one to view its items
3. Table: filename, upload time, file size, actions (Open, Reprint)
4. Open: opens `window.open(downloadUrl, '_blank')`
5. Reprint: confirmation dialog → POST reprint → success/error toast
6. Empty state when no items for selected date

- [ ] **Step 1: Create the page component**

Create `frontend/src/pages/ExpeditionListArchivePage.tsx`:

```tsx
import React, { useState, useEffect } from "react";
import { FileText, Printer, ExternalLink, ChevronLeft, ChevronRight } from "lucide-react";
import {
  useExpeditionDates,
  useExpeditionListsByDate,
  useReprintExpeditionList,
  getExpeditionListDownloadUrl,
  ExpeditionListItemDto,
} from "../api/hooks/useExpeditionListArchive";
import { useToast } from "../contexts/ToastContext";

const PAGE_SIZE = 20;

const formatFileSize = (bytes: number | null): string => {
  if (bytes === null) return "–";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
};

const formatDateTime = (iso: string | null): string => {
  if (!iso) return "–";
  return new Date(iso).toLocaleString("cs-CZ", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
};

const ExpeditionListArchivePage: React.FC = () => {
  const { showToast } = useToast();
  const [page, setPage] = useState(1);
  const [selectedDate, setSelectedDate] = useState<string>("");
  const [reprintConfirm, setReprintConfirm] = useState<ExpeditionListItemDto | null>(null);

  const { data: datesData, isLoading: datesLoading } = useExpeditionDates(page, PAGE_SIZE);
  const { data: itemsData, isLoading: itemsLoading } = useExpeditionListsByDate(selectedDate);
  const reprintMutation = useReprintExpeditionList();

  // Auto-select the first (most recent) date when dates load
  useEffect(() => {
    if (datesData?.dates?.length && !selectedDate) {
      setSelectedDate(datesData.dates[0]);
    }
  }, [datesData, selectedDate]);

  const totalPages = datesData ? Math.ceil(datesData.totalCount / PAGE_SIZE) : 0;

  const handleOpen = (item: ExpeditionListItemDto) => {
    const url = getExpeditionListDownloadUrl(item.blobPath);
    window.open(url, "_blank", "noopener,noreferrer");
  };

  const handleReprintConfirm = async () => {
    if (!reprintConfirm) return;
    try {
      await reprintMutation.mutateAsync({ blobPath: reprintConfirm.blobPath });
      showToast("Přetisk odeslán", `${reprintConfirm.fileName} byl odeslán na tiskárnu.`);
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Nepodařilo se odeslat na tisk.";
      showToast("Chyba tisku", msg);
    } finally {
      setReprintConfirm(null);
    }
  };

  return (
    <div className="p-6">
      <h1 className="text-2xl font-semibold text-gray-900 mb-6">Archiv expedičních listů</h1>

      <div className="flex gap-6">
        {/* Date list sidebar */}
        <div className="w-56 flex-shrink-0">
          <h2 className="text-sm font-medium text-gray-700 mb-2">Datum</h2>
          <div className="bg-white border border-gray-200 rounded-lg overflow-hidden">
            {datesLoading ? (
              <div className="p-4 text-center text-gray-500 text-sm">Načítám...</div>
            ) : !datesData?.dates.length ? (
              <div className="p-4 text-center text-gray-500 text-sm">Žádná data</div>
            ) : (
              <ul>
                {datesData.dates.map((date) => (
                  <li key={date}>
                    <button
                      onClick={() => setSelectedDate(date)}
                      className={`w-full text-left px-4 py-2 text-sm hover:bg-gray-50 transition-colors ${
                        selectedDate === date
                          ? "bg-indigo-50 text-indigo-700 font-medium"
                          : "text-gray-700"
                      }`}
                    >
                      {date}
                    </button>
                  </li>
                ))}
              </ul>
            )}

            {/* Pagination for dates */}
            {totalPages > 1 && (
              <div className="flex items-center justify-between px-4 py-2 border-t border-gray-200">
                <button
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                  disabled={page === 1}
                  className="text-gray-500 disabled:opacity-30 hover:text-gray-700"
                >
                  <ChevronLeft size={16} />
                </button>
                <span className="text-xs text-gray-500">
                  {page}/{totalPages}
                </span>
                <button
                  onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                  disabled={page === totalPages}
                  className="text-gray-500 disabled:opacity-30 hover:text-gray-700"
                >
                  <ChevronRight size={16} />
                </button>
              </div>
            )}
          </div>
        </div>

        {/* Items table */}
        <div className="flex-1">
          {!selectedDate ? (
            <div className="flex items-center justify-center h-48 text-gray-500">
              Vyberte datum
            </div>
          ) : itemsLoading ? (
            <div className="flex items-center justify-center h-48">
              <div className="animate-spin rounded-full h-6 w-6 border-b-2 border-indigo-600"></div>
            </div>
          ) : !itemsData?.items.length ? (
            <div className="flex flex-col items-center justify-center h-48 text-gray-500">
              <FileText size={40} className="mb-2 text-gray-300" />
              <p>Žádné soubory pro {selectedDate}</p>
            </div>
          ) : (
            <div className="bg-white border border-gray-200 rounded-lg overflow-hidden">
              <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50">
                  <tr>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Soubor</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Nahráno</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Velikost</th>
                    <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase">Akce</th>
                  </tr>
                </thead>
                <tbody className="bg-white divide-y divide-gray-100">
                  {itemsData.items.map((item) => (
                    <tr key={item.blobPath} className="hover:bg-gray-50">
                      <td className="px-4 py-3 text-sm text-gray-900 flex items-center gap-2">
                        <FileText size={16} className="text-red-400 flex-shrink-0" />
                        {item.fileName}
                      </td>
                      <td className="px-4 py-3 text-sm text-gray-500">
                        {formatDateTime(item.createdOn)}
                      </td>
                      <td className="px-4 py-3 text-sm text-gray-500">
                        {formatFileSize(item.contentLength)}
                      </td>
                      <td className="px-4 py-3 text-right">
                        <div className="flex items-center justify-end gap-2">
                          <button
                            onClick={() => handleOpen(item)}
                            className="inline-flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-indigo-600 bg-indigo-50 rounded hover:bg-indigo-100 transition-colors"
                          >
                            <ExternalLink size={12} />
                            Otevřít
                          </button>
                          <button
                            onClick={() => setReprintConfirm(item)}
                            className="inline-flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-gray-700 bg-gray-100 rounded hover:bg-gray-200 transition-colors"
                          >
                            <Printer size={12} />
                            Přetisk
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>

      {/* Reprint confirmation dialog */}
      {reprintConfirm && (
        <div className="fixed inset-0 bg-black bg-opacity-40 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg shadow-xl p-6 max-w-sm w-full mx-4">
            <h3 className="text-lg font-semibold text-gray-900 mb-2">Potvrdit přetisk</h3>
            <p className="text-sm text-gray-600 mb-4">
              Odeslat <span className="font-medium">{reprintConfirm.fileName}</span> znovu na tiskárnu?
            </p>
            <div className="flex gap-3 justify-end">
              <button
                onClick={() => setReprintConfirm(null)}
                disabled={reprintMutation.isPending}
                className="px-4 py-2 text-sm text-gray-700 bg-gray-100 rounded hover:bg-gray-200 disabled:opacity-50"
              >
                Zrušit
              </button>
              <button
                onClick={handleReprintConfirm}
                disabled={reprintMutation.isPending}
                className="px-4 py-2 text-sm text-white bg-indigo-600 rounded hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-2"
              >
                {reprintMutation.isPending ? (
                  <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white"></div>
                ) : (
                  <Printer size={14} />
                )}
                Přetisknout
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default ExpeditionListArchivePage;
```

- [ ] **Step 2: Verify the component compiles (part of build check below)**

This will be verified in the final build check in Task 10.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/ExpeditionListArchivePage.tsx
git commit -m "feat: add ExpeditionListArchivePage with date picker and table"
```

---

## Task 10: Frontend – Route + Sidebar

**Files:**
- Modify: `frontend/src/App.tsx`
- Modify: `frontend/src/components/Layout/Sidebar.tsx`

- [ ] **Step 1: Add route to App.tsx**

In `frontend/src/App.tsx`, add the import after the existing page imports (around line 37):

```typescript
import ExpeditionListArchivePage from "./pages/ExpeditionListArchivePage";
```

In the `<Routes>` block, add after the other `/logistics/` routes (e.g., after the `/logistics/packing-materials` route):

```tsx
<Route
  path="/logistics/expedition-archive"
  element={<ExpeditionListArchivePage />}
/>
```

- [ ] **Step 2: Add sidebar navigation item**

In `frontend/src/components/Layout/Sidebar.tsx`, in the `logistika` section items array (around line 200), add after `sledovani-materialu`:

```typescript
{
  id: "archiv-expedic",
  name: "Archiv expedic",
  href: "/logistics/expedition-archive",
},
```

The full updated `logistika` section items:

```typescript
items: [
  {
    id: "prijem-boxu",
    name: "Příjem boxů",
    href: "/logistics/receive-boxes",
  },
  {
    id: "transportni-boxy",
    name: "Transportní boxy",
    href: "/logistics/transport-boxes",
  },
  {
    id: "zasoby",
    name: "Zásoby produktů",
    href: "/logistics/inventory",
  },
  {
    id: "vypackovani-balicku",
    name: "Výroba dárkových balíčků",
    href: "/logistics/gift-package-manufacturing",
  },
  {
    id: "statistiky-skladu",
    name: "Statistiky skladu",
    href: "/logistics/warehouse-statistics",
  },
  {
    id: "sledovani-materialu",
    name: "Sledování materiálů",
    href: "/logistics/packing-materials",
  },
  {
    id: "archiv-expedic",
    name: "Archiv expedic",
    href: "/logistics/expedition-archive",
  },
],
```

- [ ] **Step 3: Run frontend build to verify no errors**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feature-expedition-archive/frontend
npm run build 2>&1 | tail -10
```

Expected: `Compiled successfully.`

- [ ] **Step 4: Run frontend lint**

```bash
npm run lint 2>&1 | tail -10
```

Expected: No errors.

- [ ] **Step 5: Run frontend tests**

```bash
npm test -- --watchAll=false --passWithNoTests 2>&1 | tail -5
```

Expected: All 706 tests still pass.

- [ ] **Step 6: Run full backend test suite**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feature-expedition-archive
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -5
```

Expected: 1833 passed (1825 original + 8 new handler tests), 3 Docker failures unchanged.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/App.tsx
git add frontend/src/components/Layout/Sidebar.tsx
git commit -m "feat: add expedition archive route and sidebar navigation item"
```

---

## Task 11: Final Format, Push & PR

- [ ] **Step 1: Run full dotnet format on all modified projects**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feature-expedition-archive
dotnet format backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

- [ ] **Step 2: Check git status — stage any format changes**

```bash
git status
git diff --stat
```

If any files changed from formatting, commit them:

```bash
git add -u
git commit -m "style: apply dotnet format to expedition archive code"
```

- [ ] **Step 3: Final test run — all tests must pass**

Backend:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -5
```

Frontend:
```bash
cd frontend && npm test -- --watchAll=false --passWithNoTests 2>&1 | tail -5
cd ..
```

Both must pass (backend: 1833+, frontend: 706+ passing).

- [ ] **Step 4: Final commit with @claude tag**

```bash
git add -A
git status
# Verify nothing unintended is staged
git commit -m "feat: expedition list archive – browse, open, and reprint stored picking lists @claude"
```

- [ ] **Step 5: Push branch**

```bash
git push origin feature/expedition-list-archive-422
```

- [ ] **Step 6: Create PR**

```bash
gh pr create \
  --title "feat: expedition list archive – browse, open, and reprint stored picking lists" \
  --body "$(cat <<'EOF'
## Summary

Closes #422

- Extends `IBlobStorageService` with `ListBlobsAsync` and `DownloadAsync` implemented in `AzureBlobStorageService`
- Adds `ExpeditionListArchive` vertical slice with 4 MediatR handlers (GetExpeditionDates, GetExpeditionListsByDate, DownloadExpeditionList, ReprintExpeditionList)
- New `ExpeditionListArchiveController` with 4 authenticated REST endpoints
- Path traversal protection on download and reprint endpoints (regex validation + `..` detection)
- React page with date sidebar, PDF table (Open/Reprint actions), confirmation dialog, toasts
- "Archiv expedic" added to Sklad navigation section

## Test plan

- [ ] Backend: all 8 new handler tests pass (`dotnet test --filter ExpeditionListArchive`)
- [ ] Backend: full suite passes (1833+ passing, 3 Docker-only failures pre-existing)
- [ ] Frontend: `npm run build` and `npm run lint` pass
- [ ] Frontend: all 706+ tests pass
- [ ] Path traversal: paths with `..` or non-PDF extensions rejected (unit tested)
- [ ] Empty state shown when no items for a date
- [ ] Reprint confirmation dialog appears before submitting
- [ ] "Archiv expedic" visible in Sklad sidebar section

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Self-Review

**Spec coverage check:**

| Requirement | Task |
|-------------|------|
| Extend `IBlobStorageService` with `ListBlobsAsync` + `BlobItemInfo` | Task 1 |
| Extend `IBlobStorageService` with `DownloadAsync` | Task 1 |
| Implement both in `AzureBlobStorageService` | Task 2 |
| `GetExpeditionDates` handler (paginated, descending) | Task 3 |
| `GetExpeditionListsByDate` handler | Task 4 |
| `DownloadExpeditionList` handler with path traversal protection | Task 5 |
| `ReprintExpeditionList` handler (blob → temp file → cups → delete) | Task 6 |
| `ExpeditionListArchiveModule` registration | Task 7 |
| `ExpeditionListArchiveController` with 4 endpoints + `[Authorize]` | Task 7 |
| Register in `ApplicationModule` | Task 7 |
| Frontend QUERY_KEYS addition | Task 8 |
| `useExpeditionDates` hook | Task 8 |
| `useExpeditionListsByDate` hook | Task 8 |
| `useReprintExpeditionList` mutation | Task 8 |
| `ExpeditionListArchivePage` with date picker + table | Task 9 |
| Open action: `window.open` with download URL | Task 9 |
| Reprint: confirmation dialog + loading + toast | Task 9 |
| Empty state for dates with no items | Task 9 |
| Date list pagination | Task 9 |
| Frontend route `/logistics/expedition-archive` | Task 10 |
| Sidebar item "Archiv expedic" in Sklad section | Task 10 |
| `dotnet format` + `npm run lint` | Tasks 7, 10, 11 |
| All tests pass before push | Task 11 |

**No placeholder issues found.** All steps contain actual code.

**Type consistency:**
- `BlobItemInfo` defined in Task 1 → used in Tasks 2, 3, 4
- `ExpeditionListItemDto` defined in Task 4 → used in Tasks 8, 9
- `GetExpeditionDatesHandler` constructor takes `IBlobStorageService` → matches Task 3 test
- `ReprintExpeditionListHandler` constructor takes `(IBlobStorageService, IPrintQueueSink)` → matches Task 6 test and Task 7 module registration
- `getExpeditionListDownloadUrl` defined in Task 8 → used in Task 9
- `useToast` is used in Task 9 — this is from `../contexts/ToastContext` which already exists in the codebase (pattern confirmed from other pages)
