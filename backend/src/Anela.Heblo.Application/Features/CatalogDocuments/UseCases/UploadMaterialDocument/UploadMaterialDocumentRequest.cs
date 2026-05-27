using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.UploadMaterialDocument;

public class UploadMaterialDocumentRequest : IRequest<UploadDocumentResponse>
{
    public string ProductCode { get; set; } = string.Empty;
    public string OriginalFilename { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public Stream FileStream { get; set; } = Stream.Null;

    // Structured upload fields (ignored when UploadAsIs = true)
    public string DocumentTypeCode { get; set; } = string.Empty;
    public string Lot { get; set; } = string.Empty;
    public string CommonName { get; set; } = string.Empty;

    public bool UploadAsIs { get; set; }
}
