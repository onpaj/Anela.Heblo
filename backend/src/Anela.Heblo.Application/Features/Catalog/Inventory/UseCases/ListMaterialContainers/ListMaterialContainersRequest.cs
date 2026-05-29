using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListMaterialContainers;

public class ListMaterialContainersRequest : IRequest<ListMaterialContainersResponse>
{
    public string? MaterialCode { get; set; }
    public string? LotCode { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
