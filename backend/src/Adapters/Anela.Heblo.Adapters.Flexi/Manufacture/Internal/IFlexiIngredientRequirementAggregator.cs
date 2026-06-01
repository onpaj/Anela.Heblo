using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal interface IFlexiIngredientRequirementAggregator
{
    Task<Dictionary<string, IngredientRequirement>> AggregateAsync(
        IReadOnlyList<SubmitManufactureClientItem> items,
        CancellationToken cancellationToken);
}
