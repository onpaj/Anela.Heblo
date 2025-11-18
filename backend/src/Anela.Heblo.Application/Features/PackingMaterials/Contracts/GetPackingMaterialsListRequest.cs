using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public class GetPackingMaterialsListRequest : IRequest<GetPackingMaterialsListResponse>
{
}

public class GetPackingMaterialsListResponse
{
    public List<PackingMaterialDto> Materials { get; set; } = new();
}