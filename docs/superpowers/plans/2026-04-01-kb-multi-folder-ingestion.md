# KB Multi-Folder OneDrive Ingestion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Route OneDrive ingestion through per-type folder mappings so that files in `/KnowledgeBase/Inbox` are indexed as `DocumentType.KnowledgeBase` and files in `/Conversation/Inbox` are indexed as `DocumentType.Conversation`, with each folder having its own archived destination.

**Architecture:** Add a `OneDriveFolderMapping` config record to `KnowledgeBaseOptions`, thread `DocumentType` through `IndexDocumentRequest` → `IndexDocumentHandler`, and update `IOneDriveService` to accept explicit path parameters so the ingestion job can iterate all mappings. No schema changes — `DocumentType` column already exists; `DocumentIndexingService` already routes by type.

**Tech Stack:** .NET 8, MediatR, Hangfire, Moq + xUnit for tests.

---

## File Map

| File | Action | Purpose |
|---|---|---|
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs` | Modify | Add `OneDriveFolderMapping` class + `OneDriveFolderMappings` list; remove `OneDriveInboxPath` / `OneDriveArchivedPath` |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IOneDriveService.cs` | Modify | Add `inboxPath` to `ListInboxFilesAsync`; add `archivedPath` to `MoveToArchivedAsync` |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs` | Modify | Use path params instead of reading `_options` fields |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/MockOneDriveService.cs` | Modify | Update signatures to match interface |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentRequest.cs` | Modify | Add `DocumentType` property |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs` | Modify | Set `document.DocumentType` from request |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs` | Modify | Iterate `OneDriveFolderMappings`; pass type + archived path; `DefaultIsEnabled = true` |
| `backend/src/Anela.Heblo.API/appsettings.json` | Modify | Replace `OneDriveInboxPath`/`OneDriveArchivedPath` with `OneDriveFolderMappings` array |
| `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/IndexDocumentHandlerTests.cs` | Modify | Add test: `DocumentType` from request is stored on document |
| `backend/test/Anela.Heblo.Tests/KnowledgeBase/Infrastructure/KnowledgeBaseIngestionJobTests.cs` | Create | New test file: ingestion job iterates mappings, passes correct type + archived path |

---

## Task 1: Add `OneDriveFolderMapping` to `KnowledgeBaseOptions` and update `appsettings.json`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

- [ ] **Step 1: Update `KnowledgeBaseOptions.cs`**

Replace the `OneDriveInboxPath` and `OneDriveArchivedPath` properties with a `OneDriveFolderMapping` class and a `OneDriveFolderMappings` list. The class goes at the bottom of the same file.

Full replacement of the relevant section (leave all other properties untouched):

```csharp
// Remove these two properties:
//   public string OneDriveInboxPath { get; set; } = "/KnowledgeBase/Inbox";
//   public string OneDriveArchivedPath { get; set; } = "/KnowledgeBase/Archived";

// Replace with:
public List<OneDriveFolderMapping> OneDriveFolderMappings { get; set; } =
[
    new() { InboxPath = "/KnowledgeBase/Inbox", ArchivedPath = "/KnowledgeBase/Archived", DocumentType = DocumentType.KnowledgeBase },
    new() { InboxPath = "/Conversation/Inbox",   ArchivedPath = "/Conversation/Archived",  DocumentType = DocumentType.Conversation  }
];
```

Add the `using` for the domain namespace at the top of the file and add the new class at the end of the file:

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;
```

```csharp
public class OneDriveFolderMapping
{
    public string InboxPath { get; set; } = string.Empty;
    public string ArchivedPath { get; set; } = string.Empty;
    public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;
}
```

- [ ] **Step 2: Update `appsettings.json`**

In `backend/src/Anela.Heblo.API/appsettings.json`, replace the two flat path keys under `"KnowledgeBase"`:

```json
// Remove:
"OneDriveInboxPath": "/KnowledgeBase/Inbox",
"OneDriveArchivedPath": "/KnowledgeBase/Archived",

// Replace with:
"OneDriveFolderMappings": [
  { "InboxPath": "/KnowledgeBase/Inbox", "ArchivedPath": "/KnowledgeBase/Archived", "DocumentType": "KnowledgeBase" },
  { "InboxPath": "/Conversation/Inbox",  "ArchivedPath": "/Conversation/Archived",  "DocumentType": "Conversation"  }
],
```

- [ ] **Step 3: Build to verify no compile errors**

```bash
cd backend && dotnet build --no-restore -q
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs \
        backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat(kb): replace single OneDrive paths with OneDriveFolderMappings config"
```

---

## Task 2: Update `IOneDriveService` to accept explicit paths

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IOneDriveService.cs`

- [ ] **Step 1: Update the interface**

Replace the full file content:

```csharp
namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public record OneDriveFile(string Id, string Name, string ContentType, string Path);

public interface IOneDriveService
{
    Task<List<OneDriveFile>> ListInboxFilesAsync(string inboxPath, CancellationToken ct = default);
    Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default);
    Task MoveToArchivedAsync(string fileId, string filename, string archivedPath, CancellationToken ct = default);
}
```

- [ ] **Step 2: Build (expect compile errors in implementations — fix in next tasks)**

```bash
cd backend && dotnet build --no-restore -q 2>&1 | grep -E "error|Error"
```

Expected: Errors in `GraphOneDriveService.cs` and `MockOneDriveService.cs` only.

---

## Task 3: Update `GraphOneDriveService` to use path parameters

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs`

- [ ] **Step 1: Update `ListInboxFilesAsync` signature and body**

Change the method signature and replace `_options.OneDriveInboxPath` with the parameter:

```csharp
public async Task<List<OneDriveFile>> ListInboxFilesAsync(string inboxPath, CancellationToken ct = default)
{
    _logger.LogDebug("Listing files in OneDrive inbox: {Path}", inboxPath);

    var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
    using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

    var encodedPath = string.Join("/", inboxPath.TrimStart('/').Split('/').Select(Uri.EscapeDataString));
    var url = $"{GraphBaseUrl}/users/{Uri.EscapeDataString(_options.OneDriveUserId)}/drive/root:/{encodedPath}:/children?$filter=file ne null";

    var request = CreateRequest(HttpMethod.Get, url, token);
    var response = await client.SendAsync(request, ct);
    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadAsStringAsync(ct);
    using var doc = JsonDocument.Parse(json);

    var files = new List<OneDriveFile>();
    if (doc.RootElement.TryGetProperty("value", out var value))
    {
        foreach (var item in value.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
            var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
            var webUrl = item.TryGetProperty("webUrl", out var urlProp) ? urlProp.GetString() ?? string.Empty : string.Empty;
            var mimeType = "application/octet-stream";

            if (item.TryGetProperty("file", out var fileProp) &&
                fileProp.TryGetProperty("mimeType", out var mimeProp))
            {
                mimeType = mimeProp.GetString() ?? mimeType;
            }

            files.Add(new OneDriveFile(id, name, mimeType, webUrl));
        }
    }

    return files;
}
```

- [ ] **Step 2: Update `MoveToArchivedAsync` signature and body**

Change the method signature and replace `_options.OneDriveArchivedPath` with the parameter:

```csharp
public async Task MoveToArchivedAsync(string fileId, string filename, string archivedPath, CancellationToken ct = default)
{
    _logger.LogDebug("Moving file {Filename} ({FileId}) to archived folder", filename, fileId);

    var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
    using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

    var archivedFolderPath = archivedPath.TrimStart('/');
    var body = JsonSerializer.Serialize(new
    {
        parentReference = new
        {
            path = $"/drive/root:/{archivedFolderPath}"
        }
    });

    var url = $"{GraphBaseUrl}/users/{Uri.EscapeDataString(_options.OneDriveUserId)}/drive/items/{fileId}";
    var request = CreateRequest(new HttpMethod("PATCH"), url, token);
    request.Content = new StringContent(body, Encoding.UTF8, "application/json");

    var response = await client.SendAsync(request, ct);
    response.EnsureSuccessStatusCode();
}
```

- [ ] **Step 3: Build to verify `GraphOneDriveService` compiles**

```bash
cd backend && dotnet build --no-restore -q 2>&1 | grep -E "error|Error"
```

Expected: Only `MockOneDriveService` errors remain (if any). `GraphOneDriveService` errors are gone.

---

## Task 4: Update `MockOneDriveService` signatures

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/MockOneDriveService.cs`

- [ ] **Step 1: Update mock signatures to match interface**

Replace the full file content:

```csharp
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

/// <summary>
/// Mock implementation of IOneDriveService for use in mock authentication mode (local dev and testing).
/// Returns empty results so the ingestion job runs without errors.
/// </summary>
public class MockOneDriveService : IOneDriveService
{
    private readonly ILogger<MockOneDriveService> _logger;

    public MockOneDriveService(ILogger<MockOneDriveService> logger)
    {
        _logger = logger;
    }

    public Task<List<OneDriveFile>> ListInboxFilesAsync(string inboxPath, CancellationToken ct = default)
    {
        _logger.LogInformation("Mock OneDriveService: returning empty inbox file list for {Path}", inboxPath);
        return Task.FromResult(new List<OneDriveFile>());
    }

    public Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default)
    {
        _logger.LogInformation("Mock OneDriveService: simulated download for file {FileId}", fileId);
        return Task.FromResult(Array.Empty<byte>());
    }

    public Task MoveToArchivedAsync(string fileId, string filename, string archivedPath, CancellationToken ct = default)
    {
        _logger.LogInformation("Mock OneDriveService: simulated archive for file {Filename} to {Path}", filename, archivedPath);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Build — zero errors expected**

```bash
cd backend && dotnet build --no-restore -q
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IOneDriveService.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/MockOneDriveService.cs
git commit -m "feat(kb): parameterize OneDrive service path methods"
```

---

## Task 5: Thread `DocumentType` through `IndexDocumentRequest` and `IndexDocumentHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/IndexDocumentHandlerTests.cs`

- [ ] **Step 1: Write the failing test for DocumentType propagation**

In `IndexDocumentHandlerTests.cs`, add a new test after the existing ones:

```csharp
[Fact]
public async Task Handle_SetsDocumentTypeFromRequest()
{
    KnowledgeBaseDocument? savedDoc = null;
    _repository.Setup(r => r.AddDocumentAsync(It.IsAny<KnowledgeBaseDocument>(), default))
        .Callback<KnowledgeBaseDocument, CancellationToken>((doc, _) => savedDoc = doc);

    await CreateHandler().Handle(new IndexDocumentRequest
    {
        Filename = "chat.txt",
        SourcePath = "/Conversation/Inbox/chat.txt",
        ContentType = "text/plain",
        Content = [1, 2, 3],
        ContentHash = "abc123",
        DocumentType = DocumentType.Conversation
    }, default);

    Assert.NotNull(savedDoc);
    Assert.Equal(DocumentType.Conversation, savedDoc!.DocumentType);
}
```

Add the missing using at the top of the test file if not already present:

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;
```

- [ ] **Step 2: Run the test — expect compile failure (DocumentType not on request yet)**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~IndexDocumentHandlerTests.Handle_SetsDocumentTypeFromRequest" \
  --no-build -q 2>&1 | tail -5
```

Expected: Compile error — `'IndexDocumentRequest' does not contain a definition for 'DocumentType'`.

- [ ] **Step 3: Add `DocumentType` to `IndexDocumentRequest`**

Replace the full file content:

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;
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

    /// <summary>Document type determined by the source folder. Drives indexing strategy selection.</summary>
    public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;
}
```

- [ ] **Step 4: Update `IndexDocumentHandler` to set `document.DocumentType`**

In `IndexDocumentHandler.cs`, in the `Handle` method, add `DocumentType = request.DocumentType` when constructing the `KnowledgeBaseDocument`:

```csharp
var document = new KnowledgeBaseDocument
{
    Id = Guid.NewGuid(),
    Filename = request.Filename,
    SourcePath = request.SourcePath,
    ContentType = request.ContentType,
    ContentHash = request.ContentHash,
    DocumentType = request.DocumentType,
    Status = DocumentStatus.Processing,
    CreatedAt = DateTime.UtcNow
};
```

- [ ] **Step 5: Run all `IndexDocumentHandlerTests` — expect all pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~IndexDocumentHandlerTests" -q
```

Expected: All tests pass (3 existing + 1 new = 4 total).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentRequest.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/IndexDocumentHandlerTests.cs
git commit -m "feat(kb): thread DocumentType through IndexDocumentRequest and handler"
```

---

## Task 6: Update ingestion job to iterate folder mappings

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs`
- Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Infrastructure/KnowledgeBaseIngestionJobTests.cs`

- [ ] **Step 1: Write failing tests for the ingestion job**

Create the new test file:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Infrastructure;

public class KnowledgeBaseIngestionJobTests
{
    private readonly Mock<IOneDriveService> _oneDrive = new();
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();

    private KnowledgeBaseIngestionJob CreateJob(KnowledgeBaseOptions options)
    {
        _statusChecker.Setup(s => s.IsJobEnabledAsync("knowledge-base-ingestion")).ReturnsAsync(true);
        return new KnowledgeBaseIngestionJob(
            _oneDrive.Object,
            _repository.Object,
            _mediator.Object,
            _statusChecker.Object,
            Options.Create(options),
            NullLogger<KnowledgeBaseIngestionJob>.Instance);
    }

    private static KnowledgeBaseOptions OptionsWithTwoMappings() => new()
    {
        OneDriveFolderMappings =
        [
            new() { InboxPath = "/KnowledgeBase/Inbox", ArchivedPath = "/KnowledgeBase/Archived", DocumentType = DocumentType.KnowledgeBase },
            new() { InboxPath = "/Conversation/Inbox",  ArchivedPath = "/Conversation/Archived",  DocumentType = DocumentType.Conversation  }
        ],
        OneDriveUserId = "user@test.com"
    };

    [Fact]
    public async Task ExecuteAsync_IndexesFileFromKnowledgeBaseFolder_WithCorrectDocumentType()
    {
        var options = OptionsWithTwoMappings();
        var job = CreateJob(options);

        var kbFile = new OneDriveFile("id-kb-1", "manual.pdf", "application/pdf", "/KnowledgeBase/Inbox/manual.pdf");
        _oneDrive.Setup(s => s.ListInboxFilesAsync("/KnowledgeBase/Inbox", default)).ReturnsAsync([kbFile]);
        _oneDrive.Setup(s => s.ListInboxFilesAsync("/Conversation/Inbox", default)).ReturnsAsync([]);
        _oneDrive.Setup(s => s.DownloadFileAsync("id-kb-1", default)).ReturnsAsync([1, 2, 3]);
        _repository.Setup(r => r.GetDocumentByHashAsync(It.IsAny<string>(), default)).ReturnsAsync((KnowledgeBaseDocument?)null);
        _repository.Setup(r => r.GetDocumentBySourcePathAsync(It.IsAny<string>(), default)).ReturnsAsync((KnowledgeBaseDocument?)null);

        await job.ExecuteAsync();

        _mediator.Verify(m => m.Send(
            It.Is<IndexDocumentRequest>(r =>
                r.Filename == "manual.pdf" &&
                r.DocumentType == DocumentType.KnowledgeBase),
            default), Times.Once);
        _oneDrive.Verify(s => s.MoveToArchivedAsync("id-kb-1", "manual.pdf", "/KnowledgeBase/Archived", default), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_IndexesFileFromConversationFolder_WithCorrectDocumentType()
    {
        var options = OptionsWithTwoMappings();
        var job = CreateJob(options);

        var convFile = new OneDriveFile("id-conv-1", "chat.txt", "text/plain", "/Conversation/Inbox/chat.txt");
        _oneDrive.Setup(s => s.ListInboxFilesAsync("/KnowledgeBase/Inbox", default)).ReturnsAsync([]);
        _oneDrive.Setup(s => s.ListInboxFilesAsync("/Conversation/Inbox", default)).ReturnsAsync([convFile]);
        _oneDrive.Setup(s => s.DownloadFileAsync("id-conv-1", default)).ReturnsAsync([4, 5, 6]);
        _repository.Setup(r => r.GetDocumentByHashAsync(It.IsAny<string>(), default)).ReturnsAsync((KnowledgeBaseDocument?)null);
        _repository.Setup(r => r.GetDocumentBySourcePathAsync(It.IsAny<string>(), default)).ReturnsAsync((KnowledgeBaseDocument?)null);

        await job.ExecuteAsync();

        _mediator.Verify(m => m.Send(
            It.Is<IndexDocumentRequest>(r =>
                r.Filename == "chat.txt" &&
                r.DocumentType == DocumentType.Conversation),
            default), Times.Once);
        _oneDrive.Verify(s => s.MoveToArchivedAsync("id-conv-1", "chat.txt", "/Conversation/Archived", default), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsAlreadyIndexedFileByHash()
    {
        var options = OptionsWithTwoMappings();
        var job = CreateJob(options);

        var file = new OneDriveFile("id-1", "doc.pdf", "application/pdf", "/KnowledgeBase/Inbox/doc.pdf");
        _oneDrive.Setup(s => s.ListInboxFilesAsync("/KnowledgeBase/Inbox", default)).ReturnsAsync([file]);
        _oneDrive.Setup(s => s.ListInboxFilesAsync("/Conversation/Inbox", default)).ReturnsAsync([]);
        _oneDrive.Setup(s => s.DownloadFileAsync("id-1", default)).ReturnsAsync([1, 2, 3]);
        _repository.Setup(r => r.GetDocumentByHashAsync(It.IsAny<string>(), default))
            .ReturnsAsync(new KnowledgeBaseDocument { Id = Guid.NewGuid(), SourcePath = "/KnowledgeBase/Inbox/doc.pdf" });

        await job.ExecuteAsync();

        _mediator.Verify(m => m.Send(It.IsAny<IndexDocumentRequest>(), default), Times.Never);
        _oneDrive.Verify(s => s.MoveToArchivedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }
}
```

- [ ] **Step 2: Run tests — expect compile errors (job constructor doesn't accept IOptions yet)**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~KnowledgeBaseIngestionJobTests" \
  --no-build -q 2>&1 | tail -10
```

Expected: Compile errors referencing `KnowledgeBaseIngestionJob` constructor.

- [ ] **Step 3: Rewrite `KnowledgeBaseIngestionJob`**

Replace the full file content:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure.Jobs;

public class KnowledgeBaseIngestionJob : IRecurringJob
{
    private readonly IOneDriveService _oneDrive;
    private readonly IKnowledgeBaseRepository _repository;
    private readonly IMediator _mediator;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly KnowledgeBaseOptions _options;
    private readonly ILogger<KnowledgeBaseIngestionJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "knowledge-base-ingestion",
        DisplayName = "Knowledge Base Ingestion",
        Description = "Polls OneDrive inbox folders and ingests new documents into the knowledge base vector store",
        CronExpression = "*/15 * * * *",
        DefaultIsEnabled = true
    };

    public KnowledgeBaseIngestionJob(
        IOneDriveService oneDrive,
        IKnowledgeBaseRepository repository,
        IMediator mediator,
        IRecurringJobStatusChecker statusChecker,
        IOptions<KnowledgeBaseOptions> options,
        ILogger<KnowledgeBaseIngestionJob> logger)
    {
        _oneDrive = oneDrive;
        _repository = repository;
        _mediator = mediator;
        _statusChecker = statusChecker;
        _options = options.Value;
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

        int indexed = 0;
        int skipped = 0;
        int failed = 0;

        foreach (var mapping in _options.OneDriveFolderMappings)
        {
            _logger.LogInformation("Polling {InboxPath} ({DocumentType})", mapping.InboxPath, mapping.DocumentType);

            var files = await _oneDrive.ListInboxFilesAsync(mapping.InboxPath, cancellationToken);
            _logger.LogInformation("Found {Count} files in {InboxPath}", files.Count, mapping.InboxPath);

            foreach (var file in files)
            {
                try
                {
                    var content = await _oneDrive.DownloadFileAsync(file.Id, cancellationToken);

                    var contentHash = Convert.ToHexString(
                        System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();

                    var existingDocument = await _repository.GetDocumentByHashAsync(contentHash, cancellationToken);
                    if (existingDocument is not null)
                    {
                        if (existingDocument.SourcePath != file.Path)
                        {
                            _logger.LogInformation("File {Filename} moved, updating path from {OldPath} to {NewPath}",
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

                    var existingByPath = await _repository.GetDocumentBySourcePathAsync(file.Path, cancellationToken);
                    if (existingByPath is not null)
                    {
                        _logger.LogInformation(
                            "File {Filename} at {Path} has new content (hash changed). Deleting old document {Id} before re-indexing.",
                            file.Name, file.Path, existingByPath.Id);
                        await _repository.DeleteDocumentAsync(existingByPath.Id, cancellationToken);
                    }

                    await _mediator.Send(new IndexDocumentRequest
                    {
                        Filename = file.Name,
                        SourcePath = file.Path,
                        ContentType = file.ContentType,
                        Content = content,
                        ContentHash = contentHash,
                        DocumentType = mapping.DocumentType
                    }, cancellationToken);

                    await _oneDrive.MoveToArchivedAsync(file.Id, file.Name, mapping.ArchivedPath, cancellationToken);

                    _logger.LogInformation("Indexed and archived {Filename} as {DocumentType}", file.Name, mapping.DocumentType);
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
        }

        _logger.LogInformation("{JobName} complete. Indexed: {Indexed}, Skipped: {Skipped}, Failed: {Failed}",
            Metadata.JobName, indexed, skipped, failed);
    }
}
```

- [ ] **Step 4: Run all ingestion job tests — expect all pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~KnowledgeBaseIngestionJobTests" -q
```

Expected: All 3 tests pass.

- [ ] **Step 5: Run full test suite — expect no regressions**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -q
```

Expected: All tests pass. Note the total count for reference.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/Infrastructure/KnowledgeBaseIngestionJobTests.cs
git commit -m "feat(kb): iterate OneDriveFolderMappings in ingestion job, enable by default"
```

---

## Task 7: Final build and format check

- [ ] **Step 1: Full build**

```bash
cd backend && dotnet build -q
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Format check**

```bash
cd backend && dotnet format --verify-no-changes
```

If violations are reported, run `dotnet format` (without `--verify-no-changes`) to fix them, then re-run the check.

- [ ] **Step 3: Run full test suite one final time**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -q
```

Expected: All tests pass, 0 failed.

- [ ] **Step 4: Final commit (if format fixes were needed)**

```bash
git add -u
git commit -m "style(kb): apply dotnet format after multi-folder ingestion changes"
```
