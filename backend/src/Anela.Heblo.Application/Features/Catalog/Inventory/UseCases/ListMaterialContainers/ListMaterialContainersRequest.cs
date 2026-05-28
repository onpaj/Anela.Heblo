using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListMaterialContainers;

public class ListMaterialContainersRequest : IRequest<ListMaterialContainersResponse>
{
    public int? LotId { get; set; }
    public string? MaterialCode { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
