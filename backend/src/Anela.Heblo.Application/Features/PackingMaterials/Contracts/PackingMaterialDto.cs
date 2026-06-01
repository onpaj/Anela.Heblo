using Anela.Heblo.Domain.Features.PackingMaterials.Enums;

namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public class PackingMaterialDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public decimal ConsumptionRate { get; set; }
    public ConsumptionType ConsumptionType { get; set; }
    public string ConsumptionTypeText { get; set; } = null!;
    public decimal CurrentQuantity { get; set; }
    public decimal? ForecastedDays { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}