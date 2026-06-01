using Anela.Heblo.Domain.Features.Catalog.Lots;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal sealed class FlexiLotLoader : IFlexiLotLoader
{
    private readonly ILotsClient _lotsClient;

    public FlexiLotLoader(ILotsClient lotsClient)
    {
        _lotsClient = lotsClient ?? throw new ArgumentNullException(nameof(lotsClient));
    }

    public async Task<Dictionary<string, List<CatalogLot>>> LoadAvailableLotsAsync(
        Dictionary<string, IngredientRequirement> ingredientRequirements,
        CancellationToken cancellationToken)
    {
        var ingredientLots = new Dictionary<string, List<CatalogLot>>();

        foreach (var (ingredientCode, requirement) in ingredientRequirements)
        {
            if (requirement.HasLots)
            {
                var lots = await _lotsClient.GetAsync(ingredientCode, cancellationToken: cancellationToken);
                var availableLots = lots.Where(l => l.Amount > 0).ToList();
                ingredientLots[ingredientCode] = availableLots;
            }
            else
            {
                // For items without lots, create empty list
                ingredientLots[ingredientCode] = new List<CatalogLot>();
            }
        }

        return ingredientLots;
    }
}
