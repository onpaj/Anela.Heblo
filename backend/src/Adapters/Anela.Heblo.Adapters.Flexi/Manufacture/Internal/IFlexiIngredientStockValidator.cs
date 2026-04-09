namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal interface IFlexiIngredientStockValidator
{
    Task ValidateAsync(
        Dictionary<string, IngredientRequirement> ingredientRequirements,
        CancellationToken cancellationToken);
}
