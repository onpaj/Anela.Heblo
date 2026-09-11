### task: extract-index-document-handler-phases

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs` (entire file)
- Test (read-only, must pass unmodified): `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/IndexDocumentHandlerTests.cs`

This is the entire change — one file, one class, no independent sub-boundaries (confirmed by `arch-review.r1.md` Component Overview: all four extracted methods live on the same class using the same three injected fields). Per spec NFR-1, the existing 13-test suite is the behavioral contract; it must pass unmodified before and after — this task does not add or edit any test.

- [ ] **Step 1: Run the existing tests to confirm the pre-refactor baseline is green**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.KnowledgeBase.UseCases.IndexDocumentHandlerTests"`

Expected: PASS — all 13 tests in `IndexDocumentHandlerTests` succeed against the current (pre-refactor) `IndexDocumentHandler.cs`. This establishes the baseline the refactor must not break.

- [ ] **Step 2: Replace `IndexDocumentHandler.cs` with the phase-extracted version**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs` with:

```csharp
using System.Security.Cryptography;
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Shared.Rag;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;

public class IndexDocumentHandler : IRequestHandler<IndexDocumentRequest, IndexDocumentResponse>
{
    private readonly IKnowledgeBaseRepository _repository;
    private readonly IDocumentIndexingService _indexingService;
    private readonly ILogger<IndexDocumentHandler> _logger;

    public IndexDocumentHandler(
        IKnowledgeBaseRepository repository,
        IDocumentIndexingService indexingService,
        ILogger<IndexDocumentHandler> logger)
    {
        _repository = repository;
        _indexingService = indexingService;
        _logger = logger;
    }

    public async Task<IndexDocumentResponse> Handle(IndexDocumentRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Indexing document {Filename} from {SourcePath}", request.Filename, request.SourcePath);

        var contentType = ContentTypeResolver.Resolve(request.ContentType, request.Filename);
        var contentHash = Convert.ToHexString(SHA256.HashData(request.Content));
        var useGraphIdentity = !string.IsNullOrEmpty(request.GraphItemId) && !string.IsNullOrEmpty(request.DriveId);

        var duplicateResponse = await TryResolveDuplicateAsync(request, contentHash, useGraphIdentity, cancellationToken);
        if (duplicateResponse is not null)
        {
            return duplicateResponse;
        }

        var document = await CreateAndPersistDocumentAsync(request, contentType, contentHash, useGraphIdentity, cancellationToken);
        await IndexWithErrorHandlingAsync(document, request.Content, cancellationToken);

        _logger.LogInformation("Indexed document {Filename}", request.Filename);

        return BuildResponse(document, wasDuplicate: false);
    }

    // Duplicate detection by hash (same content already indexed) followed by, if no hash
    // match, duplicate detection by identity (stable GraphItemId for OneDrive-sourced docs,
    // SourcePath fallback for manually uploaded docs). Returns a response when the caller
    // should short-circuit (hash match); returns null when the caller should proceed to
    // create a new document (no match, or identity match — whose stale document this method
    // has already deleted).
    private async Task<IndexDocumentResponse?> TryResolveDuplicateAsync(
        IndexDocumentRequest request,
        string contentHash,
        bool useGraphIdentity,
        CancellationToken cancellationToken)
    {
        var existingByHash = await _repository.GetDocumentByHashAsync(contentHash, cancellationToken);
        if (existingByHash is not null)
        {
            if (existingByHash.SourcePath != request.SourcePath)
            {
                _logger.LogInformation("Document {Filename} moved, updating path from {OldPath} to {NewPath}",
                    request.Filename, existingByHash.SourcePath, request.SourcePath);
                await _repository.UpdateDocumentSourcePathAsync(existingByHash.Id, request.SourcePath, cancellationToken);
            }
            else
            {
                _logger.LogDebug("Skipping already-indexed document {Filename} (hash match)", request.Filename);
            }

            if (useGraphIdentity && existingByHash.GraphItemId is null)
            {
                _logger.LogInformation(
                    "Backfilling DriveId/GraphItemId for legacy document {Id}",
                    existingByHash.Id);
                await _repository.UpdateDocumentGraphItemIdAsync(
                    existingByHash.Id, request.DriveId!, request.GraphItemId!, cancellationToken);
            }

            return BuildResponse(existingByHash, wasDuplicate: true);
        }

        var existingByIdentity = useGraphIdentity
            ? await _repository.GetDocumentByGraphItemIdAsync(request.DriveId!, request.GraphItemId!, cancellationToken)
            : await _repository.GetDocumentBySourcePathAsync(request.SourcePath, cancellationToken);

        if (existingByIdentity is not null)
        {
            _logger.LogInformation(
                "Replacing old document {Id} (identity match) before re-indexing.",
                existingByIdentity.Id);
            await _repository.DeleteDocumentAsync(existingByIdentity.Id, cancellationToken);
        }

        return null;
    }

    private async Task<KnowledgeBaseDocument> CreateAndPersistDocumentAsync(
        IndexDocumentRequest request,
        string contentType,
        string contentHash,
        bool useGraphIdentity,
        CancellationToken cancellationToken)
    {
        var document = new KnowledgeBaseDocument
        {
            Id = Guid.NewGuid(),
            Filename = request.Filename,
            SourcePath = request.SourcePath,
            ContentType = contentType,
            ContentHash = contentHash,
            DocumentType = request.DocumentType,
            Status = DocumentStatus.Processing,
            CreatedAt = DateTime.UtcNow,
            DriveId = useGraphIdentity ? request.DriveId : null,
            GraphItemId = useGraphIdentity ? request.GraphItemId : null,
        };

        await _repository.AddDocumentAsync(document, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return document;
    }

    private async Task IndexWithErrorHandlingAsync(
        KnowledgeBaseDocument document,
        byte[] content,
        CancellationToken cancellationToken)
    {
        try
        {
            await _indexingService.IndexChunksAsync(content, document.ContentType, document, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index document {Filename}", document.Filename);
            document.Status = DocumentStatus.Failed;
            try
            {
                await _repository.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Failed to persist Failed status for document {Filename}", document.Filename);
            }

            throw;
        }
    }

    private static IndexDocumentResponse BuildResponse(KnowledgeBaseDocument document, bool wasDuplicate)
    {
        return new IndexDocumentResponse
        {
            DocumentId = document.Id,
            Status = document.Status,
            WasDuplicate = wasDuplicate,
            Filename = document.Filename,
            ContentType = document.ContentType,
            CreatedAt = document.CreatedAt,
            IndexedAt = document.IndexedAt,
        };
    }
}
```

Notes on equivalence with the pre-refactor code (verify while editing, do not deviate):
- `contentType` is computed once in `Handle` and threaded into `CreateAndPersistDocumentAsync`; `IndexWithErrorHandlingAsync` reads it back off `document.ContentType` (set identically) rather than taking it as a second parameter — the value passed to `_indexingService.IndexChunksAsync` is therefore unchanged.
- The error-path log messages use `document.Filename`/`document.Filename` instead of `request.Filename` — these are the same string (`document.Filename = request.Filename` in `CreateAndPersistDocumentAsync`), so log output text is byte-for-byte identical.
- The nested try/catch around the Failed-status save (log + swallow only that inner exception, then `throw;` the original) is preserved verbatim inside `IndexWithErrorHandlingAsync`.
- `BuildResponse` is reused for both the duplicate-by-hash path (inside `TryResolveDuplicateAsync`) and the newly-created-document path (in `Handle`) — field mapping is identical to both original inline constructions.

- [ ] **Step 3: Run the tests again to confirm the refactor is behavior-preserving**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.KnowledgeBase.UseCases.IndexDocumentHandlerTests"`

Expected: PASS — all 13 tests still succeed, unmodified, against the refactored handler. If any test fails, the failure identifies exactly which behavior drifted (see spec.r1.md Acceptance Criteria per FR for which test maps to which phase) — fix the extracted method, do not touch the test.

- [ ] **Step 4: Run the full backend build**

Run: `dotnet build`

Expected: Build succeeds with no new warnings or errors.

- [ ] **Step 5: Run the full backend test suite**

Run: `dotnet test`

Expected: PASS — no regressions outside `IndexDocumentHandlerTests` (this handler has no other direct consumers whose tests could be affected by a purely-private-method-internal refactor, but this is the mechanical whole-suite check called for by `CLAUDE.md`'s validation gate).

- [ ] **Step 6: Run code formatting check**

Run: `dotnet format --verify-no-changes`

Expected: No formatting violations reported. If it reports violations, run `dotnet format` (without `--verify-no-changes`) to apply them, then re-run Step 3 and Step 5 to confirm tests are still green after formatting.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs
git commit -m "refactor(knowledgebase): extract IndexDocumentHandler.Handle phases into private methods"
```
