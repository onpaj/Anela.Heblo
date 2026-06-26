using Anela.Heblo.Domain.Features.PackingMaterials.Enums;

namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

internal static class PackingMaterialsTextHelper
{
    public static string ConsumptionTypeText(ConsumptionType type) => type switch
    {
        ConsumptionType.PerOrder => "za zakázku",
        ConsumptionType.PerProduct => "za produkt",
        ConsumptionType.PerDay => "za den",
        _ => type.ToString()
    };
}
