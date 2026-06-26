using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.ListPifDocuments;

public class ListPifDocumentsRequest : IRequest<ListCatalogDocumentsResponse>
{
    public string ProductCode { get; set; } = string.Empty;
}
