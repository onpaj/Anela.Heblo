using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Contracts;

public class CatalogDocumentsUploadDocumentResponse : BaseResponse
{
    public string UploadedFilename { get; set; } = string.Empty;

    public CatalogDocumentsUploadDocumentResponse() { }

    public CatalogDocumentsUploadDocumentResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
