using Anela.Heblo.Domain.Features.Catalog.Lots;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal interface IFefoConsumptionAllocator
{
    List<ConsumptionItem> Allocate(
        Dictionary<string, IngredientRequirement> ingredientRequirements,
        Dictionary<string, List<CatalogLot>> ingredientLots,
        string sourceProductCode);
}
