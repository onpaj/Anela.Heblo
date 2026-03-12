using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;

public class UploadDocumentRequest : IRequest<UploadDocumentResponse>
{
    public Stream FileStream { get; set; } = default!;
    public string Filename { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long FileSizeBytes { get; set; }
}
