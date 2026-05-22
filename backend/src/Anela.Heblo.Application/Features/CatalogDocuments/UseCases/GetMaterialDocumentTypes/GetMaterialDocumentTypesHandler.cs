using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using MediatR;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.GetMaterialDocumentTypes;

public class GetMaterialDocumentTypesHandler
    : IRequestHandler<GetMaterialDocumentTypesRequest, GetMaterialDocumentTypesResponse>
{
    public Task<GetMaterialDocumentTypesResponse> Handle(
        GetMaterialDocumentTypesRequest request, CancellationToken cancellationToken)
    {
        var dtos = MaterialDocumentTypes.All
            .Select(t => new MaterialDocumentTypeDto
            {
                Code = t.Code,
                Label = t.Label,
                LotRequired = t.LotRequired,
            })
            .ToList();

        return Task.FromResult(new GetMaterialDocumentTypesResponse { DocumentTypes = dtos });
    }
}
