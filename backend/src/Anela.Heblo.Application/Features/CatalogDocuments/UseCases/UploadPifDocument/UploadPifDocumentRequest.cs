using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.UploadPifDocument;

public class UploadPifDocumentRequest : IRequest<UploadDocumentResponse>
{
    public string ProductCode { get; set; } = string.Empty;
    public string OriginalFilename { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public Stream FileStream { get; set; } = Stream.Null;
}
