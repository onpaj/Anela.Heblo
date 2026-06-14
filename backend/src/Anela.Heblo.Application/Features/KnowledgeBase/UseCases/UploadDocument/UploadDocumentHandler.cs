using Anela.Heblo.Application.Features.KnowledgeBase;
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

        var contentType = ContentTypeResolver.Resolve(request.ContentType, request.Filename);

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
}
