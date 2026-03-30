using System.Security.Cryptography;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;

public class UploadDocumentHandler : IRequestHandler<UploadDocumentRequest, UploadDocumentResponse>
{
    private readonly IKnowledgeBaseRepository _repository;
    private readonly IEnumerable<IDocumentTextExtractor> _extractors;
    private readonly IDocumentIndexingService _indexingService;

    public UploadDocumentHandler(
        IKnowledgeBaseRepository repository,
        IEnumerable<IDocumentTextExtractor> extractors,
        IDocumentIndexingService indexingService)
    {
        _repository = repository;
        _extractors = extractors;
        _indexingService = indexingService;
    }

    public async Task<UploadDocumentResponse> Handle(
        UploadDocumentRequest request,
        CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await request.FileStream.CopyToAsync(ms, cancellationToken);
        var fileBytes = ms.ToArray();

        var hash = Convert.ToHexString(SHA256.HashData(fileBytes));
        var existing = await _repository.GetDocumentByHashAsync(hash, cancellationToken);
        if (existing != null)
        {
            return new UploadDocumentResponse { Document = MapToSummary(existing) };
        }

        var contentType = ResolveContentType(request.ContentType, request.Filename);

        // Validate extractor availability before persisting anything
        if (!_extractors.Any(e => e.CanHandle(contentType)))
        {
            return new UploadDocumentResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.UnsupportedFileType,
            };
        }

        var doc = new KnowledgeBaseDocument
        {
            Id = Guid.NewGuid(),
            Filename = request.Filename,
            SourcePath = $"upload/{Guid.NewGuid()}/{request.Filename}",
            ContentType = contentType,
            ContentHash = hash,
            Status = DocumentStatus.Processing,
            DocumentType = request.DocumentType,
            CreatedAt = DateTime.UtcNow,
        };
        await _repository.AddDocumentAsync(doc, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        try
        {
            await _indexingService.IndexChunksAsync(fileBytes, contentType, doc, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            doc.Status = DocumentStatus.Failed;
            await _repository.SaveChangesAsync(cancellationToken);
            throw;
        }

        return new UploadDocumentResponse { Document = MapToSummary(doc) };
    }

    private static DocumentSummary MapToSummary(KnowledgeBaseDocument doc) =>
        new()
        {
            Id = doc.Id,
            Filename = doc.Filename,
            Status = doc.Status.ToString().ToLowerInvariant(),
            ContentType = doc.ContentType,
            CreatedAt = doc.CreatedAt,
            IndexedAt = doc.IndexedAt,
        };

    /// <summary>
    /// Resolves the effective content type, falling back to file extension when the browser
    /// reports a generic type (application/octet-stream) for drag-and-drop uploads.
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
