using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialsList;

public class GetPackingMaterialsListResponse : BaseResponse
{
    public List<PackingMaterialDto> Materials { get; set; } = new();
}
