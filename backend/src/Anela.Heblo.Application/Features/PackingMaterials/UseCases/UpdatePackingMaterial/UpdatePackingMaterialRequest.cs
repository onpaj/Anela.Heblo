using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterial;

public class UpdatePackingMaterialRequest : IRequest<UpdatePackingMaterialResponse>
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public decimal ConsumptionRate { get; set; }
    public ConsumptionType ConsumptionType { get; set; }
}
