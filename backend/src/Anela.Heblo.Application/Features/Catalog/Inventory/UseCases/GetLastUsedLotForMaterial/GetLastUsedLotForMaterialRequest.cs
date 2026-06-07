using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLastUsedLotForMaterial;

public class GetLastUsedLotForMaterialRequest : IRequest<GetLastUsedLotForMaterialResponse>
{
    public string MaterialCode { get; set; } = null!;
}
