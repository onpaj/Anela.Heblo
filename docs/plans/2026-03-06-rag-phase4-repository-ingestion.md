# RAG Knowledge Base – Phase 4: Repository + Ingestion

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement the real EF Core repository, wire it into DI (replacing the Phase 3 placeholder), add the OneDrive service, and create the Hangfire ingestion job.

**Architecture:** Three tasks. Task 1 creates `KnowledgeBaseRepository` with pgvector cosine search and swaps the `NotImplementedKnowledgeBaseRepository` placeholder. Task 2 adds `IOneDriveService` backed by Microsoft Graph. Task 3 adds the `KnowledgeBaseIngestionJob` recurring Hangfire job that polls OneDrive every 15 minutes.

**Key facts about the codebase:**
- `GraphServiceClient` is registered in the API layer via `Microsoft.Identity.Web`'s `AddMicrosoftGraph()` — just inject it directly, do not register it yourself
- Recurring jobs implement `IRecurringJob` from `Anela.Heblo.Domain.Features.BackgroundJobs` — they are auto-discovered from the Application assembly, no manual registration needed
- `IRecurringJobStatusChecker` is injected to check if the job is enabled before running
- See `PurchasePriceRecalculationJob.cs` for a reference implementation of `IRecurringJob`

**Master plan reference:** `docs/plans/2026-03-02-rag-knowledge-base.md`, Tasks 15–18

---

## Task 1: KnowledgeBaseRepository + DI wiring

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` — add real repository registration
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` — remove placeholder
- Delete: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/NotImplementedKnowledgeBaseRepository.cs`

**Step 1: Create KnowledgeBaseRepository**

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.KnowledgeBase;

public class KnowledgeBaseRepository : IKnowledgeBaseRepository
{
    private readonly ApplicationDbContext _context;

    public KnowledgeBaseRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddDocumentAsync(KnowledgeBaseDocument document, CancellationToken ct = default)
    {
        _context.KnowledgeBaseDocuments.Add(document);
        await Task.CompletedTask;
    }

    public async Task AddChunksAsync(IEnumerable<KnowledgeBaseChunk> chunks, CancellationToken ct = default)
    {
        _context.KnowledgeBaseChunks.AddRange(chunks);
        await Task.CompletedTask;
    }

    public async Task<List<KnowledgeBaseDocument>> GetAllDocumentsAsync(CancellationToken ct = default)
    {
        return await _context.KnowledgeBaseDocuments
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<List<(KnowledgeBaseChunk Chunk, double Score)>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken ct = default)
    {
        var vector = new Vector(queryEmbedding);

        // Cosine distance: lower = more similar. Score = 1 - distance.
        var results = await _context.KnowledgeBaseChunks
            .Include(c => c.Document)
            .OrderBy(c => c.Embedding.CosineDistance(vector))
            .Take(topK)
            .Select(c => new
            {
                Chunk = c,
                Distance = c.Embedding.CosineDistance(vector)
            })
            .ToListAsync(ct);

        return results.Select(r => (r.Chunk, Score: 1.0 - (double)r.Distance)).ToList();
    }

    public async Task<KnowledgeBaseDocument?> GetDocumentByHashAsync(string contentHash, CancellationToken ct = default)
    {
        return await _context.KnowledgeBaseDocuments
            .FirstOrDefaultAsync(d => d.ContentHash == contentHash, ct);
    }

    public async Task UpdateDocumentSourcePathAsync(Guid documentId, string newSourcePath, CancellationToken ct = default)
    {
        await _context.KnowledgeBaseDocuments
            .Where(d => d.Id == documentId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.SourcePath, newSourcePath), ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }
}
```

> **NOTE:** `c.Embedding.CosineDistance(vector)` requires `Pgvector.EntityFrameworkCore` extension methods. The `Pgvector` package is already installed in the Persistence project. If `CosineDistance` is not found, verify the `using Pgvector.EntityFrameworkCore;` is present.

**Step 2: Register real repository in PersistenceModule**

Open `PersistenceModule.cs`. Add alongside the other repository registrations (after `IRecurringJobConfigurationRepository`):

```csharp
services.AddScoped<IKnowledgeBaseRepository, KnowledgeBaseRepository>();
```

Add usings at the top:

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Persistence.KnowledgeBase;
```

**Step 3: Remove placeholder from KnowledgeBaseModule**

Open `KnowledgeBaseModule.cs`. Remove this line:

```csharp
services.AddScoped<IKnowledgeBaseRepository, NotImplementedKnowledgeBaseRepository>();
```

Also remove the unused `using` for `IKnowledgeBaseRepository` if it becomes unused.

**Step 4: Delete the placeholder file**

```bash
git rm backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/NotImplementedKnowledgeBaseRepository.cs
```

**Step 5: Build and run all tests**

```bash
cd backend && dotnet build && dotnet test
```

Expected: All tests pass. No `NotImplementedException` references remaining.

**Step 6: Format and commit**

```bash
cd backend && dotnet format
git add backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs
git add backend/src/Anela.Heblo.Persistence/PersistenceModule.cs
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs
git commit -m "feat: add KnowledgeBaseRepository with pgvector cosine similarity search"
```

---

## Task 2: IOneDriveService + Microsoft Graph implementation

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IOneDriveService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` — add registration

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

Before writing, check how `GraphServiceClient` is used in existing code:

```bash
grep -r "GraphServiceClient\|_graphClient" backend/src --include="*.cs" -l
```

Open one of those files to see the injection pattern. Then create:

```csharp
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

> **NOTE:** This uses `/me/drive` which requires delegated user permissions. If the app uses application permissions (service principal), the path changes to use a specific user's drive or SharePoint. Verify against how the existing `GraphServiceClient` calls work in the project (e.g., in Manufacture Order or User Management features). Adjust if needed.

**Step 3: Register in KnowledgeBaseModule**

Add to `KnowledgeBaseModule.AddKnowledgeBaseModule()`:

```csharp
services.AddScoped<IOneDriveService, GraphOneDriveService>();
```

**Step 4: Build**

```bash
cd backend && dotnet build
```

Expected: builds with no errors. If Graph API types differ (method signatures changed between versions), adjust to match the installed `Microsoft.Graph` v5.x API.

**Step 5: Format and commit**

```bash
cd backend && dotnet format
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IOneDriveService.cs
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs
git commit -m "feat: add IOneDriveService and Microsoft Graph implementation for OneDrive access"
```

---

## Task 3: KnowledgeBaseIngestionJob (Hangfire)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs`

**Step 1: Study an existing recurring job first**

Read `backend/src/Anela.Heblo.Application/Features/Purchase/Infrastructure/Jobs/PurchasePriceRecalculationJob.cs` to confirm the exact `IRecurringJob` interface shape. The interface requires:
- `RecurringJobMetadata Metadata { get; }` property
- `Task ExecuteAsync(CancellationToken cancellationToken = default)` method

Jobs in `Application/Features/*/Infrastructure/Jobs/` are auto-discovered — no manual registration needed.

**Step 2: Create the ingestion job**

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
        CronExpression = "*/15 * * * *",
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

                // SHA-256 hash for content-based deduplication — handles moves/renames without re-embedding
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

**Step 3: Build and run full test suite**

```bash
cd backend && dotnet build && dotnet test
```

Expected: all tests pass.

**Step 4: Format and commit**

```bash
cd backend && dotnet format
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs
git commit -m "feat: add KnowledgeBaseIngestionJob Hangfire recurring job for OneDrive ingestion"
```

---

## Phase 4 complete

All infrastructure is wired up. Next: Phase 5 — REST API controller + MCP tools.
See `docs/plans/2026-03-02-rag-knowledge-base.md`, Tasks 19+.
