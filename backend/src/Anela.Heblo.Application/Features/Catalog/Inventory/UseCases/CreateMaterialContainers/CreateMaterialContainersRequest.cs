using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers;

public class CreateMaterialContainersRequest : IRequest<CreateMaterialContainersResponse>
{
    public int LotId { get; set; }
    public List<CreateMaterialContainerItem> Items { get; set; } = new();
}

public class CreateMaterialContainerItem
{
    public decimal Amount { get; set; }
    public string Unit { get; set; } = null!;
}
