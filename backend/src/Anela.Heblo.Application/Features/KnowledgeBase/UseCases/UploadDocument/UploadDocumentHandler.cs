using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;

public class UploadDocumentHandler : IRequestHandler<UploadDocumentRequest, UploadDocumentResponse>
{
    private readonly IEnumerable<IDocumentTextExtractor> _extractors;
    private readonly IMediator _mediator;

    public UploadDocumentHandler(
        IEnumerable<IDocumentTextExtractor> extractors,
        IMediator mediator)
    {
        _extractors = extractors;
        _mediator = mediator;
    }

    public async Task<UploadDocumentResponse> Handle(
        UploadDocumentRequest request,
        CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await request.FileStream.CopyToAsync(ms, cancellationToken);
        var fileBytes = ms.ToArray();

        var contentType = ResolveContentType(request.ContentType, request.Filename);

        if (!_extractors.Any(e => e.CanHandle(contentType)))
        {
            return new UploadDocumentResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.UnsupportedFileType,
            };
        }

        var sourcePath = $"upload/{Guid.NewGuid()}/{request.Filename}";

        var indexResponse = await _mediator.Send(new IndexDocumentRequest
        {
            Filename = request.Filename,
            SourcePath = sourcePath,
            ContentType = contentType,
            Content = fileBytes,
            DocumentType = request.DocumentType,
        }, cancellationToken);

        return new UploadDocumentResponse
        {
            Document = MapToSummary(indexResponse),
        };
    }

    private static DocumentSummary MapToSummary(IndexDocumentResponse response) =>
        new()
        {
            Id = response.DocumentId,
            Filename = response.Filename,
            Status = response.Status.ToString().ToLowerInvariant(),
            ContentType = response.ContentType,
            CreatedAt = response.CreatedAt,
            IndexedAt = response.IndexedAt,
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
