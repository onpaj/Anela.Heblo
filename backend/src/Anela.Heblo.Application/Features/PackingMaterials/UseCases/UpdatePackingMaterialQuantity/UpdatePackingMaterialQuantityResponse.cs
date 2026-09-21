using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterialQuantity;

public class UpdatePackingMaterialQuantityResponse : BaseResponse
{
    public PackingMaterialDto Material { get; set; } = null!;
    public string? Error { get; set; }
}
