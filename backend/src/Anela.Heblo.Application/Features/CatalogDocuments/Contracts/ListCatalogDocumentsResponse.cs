using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Contracts;

public class ListCatalogDocumentsResponse : BaseResponse
{
    public FolderStatus FolderStatus { get; set; }
    public string ExpectedPrefix { get; set; } = string.Empty;
    public string BasePath { get; set; } = string.Empty;
    public List<CatalogDocumentDto> Files { get; set; } = [];

    public ListCatalogDocumentsResponse() { }

    public ListCatalogDocumentsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
