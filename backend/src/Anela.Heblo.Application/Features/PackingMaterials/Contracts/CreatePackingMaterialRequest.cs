using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public class CreatePackingMaterialRequest : IRequest<CreatePackingMaterialResponse>
{
    public string Name { get; set; } = null!;
    public decimal ConsumptionRate { get; set; }
    public ConsumptionType ConsumptionType { get; set; }
    public decimal CurrentQuantity { get; set; }
}

public class CreatePackingMaterialResponse
{
    public int Id { get; set; }
    public PackingMaterialDto Material { get; set; } = null!;
}