using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.GetMaterialDocumentTypes;

public class GetMaterialDocumentTypesResponse : BaseResponse
{
    public List<MaterialDocumentTypeDto> DocumentTypes { get; set; } = [];
}
