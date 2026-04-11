using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal sealed class FlexiIngredientRequirementAggregator : IFlexiIngredientRequirementAggregator
{
    private readonly IFlexiManufactureTemplateService _templateService;

    public FlexiIngredientRequirementAggregator(IFlexiManufactureTemplateService templateService)
    {
        _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
    }

    public async Task<Dictionary<string, IngredientRequirement>> AggregateAsync(
        IReadOnlyList<SubmitManufactureClientItem> items,
        CancellationToken cancellationToken)
    {
        var ingredientRequirements = new Dictionary<string, IngredientRequirement>();

        foreach (var item in items)
        {
            var template = await _templateService.GetManufactureTemplateAsync(item.ProductCode, cancellationToken)
                ?? throw new ApplicationException($"No BoM header for product {item.ProductCode} found");
            var scaleFactor = (double)item.Amount / template.Amount;

            foreach (var ingredient in template.Ingredients.Where(w => w.ProductType != ProductType.UNDEFINED))
            {
                var scaledAmount = ingredient.Amount * scaleFactor;

                if (ingredientRequirements.TryGetValue(ingredient.ProductCode, out var existing))
                {
                    ingredientRequirements[ingredient.ProductCode] = new IngredientRequirement
                    {
                        ProductCode = ingredient.ProductCode,
                        ProductName = existing.ProductName,
                        ProductType = existing.ProductType,
                        RequiredAmount = existing.RequiredAmount + scaledAmount,
                        HasLots = existing.HasLots
                    };
                }
                else
                {
                    ingredientRequirements[ingredient.ProductCode] = new IngredientRequirement
                    {
                        ProductCode = ingredient.ProductCode,
                        ProductName = ingredient.ProductName,
                        ProductType = ingredient.ProductType,
                        RequiredAmount = scaledAmount,
                        HasLots = ingredient.HasLots
                    };
                }
            }
        }

        return ingredientRequirements;
    }
}
