using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public class UpdatePackingMaterialQuantityRequest : IRequest<UpdatePackingMaterialQuantityResponse>
{
    public int Id { get; set; }
    public decimal NewQuantity { get; set; }
    public DateOnly Date { get; set; }
}

public class UpdatePackingMaterialQuantityResponse : BaseResponse
{
    public PackingMaterialDto Material { get; set; } = null!;
}