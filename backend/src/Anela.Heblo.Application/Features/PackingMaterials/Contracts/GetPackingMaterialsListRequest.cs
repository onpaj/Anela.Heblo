using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public class GetPackingMaterialsListRequest : IRequest<GetPackingMaterialsListResponse>
{
}

public class GetPackingMaterialsListResponse : BaseResponse
{
    public List<PackingMaterialDto> Materials { get; set; } = new();
}