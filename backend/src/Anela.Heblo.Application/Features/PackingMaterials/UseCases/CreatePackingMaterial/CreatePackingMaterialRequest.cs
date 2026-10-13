using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.CreatePackingMaterial;

public class CreatePackingMaterialRequest : IRequest<CreatePackingMaterialResponse>
{
    public string Name { get; set; } = null!;
    public decimal ConsumptionRate { get; set; }
    public ConsumptionType ConsumptionType { get; set; }
    public decimal CurrentQuantity { get; set; }
}
