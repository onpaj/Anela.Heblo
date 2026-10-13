using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.PackingMaterials;

namespace Anela.Heblo.Application.Features.PackingMaterials.Mapping;

internal static class PackingMaterialMapper
{
    public static PackingMaterialDto ToDto(PackingMaterial material, decimal? forecastedDays) => new()
    {
        Id = material.Id,
        Name = material.Name,
        ConsumptionRate = material.ConsumptionRate,
        ConsumptionType = material.ConsumptionType,
        ConsumptionTypeText = PackingMaterialsTextHelper.ConsumptionTypeText(material.ConsumptionType),
        CurrentQuantity = material.CurrentQuantity,
        ForecastedDays = forecastedDays,
        CreatedAt = material.CreatedAt,
        UpdatedAt = material.UpdatedAt
    };
}
