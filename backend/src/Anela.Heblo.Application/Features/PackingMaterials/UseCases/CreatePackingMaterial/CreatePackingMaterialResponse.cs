using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.CreatePackingMaterial;

public class CreatePackingMaterialResponse : BaseResponse
{
    public int Id { get; set; }
    public PackingMaterialDto Material { get; set; } = null!;
}
