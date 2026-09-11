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
