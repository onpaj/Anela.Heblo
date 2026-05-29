using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers;

public class CreateMaterialContainersRequest : IRequest<CreateMaterialContainersResponse>
{
    public List<CreateMaterialContainerItem> Items { get; set; } = new();
}

public class CreateMaterialContainerItem
{
    public string MaterialCode { get; set; } = null!;
    public string LotCode { get; set; } = null!;
    public decimal? Amount { get; set; }
    public string? Unit { get; set; }
}
