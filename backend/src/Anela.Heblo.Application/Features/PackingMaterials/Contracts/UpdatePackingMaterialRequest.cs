using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public class UpdatePackingMaterialRequest : IRequest<UpdatePackingMaterialResponse>
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public decimal ConsumptionRate { get; set; }
    public ConsumptionType ConsumptionType { get; set; }
}

public class UpdatePackingMaterialResponse : BaseResponse
{
    public PackingMaterialDto Material { get; set; } = null!;
}