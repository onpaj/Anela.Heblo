using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.ListMaterialDocuments;

public class ListMaterialDocumentsRequest : IRequest<ListCatalogDocumentsResponse>
{
    public string ProductCode { get; set; } = string.Empty;
}
