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
    private readonly DocumentChunker _chunker;
    private readonly IEmbeddingService _embeddingService;

    public UploadDocumentHandler(
        IKnowledgeBaseRepository repository,
        IEnumerable<IDocumentTextExtractor> extractors,
        DocumentChunker chunker,
        IEmbeddingService embeddingService)
    {
        _repository = repository;
        _extractors = extractors;
        _chunker = chunker;
        _embeddingService = embeddingService;
    }

    public async Task<UploadDocumentResponse> Handle(
        UploadDocumentRequest request,
        CancellationToken cancellationToken)
    {
        // Buffer stream for hashing and text extraction
        using var ms = new MemoryStream();
        await request.FileStream.CopyToAsync(ms, cancellationToken);
        var fileBytes = ms.ToArray();

        // Deduplicate by SHA-256 content hash
        var hash = Convert.ToHexString(SHA256.HashData(fileBytes));
        var existing = await _repository.GetDocumentByHashAsync(hash, cancellationToken);
        if (existing != null)
        {
            return new UploadDocumentResponse { Document = MapToSummary(existing) };
        }

        // Resolve content type — browsers/OS may send application/octet-stream for known formats
        var contentType = ResolveContentType(request.ContentType, request.Filename);

        // Validate extractor availability before persisting anything
        var extractor = _extractors.FirstOrDefault(e => e.CanHandle(contentType));
        if (extractor == null)
        {
            return new UploadDocumentResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.UnsupportedFileType,
            };
        }

        // Create document record in "processing" state
        var doc = new KnowledgeBaseDocument
        {
            Id = Guid.NewGuid(),
            Filename = request.Filename,
            SourcePath = $"upload/{Guid.NewGuid()}/{request.Filename}",
            ContentType = contentType,
            ContentHash = hash,
            Status = DocumentStatus.Processing,
            CreatedAt = DateTime.UtcNow,
        };
        await _repository.AddDocumentAsync(doc, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        try
        {
            var text = await extractor.ExtractTextAsync(fileBytes, cancellationToken);

            // Chunk text
            var chunkTexts = _chunker.Chunk(text);

            // Embed each chunk and create chunk entities
            var chunks = new List<KnowledgeBaseChunk>();
            for (var i = 0; i < chunkTexts.Count; i++)
            {
                var embedding = await _embeddingService.GenerateEmbeddingAsync(chunkTexts[i], cancellationToken);
                chunks.Add(new KnowledgeBaseChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentId = doc.Id,
                    ChunkIndex = i,
                    Content = chunkTexts[i],
                    Embedding = embedding,
                });
            }

            await _repository.AddChunksAsync(chunks, cancellationToken);
            doc.Status = DocumentStatus.Indexed;
            doc.IndexedAt = DateTime.UtcNow;
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
            Status = doc.Status,
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
