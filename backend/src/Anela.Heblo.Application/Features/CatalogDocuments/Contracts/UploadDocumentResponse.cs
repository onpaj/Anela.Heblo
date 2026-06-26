using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Contracts;

public class UploadDocumentResponse : BaseResponse
{
    public string UploadedFilename { get; set; } = string.Empty;

    public UploadDocumentResponse() { }

    public UploadDocumentResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
