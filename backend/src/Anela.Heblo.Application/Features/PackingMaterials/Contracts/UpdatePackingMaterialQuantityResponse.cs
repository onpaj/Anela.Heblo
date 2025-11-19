using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public class UpdatePackingMaterialQuantityResponse : BaseResponse
{
    public PackingMaterialDto Material { get; set; } = null!;
}