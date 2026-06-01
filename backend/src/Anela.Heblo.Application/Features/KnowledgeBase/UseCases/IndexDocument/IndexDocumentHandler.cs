using System.Security.Cryptography;
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

        var contentType = ResolveContentType(request.ContentType, request.Filename);
        var contentHash = Convert.ToHexString(SHA256.HashData(request.Content));

        var useGraphIdentity = !string.IsNullOrEmpty(request.GraphItemId) && !string.IsNullOrEmpty(request.DriveId);

        // Duplicate detection by hash — same content already indexed, skip
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

            return new IndexDocumentResponse
            {
                DocumentId = existingByHash.Id,
                Status = existingByHash.Status,
                WasDuplicate = true,
                Filename = existingByHash.Filename,
                ContentType = existingByHash.ContentType,
                CreatedAt = existingByHash.CreatedAt,
                IndexedAt = existingByHash.IndexedAt,
            };
        }

        // Duplicate detection by identity: use stable GraphItemId for OneDrive-sourced docs,
        // fall back to SourcePath for manually uploaded docs (upload flow).
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

        try
        {
            await _indexingService.IndexChunksAsync(request.Content, contentType, document, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index document {Filename}", request.Filename);
            document.Status = DocumentStatus.Failed;
            try
            {
                await _repository.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Failed to persist Failed status for document {Filename}", request.Filename);
            }

            throw;
        }

        _logger.LogInformation("Indexed document {Filename}", request.Filename);

        return new IndexDocumentResponse
        {
            DocumentId = document.Id,
            Status = document.Status,
            WasDuplicate = false,
            Filename = document.Filename,
            ContentType = document.ContentType,
            CreatedAt = document.CreatedAt,
            IndexedAt = document.IndexedAt,
        };
    }

    /// <summary>
    /// Resolves the effective content type, falling back to file extension when the source
    /// reports a generic type (application/octet-stream).
    /// </summary>
    private static string ResolveContentType(string contentType, string filename) =>
        string.IsNullOrEmpty(contentType) || contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            ? Path.GetExtension(filename).ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".doc" => "application/msword",
                ".txt" => "text/plain",
                ".md" => "text/markdown",
                _ => contentType
            }
            : contentType;
}
