using Anela.Heblo.Domain.Features.Catalog.Lots;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal interface IFlexiLotLoader
{
    Task<Dictionary<string, List<CatalogLot>>> LoadAvailableLotsAsync(
        Dictionary<string, IngredientRequirement> ingredientRequirements,
        CancellationToken cancellationToken);
}
